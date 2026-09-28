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
/// <para>The integration tests use a working copy of a repository this run builds for itself
/// (<see cref="SvnWorkingCopyFixture"/>, B426) and are skipped where svn is not installed, which
/// includes CI (B486). The repository's history is this run's own, so what changed since a revision
/// is known and asserted, not just that an answer came back.</para>
/// </summary>
public class SvnChangedFilesSinceTests(SvnWorkingCopyFixture fixture) : IClassFixture<SvnWorkingCopyFixture>
{
    private readonly SvnRevisionControlSystem _svn = new();

    /// <summary>
    /// Everything trunk holds, all of it added after r1 (which only created the layout), as paths
    /// relative to the working copy - and the working copy's root, which svn lists as a directory
    /// whose properties changed (<c>item="none" props="modified"</c>). A directory matches no class's
    /// file, so the caller is unaffected by it.
    /// </summary>
    private static readonly string[] AddedSinceLayout =
    [
        ".",
        "README.txt",
        "package.mo",
        "Models",
        Path.Combine("Models", "package.mo"),
        Path.Combine("Models", "SimpleModel.mo"),
        Path.Combine("Models", "TestModel.mo"),
    ];

    /// <summary>The paths relative to the working copy, in order, so a difference reads as one.</summary>
    private static IEnumerable<string> Relative(string workingCopy, IEnumerable<string> paths) =>
        paths.Select(p => Path.GetRelativePath(workingCopy, p)).Order(StringComparer.Ordinal);

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
        var workingCopy = fixture.RequireWorkingCopy();

        // svn rejects the revision argument itself, so there is no diff to report — as distinct from
        // a diff that came back empty.
        Assert.Null(_svn.GetChangedFilePathsSince(workingCopy, "not-a-revision"));
    }

    [Fact]
    public void AgainstTheCurrentRevision_OfAnUnmodifiedWorkingCopy_ReturnsAnEmptyList()
    {
        var workingCopy = fixture.RequireWorkingCopy();

        // Nothing is modified, so nothing changed since BASE - and that answer is an empty list, the
        // "it worked" case, distinguishable from the null of the failures above.
        var changed = _svn.GetChangedFilePathsSince(workingCopy, "BASE");

        Assert.NotNull(changed);
        Assert.Empty(changed);
    }

    [Fact]
    public void SinceTheLayout_ReportsEverythingTrunkHolds_AsAbsolutePaths()
    {
        var workingCopy = fixture.RequireWorkingCopy();

        // The caller matches them against the graph's file paths, which are absolute. `svn diff
        // --summarize --xml` reports working-copy paths, and a relative one would silently match
        // nothing — the failure mode phase 3 flagged as a path-normalisation risk.
        var changed = _svn.GetChangedFilePathsSince(workingCopy, "1");

        Assert.NotNull(changed);
        Assert.All(changed, p => Assert.True(Path.IsPathRooted(p), $"'{p}' is not absolute"));
        Assert.Equal(AddedSinceLayout.Order(StringComparer.Ordinal), Relative(workingCopy, changed));
    }

    [Fact]
    public void ADeletedFileIsNotReported()
    {
        var workingCopy = fixture.RequireWorkingCopy();

        // A file that is gone cannot be checked, so escalating debt in it would name a class that no
        // longer exists. README.txt was added in r2, so deleting it makes svn report it as deleted
        // since r2 - beside the files added after r2, which are reported.
        var readme = Path.Combine(workingCopy, "README.txt");
        Assert.True(SvnCli.Run("delete", readme).Success);

        try
        {
            var changed = _svn.GetChangedFilePathsSince(workingCopy, "2");

            Assert.NotNull(changed);
            Assert.Equal(AddedSinceLayout.Where(p => p != "README.txt").Order(StringComparer.Ordinal),
                Relative(workingCopy, changed));
        }
        finally
        {
            Assert.True(_svn.RevertFiles(workingCopy, ["README.txt"]).Success);
            Assert.True(File.Exists(readme), "README.txt should be restored by RevertFiles");
        }
    }
}
