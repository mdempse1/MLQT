using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

public class PackageOrderAnalyzerTests
{
    // A package "P" whose stored code holds one constant 'c'; child classes are separate nodes
    // (mirrors real storage where standalone children are excluded from the package's own code).
    private static DirectedGraph Build(string[]? packageOrder, params string[] childClassNames)
    {
        var graph = new DirectedGraph();
        var pkg = new ModelNode("P", "P", "package P\n  constant Real c = 1;\nend P;")
        {
            ClassType = "package",
            PackageOrder = packageOrder,
            StartLine = 1
        };
        graph.AddNode(pkg);
        foreach (var name in childClassNames)
            graph.AddNode(new ModelNode("P." + name, name, $"model {name} end {name};")
            {
                ClassType = "model",
                ParentModelName = "P"
            });
        return graph;
    }

    private static List<Finding> Analyze(DirectedGraph graph)
    {
        var settings = new StyleCheckingSettings { CheckPackageOrder = true };
        var ctx = new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList());
        return GraphAnalysisRunner.Run(ctx).Where(f => f.RuleId == RuleIds.PackageOrder).ToList();
    }

    [Fact]
    public void StaleEntry_IsFlagged()
    {
        var f = Analyze(Build(new[] { "A", "B", "Ghost" }, "A", "B"));
        var stale = Assert.Single(f, x => x.Discriminator == "stale");
        Assert.Equal("Ghost", stale.ElementPath);
        Assert.Equal("P", stale.ModelId);
    }

    [Fact]
    public void MissingChildClass_IsFlagged()
    {
        var f = Analyze(Build(new[] { "A" }, "A", "B"));
        var missing = Assert.Single(f, x => x.Discriminator == "missing");
        Assert.Equal("B", missing.ElementPath);
    }

    [Fact]
    public void ConstantEntry_IsNotStale()
    {
        // 'c' is a package-level constant (a member, not a class) — a legitimate package.order entry.
        var f = Analyze(Build(new[] { "A", "B", "c" }, "A", "B"));
        Assert.Empty(f);
    }

    [Fact]
    public void ConsistentOrder_NoFindings()
    {
        Assert.Empty(Analyze(Build(new[] { "A", "B" }, "A", "B")));
    }

    [Fact]
    public void NoPackageOrderFile_NoFindings()
    {
        // PackageOrder == null means "no package.order on disk" — nothing to check.
        Assert.Empty(Analyze(Build(null, "A", "B")));
    }

    [Fact]
    public void DisabledByDefault_NoFindings()
    {
        var graph = Build(new[] { "Ghost" }, "A");
        var ctx = new GraphAnalysisContext(graph, new StyleCheckingSettings(), graph.ModelNodes.ToList());
        Assert.Empty(GraphAnalysisRunner.Run(ctx));
    }
}
