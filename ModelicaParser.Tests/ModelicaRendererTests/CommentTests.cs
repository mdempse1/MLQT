using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for C-style comment preservation in ModelicaRenderer.
/// </summary>
public class CommentTests
{
    [Fact]
    public void SingleLineComment_IsPreserved()
    {
        var testModel = @"model Test
  // This is a single-line comment
  Real x;
end Test;";

        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens("within;\n" + testModel);
        var visitor = new ModelicaRenderer(renderForCodeEditor: false, showAnnotations: true, excludeClassDefinitions: false, tokenStream);
        visitor.Visit(parseTree);

        var output = string.Join("\n", visitor.Code);
        Assert.Contains("// This is a single-line comment", output);
    }

    [Fact]
    public void MultiLineComment_IsPreserved()
    {
        var testModel = @"model Test
  /* This is a
     multi-line comment */
  Real x;
end Test;";

        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens("within;\n" + testModel);
        var visitor = new ModelicaRenderer(renderForCodeEditor: false, showAnnotations: true, excludeClassDefinitions: false, tokenStream);
        visitor.Visit(parseTree);

        var output = string.Join("\n", visitor.Code);
        Assert.Contains("/* This is a", output);
        Assert.Contains("multi-line comment */", output);
    }

    [Fact]
    public void CommentBeforeEquation_IsPreserved()
    {
        var testModel = @"model Test
  Real x;
equation
  // Set x to zero
  x = 0;
end Test;";

        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens("within;\n" + testModel);
        var visitor = new ModelicaRenderer(renderForCodeEditor: false, showAnnotations: true, excludeClassDefinitions: false, tokenStream);
        visitor.Visit(parseTree);

        var output = string.Join("\n", visitor.Code);
        Assert.Contains("// Set x to zero", output);
    }
}
