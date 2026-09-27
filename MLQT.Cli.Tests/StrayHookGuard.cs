namespace MLQT.Cli.Tests;

/// <summary>
/// Makes a hook test fail loudly, and clean up after itself, if an install lands in a repository
/// that encloses the test run instead of in the test's own temporary one (B488).
///
/// <para>The hook command finds its repository by walking up from the library it is given, and the
/// test binaries sit inside MLQT's own working copy. So anything that makes an install use the
/// current directory — a test that omits the path, or a mutant under <c>run-mutation.ps1</c> that
/// drops it — writes a hook into MLQT itself, where it blocks every commit that touches a
/// <c>.mo</c> file. That happened once and was found only when it blocked a commit days later.</para>
///
/// <para>Snapshot on construction, compare on disposal: a <c>pre-commit</c> hook that was not
/// there before and is there now was written by the test. It is removed if mlqt wrote it — never
/// otherwise — and the test fails naming the file.</para>
/// </summary>
internal sealed class StrayHookGuard : IDisposable
{
    private const string Marker = "installed by `mlqt hook install`";

    private readonly List<string> _watched;

    /// <summary>Guards the repositories enclosing the given directories.</summary>
    public StrayHookGuard(params string[] startDirectories)
    {
        _watched = startDirectories
            .SelectMany(HookPathsEnclosing)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !File.Exists(path))   // one there already is not ours to judge
            .ToList();
    }

    /// <summary>The test binaries' directory and the current one: where a defaulted path points.</summary>
    public static StrayHookGuard ForTestRun() =>
        new(AppContext.BaseDirectory, Directory.GetCurrentDirectory());

    /// <summary>
    /// The nearest directory above <paramref name="directory"/> (itself excluded) that is a git
    /// working copy, or null. A hook test's repository must have none, or an install that walked up
    /// one level too far would still "work".
    /// </summary>
    public static string? EnclosingWorkingCopy(string directory)
    {
        for (var current = Path.GetDirectoryName(Path.GetFullPath(directory));
             !string.IsNullOrEmpty(current);
             current = Path.GetDirectoryName(current))
        {
            var candidate = Path.Combine(current, ".git");
            if (Directory.Exists(candidate) || File.Exists(candidate))
                return current;
        }

        return null;
    }

    public void Dispose()
    {
        var stray = _watched.Where(File.Exists).ToList();
        if (stray.Count == 0)
            return;

        foreach (var path in stray)
        {
            try
            {
                if (File.ReadAllText(path).Contains(Marker, StringComparison.Ordinal))
                    File.Delete(path);
            }
            catch
            {
                // Reported below either way; a hook we could not read or delete is left for a person.
            }
        }

        Assert.Fail(
            "A hook test wrote a pre-commit hook outside its temporary repository: " +
            string.Join(", ", stray) + ". Removed where mlqt wrote it; check that none remains.");
    }

    /// <summary>
    /// Every place the hook command could write a <c>pre-commit</c> for the repository enclosing
    /// <paramref name="start"/>: its <c>.git/hooks</c>, or for a worktree both the worktree's git
    /// directory (where the command follows a <c>.git</c> file to) and the common one (where git reads).
    /// </summary>
    private static IEnumerable<string> HookPathsEnclosing(string start)
    {
        for (var current = Path.GetFullPath(start);
             !string.IsNullOrEmpty(current);
             current = Path.GetDirectoryName(current))
        {
            var candidate = Path.Combine(current, ".git");

            if (Directory.Exists(candidate))
                return [Path.Combine(candidate, "hooks", "pre-commit")];

            if (File.Exists(candidate))
            {
                var line = File.ReadAllText(candidate).Trim();
                const string prefix = "gitdir:";
                if (!line.StartsWith(prefix, StringComparison.Ordinal))
                    return [];

                var gitDir = Path.GetFullPath(Path.Combine(current, line[prefix.Length..].Trim()));
                var paths = new List<string> { Path.Combine(gitDir, "hooks", "pre-commit") };

                var commonDirFile = Path.Combine(gitDir, "commondir");
                if (File.Exists(commonDirFile))
                {
                    var common = Path.GetFullPath(Path.Combine(gitDir, File.ReadAllText(commonDirFile).Trim()));
                    paths.Add(Path.Combine(common, "hooks", "pre-commit"));
                }

                return paths;
            }
        }

        return [];
    }
}
