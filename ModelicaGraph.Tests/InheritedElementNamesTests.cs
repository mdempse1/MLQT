using ModelicaGraph.DataTypes;
using ModelicaParser.SpellChecking;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// The inherited element names the spell checkers use as context words, resolved through the graph.
/// </summary>
public class InheritedElementNamesTests
{
    private static DirectedGraph InheritanceGraph()
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Lib.Base", "Base",
            """
            model Base
              Real wibbler "Top of the chain";
            protected
              Real zorbal "Hidden but still visible to a derived class";
            end Base;
            """));
        graph.AddNode(new ModelNode("Lib.Middle", "Middle",
            """
            model Middle
              extends Lib.Base;
              Real frimbo "Added halfway up";
            end Middle;
            """));
        graph.AddNode(new ModelNode("Lib.Leaf", "Leaf",
            """
            model Leaf "Drives the wibbler and the frimbo"
              extends Lib.Middle;
              Real x "Position of the zorbal";
            end Leaf;
            """));
        return graph;
    }

    private static StyleCheckingSettings SpellCheckSettings() => new()
    {
        SpellCheckDescription = true,
        SpellCheckDocumentation = true,
    };

    [Fact]
    public void InheritedElementNames_CollectsTheWholeChain()
    {
        var callback = StyleChecking.CreateInheritedElementNamesCallback(InheritanceGraph())!;

        var names = callback("Lib.Leaf");

        Assert.Contains("frimbo", names);    // one level up
        Assert.Contains("wibbler", names);   // two levels up
        Assert.Contains("zorbal", names);    // protected, and still in scope of a derived class
        Assert.DoesNotContain("x", names);   // declared by Leaf itself, not inherited
    }

    [Fact]
    public void InheritedElementNames_UnknownClass_IsEmpty()
    {
        var callback = StyleChecking.CreateInheritedElementNamesCallback(InheritanceGraph())!;

        Assert.Empty(callback("Lib.NotHere"));
    }

    [Fact]
    public void InheritedElementNames_RepeatedQueriesAgree()
    {
        // Answered once per class and cached, so a repeat must give the same answer as the first.
        var callback = StyleChecking.CreateInheritedElementNamesCallback(InheritanceGraph())!;

        var first = callback("Lib.Leaf");
        var second = callback("Lib.Leaf");

        Assert.Equal(first.OrderBy(n => n), second.OrderBy(n => n));
    }

    [Fact]
    public void InheritedElementNames_NullGraph_GivesNoCallback()
    {
        Assert.Null(StyleChecking.CreateInheritedElementNamesCallback(null));
    }

    [Fact]
    public void SpellCheck_InheritedNames_AreNotReportedAsMisspellings()
    {
        var graph = InheritanceGraph();
        var leaf = graph.GetNode<ModelNode>("Lib.Leaf")!;

        var findings = StyleChecking.RunStyleCheckingFindings(
            leaf.Definition, SpellCheckSettings(), "Lib.Leaf",
            spellChecker: SpellChecker.Create(),
            inheritedElementNames: StyleChecking.CreateInheritedElementNamesCallback(graph));

        Assert.Empty(findings);
    }

    [Fact]
    public void SpellCheck_WithoutTheChain_ReportsInheritedNames()
    {
        // Guards the fix: the same class, checked with no way to resolve its base classes, reports
        // every inherited name it mentions.
        var graph = InheritanceGraph();
        var leaf = graph.GetNode<ModelNode>("Lib.Leaf")!;

        var findings = StyleChecking.RunStyleCheckingFindings(
            leaf.Definition, SpellCheckSettings(), "Lib.Leaf",
            spellChecker: SpellChecker.Create());

        Assert.Equal(
            ["frimbo", "wibbler", "zorbal"],
            findings.Select(f => f.Discriminator).OrderBy(d => d, StringComparer.Ordinal));
    }
}
