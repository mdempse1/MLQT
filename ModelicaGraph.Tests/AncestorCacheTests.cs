using ModelicaGraph;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;

namespace ModelicaGraph.Tests;

/// <summary>
/// B174 — the cache that made <c>MissingUnits</c> affordable, and the answers it must not change.
///
/// <para><b>Why it exists.</b> Profiling a check of the Modelica Standard Library put 82% of the
/// rule's time in <c>TypeResolver.CollectAncestors</c>, and the rule was 91% of the check. Collecting
/// a class's extends chain reads each ancestor's interface, which <em>parses</em> it when nobody else
/// is holding its tree — so a class with twenty unresolved type names re-parsed its whole ancestry
/// twenty times. Caching the chain for the run took the rule from 295.4s of thread-time to 129.7s,
/// with all 5,250 findings identical.</para>
///
/// <para><b>What these test is correctness, not speed.</b> A timing assertion would be noise on a
/// shared machine, and the risk in a cache is never that it is slow — it is that it answers a
/// question it was not asked. The measurement lives in the backlog, where a number belongs.</para>
/// </summary>
public class AncestorCacheTests
{
    /// <summary>
    /// A library where <c>Length</c> is only in scope through the extends chain, so resolving it
    /// needs the ancestor walk rather than a name lookup.
    /// </summary>
    private static DirectedGraph Library()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "Units.mo",
            "package Units\n  type Length = Real(unit=\"m\");\n  type Mass = Real(unit=\"kg\");\nend Units;\n");
        GraphBuilder.LoadModelicaFile(graph, "Bases.mo",
            "partial model Base\n  import Units.*;\nend Base;\n");
        GraphBuilder.LoadModelicaFile(graph, "Models.mo",
            "model A\n  extends Base;\n  Length x;\nend A;\n"
            + "model B\n  Length y;\nend B;\n");
        return graph;
    }

    [Fact]
    public void ACachedRunResolvesWhatAnUncachedOneDoes()
    {
        var graph = Library();
        var cache = new TypeResolver.AncestorCache();

        var uncached = TypeResolver.ResolveWithInheritance(graph, "A", "Length", null);
        var cached = TypeResolver.ResolveWithInheritance(graph, "A", "Length", null, cache);

        Assert.NotNull(uncached);
        Assert.Equal("Units.Length", uncached!.Id);
        Assert.Equal(uncached.Id, cached?.Id);
    }

    [Fact]
    public void TheSameCacheAnswersTwoTypesInOneClassSeparately()
    {
        // The case the cache is for — a class asking about many names — and the one where keying it
        // wrongly would show: it remembers the *chain*, which is per class, not the *answer*, which
        // is per type.
        var graph = Library();
        var cache = new TypeResolver.AncestorCache();

        Assert.Equal("Units.Length", TypeResolver.ResolveWithInheritance(graph, "A", "Length", null, cache)?.Id);
        Assert.Equal("Units.Mass", TypeResolver.ResolveWithInheritance(graph, "A", "Mass", null, cache)?.Id);
        Assert.Equal("Units.Length", TypeResolver.ResolveWithInheritance(graph, "A", "Length", null, cache)?.Id);
    }

    [Fact]
    public void OneClassesChainIsNotAnothers()
    {
        // B does not extend Base, so the name is not in scope for it. A cache keyed on anything
        // coarser than the class would hand B the chain A got and resolve it anyway.
        var graph = Library();
        var cache = new TypeResolver.AncestorCache();

        Assert.NotNull(TypeResolver.ResolveWithInheritance(graph, "A", "Length", null, cache));
        Assert.Null(TypeResolver.ResolveWithInheritance(graph, "B", "Length", null, cache));
    }

    [Fact]
    public void ANameThatIsNotThereIsStillNotThereTheSecondTime()
    {
        var graph = Library();
        var cache = new TypeResolver.AncestorCache();

        Assert.Null(TypeResolver.ResolveWithInheritance(graph, "A", "NoSuchType", null, cache));
        Assert.Null(TypeResolver.ResolveWithInheritance(graph, "A", "NoSuchType", null, cache));

        // ...and it has not poisoned the chain for a name that is there.
        Assert.Equal("Units.Length", TypeResolver.ResolveWithInheritance(graph, "A", "Length", null, cache)?.Id);
    }

    [Fact]
    public void TheUnitLookupAgreesWithItselfAcrossTheCache()
    {
        // The path the rule actually takes. CreateUnitLookup owns a cache for the run, so this is
        // the same question asked twice through the layer that memoises it.
        var graph = Library();
        var lookup = StyleChecking.CreateUnitLookup(graph);
        Assert.NotNull(lookup);

        var first = lookup!("A", "Length");
        var second = lookup("A", "Length");

        Assert.True(first.IsRealDerived);
        Assert.True(first.TypeHasUnit);
        Assert.Equal(first, second);

        // And a class the name is not in scope for still gets the honest answer.
        Assert.False(lookup("B", "Length").IsRealDerived);
    }
}
