using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// Coverage is measured per class and kept on the class. The dashboard asks for several scopes — all
/// libraries, then one sub-package, then each side by side — and measuring means parsing the class
/// and walking its interface every time it is asked.
/// </summary>
public class CoverageFactsCacheTests
{
    private const string Source = """
        model M "a model"
          parameter Real p(unit="m") "a parameter";
          Real x;
        equation
          x = p;
        end M;
        """;

    private static (DirectedGraph graph, ModelNode node) One(string id = "M", string source = Source)
    {
        var graph = new DirectedGraph();
        var node = new ModelNode(id, id, source) { ClassType = "model" };
        graph.AddNode(node);
        return (graph, node);
    }

    [Fact]
    public void MeasuringKeepsTheAnswerOnTheClass()
    {
        var (graph, node) = One();
        Assert.Null(node.Definition.Coverage);

        var facts = new CoverageMeasurer(graph).Measure(node);

        Assert.NotNull(facts);
        Assert.Same(facts, node.Definition.Coverage);
        Assert.True(facts!.HasDescription);
        Assert.Equal(1, facts.ParameterTotal);
        Assert.Equal(1, facts.ParametersWithDescription);
    }

    [Fact]
    public void AClassAlreadyMeasured_IsNotMeasuredAgain()
    {
        var (graph, node) = One();
        var first = new CoverageMeasurer(graph).Measure(node);

        // A different measurer, to be sure the answer comes from the class and not from the measurer.
        var second = new CoverageMeasurer(graph).Measure(node);

        Assert.Same(first, second);
    }

    [Fact]
    public void ReplacingTheSource_DropsTheMeasurement()
    {
        // Otherwise a correction, a reformat or a trim would leave figures describing code that has
        // gone.
        var (graph, node) = One();
        new CoverageMeasurer(graph).Measure(node);
        Assert.NotNull(node.Definition.Coverage);

        node.Definition.ModelicaCode = "model M\n  Real x;\nequation\n  x = 1;\nend M;";

        Assert.Null(node.Definition.Coverage);
        Assert.False(new CoverageMeasurer(graph).Measure(node)!.HasDescription);
    }

    [Fact]
    public void MeasuringDoesNotTakeAParseTreeItsCallerIsUsing()
    {
        // Style checking measures while holding the tree it needed anyway. Releasing it there would
        // cost that caller the re-parse this whole arrangement exists to avoid.
        var (graph, node) = One();
        var tree = node.Definition.EnsureParsed();
        Assert.NotNull(tree);

        new CoverageMeasurer(graph).Measure(node);

        Assert.Same(tree, node.Definition.ParsedCode);
    }

    [Fact]
    public void MeasuringAClassItHadToParse_LeavesNoTreeBehind()
    {
        var (graph, node) = One();
        Assert.Null(node.Definition.ParsedCode);

        new CoverageMeasurer(graph).Measure(node);

        Assert.Null(node.Definition.ParsedCode);
    }

    [Fact]
    public void PreMeasuredClasses_GiveTheSameFiguresAsMeasuringDuringTheReport()
    {
        // The dashboard must not depend on whether style checking got there first.
        var (graph, a) = One("A");
        var b = new ModelNode("B", "B", "model B\n  Real y;\nequation\n  y = 1;\nend B;") { ClassType = "model" };
        graph.AddNode(b);
        var models = new List<ModelNode> { a, b };

        var fresh = MetricsCalculator.Compute(graph, models);

        foreach (var m in models)
            m.Definition.Coverage = null;
        var measurer = new CoverageMeasurer(graph);
        foreach (var m in models)
            measurer.Measure(m);
        var preMeasured = MetricsCalculator.Compute(graph, models);

        Assert.Equal(fresh.TotalComponents, preMeasured.TotalComponents);
        Assert.Equal(
            fresh.Coverage.Select(c => (c.Dimension, c.Compliant, c.Eligible)),
            preMeasured.Coverage.Select(c => (c.Dimension, c.Compliant, c.Eligible)));
    }
}
