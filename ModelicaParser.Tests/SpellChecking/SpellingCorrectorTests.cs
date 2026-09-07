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

    [Fact]
    public void ReplaceWordInStrings_CorrectsAWordTheSourceHasQuoted()
    {
        // Real case from a library: "- 'ivc' minus 'ivo' must match ...". The tokenizer trims the
        // apostrophes, so the checker reports 'ivc' as the word ivc — and the correction has to be
        // able to find it, or the app reports a misspelling it cannot fix.
        var code = "model M \"Valve timing - 'ivc' minus 'ivo'\"\nend M;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "ivc", "IVC");

        Assert.Equal(1, count);
        Assert.Contains("'IVC' minus 'ivo'", corrected);
    }

    [Fact]
    public void ReplaceWordInStrings_CorrectsAWordWithATrailingUnderscore()
    {
        var code = "model M \"the _ivc_ timing\"\nend M;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "ivc", "IVC");

        Assert.Equal(1, count);
        Assert.Contains("_IVC_", corrected);
    }

    [Fact]
    public void ReplaceWordInStrings_DoesNotCorrectTheStemOfAPossessive()
    {
        // The checker reports "Stodola's" as one word. Correcting "Stodola" — a separate finding —
        // must leave the possessive alone, or a correction silently rewrites text nobody asked about.
        var code = "model M \"Stodola and Stodola's method\"\nend M;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "Stodola", "Stodolla");

        Assert.Equal(1, count);
        Assert.Contains("Stodolla and Stodola's method", corrected);
    }

    [Fact]
    public void ReplaceWordInStrings_CorrectsAPossessiveWhole()
    {
        var code = "model M \"Stodola's method\"\nend M;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "Stodola's", "Stodolla's");

        Assert.Equal(1, count);
        Assert.Contains("Stodolla's method", corrected);
    }

    [Fact]
    public void ReplaceWordInStrings_DoesNotCorrectPartOfAnUnderscoredToken()
    {
        var code = "model M \"the ivc_timing value\"\nend M;";

        var (_, count) = SpellingCorrector.ReplaceWordInStrings(code, "ivc", "IVC");

        Assert.Equal(0, count);
    }

    [Fact]
    public void ReplaceWordInStrings_OnSourceThatCannotBeParsed_ReportsNothingRatherThanThrowing()
    {
        // The Code Review correction runs this against whatever is on disk, including a file someone
        // has left mid-edit. Throwing here reached the renderer, which swallowed it: the menu stayed
        // open, the file was untouched, and nothing said why.
        var broken = "model M \"The postion\"\n  Real x\nequation\n  x = ;\nend";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(broken, "postion", "position");

        Assert.True(count >= 0);              // whatever it manages, it must come back
        Assert.NotNull(corrected);
    }

    // ── the places a description can be written ──

    [Fact]
    public void AShortClassDefinitionsDescription_IsCorrected()
    {
        // `type Angle = Real(unit="rad") "descrption"` has no composition, so its description hangs
        // off the short specifier. Missing it leaves the class reported and uncorrectable.
        var code = "package P\n  type Gain = Real(min = 0) \"A dimensionles gain\";\nend P;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "dimensionles", "dimensionless");

        Assert.Equal(1, count);
        Assert.Contains("A dimensionless gain", corrected);
    }

    [Fact]
    public void ADerivativeClassDefinitionsDescription_IsCorrected()
    {
        var code = "package P\n  function df = der(f, x) \"The derivitive\";\nend P;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "derivitive", "derivative");

        Assert.Equal(1, count);
        Assert.Contains("The derivative", corrected);
    }

    // ── annotations that are not documentation ──

    [Fact]
    public void AnAnnotationWithNoArguments_IsPassedOver()
    {
        var code = "model M \"A postion sensor\"\n  annotation();\nend M;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "postion", "position");

        Assert.Equal(1, count);
        Assert.Contains("A position sensor", corrected);
    }

    [Fact]
    public void TextInsideANonDocumentationAnnotation_IsNotTouched()
    {
        // Only what the spell checker inspects may be rewritten. An Icon's text primitive, a Dialog
        // group name or a __Dymola_ vendor annotation is markup, and silently editing it changes the
        // rendering of the model.
        var code =
            "model M\n  annotation(Icon(graphics = {Text(textString = \"postion\")}), " +
            "Dialog(group = \"postion\"));\nend M;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "postion", "position");

        Assert.Equal(0, count);
        Assert.Contains("textString = \"postion\"", corrected);
    }

    [Fact]
    public void AnEmptyDocumentationAnnotation_IsPassedOver()
    {
        var code = "model M \"A postion sensor\"\n  annotation(Documentation());\nend M;";

        var (_, count) = SpellingCorrector.ReplaceWordInStrings(code, "postion", "position");

        Assert.Equal(1, count);
    }

    [Fact]
    public void ADocumentationFieldThatIsNeitherInfoNorRevisions_IsLeftAlone()
    {
        // figures=, __Dymola_ extensions and the like live beside info; they are not prose the spell
        // checker reads, so they are not prose the corrector may rewrite.
        var code =
            "model M\n  annotation(Documentation(info = \"<html>The postion</html>\", " +
            "figures = {Figure(title = \"postion\")}));\nend M;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "postion", "position");

        Assert.Equal(1, count);
        Assert.Contains("<html>The position</html>", corrected);
        Assert.Contains("title = \"postion\"", corrected);
    }

    [Fact]
    public void RevisionsAreCorrectedAsWellAsInfo()
    {
        var code =
            "model M\n  annotation(Documentation(revisions = \"<html>Fixed the postion</html>\"));\nend M;";

        var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(code, "postion", "position");

        Assert.Equal(1, count);
        Assert.Contains("Fixed the position", corrected);
    }

    // ── keeping the file the shape it was in ──

    [Fact]
    public void ACrlfFile_KeepsItsLineEndings()
    {
        // The corrector works on LF-normalised text. Writing that back to a CRLF file rewrites every
        // line, so a one-word fix shows up in review as the whole file having changed.
        var original = "model M \"A postion sensor\"\r\nend M;\r\n";
        var (corrected, _) = SpellingCorrector.ReplaceWordInStrings(original, "postion", "position");

        var written = SpellingCorrector.MatchFileEnding(original, corrected);

        Assert.Contains("A position sensor", written);
        Assert.DoesNotContain("\n\n", written.Replace("\r\n", "\n").TrimEnd('\n') + "\n");
        Assert.Equal(original.Split("\r\n").Length, written.Split("\r\n").Length);
    }

    [Fact]
    public void AnLfFile_IsLeftWithLfEndings()
    {
        var original = "model M \"A postion sensor\"\nend M;\n";
        var (corrected, _) = SpellingCorrector.ReplaceWordInStrings(original, "postion", "position");

        var written = SpellingCorrector.MatchFileEnding(original, corrected);

        Assert.DoesNotContain("\r", written);
    }

    [Theory]
    [InlineData("model M\nend M;\n")]
    [InlineData("model M\nend M;")]
    [InlineData("model M\nend M;\n\n")]
    public void TheFilesTrailingWhitespace_SurvivesUnchanged(string original)
    {
        var trailing = original[original.TrimEnd(' ', '\t', '\r', '\n').Length..];

        var written = SpellingCorrector.MatchFileEnding(original, original.TrimEnd());

        Assert.EndsWith(trailing, written);
        Assert.Equal(original, written);
    }
}
