using MLQT.Shared.Dialogs;
using Xunit;

namespace MLQT.Shared.Tests.Dialogs;

/// <summary>
/// The default commit message offered after a merge.
/// </summary>
public class MergeCommitMessageTests
{
    [Fact]
    public void APlainMerge_NamesBothBranches()
    {
        Assert.Equal(
            "Merge 'feature/x' into main",
            MergeCommitMessage.Build("feature/x", null, "main"));
    }

    [Fact]
    public void TheVcsAnswerBeatsTheUsersSelection()
    {
        // The user may have picked a shorthand the VCS resolved to something fuller - a remote-
        // tracking name, or the branch an SVN URL turned out to point at. The resolved one is what
        // actually got merged, so it is what the message should say.
        Assert.Equal(
            "Merge 'origin/feature/x' into main",
            MergeCommitMessage.Build("origin/feature/x", "feature/x", "main"));
    }

    [Fact]
    public void TheUsersSelection_IsUsedWhenTheVcsReportedNothing()
    {
        Assert.Equal(
            "Merge 'feature/x' into main",
            MergeCommitMessage.Build(null, "feature/x", "main"));
    }

    [Fact]
    public void NeitherName_StillProducesAUsableMessage()
    {
        // The message is a default in an editable box. Producing something odd is fine; producing an
        // empty box, or throwing on the way to a dialog the user is waiting for, is not.
        Assert.Equal(
            "Merge 'unknown' into working copy",
            MergeCommitMessage.Build(null, null, null));
    }

    [Fact]
    public void NoCurrentBranch_ReadsAsAWorkingCopy()
    {
        // The ordinary SVN case rather than a failure: an SVN working copy has a URL, not a branch
        // name, so "into unknown" would be wrong as well as unhelpful.
        Assert.Equal(
            "Merge 'trunk' into working copy",
            MergeCommitMessage.Build("trunk", null, null));
    }

    [Fact]
    public void AKnownRevisionRange_IsInTheMessage()
    {
        // The half the Git dialog's copy never had. Without it an SVN history says a branch was
        // merged but not how much of it, which is the question anyone reading the log has.
        Assert.Equal(
            "Merge r100:200 from 'branches/feature' into trunk",
            MergeCommitMessage.Build("branches/feature", null, "trunk", 100, 200));
    }

    [Fact]
    public void AnEndRevisionAlone_SaysUpTo()
    {
        // svn does not always report where the merge started - a first merge from a branch has no
        // previous merge to start from. "up to r200" is true; "r0:200" would not be.
        Assert.Equal(
            "Merge 'branches/feature' (up to r200) into trunk",
            MergeCommitMessage.Build("branches/feature", null, "trunk", null, 200));
    }

    [Fact]
    public void AStartRevisionAlone_IsIgnored()
    {
        // A start with no end describes no range at all. Falling back to the plain message beats
        // inventing an end revision or printing "r100:".
        Assert.Equal(
            "Merge 'branches/feature' into trunk",
            MergeCommitMessage.Build("branches/feature", null, "trunk", 100, null));
    }

    [Fact]
    public void NoRevisions_GivesTheGitShape()
    {
        // What the Git dialog passes. It must not acquire an SVN-shaped message by default.
        var message = MergeCommitMessage.Build("feature/x", null, "main");

        Assert.DoesNotContain(" r", message);
    }

    [Fact]
    public void TheRangeIsWrittenLowToHigh_AsGiven()
    {
        // Not reordered. svn's own merge output is start:end and the two are not interchangeable -
        // r200:100 is a reverse merge, and rewriting it as r100:200 would make the log say the
        // opposite of what happened.
        Assert.Equal(
            "Merge r200:100 from 'branches/feature' into trunk",
            MergeCommitMessage.Build("branches/feature", null, "trunk", 200, 100));
    }
}
