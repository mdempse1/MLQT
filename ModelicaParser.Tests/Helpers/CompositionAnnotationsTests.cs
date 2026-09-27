using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.Helpers;

/// <summary>
/// B446: a class body can carry three annotations - a leading one, the external clause's and a
/// trailing one - and <c>composition.annotation()</c> lists whichever are present in source order,
/// so an index says nothing about which is which. Each reader below once took one by index.
/// </summary>
public class CompositionAnnotationsTests
{
    private const string LeadingAndExternal =
        "function f annotation(Inline=true); input Real x; external \"C\" g(x) annotation(Library=\"lib\"); end f;";

    private const string ExternalWithoutAnnotationAndTrailing =
        "function f input Real x; external \"C\" g(x); annotation(Inline=true); end f;";

    private const string ExternalAnnotationOnly =
        "function f input Real x; external \"C\" g(x) annotation(Library=\"lib\"); end f;";

    private static modelicaParser.CompositionContext Composition(string code)
    {
        var tree = ModelicaParserHelper.Parse(code);
        Assert.NotNull(tree);
        var composition = tree.class_definition()[0].class_specifier().long_class_specifier().composition();
        Assert.NotNull(composition);
        return composition;
    }

    private static string? Text(modelicaParser.AnnotationContext? annotation) => annotation?.class_modification().GetText();

    // ---- the helper ----

    [Fact]
    public void Of_SortsAllThreeByPosition()
    {
        var parts = CompositionAnnotations.Of(Composition(
            "function f annotation(A=1); input Real x; external \"C\" g(x) annotation(B=2); annotation(C=3); end f;"));

        Assert.Equal("(A=1)", Text(parts.Leading));
        Assert.Equal("(B=2)", Text(parts.External));
        Assert.Equal("(C=3)", Text(parts.Trailing));
        Assert.Equal(["(A=1)", "(C=3)"], parts.ClassLevel.Select(Text));
        Assert.Equal("(C=3)", Text(parts.Class));
    }

    [Fact]
    public void Of_LeadingAndExternal()
    {
        var parts = CompositionAnnotations.Of(Composition(LeadingAndExternal));

        Assert.Equal("(Inline=true)", Text(parts.Leading));
        Assert.Equal("(Library=\"lib\")", Text(parts.External));
        Assert.Null(parts.Trailing);
        Assert.Equal("(Inline=true)", Text(parts.Class));
        Assert.Equal(["(Inline=true)"], parts.ClassLevel.Select(Text));
    }

    [Fact]
    public void Of_ExternalWithoutAnnotation_TrailingIsTheClasses()
    {
        var parts = CompositionAnnotations.Of(Composition(ExternalWithoutAnnotationAndTrailing));

        Assert.Null(parts.Leading);
        Assert.Null(parts.External);
        Assert.Equal("(Inline=true)", Text(parts.Trailing));
    }

    [Fact]
    public void Of_ExternalAnnotationOnly_NoClassAnnotation()
    {
        var parts = CompositionAnnotations.Of(Composition(ExternalAnnotationOnly));

        Assert.Equal("(Library=\"lib\")", Text(parts.External));
        Assert.Null(parts.Class);
        Assert.Empty(parts.ClassLevel);
    }

    [Fact]
    public void Of_NoAnnotations_AndNull()
    {
        Assert.Equal(default, CompositionAnnotations.Of(Composition("model M Real x; end M;")));
        Assert.Equal(default, CompositionAnnotations.Of(null));
        Assert.Empty(CompositionAnnotations.ClassLevel(null));
    }

    [Fact]
    public void IsExternal_OnlyForTheClausesAnnotation()
    {
        var parts = CompositionAnnotations.Of(Composition(
            "function f annotation(A=1); input Real x; external \"C\" g(x) annotation(B=2); annotation(C=3); end f;"));

        Assert.True(CompositionAnnotations.IsExternal(parts.External!));
        Assert.False(CompositionAnnotations.IsExternal(parts.Leading!));
        Assert.False(CompositionAnnotations.IsExternal(parts.Trailing!));

        // An annotation that is not a class body's at all.
        var component = ModelicaParserHelper.Parse("model M Real x annotation(A=1); end M;")!
            .class_definition()[0].class_specifier().long_class_specifier().composition()
            .element_list()[0].element()[0].component_clause().component_list().component_declaration()[0]
            .comment().annotation();
        Assert.False(CompositionAnnotations.IsExternal(component));
    }

    // ---- the readers that indexed ----

    [Fact]
    public void ExternalResourceExtractor_ReadsTheClausesLibrary_NotALeadingAnnotation()
    {
        var extractor = new ExternalResourceExtractor();
        extractor.Visit(ModelicaParserHelper.Parse(LeadingAndExternal));

        var library = Assert.Single(extractor.Resources);
        Assert.Equal(ResourceReferenceType.ExternalLibrary, library.ReferenceType);
        Assert.Equal("lib", library.RawPath);
    }

    [Fact]
    public void ExternalResourceExtractor_DoesNotReadTheClassAnnotation_AsTheClauses()
    {
        var extractor = new ExternalResourceExtractor();
        extractor.Visit(ModelicaParserHelper.Parse(
            "function f input Real x; external \"C\" g(x); annotation(Library=\"notLinked\"); end f;"));

        Assert.Empty(extractor.Resources);
    }

    [Fact]
    public void ModelExtractor_ReadsExperimentFromTheLeadingAnnotation_NotTheClauses()
    {
        var model = Assert.Single(ModelicaParserHelper.ExtractModels(
            "function f annotation(experiment(StopTime=1)); input Real x; external \"C\" g(x) annotation(Library=\"lib\"); end f;"));

        Assert.True(model.HasExperimentAnnotation);
    }

    [Fact]
    public void ModelExtractor_DoesNotReadTheClausesAnnotation_AsTheClasses()
    {
        var model = Assert.Single(ModelicaParserHelper.ExtractModels(
            "function f input Real x; external \"C\" g(x) annotation(experiment(StopTime=1)); end f;"));

        Assert.False(model.HasExperimentAnnotation);
    }

    [Fact]
    public void SuppressionWriter_AddsAClassAnnotation_RatherThanWritingIntoTheClauses()
    {
        Assert.True(MlqtSuppressionWriter.TryAddSuppression(
            ExternalAnnotationOnly, component: null, "MLQT.Test.Rule", reason: null, out var result, out var error), error);

        var parts = CompositionAnnotations.Of(Composition(result));
        Assert.Equal("(Library=\"lib\")", Text(parts.External));
        Assert.Contains("__MLQT", Text(parts.Class));
    }

    [Fact]
    public void SuppressionWriter_WritesIntoTheLeadingAnnotation_RatherThanTheClauses()
    {
        Assert.True(MlqtSuppressionWriter.TryAddSuppression(
            LeadingAndExternal, component: null, "MLQT.Test.Rule", reason: null, out var result, out var error), error);

        var parts = CompositionAnnotations.Of(Composition(result));
        Assert.Equal("(Library=\"lib\")", Text(parts.External));
        Assert.Contains("__MLQT", Text(parts.Leading));
    }

    [Fact]
    public void ClassBodyLocator_InsertsBeforeTheExternalClause()
    {
        // No public element to append after, so the boundary decides - and inside the clause, or
        // after it, the result does not parse.
        const string code = "function f\nexternal \"C\" g() annotation(Library=\"lib\");\nend f;";
        var layout = ClassBodyLocator.Analyze(code);

        var edited = code.Insert(layout.PublicAppendOffset, "  input Real x;\n");

        Assert.Equal("function f\n  input Real x;\nexternal \"C\" g() annotation(Library=\"lib\");\nend f;", edited);
        Assert.Equal(layout.PublicAppendOffset, layout.BodyEndOffset);
    }

    [Fact]
    public void ClassBodyLocator_WithoutExternalClause_StillStopsAtTheTrailingAnnotation()
    {
        const string code = "model M\n  Real x;\nannotation(Icon());\nend M;";
        var layout = ClassBodyLocator.Analyze(code);

        Assert.Equal(code.IndexOf("annotation", StringComparison.Ordinal), layout.BodyEndOffset);
    }

    // ---- the readers that took every annotation as the class's ----

    [Fact]
    public void CheckClassAnnotations_DocumentationOnTheClause_IsNotTheClasses()
    {
        var visitor = new CheckClassAnnotations(checkDocumentationInfo: true, checkDocumentationRevisions: false, checkIcon: false);
        visitor.Visit(ModelicaParserHelper.Parse(
            "function f input Real x; external \"C\" g(x) annotation(Library=\"lib\", Documentation(info=\"i\")); end f;"));

        Assert.Single(visitor.RuleFindings);
    }

    [Fact]
    public void CheckClassAnnotations_DocumentationInTheLeadingAnnotation_Counts()
    {
        var visitor = new CheckClassAnnotations(checkDocumentationInfo: true, checkDocumentationRevisions: false, checkIcon: false);
        visitor.Visit(ModelicaParserHelper.Parse(
            "function f annotation(Documentation(info=\"i\")); input Real x; external \"C\" g(x) annotation(Library=\"lib\"); end f;"));

        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void SuppressionExtractor_IgnoresADirectiveOnTheClause()
    {
        var extractor = new MlqtSuppressionExtractor();
        extractor.Visit(ModelicaParserHelper.Parse(
            "function f input Real x; external \"C\" g(x) annotation(Library=\"lib\", __MLQT(format=false)); end f;"));

        Assert.False(extractor.Build().PreservesFormatting("f"));
    }

    [Fact]
    public void DocumentationExtractor_IgnoresDocumentationOnTheClause()
    {
        var (info, _) = DocumentationExtractor.ExtractFromCode(
            "function f input Real x; external \"C\" g(x) annotation(Documentation(info=\"i\")); end f;");

        Assert.Null(info);
    }

    [Fact]
    public void ExperimentAnnotationChecker_IgnoresAnExperimentOnTheClause()
    {
        Assert.False(ExperimentAnnotationChecker.Check(
            "function f input Real x; external \"C\" g(x) annotation(experiment(StopTime=1)); end f;").HasExperimentAnnotation);
        Assert.True(ExperimentAnnotationChecker.Check(
            "function f annotation(experiment(StopTime=1)); input Real x; external \"C\" g(x) annotation(Library=\"l\"); end f;").HasExperimentAnnotation);
    }

    [Fact]
    public void TokenClassifier_ColoursTheClausesAnnotation_AsTheRendererDoes()
    {
        // The graphics array is coloured as such only inside a class annotation; the renderer
        // writes the clause's as the clause's, so the classifier must not treat it as the class's.
        // A call nested deep in a class annotation's graphics is coloured as a function (B254's
        // graphics levels); anywhere else in an annotation it is not.
        const string graphics = "annotation(Icon(graphics={Rectangle(lineColor=DynamicSelect({0,0,0}, c))}))";
        var onClause = string.Join("\n", ModelicaTokenClassifier.Highlight(
            $"function f input Real x; external \"C\" g(x) {graphics}; end f;"));
        var onComponent = string.Join("\n", ModelicaTokenClassifier.Highlight(
            $"function f input Real x {graphics}; end f;"));

        Assert.Equal(Slice(onComponent), Slice(onClause));

        static string Slice(string text)
        {
            var start = text.IndexOf("graphics", StringComparison.Ordinal);
            return text[start..text.IndexOf(";", start, StringComparison.Ordinal)];
        }
    }
}
