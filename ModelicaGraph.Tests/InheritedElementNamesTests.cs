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

    // A class's documentation naming its elements in a table cell, bold as vendors often write them.
    private const string Doc =
        "annotation (Documentation(info=\"<html><table><tr><td><strong>JInput</strong></td><td>Inertia</td></tr></table></html>\"))";

    private static List<string?> Flagged(string code)
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Lib.Base", "Base", "model Base\n  parameter Real JInput = 1;\nend Base;"));
        graph.AddNode(new ModelNode("Lib.Data", "Data",
            "record Data\n  parameter Real JInput = 1;\n  Lib.Inner inner;\nprotected\n  parameter Real JHidden = 1;\nend Data;"));
        graph.AddNode(new ModelNode("Lib.Inner", "Inner", "record Inner\n  parameter Real JDeep = 1;\nend Inner;"));
        graph.AddNode(new ModelNode("Lib.HasData", "HasData", "model HasData\n  Lib.Data data;\nend HasData;"));
        var node = new ModelNode("Lib.M", "M", code);
        graph.AddNode(node);
        return StyleChecking.RunStyleCheckingFindings(
                node.Definition, SpellCheckSettings(), "Lib.M",
                spellChecker: SpellChecker.Create(),
                inheritedElementNames: StyleChecking.CreateInheritedElementNamesCallback(graph))
            .Select(f => f.Discriminator).ToList();
    }

    // A short class is its base with modifications, and its documentation and description name the
    // base's elements; its long form had them accepted, and it had them flagged.
    [Theory]
    [InlineData($"model M = Lib.Base(JInput = 2) \"Sets JInput\" {Doc};")]
    [InlineData($"model M\n  extends Lib.Base(JInput = 2);\n  {Doc};\nend M;")]
    [InlineData($"type M = enumeration(JInput \"j\", Other \"o\") \"JInput or Other\" {Doc};")]
    [InlineData($"model M\n  parameter Real JInput = 1;\n  {Doc};\nend M;")]
    // A field of a component's record, which documentation tabulates as the class's own: data.JInput.
    [InlineData($"model M\n  parameter Lib.Data data;\n  {Doc};\nend M;")]
    // And of an inherited component's.
    [InlineData($"model M\n  extends Lib.HasData;\n  {Doc};\nend M;")]
    public void SpellCheck_AClassesOwnElementNames_AreAccepted_WhateverItsForm(string code)
    {
        Assert.Empty(Flagged(code));
    }

    [Theory]
    [InlineData("JDeep")]     // a field of a field: another class's business
    [InlineData("JHidden")]   // a protected field, which nothing outside the record names
    public void SpellCheck_AComponentsDeeperOrProtectedMembers_AreStillReported(string word)
    {
        Assert.Equal([$"documentation info:{word}"],
            Flagged($"model M\n  parameter Lib.Data data;\n  {Doc.Replace("JInput", word)};\nend M;"));
    }

    [Fact]
    public void SpellCheck_AShortClass_StillReportsAWordThatIsNoElement()
    {
        Assert.Equal(["documentation info:JImput"], Flagged($"model M = Lib.Base(JInput = 2) {Doc.Replace("JInput", "JImput")};"));
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
