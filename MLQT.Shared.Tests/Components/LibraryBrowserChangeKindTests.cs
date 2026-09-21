using MLQT.Shared.Components;
using ModelicaGraph.DataTypes;
using ModelicaParser.Comparison;
using MudBlazor;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// The three decisions the library browser makes about a changed model that are not the marker
/// itself (B191): what a package says about the classes under it, what each filter selects, and
/// what the pruned tree looks like once one is on.
/// </summary>
public class LibraryBrowserChangeKindTests
{
    // ---------------------------------------------------------------- a library to climb

    /// <summary>
    /// Containment as the graph records it: each model names its parent, which is not always what
    /// splitting its dotted id would give. <c>Lib.Pack."A.B"</c> is the case that matters — a quoted
    /// Modelica identifier with a dot in it, as <c>ModelicaReference</c> has.
    /// </summary>
    private static readonly Dictionary<string, ModelNode> Library = new(StringComparer.Ordinal)
    {
        ["Lib"] = Node("Lib", "Lib", parent: null, "package"),
        ["Lib.Pack"] = Node("Lib.Pack", "Pack", parent: "Lib", "package"),
        ["Lib.Pack.A"] = Node("Lib.Pack.A", "A", parent: "Lib.Pack", "model"),
        ["Lib.Pack.B"] = Node("Lib.Pack.B", "B", parent: "Lib.Pack", "model"),
        ["Lib.Other"] = Node("Lib.Other", "Other", parent: "Lib", "package"),
        ["Lib.Other.C"] = Node("Lib.Other.C", "C", parent: "Lib.Other", "model"),
    };

    private static ModelNode Node(string id, string name, string? parent, string classType) =>
        new(id, name, $"model {name} end {name};") { ParentModelName = parent, ClassType = classType };

    private static ModelNode? Lookup(string id) => Library.GetValueOrDefault(id);

    private static Dictionary<string, VcsFileStatus> Modified(params string[] modelIds) =>
        modelIds.ToDictionary(id => id, _ => VcsFileStatus.Modified, StringComparer.Ordinal);

    private static Dictionary<string, ClassChangeKind> Kinds(
        params (string Id, ClassChangeKind Kind)[] entries) =>
        entries.ToDictionary(e => e.Id, e => e.Kind, StringComparer.Ordinal);

    // ---------------------------------------------------------------- rolling up to packages

    [Fact]
    public void APackageReportsTheStrongestChangeBelowIt()
    {
        var descendants = LibraryBrowser.DescendantKinds(
            Modified("Lib.Pack.A", "Lib.Pack.B").Keys,
            Kinds(("Lib.Pack.A", ClassChangeKind.Cosmetic), ("Lib.Pack.B", ClassChangeKind.AffectsSimulation)),
            Lookup);

        Assert.Equal(ClassChangeKind.AffectsSimulation, descendants["Lib.Pack"]);
        Assert.Equal(ClassChangeKind.AffectsSimulation, descendants["Lib"]);
    }

    [Fact]
    public void APackageWithOnlyCosmeticChangesBelowItSaysSo()
    {
        var descendants = LibraryBrowser.DescendantKinds(
            Modified("Lib.Pack.A").Keys, Kinds(("Lib.Pack.A", ClassChangeKind.Cosmetic)), Lookup);

        Assert.Equal(ClassChangeKind.Cosmetic, descendants["Lib.Pack"]);
    }

    /// <summary>
    /// Every class in a modified <c>package.mo</c> is in the status map. Only the ones that actually
    /// changed may contribute, or every package above one would claim a change it does not have.
    /// </summary>
    [Fact]
    public void AnUnchangedClassContributesNothingToItsPackages()
    {
        var descendants = LibraryBrowser.DescendantKinds(
            Modified("Lib.Pack.A", "Lib.Pack.B").Keys,
            Kinds(("Lib.Pack.A", ClassChangeKind.Unchanged), ("Lib.Pack.B", ClassChangeKind.Unchanged)),
            Lookup);

        Assert.Empty(descendants);
    }

    /// <summary>
    /// A repository whose changes could not be classified still bubbles a marker up its packages —
    /// which is what MLQT did for everything before B191, and is still the right answer when nothing
    /// better is known.
    /// </summary>
    [Fact]
    public void AnUnclassifiedChangeStillReachesItsPackages()
    {
        var descendants = LibraryBrowser.DescendantKinds(
            Modified("Lib.Pack.A").Keys, new Dictionary<string, ClassChangeKind>(), Lookup);

        Assert.Equal(ClassChangeKind.Unknown, descendants["Lib.Pack"]);
    }

    [Fact]
    public void ATopLevelClassHasNoPackageToReportTo()
    {
        var descendants = LibraryBrowser.DescendantKinds(
            Modified("Lib").Keys, Kinds(("Lib", ClassChangeKind.AffectsSimulation)), Lookup);

        Assert.Empty(descendants);
    }

    /// <summary>
    /// Containment, not string arithmetic. A class whose own name carries a dot — a quoted Modelica
    /// identifier, which <c>ModelicaReference</c> has — would otherwise be attributed to a package
    /// that does not exist, and the one that really holds it would get nothing. B189 recorded this
    /// once already for the reveal walk.
    /// </summary>
    [Fact]
    public void AQuotedIdentifierIsNotSplitIntoPackagesThatDoNotExist()
    {
        var quoted = Node("Lib.Pack.'A.B'", "'A.B'", parent: "Lib.Pack", "model");
        ModelNode? lookup(string id) => id == quoted.Id ? quoted : Lookup(id);

        var descendants = LibraryBrowser.DescendantKinds(
            Modified(quoted.Id).Keys, Kinds((quoted.Id, ClassChangeKind.AffectsSimulation)), lookup);

        Assert.Equal(["Lib", "Lib.Pack"], descendants.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    // ---------------------------------------------------------------- what each filter selects

    [Theory]
    [InlineData(ClassChangeKind.AffectsSimulation, true)]
    [InlineData(ClassChangeKind.Added, true)]
    [InlineData(ClassChangeKind.Cosmetic, true)]
    [InlineData(ClassChangeKind.Unknown, true)]
    [InlineData(ClassChangeKind.Unchanged, false)]
    public void ChangedSelectsEverythingThatChanged(ClassChangeKind kind, bool expected)
    {
        Assert.Equal(expected, LibraryBrowser.Selects(LibraryBrowser.ChangeFilter.Changed, kind));
    }

    /// <summary>
    /// "Affects simulation" is everything not <i>known</i> to be harmless, so a class MLQT could not
    /// compare is in it. A filter that hid those would hide exactly what it was asked to find.
    /// </summary>
    [Theory]
    [InlineData(ClassChangeKind.AffectsSimulation, true)]
    [InlineData(ClassChangeKind.Added, true)]
    [InlineData(ClassChangeKind.Unknown, true)]
    [InlineData(ClassChangeKind.Cosmetic, false)]
    [InlineData(ClassChangeKind.Unchanged, false)]
    public void AffectsSimulationSelectsAnythingNotKnownToBeHarmless(ClassChangeKind kind, bool expected)
    {
        Assert.Equal(expected, LibraryBrowser.Selects(LibraryBrowser.ChangeFilter.AffectsSimulation, kind));
    }

    [Theory]
    [InlineData(ClassChangeKind.Cosmetic, true)]
    [InlineData(ClassChangeKind.AffectsSimulation, false)]
    [InlineData(ClassChangeKind.Added, false)]
    [InlineData(ClassChangeKind.Unknown, false)]
    [InlineData(ClassChangeKind.Unchanged, false)]
    public void CosmeticSelectsOnlyWhatIsVouchedFor(ClassChangeKind kind, bool expected)
    {
        Assert.Equal(expected, LibraryBrowser.Selects(LibraryBrowser.ChangeFilter.Cosmetic, kind));
    }

    [Fact]
    public void NoFilterSelectsNothing()
    {
        foreach (var kind in Enum.GetValues<ClassChangeKind>())
            Assert.False(LibraryBrowser.Selects(LibraryBrowser.ChangeFilter.None, kind));
    }

    /// <summary>
    /// Between them the two narrow filters account for every change exactly once, so a user who
    /// looks at both has looked at everything.
    /// </summary>
    [Fact]
    public void TheTwoNarrowFiltersPartitionTheChangedOnes()
    {
        foreach (var kind in Enum.GetValues<ClassChangeKind>())
        {
            var changed = LibraryBrowser.Selects(LibraryBrowser.ChangeFilter.Changed, kind);
            var simulation = LibraryBrowser.Selects(LibraryBrowser.ChangeFilter.AffectsSimulation, kind);
            var cosmetic = LibraryBrowser.Selects(LibraryBrowser.ChangeFilter.Cosmetic, kind);

            Assert.Equal(changed, simulation ^ cosmetic);
        }
    }

    // ---------------------------------------------------------------- the pruned tree

    private static List<TreeItemData<ModelNode>> Pruned(params string[] modelIds) =>
        PrunedWith([], modelIds);

    private static List<TreeItemData<ModelNode>> PrunedWith(string[] expanded, params string[] modelIds) =>
        LibraryBrowser.BuildFilteredTree(
            modelIds.Select(id => Library[id]), Lookup, new HashSet<string>(expanded, StringComparer.Ordinal));

    /// <summary>Every id in the tree, outermost first, as "parent > child" paths.</summary>
    private static List<string> Paths(IEnumerable<ITreeItemData<ModelNode>> items, string prefix = "")
    {
        var paths = new List<string>();
        foreach (var item in items)
        {
            var here = prefix.Length == 0 ? item.Value!.Id : prefix + " > " + item.Value!.Id;
            paths.Add(here);
            if (item.Children is { Count: > 0 } children)
                paths.AddRange(Paths(children, here));
        }

        return paths;
    }

    [Fact]
    public void AMatchBringsThePackagesThatContainItWithIt()
    {
        Assert.Equal(
            ["Lib", "Lib > Lib.Pack", "Lib > Lib.Pack > Lib.Pack.A"],
            Paths(Pruned("Lib.Pack.A")));
    }

    /// <summary>
    /// The point of a tree rather than a list: two changes in the same package share it, and two in
    /// different packages are told apart by where they sit.
    /// </summary>
    [Fact]
    public void MatchesSharingAPackageShareOneNodeForIt()
    {
        Assert.Equal(
            [
                "Lib",
                "Lib > Lib.Other",
                "Lib > Lib.Other > Lib.Other.C",
                "Lib > Lib.Pack",
                "Lib > Lib.Pack > Lib.Pack.A",
                "Lib > Lib.Pack > Lib.Pack.B",
            ],
            Paths(Pruned("Lib.Pack.A", "Lib.Pack.B", "Lib.Other.C")));
    }

    /// <summary>
    /// Nothing the filter did not select is in the tree, however much of it sits beside a match.
    /// </summary>
    [Fact]
    public void NothingUnselectedIsInTheTree()
    {
        var paths = Paths(Pruned("Lib.Pack.A"));

        Assert.DoesNotContain(paths, p => p.EndsWith("Lib.Pack.B", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p.Contains("Lib.Other", StringComparison.Ordinal));
    }

    /// <summary>
    /// The pruned tree opens exactly as far as the user had the full one open, and no further.
    /// </summary>
    /// <remarks>
    /// The two views share one expansion record, so a filter that opened everything would leave the
    /// tree spread out after it was cleared — a filter is a question, not a rearrangement.
    /// </remarks>
    [Fact]
    public void ThePrunedTreeIsOpenWhereTheUserHadItOpen()
    {
        var items = Flatten(PrunedWith(["Lib"], "Lib.Pack.A")).ToDictionary(i => i.Value!.Id);

        Assert.True(items["Lib"].Expanded);
        Assert.False(items["Lib.Pack"].Expanded);
    }

    [Fact]
    public void ATreeTheUserHadClosedStaysClosed()
    {
        Assert.All(
            Flatten(Pruned("Lib.Pack.A", "Lib.Other.C")),
            item => Assert.False(item.Expanded));
    }

    [Fact]
    public void ATreeTheUserHadFullyOpenStaysFullyOpen()
    {
        Assert.All(
            Flatten(PrunedWith(["Lib", "Lib.Pack", "Lib.Other"], "Lib.Pack.A", "Lib.Other.C")),
            item => Assert.Equal(item.Value!.Id != "Lib.Pack.A" && item.Value.Id != "Lib.Other.C", item.Expanded));
    }

    /// <summary>
    /// A match is a leaf here whatever it holds in the real tree, so it offers no arrow: the
    /// children it has are ones the filter did not select, and opening into nothing is worse than
    /// not offering.
    /// </summary>
    [Fact]
    public void OnlyAPackageThatKeptAChildIsExpandable()
    {
        var items = Flatten(Pruned("Lib.Pack.A")).ToDictionary(i => i.Value!.Id);

        Assert.True(items["Lib"].Expandable);
        Assert.True(items["Lib.Pack"].Expandable);
        Assert.False(items["Lib.Pack.A"].Expandable);
    }

    [Fact]
    public void NoMatchesMeansNoTree()
    {
        Assert.Empty(PrunedWith([]));
    }

    /// <summary>
    /// A package the lookup cannot resolve does not take its changes down with it: the class
    /// attaches to the nearest ancestor that did resolve, and is still visible.
    /// </summary>
    [Fact]
    public void AnUnresolvableAncestorIsSkippedRatherThanLosingTheClass()
    {
        ModelNode? lookup(string id) => id == "Lib" ? null : Lookup(id);

        Assert.Equal(["Lib.Pack", "Lib.Pack > Lib.Pack.A"], Paths(
            LibraryBrowser.BuildFilteredTree([Library["Lib.Pack.A"]], lookup, new HashSet<string>())));
    }

    private static List<ITreeItemData<ModelNode>> Flatten(IEnumerable<ITreeItemData<ModelNode>> items)
    {
        var all = new List<ITreeItemData<ModelNode>>();
        foreach (var item in items)
        {
            all.Add(item);
            if (item.Children is { Count: > 0 } children)
                all.AddRange(Flatten(children));
        }

        return all;
    }
}
