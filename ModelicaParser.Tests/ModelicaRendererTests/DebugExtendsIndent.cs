using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

public class DebugExtendsIndent
{
    [Fact]
    public void Debug_ExtendsIndent()
    {
        var code = """
model EquilibriumDrumBoiler
  extends Modelica.Fluid.Interfaces.PartialTwoPort(
    final port_a_exposesState=true,
    final port_b_exposesState=true,
    redeclare replaceable package Medium = Modelica.Media.Water.StandardWater
  );
end EquilibriumDrumBoiler;
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
            var indentLevel = leadingSpaces / 2;
            Console.WriteLine($"Line {i}: [{leadingSpaces} spaces = {indentLevel} levels] {line}");
        }
    }
}
