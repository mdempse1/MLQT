using System.Diagnostics;

namespace MLQT.Cli;

/// <summary>
/// Where git keeps the hooks of the repository a library belongs to — asked of git, because the
/// answer is not "the <c>.git</c> directory above it" often enough to matter.
///
/// <para>A worktree has a <c>.git</c> <em>file</em> pointing at <c>.git/worktrees/&lt;name&gt;</c>,
/// but git reads hooks from the <em>common</em> directory, so following the file wrote a hook git
/// never ran: install said it worked, status said it was installed, and no commit was checked
/// (B490). <c>core.hooksPath</c> produced the same outcome by another route (B41). Both are answered
/// by <c>git rev-parse --git-path hooks</c>, which is git's own lookup rather than a copy of it.</para>
/// </summary>
internal sealed record HookLocation
{
    /// <summary>The directory git runs hooks from: <c>core.hooksPath</c> when set, else the common
    /// directory's <c>hooks</c>.</summary>
    public required string HooksDirectory { get; init; }

    /// <summary>The common directory's <c>hooks</c> — where git looks with no <c>core.hooksPath</c>,
    /// and the only place <c>mlqt hook install</c> ever writes.</summary>
    public required string DefaultHooksDirectory { get; init; }

    /// <summary>
    /// A worktree's own git directory's <c>hooks</c>, which git never reads. Before B490 an install
    /// from a worktree wrote there, so status and uninstall still look; null outside a worktree.
    /// </summary>
    public string? WorktreeHooksDirectory { get; init; }

    /// <summary>The top of the working tree — where git runs a hook from, and what a hook's paths are
    /// written relative to so that it checks whichever worktree is committing.</summary>
    public required string WorkingTreeRoot { get; init; }

    /// <summary>
    /// Why git was not asked, when it was not: the location was then worked out from the <c>.git</c>
    /// directory, and a <c>core.hooksPath</c> setting could not be seen. Null when git answered.
    /// </summary>
    public string? FallbackReason { get; init; }

    /// <summary>True when <c>core.hooksPath</c> sends git somewhere other than the default.</summary>
    public bool IsRedirected => !SamePath(HooksDirectory, DefaultHooksDirectory);

    public string HookPath => Path.Combine(HooksDirectory, "pre-commit");
    public string DefaultHookPath => Path.Combine(DefaultHooksDirectory, "pre-commit");
    public string? WorktreeHookPath =>
        WorktreeHooksDirectory is null ? null : Path.Combine(WorktreeHooksDirectory, "pre-commit");

    /// <summary>
    /// The hook location for the repository enclosing <paramref name="path"/>, or null when it is in
    /// no git working copy. Asks <paramref name="git"/>; when that cannot be run, or fails, falls back
    /// to reading the <c>.git</c> directory (see <see cref="FallbackReason"/>).
    /// </summary>
    public static HookLocation? Resolve(string path, string git = "git")
    {
        var start = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(start))
            return null;

        string reason;
        try
        {
            var top = RunGit(git, start, "rev-parse", "--show-toplevel");
            if (top.Code == 0 && top.Lines.Length == 1)
            {
                var root = Path.GetFullPath(top.Lines[0]);

                // Asked from the top of the working tree, so that a relative answer — a relative
                // core.hooksPath comes back as written — is relative to where git runs the hook.
                var paths = RunGit(git, root, "rev-parse", "--git-dir", "--git-common-dir", "--git-path", "hooks");
                if (paths.Code == 0 && paths.Lines.Length == 3)
                {
                    var gitDir = Path.GetFullPath(Path.Combine(root, paths.Lines[0]));
                    var commonDir = Path.GetFullPath(Path.Combine(root, paths.Lines[1]));

                    return new HookLocation
                    {
                        HooksDirectory = Path.GetFullPath(Path.Combine(root, paths.Lines[2])),
                        DefaultHooksDirectory = Path.Combine(commonDir, "hooks"),
                        WorktreeHooksDirectory = SamePath(gitDir, commonDir) ? null : Path.Combine(gitDir, "hooks"),
                        WorkingTreeRoot = root,
                    };
                }

                reason = $"`git rev-parse --git-path hooks` failed: {paths.Error}";
            }
            else
            {
                reason = $"`git rev-parse --show-toplevel` failed: {top.Error}";
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            reason = $"git could not be run ({ex.Message})";
        }

        return FromFileSystem(start, reason);
    }

    /// <summary>
    /// The location worked out without git: the nearest <c>.git</c> above <paramref name="start"/>,
    /// and for a worktree (a <c>.git</c> file) the common directory its <c>commondir</c> names. This
    /// is where git looks unless <c>core.hooksPath</c> is set, which only git can say.
    /// </summary>
    internal static HookLocation? FromFileSystem(string start, string reason)
    {
        for (var directory = Path.GetFullPath(start);
             !string.IsNullOrEmpty(directory);
             directory = Path.GetDirectoryName(directory))
        {
            var candidate = Path.Combine(directory, ".git");

            if (Directory.Exists(candidate))
                return Plain(directory, candidate, candidate, reason);

            if (!File.Exists(candidate))
                continue;

            const string prefix = "gitdir:";
            var line = File.ReadAllText(candidate).Trim();
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
                return null;

            var gitDir = Path.GetFullPath(Path.Combine(directory, line[prefix.Length..].Trim()));

            // A worktree's git directory names the shared one; a submodule's has no such file and is
            // its own common directory.
            var commonDirFile = Path.Combine(gitDir, "commondir");
            var commonDir = File.Exists(commonDirFile)
                ? Path.GetFullPath(Path.Combine(gitDir, File.ReadAllText(commonDirFile).Trim()))
                : gitDir;

            return Plain(directory, gitDir, commonDir, reason);
        }

        return null;
    }

    private static HookLocation Plain(string root, string gitDir, string commonDir, string reason) => new()
    {
        HooksDirectory = Path.Combine(commonDir, "hooks"),
        DefaultHooksDirectory = Path.Combine(commonDir, "hooks"),
        WorktreeHooksDirectory = SamePath(gitDir, commonDir) ? null : Path.Combine(gitDir, "hooks"),
        WorkingTreeRoot = root,
        FallbackReason = reason,
    };

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static (int Code, string[] Lines, string Error) RunGit(string git, string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = git,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"{git} did not start");

        var errorTask = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(10_000))
        {
            try { process.Kill(); } catch (InvalidOperationException) { /* already gone */ }
            throw new InvalidOperationException($"{git} did not answer within 10 seconds");
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (process.ExitCode, lines, errorTask.GetAwaiter().GetResult().Trim());
    }
}
