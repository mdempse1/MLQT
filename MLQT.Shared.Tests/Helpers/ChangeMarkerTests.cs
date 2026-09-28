using MLQT.Shared.Helpers;
using ModelicaParser.Comparison;
using MudBlazor;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Helpers;

/// <summary>
/// What the library browser draws beside a model (B191), and the rule that makes "modified"
/// different from every other status.
/// </summary>
public class ChangeMarkerTests
{
    private static ChangeMarker Modified(ClassChangeKind kind, ClassChangeKind descendants = ClassChangeKind.Unchanged)
        => ChangeMarker.For(VcsFileStatus.Modified, kind, descendants);

    // ---------------------------------------------------------------- nothing to show

    [Fact]
    public void AnUntouchedModelHasNoMarker()
    {
        var marker = ChangeMarker.For(null, ClassChangeKind.Unknown, ClassChangeKind.Unchanged);

        Assert.Equal(ChangeMarkerShape.None, marker.Shape);
    }

    /// <summary>
    /// The case B191 exists for: a package's file is modified because one class in it was edited,
    /// and the package itself is untouched. It gets the descendant dot, not a chip of its own.
    /// </summary>
    [Fact]
    public void AnUnchangedClassInAModifiedFileShowsOnlyWhatIsBelowIt()
    {
        Assert.Equal(ChangeMarkerShape.None, Modified(ClassChangeKind.Unchanged).Shape);
        Assert.Equal(ChangeMarkerShape.Dot, Modified(ClassChangeKind.Unchanged, ClassChangeKind.AffectsSimulation).Shape);
    }

    // ---------------------------------------------------------------- the kinds

    [Fact]
    public void AChangeThatAffectsSimulationIsTheProminentMarker()
    {
        var marker = Modified(ClassChangeKind.AffectsSimulation);

        Assert.Equal(ChangeMarkerShape.Chip, marker.Shape);
        Assert.Equal("M", marker.Text);
        Assert.Equal(Color.Warning, marker.Color);
        Assert.Contains("affect simulation", marker.Tooltip);
    }

    [Fact]
    public void ACosmeticChangeIsMarkedDifferently()
    {
        var marker = Modified(ClassChangeKind.Cosmetic);

        Assert.Equal(ChangeMarkerShape.Chip, marker.Shape);
        Assert.NotEqual("M", marker.Text);
        Assert.NotEqual(Color.Warning, marker.Color);
    }

    [Fact]
    public void ANewClassInAModifiedFileIsMarkedAsAdded()
    {
        var marker = Modified(ClassChangeKind.Added);

        Assert.Equal("A", marker.Text);
        Assert.Equal(Color.Success, marker.Color);
    }

    /// <summary>
    /// An unclassifiable change reads as it did before B191 — modified, in the colour that gets
    /// looked at. Understating it would be the one failure mode worth avoiding.
    /// </summary>
    [Fact]
    public void AnUnclassifiableChangeKeepsThePlainModifiedMarker()
    {
        var marker = Modified(ClassChangeKind.Unknown);

        Assert.Equal("M", marker.Text);
        Assert.Equal(Color.Warning, marker.Color);
        Assert.Contains("could not compare", marker.Tooltip);
    }

    // ---------------------------------------------------------------- the other statuses

    /// <summary>
    /// Every status but Modified is a fact about the file, shared by every class in it, so the kind
    /// is not consulted.
    /// </summary>
    [Theory]
    [InlineData(VcsFileStatus.Added, "A")]
    [InlineData(VcsFileStatus.Deleted, "D")]
    [InlineData(VcsFileStatus.Renamed, "R")]
    [InlineData(VcsFileStatus.Untracked, "N")]
    [InlineData(VcsFileStatus.Conflicted, "!")]
    public void AFileLevelStatusIsShownAsItself(VcsFileStatus status, string expected)
    {
        foreach (var kind in Enum.GetValues<ClassChangeKind>())
        {
            var marker = ChangeMarker.For(status, kind, ClassChangeKind.Unchanged);

            Assert.Equal(ChangeMarkerShape.Chip, marker.Shape);
            Assert.Equal(expected, marker.Text);
        }
    }

    /// <summary>
    /// The single letters predate B191 and had no tooltip at all, so the only way to learn what "R"
    /// meant was to guess.
    /// </summary>
    [Fact]
    public void EveryMarkerSaysWhatItMeans()
    {
        foreach (var status in Enum.GetValues<VcsFileStatus>())
        {
            foreach (var kind in Enum.GetValues<ClassChangeKind>())
            {
                var marker = ChangeMarker.For(status, kind, ClassChangeKind.AffectsSimulation);
                if (marker.Shape != ChangeMarkerShape.None)
                    Assert.False(string.IsNullOrWhiteSpace(marker.Tooltip));
            }
        }
    }

    // ---------------------------------------------------------------- the descendant dot

    [Theory]
    [InlineData(ClassChangeKind.AffectsSimulation, true)]
    [InlineData(ClassChangeKind.Added, true)]
    [InlineData(ClassChangeKind.Unknown, true)]
    [InlineData(ClassChangeKind.Cosmetic, true)]
    [InlineData(ClassChangeKind.Unchanged, false)]
    public void TheDescendantDotAppearsForAnythingButAnUnchangedSubtree(ClassChangeKind descendants, bool expected)
    {
        var marker = ChangeMarker.For(null, ClassChangeKind.Unknown, descendants);

        Assert.Equal(expected, marker.Shape == ChangeMarkerShape.Dot);
    }

    [Fact]
    public void ACosmeticSubtreeIsNotColouredLikeOneThatAffectsSimulation()
    {
        var cosmetic = ChangeMarker.For(null, ClassChangeKind.Unknown, ClassChangeKind.Cosmetic);
        var significant = ChangeMarker.For(null, ClassChangeKind.Unknown, ClassChangeKind.AffectsSimulation);

        Assert.NotEqual(significant.Color, cosmetic.Color);
    }

    /// <summary>
    /// A class's own change wins over what is below it — it is the specific answer, and the dot
    /// would say less.
    /// </summary>
    [Fact]
    public void AClassOwnChangeIsShownRatherThanItsDescendants()
    {
        var marker = Modified(ClassChangeKind.Cosmetic, ClassChangeKind.AffectsSimulation);

        Assert.Equal(ChangeMarkerShape.Chip, marker.Shape);
        Assert.Equal(Modified(ClassChangeKind.Cosmetic).Text, marker.Text);
    }
}
