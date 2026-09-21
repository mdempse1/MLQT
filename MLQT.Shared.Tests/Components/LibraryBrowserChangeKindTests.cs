using MLQT.Shared.Components;
using ModelicaParser.Comparison;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// The two decisions the library browser makes about a changed model that are not the marker itself
/// (B191): what a package says about the classes under it, and what each filter selects.
/// </summary>
public class LibraryBrowserChangeKindTests
{
    private static Dictionary<string, VcsFileStatus> Modified(params string[] modelIds) =>
        modelIds.ToDictionary(id => id, _ => VcsFileStatus.Modified, StringComparer.Ordinal);

    // ---------------------------------------------------------------- rolling up to packages

    [Fact]
    public void APackageReportsTheStrongestChangeBelowIt()
    {
        var status = Modified("Lib.Pack.A", "Lib.Pack.B");
        var kinds = new Dictionary<string, ClassChangeKind>
        {
            ["Lib.Pack.A"] = ClassChangeKind.Cosmetic,
            ["Lib.Pack.B"] = ClassChangeKind.AffectsSimulation,
        };

        var descendants = LibraryBrowser.DescendantKinds(status, kinds);

        Assert.Equal(ClassChangeKind.AffectsSimulation, descendants["Lib.Pack"]);
        Assert.Equal(ClassChangeKind.AffectsSimulation, descendants["Lib"]);
    }

    [Fact]
    public void APackageWithOnlyCosmeticChangesBelowItSaysSo()
    {
        var descendants = LibraryBrowser.DescendantKinds(
            Modified("Lib.Pack.A"),
            new Dictionary<string, ClassChangeKind> { ["Lib.Pack.A"] = ClassChangeKind.Cosmetic });

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
            Modified("Lib.Pack.A", "Lib.Pack.B"),
            new Dictionary<string, ClassChangeKind>
            {
                ["Lib.Pack.A"] = ClassChangeKind.Unchanged,
                ["Lib.Pack.B"] = ClassChangeKind.Unchanged,
            });

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
            Modified("Lib.Pack.A"), new Dictionary<string, ClassChangeKind>());

        Assert.Equal(ClassChangeKind.Unknown, descendants["Lib.Pack"]);
    }

    [Fact]
    public void ATopLevelClassHasNoPackageToReportTo()
    {
        var descendants = LibraryBrowser.DescendantKinds(
            Modified("Lib"),
            new Dictionary<string, ClassChangeKind> { ["Lib"] = ClassChangeKind.AffectsSimulation });

        Assert.Empty(descendants);
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
}
