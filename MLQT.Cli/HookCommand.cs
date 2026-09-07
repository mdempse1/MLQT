using System.Text;

namespace MLQT.Cli;

/// <summary>
/// Installs a git <c>pre-commit</c> hook that checks what is about to be committed.
///
/// <para>The gate itself is <c>mlqt check</c> and always was — this puts it where a mistake is
/// cheapest to fix. A finding caught in CI has already been pushed, reviewed by whoever was waiting
/// on the build, and has to be corrected in a second commit; the same finding caught here is fixed
/// before it exists.</para>
///
/// <para>Git only. SVN has no client-side hooks — a pre-commit hook there runs on the server and
/// would need MLQT installed on it, which is a different feature for a different person. The
/// desktop app's commit dialog is the SVN answer.</para>
/// </summary>
internal static class HookCommand
{
    /// <summary>Written into the hook so a later install/uninstall can tell it is ours to replace.</summary>
    private const string Marker = "# installed by `mlqt hook install` - safe to delete";

    public static Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Count == 0)
        {
            stderr.WriteLine("error: missing hook action (install|uninstall|status)");
            return Task.FromResult(ExitCodes.Error);
        }

        // IReadOnlyList has no range indexer; the first argument is the action.
        var rest = args.Skip(1).ToList();
        if (!HookOptions.TryParse(rest, out var options, out var error))
        {
            stderr.WriteLine($"error: {error}");
            return Task.FromResult(ExitCodes.Error);
        }

        return Task.FromResult(args[0] switch
        {
            "install" => Install(options!, stdout, stderr),
            "uninstall" => Uninstall(options!, stdout, stderr),
            "status" => Status(options!, stdout, stderr),
            _ => Unknown(args[0], stderr)
        });
    }

    private static int Unknown(string action, TextWriter stderr)
    {
        stderr.WriteLine($"error: unknown hook action '{action}' (expected install|uninstall|status)");
        return ExitCodes.Error;
    }

    private static int Install(HookOptions options, TextWriter stdout, TextWriter stderr)
    {
        if (!TryResolve(options, out var library, out var hookPath, stderr))
            return ExitCodes.Error;

        if (File.Exists(hookPath) && !IsOurs(hookPath) && !options.Force)
        {
            stderr.WriteLine(
                $"error: {hookPath} already exists and was not written by mlqt. " +
                "Move it aside, add the check to it yourself, or pass --force to replace it");
            return ExitCodes.Error;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);
        File.WriteAllText(hookPath, HookScript(options, library!), new UTF8Encoding(false));
        TryMakeExecutable(hookPath);

        stdout.WriteLine($"Installed pre-commit hook: {hookPath}");
        stdout.WriteLine($"  It checks {library} when a commit touches a .mo file,");
        stdout.WriteLine($"  and blocks the commit on findings at or above '{options.FailOn.ToString().ToLowerInvariant()}'.");
        stdout.WriteLine("  `git commit --no-verify` skips it.");
        return ExitCodes.Ok;
    }

    private static int Uninstall(HookOptions options, TextWriter stdout, TextWriter stderr)
    {
        if (!TryResolve(options, out _, out var hookPath, stderr, installing: false))
            return ExitCodes.Error;

        if (!File.Exists(hookPath))
        {
            stdout.WriteLine("No pre-commit hook to remove.");
            return ExitCodes.Ok;
        }

        if (!IsOurs(hookPath) && !options.Force)
        {
            stderr.WriteLine(
                $"error: {hookPath} was not written by mlqt, so it is left alone. Pass --force to delete it anyway");
            return ExitCodes.Error;
        }

        File.Delete(hookPath);
        stdout.WriteLine($"Removed {hookPath}");
        return ExitCodes.Ok;
    }

    private static int Status(HookOptions options, TextWriter stdout, TextWriter stderr)
    {
        if (!TryResolve(options, out _, out var hookPath, stderr, installing: false))
            return ExitCodes.Error;

        if (!File.Exists(hookPath))
            stdout.WriteLine($"No pre-commit hook at {hookPath}");
        else if (IsOurs(hookPath))
            stdout.WriteLine($"mlqt pre-commit hook installed at {hookPath}");
        else
            stdout.WriteLine($"A pre-commit hook exists at {hookPath}, but mlqt did not write it");

        return ExitCodes.Ok;
    }

    /// <summary>
    /// Locates the library and the hook file, or says what is wrong. The repository is found by
    /// walking up from the library, so a library in a subdirectory needs no second path.
    /// </summary>
    /// <param name="installing">
    /// True for <c>install</c>, which must refuse when <c>core.hooksPath</c> redirects git elsewhere:
    /// the file would be written and never run. <c>status</c> and <c>uninstall</c> pass false and
    /// carry on — status has to be able to say what is there, and uninstall has to be able to remove
    /// a hook installed before the redirect was set.
    /// </param>
    private static bool TryResolve(
        HookOptions options, out string? library, out string hookPath, TextWriter stderr,
        bool installing = true)
    {
        library = null;
        hookPath = string.Empty;

        var libraryPath = Path.GetFullPath(options.LibraryPath);
        if (!Directory.Exists(libraryPath) && !File.Exists(libraryPath))
        {
            stderr.WriteLine($"error: library not found: {libraryPath}");
            return false;
        }

        var gitDir = FindGitDirectory(libraryPath);
        if (gitDir is null)
        {
            stderr.WriteLine(
                $"error: {libraryPath} is not inside a git working copy. " +
                "A pre-commit hook is a git feature; SVN runs its hooks on the server");
            return false;
        }

        if (ConfiguredHooksPath(libraryPath) is { } configured)
        {
            var lead = installing ? "error" : "note";
            stderr.WriteLine(
                $"{lead}: this repository sets core.hooksPath to '{configured}', so git reads its hooks " +
                "from there and will not run one written under .git/hooks.");

            if (installing)
            {
                stderr.WriteLine(
                    "       That is usually husky, pre-commit or lefthook managing the hooks. Add the " +
                    "check to whatever they run instead:");
                stderr.WriteLine(
                    $"         mlqt check \"{libraryPath}\" --fail-on " +
                    $"{options.FailOn.ToString().ToLowerInvariant()}");
                stderr.WriteLine("       See the `hook` section of Documentation/cli.md.");
                return false;
            }
        }

        library = libraryPath;
        hookPath = Path.Combine(gitDir, "hooks", "pre-commit");
        return true;
    }

    /// <summary>
    /// The repository's <c>core.hooksPath</c>, or null when it sets none.
    ///
    /// <para>Asked because git reads hooks from that directory <em>instead of</em>
    /// <c>.git/hooks</c>, and husky, pre-commit and lefthook all set it. Writing the file anyway
    /// produced the one outcome a commit gate cannot have: install reported success, status reported
    /// the hook installed, and no commit was ever checked. Refusing with the command to add by hand
    /// is the honest answer — MLQT cannot know how somebody else's hook manager wants to be
    /// extended.</para>
    ///
    /// <para>Asked of <c>git</c> rather than read out of a config file, because the value can come
    /// from any of the system, global, local or worktree scopes.</para>
    /// </summary>
    private static string? ConfiguredHooksPath(string libraryPath)
    {
        try
        {
            var start = Directory.Exists(libraryPath) ? libraryPath : Path.GetDirectoryName(libraryPath);
            if (string.IsNullOrEmpty(start))
                return null;

            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                ArgumentList = { "config", "--get", "core.hooksPath" },
                WorkingDirectory = start,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
                return null;

            var value = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            // Exit 1 is git's "not set", which is the ordinary case and not a failure.
            return value.Length == 0 ? null : value;
        }
        catch
        {
            // No git on PATH, or it would not run. The hook is still worth installing: the
            // overwhelmingly common case is no core.hooksPath at all, and refusing to install
            // because we could not ask would be worse than installing where git looks by default.
            return null;
        }
    }

    /// <summary>
    /// The repository's <c>.git</c> directory, walking up from the library. A worktree or submodule
    /// has a <c>.git</c> <em>file</em> pointing at the real directory, which is followed here so the
    /// hook lands where git will look for it.
    /// </summary>
    private static string? FindGitDirectory(string startPath)
    {
        var directory = Directory.Exists(startPath) ? startPath : Path.GetDirectoryName(startPath);

        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(directory, ".git");

            if (Directory.Exists(candidate))
                return candidate;

            if (File.Exists(candidate))
            {
                var line = File.ReadAllText(candidate).Trim();
                const string prefix = "gitdir:";
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var target = line[prefix.Length..].Trim();
                    return Path.IsPathRooted(target) ? target : Path.GetFullPath(Path.Combine(directory, target));
                }
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static bool IsOurs(string hookPath)
    {
        try
        {
            return File.ReadAllText(hookPath).Contains(Marker, StringComparison.Ordinal);
        }
        catch
        {
            return false;   // unreadable: treat as somebody else's and leave it alone
        }
    }

    /// <summary>
    /// The hook. Written for <c>sh</c> because that is what git runs a hook with, on Windows too.
    /// </summary>
    private static string HookScript(HookOptions options, string library)
    {
        var arguments = new StringBuilder();
        arguments.Append(Quote(ToPosix(library)));
        arguments.Append(" --fail-on ").Append(options.FailOn.ToString().ToLowerInvariant());

        if (options.BaselinePath is { } baseline)
            arguments.Append(" --baseline ").Append(Quote(ToPosix(baseline)));
        if (options.ChangedFrom is { } changedFrom)
            arguments.Append(" --changed-from ").Append(Quote(changedFrom));
        foreach (var dependency in options.DependencyPaths)
            arguments.Append(" --dependency ").Append(Quote(ToPosix(dependency)));

        var executable = Quote(ToPosix(ResolveExecutable()));

        return $"""
            #!/bin/sh
            {Marker}
            #
            # Blocks a commit that would introduce findings at or above '{options.FailOn.ToString().ToLowerInvariant()}'.
            # Re-run `mlqt hook install` to change the options; `git commit --no-verify` skips it.
            #
            # Note: whether to run is decided from the staged change, but the check itself reads the
            # library as it stands on disk - so a partial commit is judged on the unstaged remainder
            # too. See the `hook` section of Documentation/cli.md.

            # Nothing Modelica in this commit: nothing for the checker to say.
            if ! git diff --cached --name-only --diff-filter=ACM | grep -q '\.mo$'; then
              exit 0
            fi

            MLQT={executable}
            if [ ! -x "$MLQT" ]; then
              MLQT=mlqt
            fi

            echo "mlqt: checking before commit..."
            "$MLQT" check {arguments} --no-color
            status=$?

            if [ $status -eq 1 ]; then
              echo ""
              echo "mlqt: commit blocked by the findings above."
              echo "      Fix them, waive one in source with __MLQT(suppress=\"<rule>\"),"
              echo "      or commit with --no-verify if this is not the moment."
            elif [ $status -ne 0 ]; then
              echo ""
              echo "mlqt: the check could not run (exit $status); the commit is blocked because"
              echo "      a check that did not run has not approved anything."
            fi

            exit $status

            """;
    }

    /// <summary>
    /// What the hook should invoke. The absolute path of the executable doing the installing, so the
    /// hook works from a GUI client whose PATH is not the shell's — but only when that executable is
    /// actually us. Launched as <c>dotnet mlqt.dll</c> (or from a test host) the process path is the
    /// host's, and baking that in would have the hook run <c>dotnet check</c>; the bare name, left to
    /// PATH, is the honest answer in that case.
    /// </summary>
    private static string ResolveExecutable()
    {
        const string command = "mlqt";   // matches ToolCommandName/AssemblyName

        var processPath = Environment.ProcessPath;
        if (processPath is not null &&
            string.Equals(Path.GetFileNameWithoutExtension(processPath), command, StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        return command;
    }

    /// <summary>Git's sh takes forward slashes on Windows; a backslash there is an escape.</summary>
    private static string ToPosix(string path) => path.Replace('\\', '/');

    /// <summary>
    /// One argument, as a POSIX shell double-quoted string.
    ///
    /// <para>Escaped rather than merely wrapped: inside double quotes <c>sh</c> still expands
    /// <c>$</c> and backticks and still honours a backslash, so a path or a <c>--changed-from</c> ref
    /// containing one produced a hook that checked the wrong thing rather than one that failed. Git
    /// ref names may contain <c>$</c>, and the value reaches here exactly as it was typed.</para>
    /// </summary>
    private static string Quote(string value)
    {
        // Backslash first, or the escapes added below would themselves be escaped.
        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("$", "\\$")
            .Replace("`", "\\`");

        return $"\"{escaped}\"";
    }

    /// <summary>
    /// Marks the hook executable where that matters. Git for Windows ignores the bit, so a failure
    /// here is not worth failing the install over.
    /// </summary>
    private static void TryMakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path,
                mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch
        {
            // Best effort: the install is still useful, and git will say if it cannot run the hook.
        }
    }
}
