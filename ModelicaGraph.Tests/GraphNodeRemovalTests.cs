using System.Diagnostics;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// Removing many nodes at once, which is the shape every caller actually has.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Removing one node costs a pass over every node's edge set, because
/// nothing indexes edges by their target. Every caller that removed a file's classes did it in a loop
/// over <c>RemoveNode</c>, so removing <i>m</i> nodes from a graph of <i>n</i> cost <i>m x n</i>. On an
/// ordinary file <i>m</i> is one or two and nobody notices; on a generated FMU interface holding
/// 4,478 classes inside a 39,860-node graph it is ~178 million set operations, and correcting a single
/// spelling mistake took the better part of a minute with the window apparently doing nothing.</para>
///
/// <para>It was found in the application log during the Photino migration, where it looked like a
/// host regression and was not one - the file was simply enormous, and the same code had always been
/// quadratic.</para>
/// </remarks>
public class GraphNodeRemovalTests
{
    private static DirectedGraph Graph(int nodes, int edgesPerNode = 3, int seed = 42)
    {
        var graph = new DirectedGraph();
        for (var i = 0; i < nodes; i++)
            graph.AddNode(new ModelNode($"m{i}", $"M{i}", "model M end M;"));

        var rng = new Random(seed);
        for (var i = 0; i < nodes; i++)
            for (var e = 0; e < edgesPerNode; e++)
                graph.AddModelUsesModel($"m{i}", $"m{rng.Next(nodes)}");

        return graph;
    }

    // ---- what it removes -----------------------------------------------------------------------

    [Fact]
    public void ItRemovesTheNodes()
    {
        var graph = Graph(20);

        var removed = graph.RemoveNodes(["m1", "m2", "m3"]);

        Assert.Equal(3, removed);
        Assert.Null(graph.GetNode("m1"));
        Assert.Null(graph.GetNode("m3"));
        Assert.NotNull(graph.GetNode("m4"));
    }

    [Fact]
    public void ItRemovesEveryEdgePointingAtThem()
    {
        // The expensive half, and the half a bulk removal could plausibly skip. A dangling edge is
        // worse than a slow removal: the node is gone and something still claims to depend on it.
        //
        // Asserted through GetIncomingNodes, which reads the edge table directly. GetUsedModels reads
        // ModelNode.UsedModelIds and filters out ids whose node has gone - so it answers "empty"
        // whether or not the edge was cleaned, and a first draft of this test used it and passed
        // against an implementation that removed no edges at all.
        var graph = new DirectedGraph();
        foreach (var id in new[] { "a", "b", "c", "d" })
            graph.AddNode(new ModelNode(id, id, "model M end M;"));
        graph.AddModelUsesModel("a", "c");
        graph.AddModelUsesModel("a", "d");
        graph.AddModelUsesModel("b", "c");

        // Two at a time, because the one-node case takes a different branch: the bulk branch is the
        // one that was worth writing and therefore the one worth covering.
        graph.RemoveNodes(["c", "d"]);

        Assert.Empty(graph.GetOutgoingNodes("a"));
        Assert.Empty(graph.GetOutgoingNodes("b"));
        Assert.Empty(graph.GetIncomingNodes("c"));
    }

    [Fact]
    public void RemovingASingleNodeAlsoRemovesItsInboundEdges()
    {
        // The other branch of the same guarantee.
        var graph = new DirectedGraph();
        foreach (var id in new[] { "a", "b" })
            graph.AddNode(new ModelNode(id, id, "model M end M;"));
        graph.AddModelUsesModel("a", "b");

        graph.RemoveNodes(["b"]);

        Assert.Empty(graph.GetOutgoingNodes("a"));
    }

    [Fact]
    public void ARemovedNodeKeepsNoEdgesOfItsOwn()
    {
        // The other direction, and a leak rather than a wrong answer: the edge table is keyed by
        // source, so dropping a node without dropping its row leaves a row for a node that no longer
        // exists - holding references to survivors, and growing every time a large file is reloaded.
        var graph = new DirectedGraph();
        foreach (var id in new[] { "gone", "alsoGone", "survivor" })
            graph.AddNode(new ModelNode(id, id, "model M end M;"));
        graph.AddModelUsesModel("gone", "survivor");
        graph.AddModelUsesModel("alsoGone", "survivor");

        graph.RemoveNodes(["gone", "alsoGone"]);

        Assert.Empty(graph.GetOutgoingNodes("gone"));
        Assert.Empty(graph.GetIncomingNodes("survivor"));
    }

    [Fact]
    public void ItAgreesWithRemovingThemOneAtATime()
    {
        // The property that makes the fast path safe to prefer everywhere: same graph afterwards.
        var ids = Enumerable.Range(0, 40).Select(i => $"m{i * 3}").ToList();

        var oneAtATime = Graph(200);
        foreach (var id in ids)
            oneAtATime.RemoveNode(id);

        var allAtOnce = Graph(200);
        allAtOnce.RemoveNodes(ids);

        Assert.Equal(
            oneAtATime.ModelNodes.Select(n => n.Id).Order(),
            allAtOnce.ModelNodes.Select(n => n.Id).Order());

        foreach (var node in allAtOnce.ModelNodes)
            Assert.Equal(
                oneAtATime.GetOutgoingNodes(node.Id).Select(n => n.Id).Order(),
                allAtOnce.GetOutgoingNodes(node.Id).Select(n => n.Id).Order());
    }

    [Fact]
    public void IdsThatAreNotThereAreNotCounted()
    {
        var graph = Graph(10);

        Assert.Equal(1, graph.RemoveNodes(["m1", "nonsense", "also-nonsense"]));
    }

    [Fact]
    public void RemovingNothingDoesNothing()
    {
        var graph = Graph(10);

        Assert.Equal(0, graph.RemoveNodes([]));
        Assert.Equal(10, graph.ModelNodes.Count());
    }

    [Fact]
    public void ARepeatedIdIsOneRemoval()
    {
        var graph = Graph(10);

        Assert.Equal(1, graph.RemoveNodes(["m1", "m1", "m1"]));
    }

    // ---- what it costs -------------------------------------------------------------------------

    [Fact]
    public void RemovingManyCostsAboutTheSameAsRemovingOne()
    {
        // A ratio, not a stopwatch bound. The house rule against timing assertions is about their
        // flakiness on a slow or loaded runner, and a ratio between two operations measured back to
        // back in the same process carries none of that: both halves slow down together.
        //
        // What it pins is the whole point of RemoveNodes. Reimplemented as a loop over RemoveNode -
        // which is what every caller used to do, and the obvious "simplification" - the two halves do
        // identical work and the ratio collapses to 1. Measured at these sizes the real ratio is
        // upwards of 100x, so the bar is set at 10x and is nowhere near either outcome.
        const int Nodes = 20_000, Removing = 2_000;
        var ids = Enumerable.Range(0, Removing).Select(i => $"m{i}").ToList();

        // Warm up, so the first measurement is not paying for JIT.
        Graph(200).RemoveNodes(Enumerable.Range(0, 20).Select(i => $"m{i}"));

        var perNode = Graph(Nodes);
        var oneAtATime = Stopwatch.StartNew();
        foreach (var id in ids)
            perNode.RemoveNode(id);
        oneAtATime.Stop();

        var bulk = Graph(Nodes);
        var allAtOnce = Stopwatch.StartNew();
        bulk.RemoveNodes(ids);
        allAtOnce.Stop();

        Assert.True(allAtOnce.Elapsed * 10 < oneAtATime.Elapsed,
            $"removing {Removing} nodes together took {allAtOnce.ElapsedMilliseconds} ms against " +
            $"{oneAtATime.ElapsedMilliseconds} ms one at a time; it should not scale with how many are removed");
    }
}
