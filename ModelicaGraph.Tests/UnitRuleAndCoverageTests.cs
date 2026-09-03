using System.Collections.Generic;
using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// The missing-unit rule resolved against the graph, and its agreement with the Unit coverage
/// dimension. The dimension counted every Real-derived quantity and called an unresolved alias a
/// gap, while the rule reported only plain <c>Real</c> — so the dashboard showed debt no finding led
/// anyone to. Both now ask the same resolver and the dimension's compliance *is* the rule's verdict.
/// </summary>
public class UnitRuleAndCoverageTests
{
    /// <summary>
    /// A library with the four cases: a type that fixes a unit, one that fixes none, a chain of each,
    /// and a connector (Real-derived but not a physical scalar).
    /// </summary>
    private static DirectedGraph Library()
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("U", "U", "package U end U;") { ClassType = "package" });
        Add(graph, "U.Length", "Length", "type Length = Real(unit=\"m\");", "type");
        Add(graph, "U.Height", "Height", "type Height = Length;", "type");
        Add(graph, "U.Fraction", "Fraction", "type Fraction = Real;", "type");
        Add(graph, "U.Ratio", "Ratio", "type Ratio = Fraction;", "type");
        Add(graph, "U.RealInput", "RealInput", "connector RealInput = input Real;", "connector");
        return graph;
    }

    private static void Add(DirectedGraph graph, string id, string name, string code, string classType)
        => graph.AddNode(new ModelNode(id, name, code)
        { ClassType = classType, IsNested = true, ParentModelName = "U" });

    private const string UsingModel = """
        model M "Uses them"
          Real bare;
          Real inline(unit="m");
          Length ell;
          Height h;
          Fraction f;
          Ratio r;
          Integer count;
          RealInput u;
        end M;
        """;

    private static List<Finding> Check(DirectedGraph graph, string code, string modelId)
        => StyleChecking.RunStyleCheckingFindings(
            new ModelDefinition("M", code),
            new StyleCheckingSettings { CheckMissingUnits = true },
            modelId,
            unitLookup: StyleChecking.CreateUnitLookup(graph));

    [Fact]
    public void OnlyTheQuantitiesThatFixNoUnitAnywhere_AreFlagged()
    {
        var graph = Library();

        var flagged = Check(graph, UsingModel, "U.M").Select(f => f.ElementPath).OrderBy(e => e).ToList();

        // bare: nothing anywhere. f and r: aliases of Real that fix nothing, and a chain of one.
        // Left alone: an inline unit, a type that fixes one, a chain that inherits one, a
        // non-quantity, and a connector.
        Assert.Equal(["bare", "f", "r"], flagged);
    }

    [Fact]
    public void WithoutTheGraph_TheRuleFallsBackToPlainRealOnly()
    {
        // What a snippet check can honestly say: the alias types cannot be resolved, so they are not
        // guessed at. Fewer findings, never wrong ones.
        var findings = StyleChecking.RunStyleCheckingFindings(
            new ModelDefinition("M", UsingModel),
            new StyleCheckingSettings { CheckMissingUnits = true },
            "U.M");

        Assert.Equal(["bare"], findings.Select(f => f.ElementPath).ToList());
    }

    [Fact]
    public void TheCoverageDimensionCountsExactlyWhatTheRuleReports()
    {
        // The invariant B5 exists for: a gap on the dashboard is a finding in the report.
        var graph = Library();
        var model = new ModelNode("U.M", "M", UsingModel) { ClassType = "model", ParentModelName = "U" };
        graph.AddNode(model);

        var measurer = new CoverageMeasurer(graph, CoverageDimension.Unit);
        measurer.Measure(model);
        var facts = model.Definition.Coverage!;

        var findings = Check(graph, UsingModel, "U.M");

        Assert.Equal(6, facts.RealTotal);                        // bare, inline, ell, h, f, r
        Assert.Equal(findings.Count, facts.RealTotal - facts.RealWithUnit);
        Assert.Equal(3, facts.RealWithUnit);
    }
}
