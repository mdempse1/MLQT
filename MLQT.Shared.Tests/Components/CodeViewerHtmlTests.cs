using MLQT.Shared.Components;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// <see cref="CodeViewer.ToHtml"/>: turning a syntax-highlighted listing into the markup the browser
/// renders.
///
/// <para>The encoding is what matters here. The input is Modelica source, which has angle brackets
/// and ampersands in ordinary strings and comments, and it is interpolated into markup rather than
/// bound as text — so a gap between "what the highlighter tagged" and "what is escaped" shows up as
/// a mangled line at best.</para>
/// </summary>
public class CodeViewerHtmlTests
{
    private static string OneLine(string tagged, params string[] misspelled) =>
        Assert.Single(CodeViewer.ToHtml([tagged], misspelled.Length == 0 ? null : misspelled));

    [Fact]
    public void ATaggedToken_BecomesASpanOfThatClass()
    {
        var html = OneLine("<KEYWORD>model</KEYWORD>");

        Assert.Contains("<span class=\"code-keyword\">model</span>", html);
    }

    [Fact]
    public void ContentInsideATag_IsHtmlEncoded()
    {
        // A Modelica string can hold anything. Unencoded, "<b>" would become live markup.
        var html = OneLine("<STRING>\"a &lt; b\"</STRING>");

        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&amp;lt;", html);
    }

    [Fact]
    public void AnAngleBracketInAComment_IsEncodedRatherThanRendered()
    {
        var html = OneLine("<COMMENT>// see <Thing></COMMENT>");

        Assert.Contains("&lt;Thing&gt;", html);
    }

    [Fact]
    public void EachLineCarriesItsNumber()
    {
        var html = CodeViewer.ToHtml(["<KEYWORD>model</KEYWORD>", "<KEYWORD>end</KEYWORD>"], null);

        Assert.Equal(2, html.Count);
        Assert.Contains("code-linenumber", html[0]);
        Assert.Contains("code-linenumber", html[1]);
    }

    [Fact]
    public void LineNumbers_ArePaddedToACommonWidth()
    {
        // So the code column starts at the same place on every line rather than stepping right as
        // the file passes 9, 99, 999 lines.
        var many = Enumerable.Repeat("<IDENT>x</IDENT>", 120).ToList();

        var html = CodeViewer.ToHtml(many, null);

        Assert.Contains(">  1<", html[0]);
        Assert.Contains(">120<", html[119]);
    }

    [Fact]
    public void AnEmptyLine_StillRendersSomething()
    {
        // An empty <span> collapses to nothing and the line vanishes, taking the alignment with it.
        var html = OneLine("");

        Assert.Contains("&nbsp;", html);
    }

    [Fact]
    public void LeadingIndentation_SurvivesAsOrdinarySpaces()
    {
        // Modelica is read by its indentation, and it survives because .code-line is styled
        // white-space: pre - not because of anything this method does. Writing this test is what
        // showed the non-breaking-space conversion that used to sit here was dead code: it counted
        // leading spaces on a string that always began with the line-number prefix, so the count was
        // always zero and the comment above it described something that never ran.
        var html = OneLine("        <KEYWORD>equation</KEYWORD>");

        Assert.Contains("        <span class=\"code-keyword\">equation</span>", html);
    }

    [Fact]
    public void NoLines_IsNoHtml()
    {
        Assert.Empty(CodeViewer.ToHtml(null, null));
        Assert.Empty(CodeViewer.ToHtml([], null));
    }

    // ---- misspelled words ---------------------------------------------------------------------

    [Fact]
    public void AMisspelledWordInAComment_IsMarkedUp()
    {
        var html = OneLine("<COMMENT>// teh model</COMMENT>", "teh");

        Assert.Contains("code-misspell", html);
        Assert.Contains("data-word=\"teh\"", html);
    }

    [Fact]
    public void AMisspelledWordInAString_IsMarkedUp()
    {
        var html = OneLine("<STRING>\"teh\"</STRING>", "teh");

        Assert.Contains("code-misspell", html);
    }

    [Fact]
    public void AMisspelledWordInAnIdentifier_IsLeftAlone()
    {
        // Identifiers and keywords are never spell-checked, so marking one up would offer a
        // correction the rest of the app will not make.
        var html = OneLine("<IDENT>teh</IDENT>", "teh");

        Assert.DoesNotContain("code-misspell", html);
    }

    [Fact]
    public void OnlyWholeWordsAreMarkedUp()
    {
        // "teh" inside "tehran" is not the misspelling that was reported.
        var html = OneLine("<COMMENT>// tehran</COMMENT>", "teh");

        Assert.DoesNotContain("code-misspell", html);
    }

    [Fact]
    public void MatchingIsCaseSensitive()
    {
        // The spell checker reports the word as it found it; "Teh" and "teh" are different findings.
        var html = OneLine("<COMMENT>// Teh model</COMMENT>", "teh");

        Assert.DoesNotContain("code-misspell", html);
    }

    [Fact]
    public void AWordNeedingEncoding_IsStillMatchedAndCarriesItsRawFormInTheAttribute()
    {
        // The match runs over the token's raw text (B340), so the word is found as written — and
        // data-word holds that raw form, because that is what gets looked up in the dictionary when
        // the user right-clicks it.
        var html = OneLine("<COMMENT>// don&apos;t</COMMENT>", "don&apos;t");

        Assert.Contains("code-misspell", html);
    }

    [Fact]
    public void NoMisspelledWords_MarksUpNothing()
    {
        Assert.DoesNotContain("code-misspell", OneLine("<COMMENT>// teh model</COMMENT>"));
        Assert.DoesNotContain("code-misspell", Assert.Single(CodeViewer.ToHtml(["<COMMENT>// teh</COMMENT>"], [])));
    }

    // ---- searching never corrupts what is shown (B340) ----------------------------------------

    private static readonly System.Text.RegularExpressions.Regex AnyHtmlTag =
        new("<[^>]*>", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// What the browser would show for a rendered line: each run of text between tags decoded on its
    /// own, as the browser does — so an entity a span was put inside comes out as its pieces rather
    /// than being quietly rejoined by stripping the tags first.
    /// </summary>
    private static string Shown(string html) =>
        string.Concat(AnyHtmlTag.Split(html).Select(System.Net.WebUtility.HtmlDecode)).Replace(' ', ' ');

    private static void AssertBalanced(string html)
    {
        var opens = System.Text.RegularExpressions.Regex.Matches(html, "<span[ >]").Count;
        var closes = System.Text.RegularExpressions.Regex.Matches(html, "</span>").Count;
        Assert.Equal(opens, closes);
    }

    /// <summary>
    /// The first keystroke of a search. Each of these letters is inside an entity the encoder emits
    /// (<c>&amp;quot;</c>, <c>&amp;lt;</c>, <c>&amp;gt;</c>, <c>&amp;amp;</c>), and each of the
    /// words inside the misspelling markup, so matching the encoded HTML split them.
    /// </summary>
    [Theory]
    [InlineData("q")]
    [InlineData("u")]
    [InlineData("o")]
    [InlineData("t")]
    [InlineData("lt")]
    [InlineData("gt")]
    [InlineData("amp")]
    [InlineData(";")]
    [InlineData("&")]
    [InlineData("class")]
    [InlineData("span")]
    [InlineData("data")]
    [InlineData("word")]
    [InlineData("code")]
    public void SearchingNeverChangesTheTextOnScreen(string term)
    {
        var lines = new List<string>
        {
            "<KEYWORD>model</KEYWORD> <IDENT>M</IDENT> <STRING>\"a quoted &amp; <b>teh</b> word\"</STRING>",
            "  <COMMENT>// teh class span data word code &lt;tag&gt;</COMMENT>",
            "  a &lt; b &amp;&amp; c &quot;plain&quot;",
        };
        var before = CodeViewer.ToHtml(lines, ["teh"]);

        var after = CodeViewer.ToHtml(lines, ["teh"], term);

        Assert.Equal(before.Select(Shown), after.Select(Shown));
        Assert.All(after, AssertBalanced);
        Assert.Contains("code-search-match", string.Join("\n", after));
        // The misspelling markup is still the misspelling markup, attribute and all.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(
            string.Join("\n", after), "<span class=\"code-misspell\" data-word=\"teh\">").Count);
    }

    [Fact]
    public void AQuoteIsFoundAsAQuote()
    {
        var html = OneLineSearched("<STRING>\"a\"</STRING>", "\"a");

        Assert.Contains("<span class=\"code-search-match\">&quot;a</span>", html);
        Assert.Equal("\"a\"", Shown(html).Trim()[3..].Trim());
    }

    [Fact]
    public void ALiteralEntityInADocumentationStringIsFoundAsTheUserSeesIt()
    {
        // Modelica documentation is HTML inside a string, so the source really does say `&lt;`, and
        // the viewer shows exactly that. A search for it finds it, and a search for `<` does not.
        var html = OneLineSearched("<STRING>\"a &lt; b\"</STRING>", "&lt;");

        Assert.Contains("<span class=\"code-search-match\">&amp;lt;</span>", html);
        Assert.DoesNotContain("code-search-match", OneLineSearched("<STRING>\"a &lt; b\"</STRING>", "<"));
    }

    [Fact]
    public void AMatchAcrossTheEdgeOfAMisspelledWordIsTintedEitherSideOfIt()
    {
        var html = OneLineSearched("<COMMENT>// teh model</COMMENT>", "eh mo", "teh");

        AssertBalanced(html);
        Assert.Contains("<span class=\"code-misspell\" data-word=\"teh\">t<span class=\"code-search-match\">eh</span></span>"
            + "<span class=\"code-search-match\"> mo</span>del", html);
    }

    [Fact]
    public void TextOutsideAnyTagIsSearchedToo()
    {
        // Highlighting switched off gives lines with no tags at all, encoded by the classifier.
        var html = OneLineSearched("a &lt; b", "<");

        Assert.Contains("<span class=\"code-search-match\">&lt;</span>", html);
        Assert.Equal("a < b", Shown(html)[5..]);
    }

    [Fact]
    public void VisibleTextDecodesOnlyWhatIsOutsideATag()
    {
        Assert.Equal("a < b \"x &lt; y\"", CodeViewer.VisibleText("a &lt; b <STRING>\"x &lt; y\"</STRING>"));
    }

    private static string OneLineSearched(string tagged, string search, params string[] misspelled) =>
        Assert.Single(CodeViewer.ToHtml([tagged], misspelled.Length == 0 ? null : misspelled, search));
}
