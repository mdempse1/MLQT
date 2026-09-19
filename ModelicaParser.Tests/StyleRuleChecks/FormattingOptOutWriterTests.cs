using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;

namespace ModelicaParser.Tests.StyleRuleChecks;

/// <summary>
/// B175 — writing and removing <c>__MLQT(format=false)</c>, the rename-safe way to take a class out
/// of formatting.
///
/// <para>The writing half is a variation on the suppression writer: a bare directive rather than an
/// entry appended to a quoted list. The <b>removing</b> half is new — nothing had ever needed to
/// take a directive back out — and it is where the risk is, because it rewrites a class's source
/// rather than adding to it. So the cases below are mostly about what is left behind.</para>
/// </summary>
public class FormattingOptOutWriterTests
{
    private static string Add(string source, params string[] classPath)
    {
        Assert.True(MlqtSuppressionWriter.TryAddFormattingOptOutToFile(
            source, classPath.Length == 0 ? null : classPath, out var result, out var error), error);
        return result;
    }

    private static string Remove(string source, params string[] classPath)
    {
        Assert.True(MlqtSuppressionWriter.TryRemoveFormattingOptOutFromFile(
            source, classPath.Length == 0 ? null : classPath, out var result, out var error), error);
        return result;
    }

    /// <summary>What the extractor makes of the result — the question that actually matters.</summary>
    private static bool SaysDoNotFormat(string source, string modelId = "M")
    {
        var tree = ModelicaParserHelper.Parse(source);
        Assert.NotNull(tree);
        var extractor = new MlqtSuppressionExtractor();
        extractor.Visit(tree);
        return extractor.Build().PreservesFormatting(modelId);
    }

    [Fact]
    public void AClassWithNoAnnotation_GainsOne()
    {
        var result = Add("model M\n  Real x;\nend M;\n");

        Assert.Contains("__MLQT(format=false)", result);
        Assert.True(SaysDoNotFormat(result));
    }

    [Fact]
    public void TheValueIsWrittenUnquoted()
    {
        // `format="false"` would be a string where the reader expects a boolean. The suppression and
        // spelling directives are quoted lists; this one is not, and the writer has to know that.
        Assert.DoesNotContain("format=\"false\"", Add("model M\nend M;\n"));
    }

    [Fact]
    public void AnExistingMlqtAnnotation_GainsTheArgument()
    {
        var source = "model M\n  Real x;\n  annotation(__MLQT(suppress=\"MLQT.Doc.ClassDescription\"));\nend M;\n";

        var result = Add(source);

        Assert.Contains("format=false", result);
        Assert.Contains("suppress=\"MLQT.Doc.ClassDescription\"", result);
        Assert.True(SaysDoNotFormat(result));
    }

    [Fact]
    public void AnExistingAnnotationWithoutMlqt_KeepsWhatItHad()
    {
        var source = "model M\n  Real x;\n  annotation(Icon(graphics={Line(points={{0,0},{1,1}})}));\nend M;\n";

        var result = Add(source);

        Assert.Contains("Icon(graphics={Line(points={{0,0},{1,1}})})", result);
        Assert.True(SaysDoNotFormat(result));
    }

    [Fact]
    public void AClassThatAlreadySaysIt_IsLeftAlone()
    {
        // Not appended to, unlike a list directive: the class already says what we are about to say.
        var source = "model M\n  annotation(__MLQT(format=false));\nend M;\n";

        Assert.Equal(source, Add(source));
    }

    [Fact]
    public void RemovingTheOnlyDirective_TakesTheAnnotationWithIt()
    {
        var source = "model M\n  Real x;\n  annotation(__MLQT(format=false));\nend M;\n";

        var result = Remove(source);

        Assert.DoesNotContain("annotation", result);
        Assert.DoesNotContain("__MLQT", result);
        Assert.False(SaysDoNotFormat(result));
        Assert.Contains("Real x;", result);
    }

    [Fact]
    public void RemovingLeavesAnyOtherDirectiveInPlace()
    {
        var source = "model M\n  annotation(__MLQT(format=false, suppress=\"MLQT.Doc.ClassDescription\"));\nend M;\n";

        var result = Remove(source);

        Assert.DoesNotContain("format=false", result);
        Assert.Contains("suppress=\"MLQT.Doc.ClassDescription\"", result);
        Assert.False(SaysDoNotFormat(result));
    }

    [Fact]
    public void RemovingLeavesTheRestOfTheAnnotationInPlace()
    {
        var source = "model M\n  annotation(Icon(graphics={Line(points={{0,0},{1,1}})}), __MLQT(format=false));\nend M;\n";

        var result = Remove(source);

        Assert.Contains("Icon(graphics={Line(points={{0,0},{1,1}})})", result);
        Assert.DoesNotContain("__MLQT", result);
        // ...and no comma left dangling where __MLQT was.
        Assert.DoesNotContain(", )", result);
        Assert.NotNull(ModelicaParserHelper.Parse(result));
    }

    [Fact]
    public void RemovingTheFirstOfSeveralAnnotationArguments_LeavesNoLeadingComma()
    {
        var source = "model M\n  annotation(__MLQT(format=false), Icon(graphics={Line(points={{0,0},{1,1}})}));\nend M;\n";

        var result = Remove(source);

        Assert.Contains("Icon(graphics={Line(points={{0,0},{1,1}})})", result);
        Assert.DoesNotContain("(, ", result);
        Assert.NotNull(ModelicaParserHelper.Parse(result));
    }

    [Fact]
    public void RemovingWhatIsNotThere_ChangesNothingAndIsNotAnError()
    {
        var source = "model M\n  annotation(Icon(graphics={Line(points={{0,0},{1,1}})}));\nend M;\n";

        Assert.Equal(source, Remove(source));
    }

    [Fact]
    public void AddAndRemove_RoundTripToTheOriginal()
    {
        // The toggle's real promise: switching it on and off again leaves the file as it was.
        const string Source = "model M\n  Real x;\nend M;\n";

        Assert.Equal(Source, Remove(Add(Source)));
    }

    [Fact]
    public void AddAndRemove_RoundTripWhenTheClassAlreadyHadAnAnnotation()
    {
        const string Source = "model M\n  Real x;\n  annotation(Icon(graphics={Line(points={{0,0},{1,1}})}));\nend M;\n";

        Assert.Equal(Source, Remove(Add(Source)));
    }

    [Fact]
    public void ANestedClass_IsTargetedByPath()
    {
        var source = "package P\n  model Inner\n    Real x;\n  end Inner;\nend P;\n";

        var result = Add(source, "Inner");

        // On Inner, not on P: the outer package must still be formatted.
        var innerStart = result.IndexOf("model Inner", StringComparison.Ordinal);
        Assert.True(result.IndexOf("__MLQT", StringComparison.Ordinal) > innerStart);
    }

    [Fact]
    public void AShortClassDefinition_IsAnnotatedInline()
    {
        var source = "package P\n  type Fraction = Real;\nend P;\n";

        var result = Add(source, "Fraction");

        Assert.Contains("__MLQT(format=false)", result);
        Assert.NotNull(ModelicaParserHelper.Parse(result));
    }

    [Fact]
    public void AShortClassDefinition_RoundTrips()
    {
        // The inline case is where removal could eat the element's own semicolon, which belongs to
        // the declaration rather than to the annotation.
        const string Source = "package P\n  type Fraction = Real;\nend P;\n";

        Assert.Equal(Source, Remove(Add(Source, "Fraction"), "Fraction"));
    }

    [Fact]
    public void CrlfFilesKeepTheirLineEndings()
    {
        var source = "model M\r\n  Real x;\r\nend M;\r\n";

        var added = Add(source);

        Assert.DoesNotContain('\n', added.Replace("\r\n", ""));
        Assert.Equal(source, Remove(added));
    }

    [Fact]
    public void AFileThatDoesNotParse_IsLeftExactlyAsItIs()
    {
        // The parser recovers from most errors rather than returning nothing, so the re-parse in the
        // remover is a backstop against a bad splice and not a syntax check on the input. What this
        // pins down is the outcome that matters either way: a file MLQT cannot make sense of is not
        // rewritten.
        var source = "model M\n  this is not Modelica\nend M;\n";

        MlqtSuppressionWriter.TryRemoveFormattingOptOutFromFile(source, null, out var result, out _);

        Assert.Equal(source, result);
    }
}
