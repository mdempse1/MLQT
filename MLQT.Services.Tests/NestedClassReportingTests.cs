using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.StyleRules;
using MLQT.Services.Checking;

namespace MLQT.Services.Tests;

/// <summary>
/// A check reports about the class it was given. A nested class carrying <c>replaceable</c> is walked
/// by its parent's visitors — deliberately, so a check with no graph behind it sees it at all — but it
/// also has a node of its own and is checked in its own right, so the parent's pass was a second copy
/// of every finding in it.
/// </summary>
public class NestedClassReportingTests
{
    private const string OuterCode = """
        model Outer "Has a replaceable nested class"
          Real top;
          replaceable model Inner
            Real deep;
          end Inner;
        end Outer;
        """;

    private const string InnerCode = """
        model Inner
          Real deep;
        end Inner;
        """;

    private static StyleCheckingSettings UnitRule => new() { CheckMissingUnits = true };

    private static (DirectedGraph Graph, ModelNode Outer, ModelNode Inner) Library()
    {
        var graph = new DirectedGraph();
        var file = new FileNode("f1", @"C:\lib\Outer.mo");
        graph.AddNode(file);

        var outer = new ModelNode("P.Outer", "Outer", OuterCode) { ClassType = "model", StartLine = 1 };
        var inner = new ModelNode("P.Outer.Inner", "Inner", InnerCode)
        {
            ClassType = "model",
            IsNested = true,
            ParentModelName = "P.Outer",
            CanBeStoredStandalone = false,   // it carries `replaceable`
            ElementPrefix = "replaceable",
            StartLine = 3
        };
        graph.AddNode(outer);
        graph.AddNode(inner);
        graph.AddFileContainsModel("f1", outer.Id);
        graph.AddFileContainsModel("f1", inner.Id);
        return (graph, outer, inner);
    }

    [Fact]
    public void TheParentDoesNotReportTheNestedClassesFindings()
    {
        var (graph, outer, _) = Library();
        var context = StyleCheckContext.Build(UnitRule, graph, spellChecker: null);

        var findings = StyleCheckRunner.RunFindings(outer, UnitRule, context);

        // `top` is Outer's own; `deep` belongs to Inner, which is checked separately.
        Assert.Equal(["top"], findings.Select(f => f.ElementPath).ToList());
        Assert.All(findings, f => Assert.Equal("P.Outer", f.ModelId));
    }

    [Fact]
    public void TheNestedClassReportsItsOwnFindings()
    {
        var (graph, _, inner) = Library();
        var context = StyleCheckContext.Build(UnitRule, graph, spellChecker: null);

        var findings = StyleCheckRunner.RunFindings(inner, UnitRule, context);

        var finding = Assert.Single(findings);
        Assert.Equal("deep", finding.ElementPath);
        Assert.Equal("P.Outer.Inner", finding.ModelId);
        // Line 2 of Inner's own source, which is what maps to the right line of the file.
        Assert.Equal(2, finding.LineNumber);
    }

    [Fact]
    public void TogetherTheyReportEachElementExactlyOnce()
    {
        var (graph, outer, inner) = Library();
        var context = StyleCheckContext.Build(UnitRule, graph, spellChecker: null);

        var all = StyleCheckRunner.RunFindings(outer, UnitRule, context)
            .Concat(StyleCheckRunner.RunFindings(inner, UnitRule, context))
            .ToList();

        Assert.Equal(2, all.Count);
        Assert.Equal(2, all.Select(f => f.Fingerprint).Distinct().Count());
    }

    [Fact]
    public void ANestedClassWithNoNodeOfItsOwn_IsStillReportedByItsParent()
    {
        // The guard against losing anything: with nothing else to report it, the parent's pass is
        // all there is. A snippet check is this case, and it keeps everything it found.
        var graph = new DirectedGraph();
        var file = new FileNode("f1", @"C:\lib\Outer.mo");
        graph.AddNode(file);
        var outer = new ModelNode("P.Outer", "Outer", OuterCode) { ClassType = "model", StartLine = 1 };
        graph.AddNode(outer);
        graph.AddFileContainsModel("f1", outer.Id);

        var context = StyleCheckContext.Build(UnitRule, graph, spellChecker: null);

        var findings = StyleCheckRunner.RunFindings(outer, UnitRule, context);

        Assert.Equal(["top", "deep"], findings.Select(f => f.ElementPath).ToList());
    }

    [Fact]
    public void TheLogMessageProjectionDropsTheDuplicatesToo()
    {
        // The GUI and MCP read this path. It used to return the raw findings, so the de-duplication
        // would have applied to the CLI alone.
        var (graph, outer, _) = Library();
        var context = StyleCheckContext.Build(UnitRule, graph, spellChecker: null);

        var messages = StyleCheckRunner.Run(outer, UnitRule, context);

        Assert.All(messages, m => Assert.Equal("P.Outer", m.ModelName));
        Assert.Single(messages);
    }
}
