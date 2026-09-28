using MLQT.Shared.Pages;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// Going into a class this one uses (B197) — "checking what it should be called means finding the
/// base class by hand".
/// </summary>
public class CodeReviewUsedClassesTests
{
    private static DirectedGraph GraphOf(bool analysed, params (string From, string To)[] uses)
    {
        var graph = new DirectedGraph();
        foreach (var id in uses.SelectMany(u => new[] { u.From, u.To }).Distinct(StringComparer.Ordinal))
            graph.AddNode(new ModelNode(id, id));

        foreach (var (from, to) in uses)
            graph.AddModelUsesModel(from, to);

        if (analysed)
            graph.MarkDependenciesAnalyzed();

        return graph;
    }

    [Fact]
    public void TheClassesItUsesAreOfferedInNameOrder()
    {
        var graph = GraphOf(analysed: true,
            ("Lib.Thing", "Lib.Zed"), ("Lib.Thing", "Lib.Alpha"), ("Lib.Thing", "Lib.Mid"));

        Assert.Equal(
            ["Lib.Alpha", "Lib.Mid", "Lib.Zed"],
            CodeReview.UsedClassesOf(graph, "Lib.Thing").Select(m => m.Id));
    }

    [Fact]
    public void NothingIsOfferedUntilDependencyAnalysisHasRun()
    {
        // The edges are what this reads and that pass is what builds them — deferred for a large
        // repository until the user asks. The menu has to say "not analysed" rather than look like
        // a class that uses nothing, which is why the flag is asked rather than the edge count.
        var graph = GraphOf(analysed: false, ("Lib.Thing", "Lib.Alpha"));

        Assert.Empty(CodeReview.UsedClassesOf(graph, "Lib.Thing"));
    }

    [Fact]
    public void AClassDoesNotLeadToItself()
    {
        var graph = GraphOf(analysed: true, ("Lib.Thing", "Lib.Thing"), ("Lib.Thing", "Lib.Alpha"));

        Assert.Equal(["Lib.Alpha"], CodeReview.UsedClassesOf(graph, "Lib.Thing").Select(m => m.Id));
    }

    [Fact]
    public void AClassUsedTwiceIsOfferedOnce()
    {
        var graph = GraphOf(analysed: true, ("Lib.Thing", "Lib.Alpha"), ("Lib.Thing", "Lib.Alpha"));

        Assert.Single(CodeReview.UsedClassesOf(graph, "Lib.Thing"));
    }

    [Fact]
    public void AClassThatUsesNothingOffersNothing()
    {
        var graph = GraphOf(analysed: true, ("Lib.Other", "Lib.Alpha"));
        graph.AddNode(new ModelNode("Lib.Thing", "Lib.Thing"));

        Assert.Empty(CodeReview.UsedClassesOf(graph, "Lib.Thing"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Lib.NotLoaded")]
    public void NoClassOrAnUnknownOneOffersNothing(string? modelId) =>
        Assert.Empty(CodeReview.UsedClassesOf(GraphOf(analysed: true, ("Lib.Thing", "Lib.Alpha")), modelId));
}
