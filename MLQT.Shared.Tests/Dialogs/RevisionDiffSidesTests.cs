using MLQT.Shared.Dialogs;
using Xunit;

namespace MLQT.Shared.Tests.Dialogs;

/// <summary>
/// Which side of the revision diff shows what (B155, then B202).
/// </summary>
/// <remarks>
/// <para><b>The dialog shows what a commit changed</b>: the revision before it on the left, the
/// revision itself on the right. It used to compare the revision against the <i>working copy</i>,
/// which is a different question and not the one a user clicking a file in a commit's changed-file
/// list is asking — against an old commit that diff is mostly other people's later work (B202).</para>
///
/// <para><b>The change type is not a parameter</b>, and that is B155's fix, kept. The sides used to
/// be chosen by it, and for an added file that put the revision's content in the pane labelled
/// "Working Copy" and nothing in the pane labelled "Revision N". A mapping cannot disagree with
/// itself about something it is not given.</para>
///
/// <para>These tests were rewritten rather than extended when the behaviour changed. They passed
/// unaltered against it — <c>SidesFor</c> takes two strings and does not know what they mean — while
/// their names and their prose described a dialog that no longer existed, which is worse than no
/// tests at all.</para>
/// </remarks>
public class RevisionDiffSidesTests
{
    private const string Before = "model M\n  Real x;\nend M;";
    private const string AtRevision = "model M\n  Real x;\n  Real y;\nend M;";

    [Fact]
    public void ThePreviousRevisionIsOnTheLeftAndTheCommitOnTheRight()
    {
        var (original, modified) = RevisionDiffDialog.SidesFor(Before, AtRevision);

        Assert.Equal(Before, original);
        Assert.Equal(AtRevision, modified);
    }

    [Fact]
    public void AFileTheCommitAddedHasAnEmptyLeftHandSide()
    {
        // Nothing to compare against: the file did not exist at the previous revision, so the
        // caller passes null and the whole file reads as added. No branch on ChangeType says so.
        var (original, modified) = RevisionDiffDialog.SidesFor(null, AtRevision);

        Assert.Equal(string.Empty, original);
        Assert.Equal(AtRevision, modified);
    }

    [Fact]
    public void AFileTheCommitDeletedHasAnEmptyRightHandSide()
    {
        var (original, modified) = RevisionDiffDialog.SidesFor(Before, null);

        Assert.Equal(Before, original);
        Assert.Equal(string.Empty, modified);
    }

    [Fact]
    public void TheFirstCommitOfARepositoryReadsAsAllAdditions()
    {
        // There is no revision before the first one, so the dialog has no predecessor to ask for -
        // the same empty left-hand side as a file added later, reached by a different route.
        var (original, modified) = RevisionDiffDialog.SidesFor(null, AtRevision);

        Assert.Equal(string.Empty, original);
        Assert.Equal(AtRevision, modified);
    }

    [Theory]
    [InlineData("1a2b3c4d5e6f7a8b9c0d", "1a2b3c4")]   // a Git SHA, cut to what the rest of the UI shows
    [InlineData("4711", "4711")]                       // an SVN revision number, already short
    [InlineData("", "")]
    public void ARevisionIsShortenedForItsPaneLabel(string revision, string expected)
    {
        Assert.Equal(expected, RevisionDiffDialog.Shorten(revision));
    }
}
