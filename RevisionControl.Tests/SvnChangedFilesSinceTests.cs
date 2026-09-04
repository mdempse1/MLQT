namespace RevisionControl.Tests;

/// <summary>
/// <c>SvnRevisionControlSystem.GetChangedFilePathsSince</c> — the SVN half of the baseline ratchet's
/// <c>--changed-from</c>.
///
/// <para>Git's half has had four tests since it landed; this one shipped on inspection, and the phase-3
/// note said as much ("SVN is implemented but lightly tested") without anyone going back to it. The
/// contract it has to keep is the one B14 established: <b>null means the diff could not be taken</b>
/// and an empty list means nothing changed. Treating those alike is what let a broken diff in CI
/// escalate no debt and pass the build looking like a clean one, so the null paths are what most of
/// this file is about — and they are the paths that need no working copy to reach.</para>
///
/// <para>The integration tests use the real working copy at <c>C:\Projects\ModelicaEditorTest</c> and
/// return without asserting when it is absent, matching <c>SvnOperationsTests</c>. CI has neither the
/// working copy nor a server, and filters this suite out by name.</para>
/// </summary>
public class SvnChangedFilesSinceTests
{
    private readonly SvnRevisionControlSystem _svn = new();
    private static readonly string RealSvnPath = @"C:\Projects\ModelicaEditorTest";
    private static bool RealSvnAvailable => Directory.Exists(RealSvnPath);

    [Fact]
    public void NotAWorkingCopy_ReturnsNull()
    {
        // `svn diff` fails outside a working copy. Null, not empty: the caller stops the run rather
        // than reporting that nothing changed.
        var path = Path.Combine(Path.GetTempPath(), "mlqt-not-svn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        try
        {
            Assert.Null(_svn.GetChangedFilePathsSince(path, "1"));
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void APathThatDoesNotExist_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), "mlqt-missing-" + Guid.NewGuid().ToString("N"));

        Assert.Null(_svn.GetChangedFilePathsSince(path, "1"));
    }

    [Fact]
    public void AnUnusableRevision_ReturnsNull()
    {
        if (!RealSvnAvailable)
            return;

        // svn rejects the revision argument itself, so there is no diff to report — as distinct from
        // a diff that came back empty.
        Assert.Null(_svn.GetChangedFilePathsSince(RealSvnPath, "not-a-revision"));
    }

    [Fact]
    public void AgainstTheCurrentRevision_ReturnsSomethingRatherThanNull()
    {
        if (!RealSvnAvailable)
            return;

        // Whatever the working copy happens to hold, the answer is a list: this is the "it worked"
        // case, and the point is that it is distinguishable from the failures above.
        var changed = _svn.GetChangedFilePathsSince(RealSvnPath, "BASE");

        Assert.NotNull(changed);
    }

    [Fact]
    public void ThePathsAreAbsolute()
    {
        if (!RealSvnAvailable)
            return;

        // The caller matches them against the graph's file paths, which are absolute. `svn diff
        // --summarize --xml` reports working-copy paths, and a relative one would silently match
        // nothing — the failure mode phase 3 flagged as a path-normalisation risk.
        var changed = _svn.GetChangedFilePathsSince(RealSvnPath, "1");
        if (changed is null or { Count: 0 })
            return;

        Assert.All(changed, p => Assert.True(Path.IsPathRooted(p), $"'{p}' is not absolute"));
    }

    [Fact]
    public void ADeletedFileIsNotReported()
    {
        if (!RealSvnAvailable)
            return;

        // A file that is gone cannot be checked, so escalating debt in it would name a class that no
        // longer exists. Nothing here forces a deletion into the working copy; what it asserts is
        // that every path handed back is one that can still be read.
        var changed = _svn.GetChangedFilePathsSince(RealSvnPath, "1");
        if (changed is null or { Count: 0 })
            return;

        Assert.All(changed, p => Assert.True(File.Exists(p) || Directory.Exists(p),
            $"'{p}' was reported as changed but is not on disk"));
    }
}
