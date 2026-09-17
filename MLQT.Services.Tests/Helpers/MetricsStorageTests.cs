using MLQT.Services.DataTypes;
using MLQT.Services.Helpers;
using Xunit;

namespace MLQT.Services.Tests.Helpers;

/// <summary>
/// Where a metrics snapshot goes, and which histories the trend reads (backlog B119).
/// </summary>
/// <remarks>
/// The decisions were inline in <c>MetricsDashboard.razor.cs</c>, reachable only by rendering the
/// page. They are the half with consequences: a snapshot in the wrong file either dirties a checkout
/// the user marked hands-off, or writes a vendor library's numbers into the user's own history, where
/// they read as their code getting worse.
/// </remarks>
public class MetricsStorageTests
{
    private static LoadedLibrary Library(string name, string? repositoryId = null, params string[] models) =>
        new() { Name = name, RepositoryId = repositoryId, ModelIds = [.. models] };

    [Fact]
    public void ALibraryWithNoRepositoryGoesToThePerUserHistory()
    {
        // A library loaded from a file or a directory has nowhere shared to put a snapshot.
        var destination = MetricsStorage.DestinationFor(null);

        Assert.False(destination.Shared);
        Assert.Equal(MetricsHistoryStore.DefaultPath, destination.Path);
        Assert.Equal(destination, MetricsStorage.DestinationFor(""));
    }

    [Fact]
    public void ARepositoryBackedLibraryGoesToTheCommittedFile()
    {
        var destination = MetricsStorage.DestinationFor(Path.Combine("C:", "repos", "Lib"));

        Assert.True(destination.Shared);
        Assert.Equal(MetricsHistoryStore.RepoPath(Path.Combine("C:", "repos", "Lib")), destination.Path);
        Assert.Contains(".mlqt", destination.Path);      // the folder that travels with the code
    }

    [Fact]
    public void LibrariesSharingARepositoryShareItsFile()
    {
        var groups = MetricsStorage.GroupByDestination(
            [Library("A", "repo1", "A.One"), Library("B", "repo1", "B.One"), Library("C", null, "C.One")],
            lib => lib.RepositoryId == "repo1" ? Path.Combine("C:", "repos", "One") : null,
            _ => false);

        Assert.Equal(2, groups.Count);

        var shared = Assert.Single(groups, g => g.Destination.Shared);
        Assert.Equal(["A", "B"], shared.Libraries.Select(l => l.Name));

        var perUser = Assert.Single(groups, g => !g.Destination.Shared);
        Assert.Equal(["C"], perUser.Libraries.Select(l => l.Name));
    }

    [Fact]
    public void AReferenceOnlyLibraryIsNotMeasuredAndNothingIsWrittenForIt()
    {
        // The one with teeth. Writing here dirties a checkout the user marked hands-off, and for a
        // reference library with no repository at all it lands in the per-user history - which on a
        // machine with a tool's library folder configured meant a trend describing a vendor's library.
        var groups = MetricsStorage.GroupByDestination(
            [Library("Ours", "repo1", "Ours.One"), Library("Vendor", null, "Vendor.One")],
            _ => Path.Combine("C:", "repos", "One"),
            lib => lib.Name == "Vendor");

        var group = Assert.Single(groups);
        Assert.Equal(["Ours"], group.Libraries.Select(l => l.Name));
    }

    [Fact]
    public void EveryLibraryBeingReferenceOnlyWritesNothingAtAll()
    {
        Assert.Empty(MetricsStorage.GroupByDestination(
            [Library("Vendor", null, "Vendor.One")], _ => null, _ => true));
    }

    [Fact]
    public void AScopeIsOwnedByTheLibraryThatContainsIt()
    {
        var libraries = new[] { Library("A", "repo1", "A.One", "A.Two"), Library("B", "repo2", "B.One") };

        Assert.Equal("repo1", MetricsStorage.OwningRepositoryId("A.Two", libraries));
        Assert.Equal("repo2", MetricsStorage.OwningRepositoryId("B.One", libraries));
        Assert.Null(MetricsStorage.OwningRepositoryId("Unknown.Class", libraries));
    }

    [Fact]
    public void AScopeInALibraryWithNoRepositoryIsOwnedByNone()
    {
        Assert.Null(MetricsStorage.OwningRepositoryId("C.One", [Library("C", null, "C.One")]));
    }

    [Fact]
    public void TheAllLibrariesScopeResolvesOnlyWhenOneRepositoryIsLoaded()
    {
        // With several it spans them all, and no single repository's revision could honestly be
        // claimed to describe the snapshot - so it is written per library instead.
        Assert.Equal("repo1", MetricsStorage.OwningRepositoryId("", [Library("A", "repo1", "A.One")]));

        Assert.Null(MetricsStorage.OwningRepositoryId(
            "", [Library("A", "repo1", "A.One"), Library("B", "repo2", "B.One")]));

        Assert.Null(MetricsStorage.OwningRepositoryId("", [Library("C", null, "C.One")]));

        // Two libraries from the same repository is still one repository.
        Assert.Equal("repo1", MetricsStorage.OwningRepositoryId(
            "", [Library("A", "repo1", "A.One"), Library("B", "repo1", "B.One")]));
    }

    [Fact]
    public void TheTrendReadsEveryLoadedRepositorysHistoryAndThePerUserOne()
    {
        var files = MetricsStorage.HistoryFilesToRead(
        [
            new Repository { LocalPath = Path.Combine("C:", "repos", "One") },
            new Repository { LocalPath = Path.Combine("C:", "repos", "Two") },
        ]);

        Assert.Equal(3, files.Count);
        Assert.Contains(MetricsHistoryStore.RepoPath(Path.Combine("C:", "repos", "One")), files);
        Assert.Contains(MetricsHistoryStore.RepoPath(Path.Combine("C:", "repos", "Two")), files);
        Assert.Contains(MetricsHistoryStore.DefaultPath, files);
    }

    [Fact]
    public void AReferenceOnlyRepositorysHistoryIsNotMerged()
    {
        // It may carry its owner's own file. Those points describe classes this report does not
        // measure, so merging them moves the trend - and at the "all libraries" scope they would be
        // aggregated into our own points by timestamp.
        var files = MetricsStorage.HistoryFilesToRead(
        [
            new Repository { LocalPath = Path.Combine("C:", "repos", "Ours") },
            new Repository { LocalPath = Path.Combine("C:", "repos", "Vendor"), IsReferenceOnly = true },
            new Repository { LocalPath = "" },
        ]);

        Assert.DoesNotContain(MetricsHistoryStore.RepoPath(Path.Combine("C:", "repos", "Vendor")), files);
        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void AFileIsReadOnceEvenWhenTwoRepositoriesPointAtIt()
    {
        // Two repositories checked out to the same path would otherwise have every snapshot counted
        // twice, which at the aggregate scope doubles the numbers rather than duplicating a row.
        var files = MetricsStorage.HistoryFilesToRead(
        [
            new Repository { LocalPath = Path.Combine("C:", "repos", "Same") },
            new Repository { LocalPath = Path.Combine("C:", "repos", "Same") },
        ]);

        Assert.Equal(2, files.Count);   // the shared file once, plus the per-user one
    }
}
