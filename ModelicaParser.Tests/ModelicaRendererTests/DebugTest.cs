using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Debug test to see actual output from the visitor
/// </summary>
public class DebugTest
{
    [Fact]
    public void ShowActualOutput_SimpleModel()
    {
        var input = "model Test Real x; end Test;";
        var parseTree = ModelicaParserHelper.Parse(input);
        var visitor = new ModelicaRenderer(renderForCodeEditor: false);
        visitor.Visit(parseTree);

        var output = visitor.Code;

        // Output each line with line numbers for debugging
        for (int i = 0; i < output.Count; i++)
        {
            Console.WriteLine($"[{i}] \"{output[i]}\"");
        }

        // This will always pass - it's just to see the output
        Assert.True(true);
    }

    [Fact]
    public void ShowActualOutput_WithEquation()
    {
        var input = "model Test equation x = 5; end Test;";
        var parseTree = ModelicaParserHelper.Parse(input);
        var visitor = new ModelicaRenderer(renderForCodeEditor: false);
        visitor.Visit(parseTree);

        var output = visitor.Code;

        // Output each line with line numbers for debugging
        for (int i = 0; i < output.Count; i++)
        {
            Console.WriteLine($"[{i}] \"{output[i]}\"");
        }

        Assert.True(true);
    }

    [Fact]
    public void ShowActualOutput_WithMarkup()
    {
        var input = "model Test Real x; end Test;";
        var parseTree = ModelicaParserHelper.Parse(input);
        var visitor = new ModelicaRenderer(renderForCodeEditor: true);
        visitor.Visit(parseTree);

        var output = visitor.Code;

        // Output each line with line numbers for debugging
        for (int i = 0; i < output.Count; i++)
        {
            Console.WriteLine($"[{i}] \"{output[i]}\"");
        }

        Assert.True(true);
    }
}
