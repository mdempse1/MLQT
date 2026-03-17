using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using Xunit;

namespace ModelicaParser.Tests.ModelicaRendererTests;

public class DebugLineWrapping
{
    [Fact]
    public void Debug_ComponentWrapping()
    {
        var code = @"
model Test
  parameter Real myParameter = 1.0 ""This is a very long comment that should wrap to a new line when it exceeds the maximum line length"";
end Test;";

        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens("within;\n" + code);
        var visitor = new ModelicaRenderer(false, true, false, tokenStream, null, 100);
        visitor.Visit(parseTree);

        var output = visitor.Code.ToList();
        while (output.Count > 0 && string.IsNullOrEmpty(output[output.Count - 1]))
            output.RemoveAt(output.Count - 1);
        output.RemoveAt(0); // Remove "within" line

        // Print actual output
        System.Console.WriteLine($"\n=== ACTUAL OUTPUT ({output.Count} lines) ===");
        for (int i = 0; i < output.Count; i++)
        {
            System.Console.WriteLine($"{i+1}: |{output[i]}|");
        }

        var expectedOutput = @"
model Test
  parameter Real myParameter = 1.0
    ""This is a very long comment that should wrap to a new line when it exceeds the maximum line length"";
end Test;";

        var expectedLines = expectedOutput.Split('\n');
        System.Console.WriteLine($"\n=== EXPECTED OUTPUT ({expectedLines.Length} lines) ===");
        for (int i = 0; i < expectedLines.Length; i++)
        {
            System.Console.WriteLine($"{i+1}: |{expectedLines[i]}|");
        }
    }
}
