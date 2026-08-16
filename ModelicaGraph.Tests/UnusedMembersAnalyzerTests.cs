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
}
