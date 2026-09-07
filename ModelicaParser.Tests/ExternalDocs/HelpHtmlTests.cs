using ModelicaParser.ExternalDocs;
using Xunit;

namespace ModelicaParser.Tests.ExternalDocs;

/// <summary>
/// The tag-stream scanner the generated help is read with.
///
/// <para>It exists because Dymola 2024x Refresh 1 shipped a generator regression that emits a junk
/// token where newlines belong, collapsing whole tables onto one line — so every answer here has to
/// come from the tag structure and never from where a line happens to break. The inputs it is fed
/// are machine-generated, which makes the near-misses the interesting cases: a tag whose name is a
/// prefix of another, an attribute name that ends with the one being read, a base-class list whose
/// descriptions contain commas of their own.</para>
/// </summary>
public class HelpHtmlTests
{
    // ── finding a tag ──

    [Fact]
    public void ATagWhoseNameIsAPrefixOfAnother_IsNotMatched()
    {
        // Looking for <a> must not find <abbr>, or a base class would be read from the wrong element.
        const string html = "<abbr title=\"x\">t</abbr><a href=\"y\">z</a>";

        Assert.Equal(html.IndexOf("<a href", StringComparison.Ordinal), HelpHtml.FindTag(html, 0, "a"));
    }

    [Theory]
    [InlineData("<td>x</td>")]
    [InlineData("<td class=\"c\">x</td>")]
    [InlineData("<td/>")]
    [InlineData("<TD>x</TD>")]
    public void EachWayOfClosingATagName_Counts(string html)
    {
        Assert.Equal(0, HelpHtml.FindTag(html, 0, "td"));
    }

    [Fact]
    public void ATagThatIsNotThere_IsMinusOne()
    {
        Assert.Equal(-1, HelpHtml.FindTag("<table><tr></tr></table>", 0, "img"));
    }

    [Fact]
    public void OnlyPrefixMatches_AreAlsoMinusOne()
    {
        // The scan has to keep going past each near-miss and still come back empty.
        Assert.Equal(-1, HelpHtml.FindTag("<tables><tabled></tabled>", 0, "table"));
    }

    [Fact]
    public void ATagNameRunningToTheEndOfTheText_IsStillFound()
    {
        // Truncated help files exist; the boundary check must not read past the end to reject one.
        Assert.Equal(5, HelpHtml.FindTag("<div><h2", 0, "h2"));
    }

    [Fact]
    public void AnUnterminatedTag_EndsAtTheEndOfTheText()
    {
        const string html = "<img src=\"x.png\"";

        Assert.Equal(html.Length, HelpHtml.EndOfTag(html, 0));
        Assert.Equal(html, HelpHtml.TagTextAt(html, 0));
    }

    // ── reading an attribute ──

    [Fact]
    public void AnAttributeIsReadFromItsOwnTag()
    {
        Assert.Equal("Lib.M", HelpHtml.ReadAttribute("<img src=\"i.png\" alt=\"Lib.M\">", "alt"));
    }

    [Fact]
    public void AnAttributeWhoseNameEndsWithTheOneAskedFor_IsNotIt()
    {
        // data-alt="…" is not alt="…". Reading it would name the image for the wrong class, and the
        // class name is what decides which class the icon belongs to.
        Assert.Null(HelpHtml.ReadAttribute("<img data-alt=\"wrong\">", "alt"));
    }

    [Fact]
    public void TheRealAttributeIsFoundPastANearMiss()
    {
        Assert.Equal("right", HelpHtml.ReadAttribute("<img data-alt=\"wrong\" alt=\"right\">", "alt"));
    }

    [Fact]
    public void AnAbsentAttribute_IsNull()
    {
        Assert.Null(HelpHtml.ReadAttribute("<img src=\"i.png\">", "alt"));
    }

    [Fact]
    public void AnAttributeWhoseValueIsNeverClosed_IsNull()
    {
        Assert.Null(HelpHtml.ReadAttribute("<img alt=\"unclosed", "alt"));
    }

    [Fact]
    public void EntitiesInAnAttributeValue_AreDecoded()
    {
        // A quoted Modelica identifier reaches the help as &#39;, and the decoded form is what the
        // class is actually called.
        Assert.Equal("Lib.'a b'", HelpHtml.ReadAttribute("<a name=\"Lib.&#39;a b&#39;\">", "name"));
    }

    // ── turning markup into text ──

    [Fact]
    public void StrippingTags_LeavesProseWithItsEntitiesDecoded()
    {
        // This text becomes a description string and is spell-checked; an entity that survived would
        // be reported as a misspelling nobody can correct.
        Assert.Equal("Peltier's element ©",
            HelpHtml.StripTags("<p>Peltier&#39;s <b>element</b> &copy;</p>"));
    }

    [Fact]
    public void RunsOfWhitespace_BecomeOneSpace()
    {
        Assert.Equal("a b c", HelpHtml.StripTags("  a\n\t b   <br>c  "));
    }

    [Fact]
    public void TheGeneratorsEmptyCellFiller_ComesOutEmpty()
    {
        // &nbsp; is what an empty table cell holds. Left in, it becomes a description of one
        // invisible character, which reads as "documented" everywhere downstream.
        Assert.Equal(string.Empty, HelpHtml.StripTags("<td>&nbsp;</td>"));
    }

    [Fact]
    public void CollapsingWhitespaceOnTextWithNone_ChangesNothing()
    {
        Assert.Equal("already-tight", HelpHtml.CollapseWhitespace("already-tight"));
    }

    // ── splitting a base-class list ──

    [Fact]
    public void ABaseClassListSplitsOnItsOwnCommas()
    {
        var parts = HelpHtml.SplitTopLevelCommas("A, B, C");

        Assert.Equal(3, parts.Count);
        Assert.Equal("A", parts[0].Trim());
    }

    [Fact]
    public void ACommaInsideADescription_DoesNotTearAnEntryInHalf()
    {
        // "Extends from A (a desc, with comma), B" — splitting on that comma would invent a base
        // class called "with comma)" and lose B's identity.
        var parts = HelpHtml.SplitTopLevelCommas("A (a desc, with comma), B (another)");

        Assert.Equal(2, parts.Count);
        Assert.Contains("with comma", parts[0]);
        Assert.Contains("B", parts[1]);
    }

    [Fact]
    public void ACommaInsideATag_IsNotASeparator()
    {
        var parts = HelpHtml.SplitTopLevelCommas("<a href=\"x.html?a,b\">A</a>, B");

        Assert.Equal(2, parts.Count);
        Assert.Contains("x.html?a,b", parts[0]);
    }

    [Fact]
    public void AnUnbalancedClosingParenthesis_DoesNotBreakTheSplit()
    {
        // Author-written descriptions reach this verbatim, so the depth counter must not go negative
        // and swallow every later comma.
        var parts = HelpHtml.SplitTopLevelCommas("A), B, C");

        Assert.Equal(3, parts.Count);
    }

    [Fact]
    public void ATrailingComma_AddsNoEmptyEntry()
    {
        Assert.Single(HelpHtml.SplitTopLevelCommas("A,"));
    }

    // ── reading a name out of prose ──

    [Fact]
    public void AQualifiedNameIsReadFromTheStartOfTheText()
    {
        Assert.Equal("Modelica.Blocks.Interfaces.SISO",
            HelpHtml.LeadingQualifiedName("Modelica.Blocks.Interfaces.SISO (Single Input Single Output)"));
    }

    [Fact]
    public void LeadingWhitespace_IsSkipped()
    {
        Assert.Equal("Lib.M", HelpHtml.LeadingQualifiedName("   \n Lib.M"));
    }

    [Fact]
    public void ATrailingSeparator_IsNotSwallowed()
    {
        // The generator writes "Extends from Real." — the sentence's full stop is not part of the
        // name, and a name ending in a dot resolves to nothing.
        Assert.Equal("Real", HelpHtml.LeadingQualifiedName("Real."));
    }

    [Fact]
    public void AQuotedIdentifier_IsOneSegment()
    {
        // Modelica permits almost anything inside single quotes, dots and spaces included.
        Assert.Equal("Lib.'a name'.M", HelpHtml.LeadingQualifiedName("Lib.'a name'.M and then prose"));
    }

    [Fact]
    public void AQuotedIdentifierThatIsNeverClosed_StopsAtTheEnd()
    {
        Assert.Equal("Lib.'unclosed", HelpHtml.LeadingQualifiedName("Lib.'unclosed"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("(not a name)")]
    [InlineData("1stThing")]
    public void TextThatDoesNotBeginWithAName_IsNull(string text)
    {
        Assert.Null(HelpHtml.LeadingQualifiedName(text));
    }

    [Fact]
    public void AnUnderscoreStartsAName()
    {
        Assert.Equal("_private.M", HelpHtml.LeadingQualifiedName("_private.M"));
    }
}
