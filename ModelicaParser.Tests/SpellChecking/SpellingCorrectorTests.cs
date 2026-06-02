using ModelicaParser.SpellChecking;

namespace ModelicaParser.Tests.SpellChecking;

public class SpellingCorrectorTests
{
    [Fact]
    public void ReplaceWordInStrings_CorrectsClassDescription()
    {
        var code = "model Foo \"A simple exmaple model\"\nend Foo;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "exmaple", "example");

        Assert.Equal(1, count);
        Assert.Contains("A simple example model", corrected);
        Assert.DoesNotContain("exmaple", corrected);
    }

    [Fact]
    public void ReplaceWordInStrings_CorrectsComponentDescription()
    {
        var code = "model Foo\n  Real x \"the exmaple signal\";\nend Foo;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "exmaple", "example");

        Assert.Equal(1, count);
        Assert.Contains("the example signal", corrected);
    }

    [Fact]
    public void ReplaceWordInStrings_IsCaseSensitive()
    {
        var code = "model Foo \"Exmaple of exmaple usage\"\nend Foo;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "exmaple", "example");

        Assert.Equal(1, count);
        Assert.Contains("Exmaple of example usage", corrected);
    }

    [Fact]
    public void ReplaceWordInStrings_MatchesWholeWordsOnly()
    {
        var code = "model Foo \"exmaples and exmaple\"\nend Foo;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "exmaple", "example");

        Assert.Equal(1, count);
        Assert.Contains("exmaples and example", corrected);
    }

    [Fact]
    public void ReplaceWordInStrings_DoesNotTouchIdentifiers()
    {
        // The misspelled token also appears as a component name; only the description must change.
        var code = "model Foo\n  Real exmaple \"an exmaple\";\nend Foo;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "exmaple", "example");

        Assert.Equal(1, count);
        Assert.Contains("Real exmaple", corrected);   // identifier untouched
        Assert.Contains("\"an example\"", corrected);  // description corrected
    }

    [Fact]
    public void ReplaceWordInStrings_ReplacesAllOccurrences()
    {
        var code = "model Foo \"exmaple\"\n  Real x \"exmaple\";\nend Foo;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "exmaple", "example");

        Assert.Equal(2, count);
        Assert.DoesNotContain("exmaple", corrected);
    }

    [Fact]
    public void ReplaceWordInStrings_DoesNotBreakLinkHref()
    {
        var code = "model Foo\n" +
            "  annotation(Documentation(info=\"<html><p>This exmaple shows " +
            "<a href='modelica://Foo.exmaple'>exmaple link</a>.</p></html>\"));\n" +
            "end Foo;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "exmaple", "example");

        Assert.Contains("This example shows", corrected);          // prose corrected
        Assert.Contains("example link", corrected);                // visible link text corrected
        Assert.Contains("href='modelica://Foo.exmaple'", corrected); // href preserved
        Assert.Equal(2, count);
    }

    [Fact]
    public void ReplaceWordInStrings_DoesNotTouchCodeBlocks()
    {
        var code = "model Foo\n" +
            "  annotation(Documentation(info=\"<html><p>An exmaple:</p>" +
            "<code>exmaple()</code></html>\"));\n" +
            "end Foo;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "exmaple", "example");

        Assert.Contains("An example:", corrected);          // prose corrected
        Assert.Contains("<code>exmaple()</code>", corrected); // code sample preserved
        Assert.Equal(1, count);
    }

    [Fact]
    public void ReplaceWordInStrings_CorrectsRevisionsDocumentation()
    {
        var code = "model Foo\n" +
            "  annotation(Documentation(revisions=\"<html><p>Fixed exmaple bug.</p></html>\"));\n" +
            "end Foo;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "exmaple", "example");

        Assert.Equal(1, count);
        Assert.Contains("Fixed example bug.", corrected);
    }

    [Fact]
    public void ReplaceWordInStrings_ReturnsZeroWhenNoMatch()
    {
        var code = "model Foo \"a correct description\"\nend Foo;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "exmaple", "example");

        Assert.Equal(0, count);
        Assert.Contains("a correct description", corrected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ReplaceWordInStrings_HandlesEmptyInput(string? code)
    {
        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code!, "exmaple", "example");

        Assert.Equal(0, count);
        Assert.Equal(code, corrected);
    }
}
