using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// The shared interface cache (backlog B128): the same answer, far less work.
/// </summary>
/// <remarks>
/// <para>Resolving a class means extracting the interface of everything it extends, and a base class
/// near the root of a library is on the chain of hundreds of others — each extraction re-parsing it,
/// because the checker releases parse trees deliberately to bound memory. Measured over the Modelica
/// Standard Library, resolving inherited element names for the spell rules was <b>49% of the whole
/// check</b>: 122 thread-seconds across 5,631 classes. With the cache it is 6.</para>
///
/// <para>What has to be true is that it changes nothing else, which is what these assert: the same
/// elements, from the same walk, with or without one.</para>
/// </remarks>
public class ClassElementResolverCacheTests
{
    private static ModelNode Model(string id, string code) =>
        new(id, id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id, code) { ClassType = "model" };

    /// <summary>A three-deep chain, which is what makes a cache worth having.</summary>
    private static DirectedGraph Hierarchy()
    {
        var graph = new DirectedGraph();
        graph.AddNode(Model("Root", "model Root\n  Real fromRoot;\nend Root;"));
        graph.AddNode(Model("Middle", "model Middle\n  extends Root;\n  Real fromMiddle;\nend Middle;"));
        graph.AddNode(Model("LeafA", "model LeafA\n  extends Middle;\n  Real ownA;\nend LeafA;"));
        graph.AddNode(Model("LeafB", "model LeafB\n  extends Middle;\n  Real ownB;\nend LeafB;"));
        return graph;
    }

    private static List<string> NamesOf(DirectedGraph graph, string id, ClassElementResolver.InterfaceCache? cache)
        => [.. ClassElementResolver
            .Collect(graph, graph.GetNode<ModelNode>(id)!, includeProtected: true, includeInherited: true, cache)
            .Select(e => e.Element.Name)
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void TheCachedAnswerIsTheUncachedAnswer()
    {
        var graph = Hierarchy();
        var cache = new ClassElementResolver.InterfaceCache();

        foreach (var id in new[] { "LeafA", "LeafB", "Middle", "Root" })
            Assert.Equal(NamesOf(graph, id, null), NamesOf(graph, id, cache));
    }

    [Fact]
    public void AWarmCacheAnswersTheSameAsAColdOne()
    {
        // The second class through shares every base class with the first, so it reads what the first
        // put there rather than deriving it. That is the whole point, and it is also where a cache
        // goes wrong - by handing back something that belonged to the previous query.
        var graph = Hierarchy();
        var cache = new ClassElementResolver.InterfaceCache();

        var leafAFirst = NamesOf(graph, "LeafA", cache);   // populates Root and Middle
        var leafB = NamesOf(graph, "LeafB", cache);        // reads them back
        var leafAAgain = NamesOf(graph, "LeafA", cache);

        Assert.Equal(leafAFirst, leafAAgain);
        Assert.Contains("fromRoot", leafB);
        Assert.Contains("fromMiddle", leafB);
        Assert.Contains("ownB", leafB);
        Assert.DoesNotContain("ownA", leafB);              // LeafA's own member must not leak across
    }

    [Fact]
    public void InheritanceIsStillFollowedToTheTop()
    {
        var graph = Hierarchy();
        var names = NamesOf(graph, "LeafA", new ClassElementResolver.InterfaceCache());

        Assert.Contains("fromRoot", names);     // two levels up
        Assert.Contains("fromMiddle", names);
        Assert.Contains("ownA", names);
    }

    [Fact]
    public void ItIsSafeToShareAcrossThreads()
    {
        // It is handed to the per-class checks, which run on every core at once, so several threads
        // ask for the same base class before any of them has finished extracting it.
        var graph = Hierarchy();
        var cache = new ClassElementResolver.InterfaceCache();
        var expected = NamesOf(graph, "LeafA", null);

        var answers = new System.Collections.Concurrent.ConcurrentBag<List<string>>();
        Parallel.For(0, 64, _ => answers.Add(NamesOf(graph, "LeafA", cache)));

        Assert.All(answers, a => Assert.Equal(expected, a));
    }

    [Fact]
    public void ACacheIsNotRequired()
    {
        // Callers that resolve one class - the MCP tools answering a question about it - pass nothing
        // and must keep working. It is an optimisation for bulk, not a new obligation.
        var graph = Hierarchy();

        Assert.Contains("fromRoot", NamesOf(graph, "LeafA", null));
    }
}
