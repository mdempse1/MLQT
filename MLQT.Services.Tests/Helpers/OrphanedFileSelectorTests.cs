using MLQT.Services.Helpers;
using Xunit;

namespace MLQT.Services.Tests.Helpers;

/// <summary>
/// <see cref="OrphanedFileSelector"/>, lifted out of <c>MainLayout</c> in phase 7a-4.
///
/// <para>It decides what gets deleted from a user's working copy after a full library save, which
/// makes both of its mistakes expensive: deleting too much loses work, and deleting too little
/// leaves the library holding two copies of the same class.</para>
/// </summary>
public class OrphanedFileSelectorTests
{
    private static HashSet<string> Set(params string[] paths) => new(paths, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void AFileThatWasNotWrittenAgain_IsAnOrphan()
    {
        // The old spelling of a class that now lives somewhere else. Left behind, the library on
        // disk holds it twice.
        var orphans = OrphanedFileSelector.SelectOrphans(
            ["old.mo"], [], Set("new.mo"), Set());

        Assert.Equal(["old.mo"], orphans);
    }

    [Fact]
    public void AFileThatWasWrittenAgain_IsKept()
    {
        var orphans = OrphanedFileSelector.SelectOrphans(
            ["kept.mo"], [], Set("kept.mo"), Set());

        Assert.Empty(orphans);
    }

    [Fact]
    public void AnOrphanedPackageOrderFile_IsIncluded()
    {
        // A directory that collapsed back into a single file leaves its package.order behind, and a
        // stale one names classes that are no longer there.
        var orphans = OrphanedFileSelector.SelectOrphans(
            [], ["Sub/package.order"], Set("Lib.mo"), Set());

        Assert.Equal(["Sub/package.order"], orphans);
    }

    [Fact]
    public void AFileScheduledForVcsAddition_IsNeverDeleted()
    {
        // The rule with the sharpest consequence. A file added by an SVN or Git merge is scheduled
        // for addition but may not be written by the formatter - a malformed within clause, a class
        // name that does not match its file. Deleting it leaves the VCS tracking a file it cannot
        // find, and the next commit fails with "scheduled for addition, but is missing". The user's
        // merge is then stuck behind a file MLQT removed.
        var orphans = OrphanedFileSelector.SelectOrphans(
            ["merged.mo"], [], Set(), Set("merged.mo"));

        Assert.Empty(orphans);
    }

    [Fact]
    public void AnAddedPackageOrderFile_IsAlsoProtected()
    {
        var orphans = OrphanedFileSelector.SelectOrphans(
            [], ["Sub/package.order"], Set(), Set("Sub/package.order"));

        Assert.Empty(orphans);
    }

    [Fact]
    public void PathsAreComparedWithoutRegardToCase()
    {
        // Windows reports whatever case the operation used. A file written as Lib.mo and remembered
        // as lib.mo would otherwise be deleted immediately after being written.
        var orphans = OrphanedFileSelector.SelectOrphans(
            [@"C:\lib\Thing.mo"], [], Set(@"c:\LIB\THING.MO"), Set());

        Assert.Empty(orphans);
    }

    [Fact]
    public void TheVcsProtectionAlsoIgnoresCase()
    {
        var orphans = OrphanedFileSelector.SelectOrphans(
            [@"C:\lib\Merged.mo"], [], Set(), Set(@"c:\LIB\MERGED.MO"));

        Assert.Empty(orphans);
    }

    [Fact]
    public void BothKindsOfFile_AreReturnedTogether()
    {
        var orphans = OrphanedFileSelector.SelectOrphans(
            ["a.mo", "b.mo"], ["Sub/package.order"], Set("b.mo"), Set());

        // Asserted as a set: the order the two kinds come back in is not something a caller relies
        // on, and pinning it would only pin the sort the assertion itself chose.
        Assert.Equal(["Sub/package.order", "a.mo"], orphans.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void ASaveThatRewroteEverything_LeavesNothingToDelete()
    {
        var orphans = OrphanedFileSelector.SelectOrphans(
            ["a.mo", "b.mo"], ["package.order"], Set("a.mo", "b.mo", "package.order"), Set());

        Assert.Empty(orphans);
    }

    [Fact]
    public void NothingThereBefore_IsNothingToDelete()
    {
        Assert.Empty(OrphanedFileSelector.SelectOrphans([], [], Set("new.mo"), Set()));
    }
}
