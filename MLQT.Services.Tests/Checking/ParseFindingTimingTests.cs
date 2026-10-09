using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using MLQT.Services.Checking;

namespace MLQT.Services.Tests.Checking;

/// <summary>
/// A check's parse findings are the ones the classes have once the check has read them, not the ones
/// they had before it started (B352, a lead for B166).
/// </summary>
/// <remarks>
/// <para>A class records a parse error lazily when something first parses it on its own - which the
/// per-class pass does - unless its file's load already settled the diagnosis. LibraryCheckSession read
/// the parse findings before that pass, so such an error reached only the <em>next</em> run in the same
/// session: the MCP server's second <c>mlqt_check_library</c> reported one more finding than its first on
/// unchanged code.</para>
///
/// <para><b>Measured before being credited:</b> no library loaded from disk produces such a class. Over
/// MSL (Modelica, ModelicaTest, ModelicaReference, ObsoleteModelica4, ModelicaTestConversion4,
/// ModelicaServices), Buildings and ExternData - some 22,000 classes, package code trimmed as a check
/// does - parsing every class on its own recorded nothing the load had not, because a file that parses
/// as a whole yields classes that parse alone and a file that does not bars every class in it from
/// recording. So this does not explain B166's spread; the class below is built directly, which is the
/// only way found to reach the path. The order is fixed anyway, because a report that depends on what
/// earlier runs happened to parse is the shape B166 is about.</para>
/// </remarks>
public class ParseFindingTimingTests
{
    // Missing semicolon: the class does not parse, and nothing has parsed it yet.
    private static ModelNode Unparsed() => new("Broken", "Broken", "model Broken \"b\"\n  Real x\nend Broken;");

    private static IReadOnlyList<Finding> Check(DirectedGraph graph) =>
        LibraryCheckSession.Check(
            graph, graph.ModelNodes.ToList(), new StyleCheckingSettings { ClassHasDescription = true },
            new CustomDictionaryService(), new DictionaryManagerService());

    [Fact]
    public void AParseErrorRecordedDuringTheCheck_IsInThatChecksReport()
    {
        var graph = new DirectedGraph();
        var node = Unparsed();
        graph.AddNode(node);
        Assert.Empty(node.Definition.ParserErrors);   // the precondition: nothing recorded yet

        var first = Check(graph);

        Assert.NotEmpty(node.Definition.ParserErrors);   // the check did record it...
        Assert.Contains(first, f => f.RuleId == RuleIds.SyntaxError && f.ModelId == "Broken");   // ...and reports it
    }

    [Fact]
    public void ACheckAfterTheCodeWasReplacedInMemory_ReportsTheNewCodesErrorsNotTheOld()
    {
        // B389. Format All sets each class's code to what it wrote without reloading the file. The
        // load had recorded the old file's error and barred the class from recording its own, so the
        // check went on reporting an error the code no longer had, and none it did.
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "A.mo", "model A \"a\"\n  Real x\nend A;");
        var node = graph.GetNode<ModelNode>("A")!;
        var old = Assert.Single(node.Definition.ParserErrors);

        node.Definition.ModelicaCode = "model A \"a\"\n  Real x;\n  Real y\nend A;";
        var findings = Check(graph).Where(f => RuleIds.IsDiagnostic(f.RuleId)).ToList();

        var reported = Assert.Single(findings);
        Assert.Equal(4, reported.LineNumber);   // the new error, below the line the old one was on
        Assert.NotEqual(old.Line, reported.LineNumber);
    }

    [Fact]
    public void TwoChecksOfUnchangedCode_ReportTheSameParseFindings()
    {
        var graph = new DirectedGraph();
        graph.AddNode(Unparsed());

        var first = Check(graph).Count(f => RuleIds.IsDiagnostic(f.RuleId));
        var second = Check(graph).Count(f => RuleIds.IsDiagnostic(f.RuleId));

        Assert.Equal(second, first);
        Assert.True(first > 0);
    }
}
