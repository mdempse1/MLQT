using System.Collections.Generic;
using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

public class UsesHygieneAnalyzerTests
{
    private static ModelNode Pkg(string id, Dictionary<string, string>? uses = null)
        => new(id, id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id, $"package {id} end {id};")
        { ClassType = "package", Uses = uses };

    private static ModelNode Model(string id, string parent)
        => new(id, id[(id.LastIndexOf('.') + 1)..], "model x end x;") { ClassType = "model", ParentModelName = parent };

    private static List<Finding> Run(DirectedGraph graph, bool depAnalyzed = true)
    {
        var settings = new StyleCheckingSettings { CheckUsesUndeclared = true, CheckUsesDeclaredUnused = true };
        var ctx = new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList(), depAnalyzed);
        return GraphAnalysisRunner.Run(ctx, new IGraphAnalyzer[] { new UsesHygieneAnalyzer() });
    }

    // MyLib.M references Modelica.Blocks; Modelica and (optionally) Foo are loaded.
    private static DirectedGraph BuildLib(Dictionary<string, string>? uses, bool loadFoo = false)
    {
        var graph = new DirectedGraph();
        graph.AddNode(Pkg("MyLib", uses));
        graph.AddNode(Model("MyLib.M", "MyLib"));
        graph.AddNode(Pkg("Modelica"));
        graph.AddNode(Model("Modelica.Blocks", "Modelica"));
        if (loadFoo) graph.AddNode(Pkg("Foo"));
        graph.AddModelUsesModel("MyLib.M", "Modelica.Blocks");
        return graph;
    }

    [Fact]
    public void ReferencedButUndeclared_IsFlagged()
    {
        var f = Run(BuildLib(uses: null));   // no uses() at all
        var undeclared = Assert.Single(f, x => x.RuleId == RuleIds.UsesUndeclared);
        Assert.Equal("Modelica", undeclared.ElementPath);
        Assert.Equal("MyLib", undeclared.ModelId);
    }

    [Fact]
    public void DeclaredAndReferenced_NoFinding()
    {
        var f = Run(BuildLib(uses: new() { ["Modelica"] = "4.0.0" }));
        Assert.Empty(f);
    }

    [Fact]
    public void DeclaredButUnused_LoadedLibrary_IsFlagged()
    {
        var f = Run(BuildLib(uses: new() { ["Modelica"] = "4.0.0", ["Foo"] = "1.0" }, loadFoo: true));
        var unused = Assert.Single(f, x => x.RuleId == RuleIds.UsesDeclaredUnused);
        Assert.Equal("Foo", unused.ElementPath);
    }

    [Fact]
    public void DeclaredButUnused_NotLoaded_IsNotFlagged()
    {
        // 'Ghost' is declared but not loaded — we can't tell if it's used, so we don't flag it.
        var f = Run(BuildLib(uses: new() { ["Modelica"] = "4.0.0", ["Ghost"] = "1.0" }));
        Assert.DoesNotContain(f, x => x.RuleId == RuleIds.UsesDeclaredUnused);
    }

    [Fact]
    public void OwnLibraryReferences_AreNotCountedAsExternal()
    {
        // MyLib.M referencing another MyLib model must not appear as an external dependency.
        var graph = new DirectedGraph();
        graph.AddNode(Pkg("MyLib", uses: new() { ["MyLib"] = "1.0" }));
        graph.AddNode(Model("MyLib.M", "MyLib"));
        graph.AddNode(Model("MyLib.N", "MyLib"));
        graph.AddModelUsesModel("MyLib.M", "MyLib.N");
        // self-declared 'MyLib' in uses is unusual but the point: no undeclared finding for self-refs.
        Assert.DoesNotContain(Run(graph), x => x.RuleId == RuleIds.UsesUndeclared && x.ElementPath == "MyLib");
    }

    [Fact]
    public void SkippedEntirely_WhenDependenciesNotAnalysed()
    {
        // Without dependency edges every reference would look absent — the analyzer must not run.
        Assert.Empty(Run(BuildLib(uses: null), depAnalyzed: false));
    }
}
