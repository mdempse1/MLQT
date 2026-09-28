using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// B252 — the half of <see cref="DeclarationKind"/> the grammar cannot answer: whether a declared
/// type is a quantity or a structured class. A Modelica <c>type</c> is exactly the restricted class
/// for a derived simple type, so the question is what the name resolves to and what kind of class
/// that is.
/// </summary>
public class SimpleTypeLookupTests
{
    private static DirectedGraph Library()
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Lib", "Lib", "package Lib end Lib;") { ClassType = "package" });
        graph.AddNode(new ModelNode("Lib.SI", "SI", "package SI end SI;")
        { ClassType = "package", ParentModelName = "Lib" });
        graph.AddNode(new ModelNode("Lib.SI.Length", "Length", "type Length = Real(unit=\"m\");")
        { ClassType = "type", ParentModelName = "Lib.SI" });
        graph.AddNode(new ModelNode("Lib.Resistor", "Resistor", "model Resistor end Resistor;")
        { ClassType = "model", ParentModelName = "Lib" });
        graph.AddNode(new ModelNode("Lib.Data", "Data", "record Data end Data;")
        { ClassType = "record", ParentModelName = "Lib" });
        graph.AddNode(new ModelNode("Lib.M", "M", "model M end M;")
        { ClassType = "model", ParentModelName = "Lib" });
        return graph;
    }

    [Fact]
    public void ADerivedTypeIsSimple_AndAStructuredClassIsNot()
    {
        var lookup = StyleChecking.CreateSimpleTypeLookup(Library())!;

        Assert.True(lookup("Lib.M", "SI.Length"));
        Assert.False(lookup("Lib.M", "Resistor"));
        Assert.False(lookup("Lib.M", "Data"));
    }

    [Fact]
    public void ThePredefinedTypesAreSimple_WithoutResolvingAnything()
    {
        var lookup = StyleChecking.CreateSimpleTypeLookup(Library())!;

        Assert.True(lookup("Lib.M", "Real"));
        Assert.True(lookup("Lib.M", "Integer"));
        Assert.True(lookup("Lib.M", "Boolean"));
        Assert.True(lookup("Lib.M", "String"));
    }

    [Fact]
    public void ATypeThatResolvesToNothing_IsNotSimple()
    {
        // The same answer a caller with no graph gets, so a library that will not load does not have
        // its declarations rearranged on a guess.
        var lookup = StyleChecking.CreateSimpleTypeLookup(Library())!;

        Assert.False(lookup("Lib.M", "Somebody.Elses.Type"));
    }

    [Fact]
    public void WithoutAGraph_ThereIsNoLookup()
        => Assert.Null(StyleChecking.CreateSimpleTypeLookup(null));

    [Fact]
    public void TheRuleRunsThroughTheCheckingPipeline()
    {
        // End to end: the settings switch, the dispatch and the lookup, over a class whose ordering
        // only the graph can judge.
        var graph = Library();
        var settings = new StyleCheckingSettings
        {
            OneOfEachSection = true,
            ImportStatementsFirst = true,
            ComponentsBeforeClasses = true,
            DeclarationOrder = true
        };

        var definition = new ModelDefinition("M", """
            model M
              Resistor r;
              SI.Length len;
            end M;
            """);

        var findings = StyleChecking.RunStyleCheckingFindings(
            definition, settings, "Lib.M",
            isSimpleType: StyleChecking.CreateSimpleTypeLookup(graph));

        var finding = Assert.Single(findings, f => f.RuleId == RuleIds.DeclarationOrder);
        Assert.Contains("Variable 'len'", finding.Message);
    }

    [Fact]
    public void TheRuleIsSilentWhenTheSwitchIsOff()
    {
        var settings = new StyleCheckingSettings
        {
            OneOfEachSection = true,
            ImportStatementsFirst = true,
            ComponentsBeforeClasses = true,
            DeclarationOrder = false
        };

        var definition = new ModelDefinition("M", """
            model M
              Real x;
              parameter Real m;
            end M;
            """);

        var findings = StyleChecking.RunStyleCheckingFindings(definition, settings, "Lib.M");

        Assert.DoesNotContain(findings, f => f.RuleId == RuleIds.DeclarationOrder);
    }
}
