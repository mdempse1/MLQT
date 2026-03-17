using ModelicaParser;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

var testModel = """
type NewType = Real(
  quantity="Something",
  unit="m/s",
  displayUnit="km/h"
)
  "A new type defined as Real";
""";

string testModelCode = "within;\n" + testModel;
var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(testModelCode);
var visitor = new ModelicaRenderer(false, true, false, tokenStream, null, 100);
visitor.Visit(parseTree);

var actualOutput = visitor.Code.ToList();
while (actualOutput.Count > 0 && string.IsNullOrEmpty(actualOutput[actualOutput.Count - 1]))
    actualOutput.RemoveAt(actualOutput.Count - 1);
actualOutput.RemoveAt(0); // Remove "within" line

Console.WriteLine($"Line count: {actualOutput.Count}");
for (int i = 0; i < actualOutput.Count; i++)
{
    Console.WriteLine($"Line {i+1}: '{actualOutput[i]}'");
}
