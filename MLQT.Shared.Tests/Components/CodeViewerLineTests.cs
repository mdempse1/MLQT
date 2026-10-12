using Bunit;
using Microsoft.AspNetCore.Components;
using MLQT.Shared.Components;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// What a caller can say about <see cref="CodeViewer"/>'s lines: each carries its number, some can be
/// marked, and one can be scrolled to.
///
/// <para>Asked for by a host showing a class beside a description of it, which tints the lines each
/// claim cites. A viewer that numbered its lines only in the gutter's text left such a caller nothing
/// to address a line by.</para>
/// </summary>
public class CodeViewerLineTests : MlqtComponentTestBase
{
    private static readonly List<string> ThreeLines =
    [
        "<KEYWORD>model</KEYWORD> M",
        "  <TYPE>Real</TYPE> x;",
        "<KEYWORD>end</KEYWORD> M;",
    ];

    private IRenderedComponent<CodeViewer> View(
        List<string> lines, IReadOnlySet<int>? marked = null, int? scrollTo = null) =>
        Render<CodeViewer>(p => p
            .Add(c => c.Lines, lines)
            .Add(c => c.MarkedLines, marked)
            .Add(c => c.ScrollToLine, scrollTo));

    // ---------------------------------------------------------------- data-line

    [Fact]
    public void EachLine_CarriesItsOneBasedNumber()
    {
        var lines = View(ThreeLines).FindAll(".code-line");

        Assert.Equal(["1", "2", "3"], lines.Select(l => l.GetAttribute("data-line")));
    }

    // ---------------------------------------------------------------- MarkedLines

    [Fact]
    public void WithoutMarkedLines_NoLineIsMarked()
    {
        var view = View(ThreeLines);

        Assert.Empty(view.FindAll(".code-line-marked"));
        Assert.All(view.FindAll(".code-line"), l => Assert.Equal("code-line", l.ClassName));
    }

    [Fact]
    public void AMarkedLine_GainsTheMarkedClassAndKeepsItsOwn()
    {
        var marked = View(ThreeLines, marked: new HashSet<int> { 2, 3 }).FindAll(".code-line-marked");

        Assert.Equal(["2", "3"], marked.Select(l => l.GetAttribute("data-line")));
        Assert.All(marked, l => Assert.Contains("code-line", l.ClassList));
    }

    [Fact]
    public void ANumberOutsideTheListing_MarksNothing()
    {
        var view = View(ThreeLines, marked: new HashSet<int> { 0, 4, -1 });

        Assert.Empty(view.FindAll(".code-line-marked"));
    }

    [Fact]
    public void ChangingTheMarkedLines_MovesTheMark()
    {
        // The rendered HTML of each line is cached against the lines; the mark must not be.
        var view = View(ThreeLines, marked: new HashSet<int> { 1 });

        view.Render(p => p.Add(c => c.MarkedLines, new HashSet<int> { 3 }));

        Assert.Equal("3", Assert.Single(view.FindAll(".code-line-marked")).GetAttribute("data-line"));
    }

    // ---------------------------------------------------------------- ScrollToLine

    [Fact]
    public void WithoutScrollToLine_NothingIsScrolled()
    {
        View(ThreeLines);

        Assert.Empty(JSInterop.Invocations["spellCheck.scrollLineIntoView"]);
    }

    [Fact]
    public void ScrollToLine_ScrollsThisViewerToThatLine()
    {
        View(ThreeLines, scrollTo: 2);

        var call = Assert.Single(JSInterop.Invocations["spellCheck.scrollLineIntoView"]);
        // The viewer's own element rather than a selector, which would find the first viewer on the page.
        Assert.IsType<ElementReference>(call.Arguments[0]);
        Assert.Equal(2, call.Arguments[1]);
    }

    [Fact]
    public void ScrollToLine_IsNotRepeatedByARenderThatChangedNeitherIt()
    {
        // Every re-render would otherwise yank the view back while the user scrolls away.
        var view = View(ThreeLines, scrollTo: 2);

        view.Render(p => p.Add(c => c.MarkedLines, new HashSet<int> { 1 }));

        Assert.Single(JSInterop.Invocations["spellCheck.scrollLineIntoView"]);
    }

    [Fact]
    public void NewLinesWithTheSameScrollToLine_ScrollAgain()
    {
        // Another class shown with the same line asked for is a new place to be.
        var view = View(ThreeLines, scrollTo: 2);

        view.Render(p => p.Add(c => c.Lines, ThreeLines.ToList()));

        Assert.Equal(2, JSInterop.Invocations["spellCheck.scrollLineIntoView"].Count);
    }

    [Fact]
    public void ALineTheListingDoesNotHave_IsNotScrolledTo()
    {
        View(ThreeLines, scrollTo: 9);

        Assert.Empty(JSInterop.Invocations["spellCheck.scrollLineIntoView"]);
    }
}
