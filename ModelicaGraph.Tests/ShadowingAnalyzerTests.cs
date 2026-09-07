using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

public class ShadowingAnalyzerTests
{
    private static ModelNode Model(string id, string code)
        => new(id, id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id, code) { ClassType = "model" };

    private static System.Collections.Generic.List<Finding> Run(params ModelNode[] models)
    {
        var graph = new DirectedGraph();
        foreach (var m in models) graph.AddNode(m);
        var settings = new StyleCheckingSettings { CheckShadowing = true };
        var ctx = new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList());
        return GraphAnalysisRunner.Run(ctx, new IGraphAnalyzer[] { new ShadowingAnalyzer() });
    }

    [Fact]
    public void RedeclaringInheritedMemberName_WithoutRedeclare_IsFlagged()
    {
        var baseC = Model("Base", "model Base\n  Real x;\nend Base;");
        var derived = Model("Derived", "model Derived\n  extends Base;\n  Real x;\nend Derived;");
        var f = Run(baseC, derived);
        var shadow = Assert.Single(f, x => x.RuleId == RuleIds.ShadowingInheritedMember);
        Assert.Equal("Derived", shadow.ModelId);
        Assert.Equal("x", shadow.ElementPath);
    }

    [Fact]
    public void DistinctMemberNames_NotFlagged()
    {
        var baseC = Model("Base", "model Base\n  Real x;\nend Base;");
        var derived = Model("Derived", "model Derived\n  extends Base;\n  Real y;\nend Derived;");
        Assert.DoesNotContain(Run(baseC, derived), x => x.RuleId == RuleIds.ShadowingInheritedMember);
    }

    [Fact]
    public void Redeclare_IsNotFlagged()
    {
        var baseC = Model("Base", "model Base\n  replaceable Real x;\nend Base;");
        var derived = Model("Derived", "model Derived\n  extends Base;\n  redeclare Real x;\nend Derived;");
        Assert.DoesNotContain(Run(baseC, derived), x => x.RuleId == RuleIds.ShadowingInheritedMember);
    }

    [Fact]
    public void UnresolvableBase_IsNotFlagged()
    {
        // Base is not loaded → we can't see its members, so we don't guess a shadow.
        var derived = Model("Derived", "model Derived\n  extends Missing;\n  Real x;\nend Derived;");
        Assert.DoesNotContain(Run(derived), x => x.RuleId == RuleIds.ShadowingInheritedMember);
    }

    [Fact]
    public void NoExtends_NotFlagged()
    {
        Assert.Empty(Run(Model("M", "model M\n  Real x;\nend M;")));
    }
}
