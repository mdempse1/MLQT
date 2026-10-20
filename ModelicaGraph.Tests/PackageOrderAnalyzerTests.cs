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
    public void DuplicateEntry_IsFlaggedOncePerName_WithItsCount()
    {
        var f = Analyze(Build(new[] { "A", "B", "A", "B", "A" }, "A", "B"));
        var duplicates = f.Where(x => x.Discriminator == "duplicate").OrderBy(x => x.ElementPath).ToList();
        Assert.Equal(new[] { "A", "B" }, duplicates.Select(x => x.ElementPath));
        Assert.All(duplicates, d => Assert.Equal("P", d.ModelId));
        Assert.Contains("'A' 3 times", duplicates[0].Message);
        Assert.Contains("'B' 2 times", duplicates[1].Message);
        Assert.Equal(2, f.Count); // nothing stale or missing besides
    }

    [Fact]
    public void DuplicateEntry_DifferingOnlyByTrailingWhitespaceOrCR_IsADuplicate()
    {
        var f = Analyze(Build(new[] { "A", "B", "A \t", "B\r" }, "A", "B"));
        Assert.Equal(new[] { "A", "B" },
            f.Where(x => x.Discriminator == "duplicate").Select(x => x.ElementPath).OrderBy(x => x));
    }

    [Fact]
    public void DuplicateEntry_IsReportedWhenMatchingDymola()
    {
        var graph = Build(new[] { "A", "A" }, "A");
        var settings = new StyleCheckingSettings { CheckPackageOrder = true, PackageOrderMatchesDymola = true };
        var ctx = new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList());
        var f = GraphAnalysisRunner.Run(ctx).Where(x => x.RuleId == RuleIds.PackageOrder).ToList();
        Assert.Equal("A", Assert.Single(f, x => x.Discriminator == "duplicate").ElementPath);
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
        // NothingEnabled() rather than a fresh object: MLQT.Structure.SingleFilePackage is on by
        // default and would report this fixture, which is about package.order and not about it.
        var ctx = new GraphAnalysisContext(graph, StyleCheckingSettings.NothingEnabled(), graph.ModelNodes.ToList());
        Assert.Empty(GraphAnalysisRunner.Run(ctx));
    }
}
