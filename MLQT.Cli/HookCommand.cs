using System.Text;
using MLQT.Services;

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
        if (!TryResolve(options, out var library, out var location, stderr))
            return ExitCodes.Error;

        // core.hooksPath sends git somewhere else - usually husky, pre-commit or lefthook, which own
        // that directory. Writing .git/hooks anyway produced the one outcome a commit gate cannot
        // have: install said it worked, status said it was installed, and no commit was checked
        // (B41). MLQT cannot know how somebody else's hook manager wants to be extended, so it says
        // what to add instead.
        if (location.IsRedirected)
        {
            stderr.WriteLine(
                $"error: this repository sets core.hooksPath, so git runs its hooks from {location.HooksDirectory} " +
                $"and will not run one written under {location.DefaultHooksDirectory}.");
            stderr.WriteLine(
                "       That is usually husky, pre-commit or lefthook managing the hooks. Add the " +
                "check to whatever they run instead:");
            stderr.WriteLine(
                $"         mlqt check \"{library}\" --fail-on " +
                $"{options.FailOn.ToString().ToLowerInvariant()}");
            stderr.WriteLine("       See the `hook` section of Documentation/cli.md.");
            return ExitCodes.Error;
        }

        // A hook for something that is not a library fails every commit, and the path it was given
        // decides which repository it lands in: the CLI test binaries' directory is inside MLQT's own
        // working copy, so an install that fell back to the current directory wrote a hook checking
        // `bin/Release/net10.0` into MLQT itself (B488). Asked with the discovery `mlqt check` uses,
        // so what is refused here is exactly what the hook's check would have refused.
        if (LibraryDiscovery.DiscoverLibraryPaths(library).Count == 0)
        {
            stderr.WriteLine(
                $"error: no Modelica library found in {library} " +
                "(expected a package.mo, sub-package directories, or .mo files), so no hook was installed.");
            stderr.WriteLine(
                $"       It would have gone into the repository at {location.WorkingTreeRoot}. " +
                "Give the path of the library the hook should check.");
            return ExitCodes.Error;
        }

        var hookPath = location.HookPath;
        if (File.Exists(hookPath) && !IsOurs(hookPath) && !options.Force)
        {
            stderr.WriteLine(
                $"error: {hookPath} already exists and was not written by mlqt. " +
                "Move it aside, add the check to it yourself, or pass --force to replace it");
            return ExitCodes.Error;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);
        File.WriteAllText(hookPath, HookScript(options, library, location.WorkingTreeRoot), new UTF8Encoding(false));
        TryMakeExecutable(hookPath);

        stdout.WriteLine($"Installed pre-commit hook: {hookPath}");
        stdout.WriteLine($"  in the repository at {location.WorkingTreeRoot}.");
        if (location.WorktreeHooksDirectory is not null)
        {
            stdout.WriteLine("  Git runs it for every worktree of this repository, and in each it checks");
            stdout.WriteLine("  that worktree's own copy of the library.");
        }
        stdout.WriteLine($"  It checks {library} when a commit touches a .mo file,");
        stdout.WriteLine($"  and blocks the commit on findings at or above '{options.FailOn.ToString().ToLowerInvariant()}'.");
        stdout.WriteLine("  `git commit --no-verify` skips it.");
        return ExitCodes.Ok;
    }

    private static int Uninstall(HookOptions options, TextWriter stdout, TextWriter stderr)
    {
        if (!TryResolve(options, out _, out var location, stderr))
            return ExitCodes.Error;

        var removed = false;

        // Before B490 an install from a worktree wrote into the worktree's own git directory, where
        // git never looks. Only ever ours to remove, so --force plays no part.
        if (location.WorktreeHookPath is { } stranded && File.Exists(stranded) && IsOurs(stranded))
        {
            File.Delete(stranded);
            stdout.WriteLine($"Removed {stranded} (git never ran it: a worktree's hooks are the repository's)");
            removed = true;
        }

        // Where install writes: git's own location unless core.hooksPath redirects it, in which case
        // this is a hook installed before the redirect was set, which can still be removed. A
        // redirected directory belongs to whatever set core.hooksPath and is never touched.
        var hookPath = location.DefaultHookPath;
        if (File.Exists(hookPath))
        {
            if (!IsOurs(hookPath) && !options.Force)
            {
                stderr.WriteLine(
                    $"error: {hookPath} was not written by mlqt, so it is left alone. Pass --force to delete it anyway");
                return ExitCodes.Error;
            }

            File.Delete(hookPath);
            stdout.WriteLine($"Removed {hookPath}");
            removed = true;
        }

        if (!removed)
            stdout.WriteLine("No pre-commit hook to remove.");

        return ExitCodes.Ok;
    }

    private static int Status(HookOptions options, TextWriter stdout, TextWriter stderr)
    {
        if (!TryResolve(options, out _, out var location, stderr))
            return ExitCodes.Error;

        // What git will run is the answer; anything of ours elsewhere is reported as not run.
        var hookPath = location.HookPath;
        if (!File.Exists(hookPath))
            stdout.WriteLine($"No pre-commit hook at {hookPath}");
        else if (IsOurs(hookPath))
            stdout.WriteLine($"mlqt pre-commit hook installed at {hookPath}");
        else
            stdout.WriteLine($"A pre-commit hook exists at {hookPath}, but mlqt did not write it");

        foreach (var path in new[] { location.IsRedirected ? location.DefaultHookPath : null, location.WorktreeHookPath })
        {
            if (path is not null && File.Exists(path) && IsOurs(path))
            {
                stdout.WriteLine(
                    $"An mlqt pre-commit hook is also at {path}, where git does not run it; " +
                    "`mlqt hook uninstall` removes it.");
            }
        }

        return ExitCodes.Ok;
    }

    /// <summary>
    /// Locates the library and where git keeps the repository's hooks, or says what is wrong. The
    /// repository is the one enclosing the library, so a library in a subdirectory needs no second
    /// path. A <c>core.hooksPath</c> redirect is noted here for status and uninstall, which have to
    /// be able to see and remove a hook installed before it was set; install refuses it.
    /// </summary>
    private static bool TryResolve(
        HookOptions options, out string library, out HookLocation location, TextWriter stderr)
    {
        library = Path.GetFullPath(options.LibraryPath);
        location = null!;

        if (!Directory.Exists(library) && !File.Exists(library))
        {
            stderr.WriteLine($"error: library not found: {library}");
            return false;
        }

        if (HookLocation.Resolve(library) is not { } found)
        {
            stderr.WriteLine(
                $"error: {library} is not inside a git working copy. " +
                "A pre-commit hook is a git feature; SVN runs its hooks on the server");
            return false;
        }

        location = found;

        if (found.FallbackReason is { } reason)
        {
            stderr.WriteLine(
                $"note: {reason}, so the hooks directory was taken from the .git directory: " +
                $"{found.DefaultHooksDirectory}. If this repository sets core.hooksPath, git will not run " +
                "a hook written there.");
        }
        else if (found.IsRedirected)
        {
            stderr.WriteLine(
                $"note: this repository sets core.hooksPath, so git runs its hooks from {found.HooksDirectory}, " +
                $"not {found.DefaultHooksDirectory}.");
        }

        return true;
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
    ///
    /// <para>One hook serves every worktree of a repository, so a path inside the working tree is
    /// written relative to <c>$TOP</c> — the top of whichever worktree is committing — rather than
    /// as the absolute path it was installed from. Absolute, a commit in one worktree was judged on
    /// another's files, and removing the worktree the hook was installed from blocked every commit
    /// in the rest (B490).</para>
    /// </summary>
    private static string HookScript(HookOptions options, string library, string workingTreeRoot)
    {
        var arguments = new StringBuilder();
        arguments.Append(ShellPath(library, workingTreeRoot));
        arguments.Append(" --fail-on ").Append(options.FailOn.ToString().ToLowerInvariant());

        // HookOptions has already made these absolute, against the library; one inside the working
        // tree - a committed baseline, a library beside this one - is followed into each worktree too.
        if (options.BaselinePath is { } baseline)
            arguments.Append(" --baseline ").Append(ShellPath(baseline, workingTreeRoot));
        if (options.ChangedFrom is { } changedFrom)
            arguments.Append(" --changed-from ").Append(Quote(changedFrom));
        foreach (var dependency in options.DependencyPaths)
            arguments.Append(" --dependency ").Append(ShellPath(dependency, workingTreeRoot));

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

            # The worktree being committed; the library is checked in it, not where it was installed from.
            TOP=$(git rev-parse --show-toplevel)

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

    /// <summary>
    /// A path argument for the hook: <c>"$TOP"/"rel"</c> when it is an absolute path inside the
    /// working tree, and otherwise as given, quoted.
    /// </summary>
    private static string ShellPath(string path, string workingTreeRoot)
    {
        if (!Path.IsPathRooted(path))
            return Quote(ToPosix(path));

        var relative = Path.GetRelativePath(workingTreeRoot, path);
        if (relative == ".")
            return "\"$TOP\"";

        var outside = Path.IsPathRooted(relative) || relative == ".." ||
                      relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        return outside ? Quote(ToPosix(path)) : "\"$TOP\"/" + Quote(ToPosix(relative));
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
