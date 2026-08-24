using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.ExternalDocs;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// The contracts that keep a check fast when a tool's whole library folder is loaded for reference
/// resolution — tens of thousands of classes that are never reported on.
///
/// <para>These are behavioural, not timing, assertions: a stopwatch in a test is flaky, but the
/// property that makes the difference — that reference libraries are not analysed as if they were
/// the code under check — is exact and worth stating.</para>
/// </summary>
public class ExternalStubPerformanceTests
{
    private static DocumentedClass Documented(string fullName, IReadOnlyList<string>? extends = null) =>
        new(fullName, null, extends, true, null, DocumentedClass.KindModel, [], [], [], [], [], []);

    private static DirectedGraph BuildGraph(out ModelNode userModel)
    {
        var graph = new DirectedGraph();

        // A vendor library recovered from documentation: Derived extends Base.
        ExternalStubBuilder.AddDocumentedClasses(graph,
            [
                Documented("Vendor.Base"),
                Documented("Vendor.Derived", ["Vendor.Base"])
            ],
            @"C:\libs\Vendor 1.0\package.moe");

        // The user's own class, extending into the vendor library.
        const string source = "within MyLib;\nmodel Widget \"A widget\"\n  extends Vendor.Derived;\nend Widget;\n";
        GraphBuilder.LoadModelicaFile(graph, @"C:\src\MyLib\Widget.mo",
            "package MyLib\nend MyLib;\n");
        userModel = new ModelNode("MyLib.Widget", "Widget", source) { ParentModelName = "MyLib" };
        graph.AddNode(userModel);

        return graph;
    }

    [Fact]
    public async Task DependencyAnalysis_DoesNotAnalyseExternalStubs()
    {
        // A stub's source is a synthesized declaration with nothing in it to analyse, and a
        // reference library can outnumber the code under check many times over. Analysing them was
        // the single largest cost of loading a tool's library folder.
        var graph = BuildGraph(out _);

        await GraphBuilder.AnalyzeDependenciesAsync(graph);

        Assert.Empty(graph.GetNode<ModelNode>("Vendor.Derived")!.UsedModelIds);
    }

    [Fact]
    public async Task DependencyAnalysis_StillRecordsWhatTheUsersCodeUses()
    {
        // Skipping stubs as analysis *sources* must not cost us the edges that matter: those are
        // created from the user's side, and they are what "who uses this vendor class" reads.
        var graph = BuildGraph(out var userModel);

        await GraphBuilder.AnalyzeDependenciesAsync(graph);

        Assert.Contains("Vendor.Derived", userModel.UsedModelIds);
        Assert.Contains("MyLib.Widget", graph.GetNode<ModelNode>("Vendor.Derived")!.UsedByModelIds);
    }

    [Fact]
    public async Task DependencyAnalysis_LeavesStubSourceIntact()
    {
        // Analysis releases parse trees and rewrites nothing, but a stub must come through
        // untouched either way: it is the only record of what the vendor's class is.
        var graph = BuildGraph(out _);
        var before = graph.GetNode<ModelNode>("Vendor.Base")!.Definition.ModelicaCode;

        await GraphBuilder.AnalyzeDependenciesAsync(graph);

        Assert.Equal(before, graph.GetNode<ModelNode>("Vendor.Base")!.Definition.ModelicaCode);
    }

    #region Icon inheritance

    private static DirectedGraph IconGraph()
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Lib.WithIcon", "WithIcon",
            "within Lib;\nmodel WithIcon\n  annotation (Icon(graphics={Rectangle(extent={{-1,-1},{1,1}})}));\nend WithIcon;\n"));
        graph.AddNode(new ModelNode("Lib.Middle", "Middle",
            "within Lib;\nmodel Middle\n  extends Lib.WithIcon;\nend Middle;\n"));
        graph.AddNode(new ModelNode("Lib.Leaf", "Leaf",
            "within Lib;\nmodel Leaf\n  extends Lib.Middle;\nend Leaf;\n"));
        graph.AddNode(new ModelNode("Lib.NoIcon", "NoIcon",
            "within Lib;\nmodel NoIcon\nend NoIcon;\n"));
        return graph;
    }

    [Fact]
    public void BaseClassHasIcon_IsConsistentAcrossRepeatedQueries()
    {
        // The answer is cached per class, because the classes at the top of a hierarchy are the ones
        // most extended and were being reparsed once per derived class. Repeating a query must give
        // the same answer as the first time — that is what makes caching it legitimate.
        var callback = StyleChecking.CreateBaseClassHasIconCallback(IconGraph())!;

        for (var i = 0; i < 3; i++)
        {
            Assert.True(callback("Lib.Middle", "Lib.Leaf"));
            Assert.True(callback("Lib.WithIcon", "Lib.Middle"));
            Assert.False(callback("Lib.NoIcon", "Lib.Leaf"));
            Assert.False(callback("Lib.Missing", "Lib.Leaf"));
        }
    }

    [Fact]
    public void BaseClassHasIcon_FindsAnIconSeveralLevelsUp()
    {
        var callback = StyleChecking.CreateBaseClassHasIconCallback(IconGraph())!;

        Assert.True(callback("Lib.Leaf", "Lib.Leaf"));
    }

    [Fact]
    public void BaseClassHasIcon_CyclicExtends_DoesNotHangOrPoisonLaterAnswers()
    {
        // Cyclic extends is invalid Modelica, but a broken library must not hang the check — and,
        // less obviously, the truncated "no" from breaking the cycle must not be remembered as if
        // it were the real answer for a class that is also reachable another way.
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Lib.A", "A", "within Lib;\nmodel A\n  extends Lib.B;\nend A;\n"));
        graph.AddNode(new ModelNode("Lib.B", "B", "within Lib;\nmodel B\n  extends Lib.A;\nend B;\n"));
        graph.AddNode(new ModelNode("Lib.Iconed", "Iconed",
            "within Lib;\nmodel Iconed\n  annotation (Icon(graphics={Rectangle(extent={{-1,-1},{1,1}})}));\nend Iconed;\n"));
        graph.AddNode(new ModelNode("Lib.C", "C",
            "within Lib;\nmodel C\n  extends Lib.A;\n  extends Lib.Iconed;\nend C;\n"));

        var callback = StyleChecking.CreateBaseClassHasIconCallback(graph)!;

        Assert.False(callback("Lib.A", "Lib.A"));
        Assert.True(callback("Lib.C", "Lib.C"));
        Assert.False(callback("Lib.A", "Lib.A"));
    }

    #endregion
}
