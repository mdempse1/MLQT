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

    [Fact]
    public void ReplaceWordInStrings_CorrectsAWholeFileIncludingNestedClasses()
    {
        // The Code Review correction reads the .mo file from disk and rewrites it, so the input is a
        // complete file — a within clause, a package, and the classes stored inline in it. Working
        // from a class's stored source instead would miss these: style checking trims inline
        // standalone children out of a package's stored code, and rewriting the file from what was
        // left would delete them.
        var file = @"within Lib;
package Sources ""Signal sources""
  model Step ""Genarate a step signal""
    parameter Real height = 1 ""Height of the genarate step"";
  end Step;

  model Ramp ""Genarate a ramp signal""
  end Ramp;
end Sources;
";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(file, "Genarate", "Generate");

        Assert.Equal(2, count);
        Assert.Contains("within Lib;", corrected);
        Assert.Contains(@"model Step ""Generate a step signal""", corrected);
        Assert.Contains(@"model Ramp ""Generate a ramp signal""", corrected);
        // Lower-case "genarate" is a different word to a case-sensitive replace, and is left alone.
        Assert.Contains("Height of the genarate step", corrected);
    }
}
