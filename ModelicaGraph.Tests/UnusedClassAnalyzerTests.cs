using System.Collections.Generic;
using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

public class UnusedClassAnalyzerTests
{
    // Package P with a public model A and two protected models (Helper, Used); A references Used.
    private const string ParentCode =
        "package P\n  model A end A;\nprotected\n  model Helper end Helper;\n  model Used end Used;\nend P;";

    private static DirectedGraph Build(bool partialHelper = false)
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("P", "P", ParentCode) { ClassType = "package" });
        graph.AddNode(new ModelNode("P.A", "A", "model A end A;") { ClassType = "model", IsNested = true, ParentModelName = "P" });
        graph.AddNode(new ModelNode("P.Helper", "Helper", "model Helper end Helper;")
        { ClassType = "model", IsNested = true, ParentModelName = "P", IsPartial = partialHelper });
        graph.AddNode(new ModelNode("P.Used", "Used", "model Used end Used;") { ClassType = "model", IsNested = true, ParentModelName = "P" });
        graph.AddModelUsesModel("P.A", "P.Used");   // Used is referenced → not dead
        return graph;
    }

    private static List<Finding> Run(DirectedGraph graph, bool depAnalyzed = true)
    {
        var settings = new StyleCheckingSettings { CheckUnusedClass = true };
        var ctx = new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList(), depAnalyzed);
        return GraphAnalysisRunner.Run(ctx, new IGraphAnalyzer[] { new UnusedClassAnalyzer() });
    }

    [Fact]
    public void UnreferencedProtectedClass_IsFlagged()
    {
        var f = Assert.Single(Run(Build()), x => x.RuleId == RuleIds.UnusedClass);
        Assert.Equal("P.Helper", f.ModelId);
    }

    [Fact]
    public void ReferencedProtectedClass_IsNotFlagged()
    {
        Assert.DoesNotContain(Run(Build()), x => x.ModelId == "P.Used");
    }

    [Fact]
    public void PublicClass_IsNotFlagged_EvenIfUnreferenced()
    {
        // A is public and has no usedBy, but public classes may be used by invisible downstream libraries.
        Assert.DoesNotContain(Run(Build()), x => x.ModelId == "P.A");
    }

    [Fact]
    public void PartialProtectedClass_IsNotFlagged()
    {
        // A partial class is meant to be extended, not instantiated — never flagged as unused.
        Assert.DoesNotContain(Run(Build(partialHelper: true)), x => x.ModelId == "P.Helper");
    }

    [Fact]
    public void SkippedEntirely_WhenDependenciesNotAnalysed()
    {
        // Without edges every class looks unreferenced — the analyzer must not run.
        Assert.Empty(Run(Build(), depAnalyzed: false));
    }
}
