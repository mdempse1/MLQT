using MLQT.Shared.Pages;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// B346: in diff mode the find box counted matches in the single view's text, which is not on
/// screen there, and scrolled a <c>.code-viewer</c> that was not rendered; a clicked finding's line
/// was consumed against it and lost. The single view's search and scrolls now stand down while the
/// diff is showing, and the scroll is held for when the user switches back.
/// </summary>
public class CodeReviewDiffModeTests
{
    private static readonly List<string> Lines = ["model M", "  Real gain;", "end M;"];

    [Fact]
    public void TheFindBoxIsOffWhileTheDiffIsShowing()
    {
        Assert.False(CodeReview.CodeSearchAvailable(isDiffMode: true, Lines));
    }

    [Fact]
    public void TheFindBoxIsOnInTheSingleViewWithAClassToSearch()
    {
        // The control, and the existing rule that there has to be something to search.
        Assert.True(CodeReview.CodeSearchAvailable(isDiffMode: false, Lines));
        Assert.False(CodeReview.CodeSearchAvailable(isDiffMode: false, null));
        Assert.False(CodeReview.CodeSearchAvailable(isDiffMode: false, []));
    }

    [Fact]
    public void AFindingsScrollIsHeldWhileTheDiffIsShowing()
    {
        Assert.False(CodeReview.PendingScrollsCanLand(
            isLoading: false, showingQuickPaint: false, isDiffMode: true, Lines));
    }

    [Fact]
    public void AFindingsScrollLandsInTheSingleView()
    {
        Assert.True(CodeReview.PendingScrollsCanLand(
            isLoading: false, showingQuickPaint: false, isDiffMode: false, Lines));
    }
}
