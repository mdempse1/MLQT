using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for ModelicaRendererHelper static methods, covering edge cases not exercised by full renderer tests.
/// </summary>
public class ModelicaRendererHelperTests
{
    [Fact]
    public void HasSingleLineGraphics_NullArgumentList_ReturnsFalse()
    {
        // Covers line 271: return false when argument_list() is null (empty class modification)
        // e.g., annotation(Icon()) where Icon's inner modification is "()" with no arguments
        var code = "within;\nmodel T\n  Real x;\n  annotation(Icon());\nend T;";
        var parseTree = ModelicaParserHelper.Parse(code);

        var composition = parseTree.class_definition(0).class_specifier()
            .long_class_specifier()?.composition();
        var annotation = composition?.annotation(0);
        var outerMod = annotation?.class_modification();
        var iconArg = outerMod?.argument_list()?.argument(0);
        var iconElemMod = iconArg?.element_modification_or_replaceable()?.element_modification();
        var innerMod = iconElemMod?.modification()?.class_modification();

        Assert.NotNull(innerMod);
        // innerMod is "()" - argument_list() returns null
        var result = ModelicaRendererHelper.HasSingleLineGraphics(innerMod);
        Assert.False(result);
    }

    [Fact]
    public void HasSingleLineGraphics_GraphicsWithNonArrayValue_ReturnsFalse()
    {
        // Covers lines 318-325: closing braces when graphics= value is not a {...} array
        // The deep nested if chain falls through to return false at line 329
        var code = "within;\nmodel T\n  Real x;\n  annotation(Icon(graphics=false));\nend T;";
        var parseTree = ModelicaParserHelper.Parse(code);

        var composition = parseTree.class_definition(0).class_specifier()
            .long_class_specifier()?.composition();
        var annotation = composition?.annotation(0);
        var outerMod = annotation?.class_modification();
        var iconArg = outerMod?.argument_list()?.argument(0);
        var iconElemMod = iconArg?.element_modification_or_replaceable()?.element_modification();
        var innerMod = iconElemMod?.modification()?.class_modification();

        Assert.NotNull(innerMod);
        // innerMod has graphics=false; "false" doesn't start with '{', so all nested ifs fall through
        var result = ModelicaRendererHelper.HasSingleLineGraphics(innerMod);
        Assert.False(result);
    }

    [Fact]
    public void HasSingleLineGraphicsInInheritence_GraphicsWithNonArrayValue_ReturnsFalse()
    {
        // Covers lines 421-427: closing braces when graphics= value is not a {...} array
        // in class_or_inheritence_modification context
        var code = "within;\nmodel T\n  extends Base(graphics=false);\n  Real x;\nend T;";
        var parseTree = ModelicaParserHelper.Parse(code);

        var composition = parseTree.class_definition(0).class_specifier()
            .long_class_specifier()?.composition();
        var extendsClause = composition?.element_list(0)?.element(0)?.extends_clause();
        var classOrInheritMod = extendsClause?.class_or_inheritence_modification();

        Assert.NotNull(classOrInheritMod);
        // classOrInheritMod has graphics=false; falls through nested ifs to return false
        var result = ModelicaRendererHelper.HasSingleLineGraphicsInInheritence(classOrInheritMod);
        Assert.False(result);
    }

    [Fact]
    public void HasOnlyIconWithSingleLineGraphicsInInheritence_IconWithValueModification_ReturnsFalse()
    {
        // Covers lines 476, 478: when Icon has a modification that is NOT a class_modification
        // Icon = 1.0 has modification_expression (not class_modification), so class_modification() is null
        var code = "within;\nmodel T\n  extends Base(Icon=1.0);\n  Real x;\nend T;";
        var parseTree = ModelicaParserHelper.Parse(code);

        var composition = parseTree.class_definition(0).class_specifier()
            .long_class_specifier()?.composition();
        var extendsClause = composition?.element_list(0)?.element(0)?.extends_clause();
        var classOrInheritMod = extendsClause?.class_or_inheritence_modification();

        Assert.NotNull(classOrInheritMod);
        // iconElementMod != null (Icon found), but modification.class_modification() == null
        // → falls to closing brace at line 476, then return false at line 478
        var result = ModelicaRendererHelper.HasOnlyIconWithSingleLineGraphicsInInheritence(classOrInheritMod);
        Assert.False(result);
    }

    [Fact]
    public void GenerateCode_FunctionPartialApplicationWithExtraArg_RendersCorrectly()
    {
        // Covers CountFunctionArguments lines 35-37:
        // function_partial_application != null AND function_arguments_non_first != null
        // e.g., f(function g(a = 1.0), 2.0)
        var code = """
model WithFuncPartialApp
  Real x;
equation
  x = f(function g(a = 1.0), 2.0);
end WithFuncPartialApp;
""";
        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(code);
        var renderer = new ModelicaRenderer(false, true, false, tokenStream, null);
        renderer.Visit(parseTree);
        var result = string.Join("\n", renderer.Code);
        Assert.Contains("function g", result);
    }
}
