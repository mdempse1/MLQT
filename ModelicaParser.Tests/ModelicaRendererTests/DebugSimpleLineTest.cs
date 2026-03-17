using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

public class DebugSimpleLineTest
{
    [Fact]
    public void Debug_SimpleLine()
    {
        var testModel = """
        type PWMType = enumeration(
          SVPWM "SpaceVector PWM",
          Intersective "Intersective PWM"
        ) "Enumeration defining the PWM type";
        """;

        var testModelCode = "within;\n" + testModel;

        // Test with renderForCodeEditor = false
        var (parseTree1, tokenStream1) = ModelicaParserHelper.ParseWithTokens(testModelCode);
        var visitor1 = new ModelicaRenderer(renderForCodeEditor: false, showAnnotations: true, excludeClassDefinitions: false, tokenStream1);
        visitor1.Visit(parseTree1);
        var output1 = visitor1.Code.ToList();
        while (output1.Count > 0 && string.IsNullOrEmpty(output1[output1.Count - 1]))
            output1.RemoveAt(output1.Count - 1);
        output1.RemoveAt(0); // Remove "within" line

        // Test with renderForCodeEditor = true
        var (parseTree2, tokenStream2) = ModelicaParserHelper.ParseWithTokens(testModelCode);
        var visitor2 = new ModelicaRenderer(renderForCodeEditor: true, showAnnotations: true, excludeClassDefinitions: false, tokenStream2);
        visitor2.Visit(parseTree2);
        var output2 = visitor2.Code.ToList();
        while (output2.Count > 0 && string.IsNullOrEmpty(output2[output2.Count - 1]))
            output2.RemoveAt(output2.Count - 1);
        output2.RemoveAt(0); // Remove "within" line

        // Write debug output
        var debugPath = Path.Combine(Path.GetTempPath(), "simpleline_debug.txt");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Output WITHOUT renderForCodeEditor: {output1.Count} lines");
        sb.AppendLine("========================================");
        for (int i = 0; i < output1.Count; i++)
            sb.AppendLine($"[{i}]: '{output1[i]}'");
        sb.AppendLine();
        sb.AppendLine($"Output WITH renderForCodeEditor: {output2.Count} lines");
        sb.AppendLine("========================================");
        for (int i = 0; i < output2.Count; i++)
            sb.AppendLine($"[{i}]: '{output2[i]}'");
        sb.AppendLine();
        sb.AppendLine("Line count comparison:");
        sb.AppendLine($"  Without markup: {output1.Count}");
        sb.AppendLine($"  With markup: {output2.Count}");
        sb.AppendLine($"  Match: {output1.Count == output2.Count}");

        File.WriteAllText(debugPath, sb.ToString());
        Console.WriteLine($"Debug output written to: {debugPath}");
        Console.WriteLine($"Line counts - Without markup: {output1.Count}, With markup: {output2.Count}");
    }
}
