using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

public class DebugConstrainedBy
{
    [Fact]
    public void Debug_ConstrainedBy()
    {
        var code = """
model Test
  extends Base(
    final port_a_exposesState=true,
    final port_b_exposesState=true,
    redeclare replaceable package Medium = Modelica.Media.Water.StandardWater
      constrainedby Modelica.Media.Interfaces.PartialTwoPhaseMedium
  );
end Test;
""";

        var testModelCode = "within;\n" + code;
        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(testModelCode);
        var visitor = new ModelicaRenderer(
            renderForCodeEditor: false,
            showAnnotations: true,
            excludeClassDefinitions: false,
            tokenStream,
            classNamesToExclude: null,
            maxLineLength: 100);
        visitor.Visit(parseTree);

        var actualOutput = visitor.Code.ToList();

        // Print each line with its length and leading spaces count
        for (int i = 0; i < actualOutput.Count; i++)
        {
            var line = actualOutput[i];
            var leadingSpaces = line.Length - line.TrimStart().Length;
            Console.WriteLine($"Line {i}: [{leadingSpaces} spaces] {line}");
        }

        // Check the constrainedby line
        var constrainedByLine = actualOutput.FirstOrDefault(l => l.Contains("constrainedby"));
        Assert.NotNull(constrainedByLine);
        var spaces = constrainedByLine.Length - constrainedByLine.TrimStart().Length;
        Assert.Equal(6, spaces); // Should have 6 spaces (4 base + 2 continuation)
    }
}
