using MLQT.Shared.Components;
using MLQT.Shared.Pages;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// Searching the class on screen (B176) — "locating a parameter by name in a long class means
/// scrolling".
///
/// <para>Two halves, and they search different things on purpose. The page finds the <b>lines</b>,
/// over the text as the user reads it: tags stripped and entities decoded, so a search for
/// <c>&lt;html&gt;</c> finds the documentation string that contains it and a search for
/// <c>KEYWORD</c> finds nothing. <c>CodeViewer</c> then tints the occurrences, inside each token's
/// own span. That split is why a match spanning two tokens is still reachable, although it cannot be
/// tinted — the line is found, and stepping to it scrolls there.</para>
/// </summary>
public class CodeReviewCodeSearchTests
{
    private static readonly List<string> Markup =
    [
        "<KEYWORD>model</KEYWORD> <IDENT>M</IDENT> <STRING>\"a model\"</STRING>",
        "  <KEYWORD>parameter</KEYWORD> <TYPE>Real</TYPE> <IDENT>gain</IDENT> <OPERATOR>=</OPERATOR> <NUMBER>1</NUMBER>",
        "  <COMMENT>// the gain again</COMMENT>",
        // Raw, because that is what the classifier emits: it leaves tag contents as the source wrote
        // them and CodeViewer encodes them on the way out. Writing this pre-encoded made the search
        // look broken when it was the fixture that was lying.
        "  <STRING>\"<html>body</html>\"</STRING>",
        "<KEYWORD>end</KEYWORD> <IDENT>M</IDENT>",
    ];

    // ── finding the lines ─────────────────────────────────────────────────────────

    [Fact]
    public void EveryLineHoldingTheTermIsFound_InOrder() =>
        Assert.Equal([2, 3], CodeReview.FindMatchingLines(Markup, "gain"));

    [Fact]
    public void TheSearchIgnoresCase() =>
        Assert.Equal([2, 3], CodeReview.FindMatchingLines(Markup, "GAIN"));

    [Fact]
    public void TheMarkupItselfIsNotSearchable()
    {
        // Searching the raw lines would match every one of them, which is the mistake this is
        // written to prevent: what the user sees has no tags in it.
        Assert.Empty(CodeReview.FindMatchingLines(Markup, "KEYWORD"));
        Assert.Empty(CodeReview.FindMatchingLines(Markup, "IDENT"));
    }

    [Fact]
    public void EntitiesAreSearchedAsTheUserSeesThem()
    {
        // The documentation string holds `<html>`; the markup holds `&lt;html&gt;`. The user typed
        // the first.
        Assert.Equal([4], CodeReview.FindMatchingLines(Markup, "<html>"));
        Assert.Empty(CodeReview.FindMatchingLines(Markup, "&lt;"));
    }

    [Fact]
    public void AMatchSpanningTwoTokensStillFindsItsLine()
    {
        // `Real gain` is a TYPE and an IDENT in separate spans, so CodeViewer cannot tint it — but
        // the line is found, which is what makes it reachable.
        Assert.Equal([2], CodeReview.FindMatchingLines(Markup, "Real gain"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NothingToSearchForFindsNothing(string? term) =>
        Assert.Empty(CodeReview.FindMatchingLines(Markup, term));

    [Fact]
    public void NoCodeFindsNothing() =>
        Assert.Empty(CodeReview.FindMatchingLines(null, "gain"));

    // ── tinting the matches ───────────────────────────────────────────────────────

    [Fact]
    public void AMatchIsWrappedWithoutLosingItsSyntaxColour()
    {
        var html = CodeViewer.ToHtml(Markup, null, "gain");

        // Inside the token's own span, not instead of it: the code stays readable as code.
        Assert.Contains("<span class=\"code-ident\"><span class=\"code-search-match\">gain</span></span>",
            html[1]);
    }

    [Fact]
    public void MatchesAreTintedInEveryKindOfToken()
    {
        var html = CodeViewer.ToHtml(Markup, null, "model");

        Assert.Contains("code-search-match", html[0]);   // a keyword
        Assert.Contains("code-search-match", html[0]);   // and the string on the same line
    }

    [Fact]
    public void TheLineNumberGutterIsNeverTinted()
    {
        // Searching for a digit would otherwise light up the gutter rather than the code.
        var html = CodeViewer.ToHtml(Markup, null, "1");

        Assert.DoesNotContain("code-linenumber\"><span class=\"code-search-match\"", html[0]);
        Assert.Contains("<span class=\"code-number\"><span class=\"code-search-match\">1</span></span>",
            html[1]);
    }

    [Fact]
    public void SearchingForMarkupCharactersFindsTheTextNotTheTags()
    {
        // The term is encoded before it is matched, because the content it is matched against has
        // been. Without that, searching for `<html>` inside a documentation string finds nothing.
        var html = CodeViewer.ToHtml(Markup, null, "<html>");

        Assert.Contains("code-search-match", html[3]);
        Assert.DoesNotContain("code-search-match", html[0]);
    }

    [Fact]
    public void NoTermLeavesTheMarkupAlone()
    {
        Assert.DoesNotContain("code-search-match", string.Join("\n", CodeViewer.ToHtml(Markup, null, null)));
        Assert.DoesNotContain("code-search-match", string.Join("\n", CodeViewer.ToHtml(Markup, null, "  ")));
    }
}
