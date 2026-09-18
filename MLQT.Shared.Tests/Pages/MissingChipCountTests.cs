using MLQT.Services.DataTypes;
using MLQT.Shared.Pages;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// That the External Resources chips count what selecting them shows (B207).
///
/// <para><b>Reported:</b> "Missing (5)" over a tree that emptied when the chip was clicked. Two
/// independent causes, and either alone produces a mismatch.</para>
///
/// <para><b>The count came from the wrong collection.</b> It was the number of
/// <c>MissingFile</c> <i>warnings</i>, and one of those is recorded per reference — so a file six
/// models mention is six warnings and one tree node. The warning list disagrees the other way too: a
/// path that cannot be resolved at all shows in the tree as missing and produces no warning, because
/// the warning needs a resolved path to test for existence. The counts now come from the tree's own
/// nodes, which are already de-duplicated by path.</para>
///
/// <para><b>And the file-type filter could hide what the chip had counted.</b> It defaults to data,
/// C code and libraries, so a missing image, document, or anything with an unlisted extension — the
/// <c>other</c> catch-all, which includes having no extension — was counted and then filtered out of
/// the tree. A warning filter now answers on its own.</para>
/// </summary>
public class MissingChipCountTests
{
    private static ResourceTreeNode File(string name, bool missing = false, bool absolute = false) =>
        new()
        {
            Name = name,
            FullPath = @"C:\lib\Resources\" + name,
            IsDirectory = false,
            FileExtension = Path.GetExtension(name),
            IsMissing = missing,
            IsAbsolutePath = absolute
        };

    private static readonly string[] DefaultFileTypes = ["data", "ccode", "lib"];
    private static readonly string[] MissingOnly = ["missing"];
    private static readonly string[] NoWarningFilter = [];

    [Fact]
    public void EachResourceIsCountedOnce()
    {
        // The tree de-duplicates by path before this sees the nodes, so what arrives is one per
        // resource however many models referenced it. Six mentions used to read as six.
        var (missing, _) = ExternalResources.CountResourceWarnings(
            [File("shared.mat", missing: true), File("present.mat"), File("gone.csv", missing: true)]);

        Assert.Equal(2, missing);
    }

    [Fact]
    public void AnUnresolvedPathIsCountedAsMissing()
    {
        // It shows in the tree under "Unresolved References" and is flagged missing there, but it
        // produces no warning at all, so the old count was short by one for each of these.
        var unresolved = File("cannot-resolve.mat", missing: true);

        var (missing, _) = ExternalResources.CountResourceWarnings([unresolved]);

        Assert.Equal(1, missing);
    }

    [Fact]
    public void AbsolutePathsAreCountedSeparately()
    {
        var (missing, absolute) = ExternalResources.CountResourceWarnings(
            [File("a.mat", absolute: true), File("b.mat", missing: true, absolute: true)]);

        Assert.Equal(1, missing);
        Assert.Equal(2, absolute);
    }

    [Fact]
    public void NothingReferencedIsZero()
    {
        var (missing, absolute) = ExternalResources.CountResourceWarnings([]);

        Assert.Equal(0, missing);
        Assert.Equal(0, absolute);
    }

    [Theory]
    [InlineData("diagram.png")]     // images - not in the default type selection
    [InlineData("manual.pdf")]      // documents - likewise
    [InlineData("data.weird")]      // other, the catch-all
    [InlineData("LICENSE")]         // other, by having no extension at all
    public void AMissingResourceIsShownEvenWhenItsTypeIsNotSelected(string name)
    {
        // The half that emptied the tree. Every one of these is counted, so every one has to be
        // reachable, or the chip is promising something the filter will not deliver.
        var node = File(name, missing: true);

        Assert.True(ExternalResources.NodePassesFilters(node, MissingOnly, DefaultFileTypes));
    }

    [Fact]
    public void AMissingResourceOfASelectedTypeIsStillShown()
    {
        Assert.True(ExternalResources.NodePassesFilters(File("table.mat", missing: true), MissingOnly, DefaultFileTypes));
    }

    [Fact]
    public void AResourceThatIsNotMissingIsNotShownUnderTheMissingFilter()
    {
        // The control: overriding the type filter must not turn the warning filter off as well.
        Assert.False(ExternalResources.NodePassesFilters(File("table.mat"), MissingOnly, DefaultFileTypes));
    }

    [Fact]
    public void WithNoWarningFilterTheFileTypeFilterStillApplies()
    {
        // Ordinary browsing is unchanged; the override is only while a warning chip is selected.
        Assert.True(ExternalResources.NodePassesFilters(File("table.mat"), NoWarningFilter, DefaultFileTypes));
        Assert.False(ExternalResources.NodePassesFilters(File("diagram.png"), NoWarningFilter, DefaultFileTypes));
    }

    [Fact]
    public void EveryCountedMissingResourceIsReachable()
    {
        // The two halves stated as one property, which is the thing the report was really about:
        // whatever the chip counts, selecting it shows.
        ResourceTreeNode[] nodes =
        [
            File("table.mat", missing: true),
            File("diagram.png", missing: true),
            File("manual.pdf", missing: true),
            File("LICENSE", missing: true),
            File("present.mat")
        ];

        var (missing, _) = ExternalResources.CountResourceWarnings(nodes);
        var shown = nodes.Count(n => ExternalResources.NodePassesFilters(n, MissingOnly, DefaultFileTypes));

        Assert.Equal(missing, shown);
        Assert.Equal(4, shown);
    }
}
