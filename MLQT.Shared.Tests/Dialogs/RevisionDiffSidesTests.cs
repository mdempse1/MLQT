using MLQT.Shared.Dialogs;
using Xunit;

namespace MLQT.Shared.Tests.Dialogs;

/// <summary>
/// Which side of the revision diff shows what (B155).
/// </summary>
/// <remarks>
/// <para>The dialog compares <b>one revision against the working copy</b>, and says so three times:
/// in its title, in its pane labels, and in the message it shows when the two match. The sides used
/// to be chosen by the file's change type instead, and for an <b>added</b> file that put the
/// revision's content in the pane labelled "Working Copy" and nothing in the pane labelled
/// "Revision N" - so the first commit of a file changed later reported that revision's lines as
/// additions and never showed the lines the working copy actually has.</para>
///
/// <para><b>The change type is no longer a parameter</b>, which is the real fix: the mapping cannot
/// depend on something it is not given. These pin what is left, because it is the contract three
/// pieces of the dialog's own text promise a reader.</para>
/// </remarks>
public class RevisionDiffSidesTests
{
    private const string AtRevision = "model M\n  Real x;\nend M;";
    private const string InWorkingCopy = "model M\n  Real x;\n  Real y;\nend M;";

    [Fact]
    public void TheRevisionIsOnTheLeftAndTheWorkingCopyOnTheRight()
    {
        var (original, modified) = RevisionDiffDialog.SidesFor(AtRevision, InWorkingCopy);

        Assert.Equal(AtRevision, original);
        Assert.Equal(InWorkingCopy, modified);
    }

    [Fact]
    public void AFileAddedInThatRevisionStillComparesAgainstTheWorkingCopy()
    {
        // The B155 case. An added file has content at the revision that added it, so there is a real
        // comparison to make - and the two lines the working copy has gained since are the answer.
        // The bug put string.Empty on the left and the revision on the right, which is the diff of
        // the commit rather than the diff this dialog offers, and mislabelled both panes doing it.
        var (original, modified) = RevisionDiffDialog.SidesFor(AtRevision, InWorkingCopy);

        Assert.NotEqual(string.Empty, original);
        Assert.NotEqual(AtRevision, modified);
    }

    [Fact]
    public void AFileNoLongerInTheWorkingCopyHasAnEmptyRightHandSide()
    {
        // A file deleted since that revision: null is what the caller passes when File.Exists said
        // no, and it needs no branch of its own to come out empty.
        var (original, modified) = RevisionDiffDialog.SidesFor(AtRevision, null);

        Assert.Equal(AtRevision, original);
        Assert.Equal(string.Empty, modified);
    }

    [Fact]
    public void AFileNotPresentAtThatRevisionHasAnEmptyLeftHandSide()
    {
        var (original, modified) = RevisionDiffDialog.SidesFor(null, InWorkingCopy);

        Assert.Equal(string.Empty, original);
        Assert.Equal(InWorkingCopy, modified);
    }
}
