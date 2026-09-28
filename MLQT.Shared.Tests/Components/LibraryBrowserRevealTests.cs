using MLQT.Shared.Components;
using ModelicaGraph.DataTypes;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// Revealing a class in the tree when it was opened from somewhere else (B189) — "clicking a finding
/// opens the model in the viewer but leaves the browser wherever it was, so there is no context for
/// what was just opened".
///
/// <para>The part worth testing on its own is which packages have to be open. The rest is the tree's
/// existing restore-expansion walk, which already knows how to fetch children for a node marked
/// expanded.</para>
/// </summary>
public class LibraryBrowserRevealTests
{
    /// <summary>A graph of ids to their parents, as the lookup sees it.</summary>
    private static Func<string, ModelNode?> Graph(params (string Id, string? Parent)[] models)
    {
        var byId = models.ToDictionary(
            m => m.Id,
            m => new ModelNode(m.Id, m.Id) { ParentModelName = m.Parent },
            StringComparer.Ordinal);

        return id => byId.GetValueOrDefault(id);
    }

    [Fact]
    public void TheChainIsEveryPackageAboveTheClass_OutermostFirst()
    {
        var lookup = Graph(
            ("Lib", null),
            ("Lib.Components", "Lib"),
            ("Lib.Components.Profile", "Lib.Components"));

        Assert.Equal(
            ["Lib", "Lib.Components"],
            LibraryBrowser.AncestorChain("Lib.Components.Profile", lookup));
    }

    [Fact]
    public void AClassAtTheTopHasNothingAboveIt() =>
        Assert.Empty(LibraryBrowser.AncestorChain("Lib", Graph(("Lib", null))));

    [Fact]
    public void ContainmentIsFollowed_NotTheDots()
    {
        // The reason this walks ParentModelName rather than splitting the name: a class whose own
        // name carries dots — a quoted identifier, and ModelicaReference really has one — would give
        // a chain of packages that do not exist.
        var lookup = Graph(
            ("Lib", null),
            ("Lib.'Connections.branch()'", "Lib"));

        Assert.Equal(["Lib"], LibraryBrowser.AncestorChain("Lib.'Connections.branch()'", lookup));
    }

    [Fact]
    public void AnUnknownClassRevealsNothing() =>
        Assert.Empty(LibraryBrowser.AncestorChain("Lib.NotLoaded", Graph(("Lib", null))));

    [Fact]
    public void AParentTheGraphHasLostStopsTheWalkRatherThanFailing()
    {
        // Half a graph — the class knows its parent, the parent is not there. Showing what can be
        // shown beats throwing inside a selection handler.
        var lookup = Graph(("Lib.Components.Profile", "Lib.Components"));

        Assert.Equal(["Lib.Components"], LibraryBrowser.AncestorChain("Lib.Components.Profile", lookup));
    }

    [Fact]
    public void ACycleDoesNotSpin()
    {
        // Nothing should produce this, which is exactly why it is worth a guard: a malformed graph
        // should leave the tree unrevealed rather than hang the UI thread.
        var lookup = Graph(("A", "B"), ("B", "A"));

        var chain = LibraryBrowser.AncestorChain("A", lookup);

        Assert.True(chain.Count <= 2, $"the walk should stop at the repeat; it returned {chain.Count} ids");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NothingSelectedRevealsNothing(string? modelId) =>
        Assert.Empty(LibraryBrowser.AncestorChain(modelId, Graph(("Lib", null))));
}
