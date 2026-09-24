using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

public class UnusedMembersAnalyzerTests
{
    private static ModelNode Model(string id, string code, bool partial = false)
        => new(id, id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id, code) { ClassType = "model", IsPartial = partial };

    private static System.Collections.Generic.List<Finding> Run(params ModelNode[] models)
    {
        var graph = new DirectedGraph();
        foreach (var m in models) graph.AddNode(m);
        var settings = new StyleCheckingSettings { CheckUnusedMembers = true };
        var ctx = new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList());
        return GraphAnalysisRunner.Run(ctx, new IGraphAnalyzer[] { new UnusedMembersAnalyzer() });
    }

    [Fact]
    public void UnusedProtectedMember_IsFlagged()
    {
        var m = Model("M", "model M\n  Real y;\nprotected\n  Real helper;\nequation\n  y = 1;\nend M;");
        var f = Assert.Single(Run(m), x => x.RuleId == RuleIds.UnusedMember);
        Assert.Equal("helper", f.ElementPath);
    }

    [Fact]
    public void UsedProtectedMember_IsNotFlagged()
    {
        var m = Model("M", "model M\n  Real y;\nprotected\n  Real helper;\nequation\n  y = helper;\nend M;");
        Assert.DoesNotContain(Run(m), x => x.RuleId == RuleIds.UnusedMember);
    }

    [Fact]
    public void PublicMember_IsNotFlagged()
    {
        // Public members are interface — not flagged even if unused in equations.
        var m = Model("M", "model M\n  Real y;\nequation\n  y = 1;\nend M;");
        Assert.DoesNotContain(Run(m), x => x.RuleId == RuleIds.UnusedMember);
    }

    [Fact]
    public void ProtectedMemberOfExtendedClass_IsNotFlagged()
    {
        // Base is extended, so its protected 'helper' might be used by the subclass → not flagged.
        var baseC = Model("Base", "model Base\nprotected\n  Real helper;\nend Base;");
        var derived = Model("Derived", "model Derived\n  extends Base;\n  Real y;\nequation\n  y = helper;\nend Derived;");
        Assert.DoesNotContain(Run(baseC, derived), x => x.RuleId == RuleIds.UnusedMember && x.ModelId == "Base");
    }

    [Fact]
    public void ClassWithNestedClass_IsSkipped()
    {
        // A nested class could reference the protected member lexically — don't guess.
        var m = Model("M", "model M\nprotected\n  Real helper;\n  model Inner\n    Real z;\n  end Inner;\nend M;");
        Assert.DoesNotContain(Run(m), x => x.RuleId == RuleIds.UnusedMember);
    }

    [Fact]
    public void PartialClass_IsSkipped()
    {
        var m = Model("M", "partial model M\nprotected\n  Real helper;\nend M;", partial: true);
        Assert.DoesNotContain(Run(m), x => x.RuleId == RuleIds.UnusedMember);
    }
    // B281: with dependency analysis done, "is this class extended?" is asked only of the classes that
    // use it, not of every class in the graph. These pin both halves of that.

    private const string BaseCode = "model Base\nprotected\n  Real helper;\nend Base;";
    private const string DerivedCode = "model Derived\n  extends Base;\n  Real y;\nequation\n  y = helper;\nend Derived;";

    private static System.Collections.Generic.List<Finding> RunAnalysed(
        DirectedGraph graph, params ModelNode[] reported)
    {
        var settings = new StyleCheckingSettings { CheckUnusedMembers = true };
        var ctx = new GraphAnalysisContext(graph, settings, reported, dependenciesAnalyzed: true);
        return GraphAnalysisRunner.Run(ctx, new IGraphAnalyzer[] { new UnusedMembersAnalyzer() });
    }

    [Fact]
    public void AnExtenderOutsideTheCheckedSet_IsFoundThroughItsEdge()
    {
        // Only Base is being reported on; Derived is elsewhere in the graph - a test library, a
        // repository that depends on this one. Its edge is what makes it an asker.
        var graph = new DirectedGraph();
        var baseC = Model("Base", BaseCode);
        var derived = Model("Derived", DerivedCode);
        graph.AddNode(baseC);
        graph.AddNode(derived);
        graph.AddModelUsesModel("Derived", "Base");

        Assert.DoesNotContain(RunAnalysed(graph, baseC), x => x.RuleId == RuleIds.UnusedMember);
    }

    [Fact]
    public void WithTheEdgesAnalysed_AClassWithNoEdgeIsNotAsked()
    {
        // What the narrowing trusts, stated as a test: a graph that says its dependencies are analysed
        // is believed, so an extender with no edge is not found and Base's member reads as unused.
        // Dependency analysis records every extends clause as an edge - measured over Claytex with the
        // Dymola library folder, 16,226 of 16,226 - so this is a graph that could not come from a load.
        // It is here because it is the one observable difference between asking the users and asking
        // everyone: an implementation that quietly went back to scanning the whole graph fails it.
        var graph = new DirectedGraph();
        var baseC = Model("Base", BaseCode);
        graph.AddNode(baseC);
        graph.AddNode(Model("Derived", DerivedCode));

        Assert.Contains(RunAnalysed(graph, baseC), x => x.RuleId == RuleIds.UnusedMember && x.ModelId == "Base");
    }

    [Fact]
    public void WithoutTheEdges_TheWholeGraphIsStillAsked()
    {
        // The fallback: no dependency analysis, no edges to go by, so every class is asked as before.
        var graph = new DirectedGraph();
        var baseC = Model("Base", BaseCode);
        graph.AddNode(baseC);
        graph.AddNode(Model("Derived", DerivedCode));
        var settings = new StyleCheckingSettings { CheckUnusedMembers = true };
        var ctx = new GraphAnalysisContext(graph, settings, [baseC], dependenciesAnalyzed: false);

        var findings = GraphAnalysisRunner.Run(ctx, new IGraphAnalyzer[] { new UnusedMembersAnalyzer() });

        Assert.DoesNotContain(findings, x => x.RuleId == RuleIds.UnusedMember);
    }

    [Fact]
    public void FindingsComeBackInTheOrderTheClassesWereGiven()
    {
        // Checked in parallel now; the report must not depend on which thread finished first.
        var models = Enumerable.Range(0, 40)
            .Select(i => Model($"M{i:D2}", $"model M{i:D2}\nprotected\n  Real unused{i};\nend M{i:D2};"))
            .ToArray();

        var graph = new DirectedGraph();
        foreach (var m in models) graph.AddNode(m);

        var order = RunAnalysed(graph, models)
            .Where(x => x.RuleId == RuleIds.UnusedMember).Select(x => x.ModelId).ToList();

        Assert.Equal(models.Select(m => m.Id), order);
    }
}
