using MLQT.Shared.Pages;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// The count beside the find-in-code box (B176), and the thing B248 turned on.
///
/// <para>B248 closed an 84px gap between that box and its next/previous arrows, which was there
/// because this count reserved the space whether or not it had anything to put in it. The element
/// is now always rendered and sizes to its content, so <b>the empty string is load-bearing</b>:
/// returning "0 of 0" or "&#8212;" instead would reopen the gap, and returning nothing when there
/// are matches would take away what B176 added.</para>
/// </summary>
public class CodeReviewMatchCountTests
{
    [Fact]
    public void NothingSearchedForShowsNothingAtAll()
    {
        // Not "0 of 0", and not a dash. An empty element takes no width, which is what keeps the
        // arrows against the field in the toolbar's resting state.
        Assert.Equal("", CodeReview.CodeSearchStatusText("", matchCount: 0, matchIndex: 0));
    }

    [Fact]
    public void ASearchWithMatchesSaysWhichOfHowMany()
    {
        // One-based for the reader; the index is zero-based.
        Assert.Equal("1 of 12", CodeReview.CodeSearchStatusText("der", matchCount: 12, matchIndex: 0));
        Assert.Equal("12 of 12", CodeReview.CodeSearchStatusText("der", matchCount: 12, matchIndex: 11));
    }

    [Fact]
    public void ASearchWithNoMatchesSaysSo()
    {
        // The case that most needs saying, and the one an empty string would hide: the user typed
        // something and the viewer did not move.
        Assert.Equal("no matches", CodeReview.CodeSearchStatusText("qqq", matchCount: 0, matchIndex: 0));
    }

    [Fact]
    public void WhitespaceIsASearch()
    {
        // The emptiness that matters is "the user has typed nothing", not "the term is blank" — a
        // space is something they typed and the viewer will have answered it.
        Assert.NotEqual("", CodeReview.CodeSearchStatusText(" ", matchCount: 0, matchIndex: 0));
    }
}
