using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using MLQT.Services.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// A file reload followed by a re-check (the GUI's "Refresh libraries" button, and every VCS path)
/// must land on the same issue list a fresh load produces. Anything else means the number a user sees
/// depends on how they got there.
/// </summary>
public class RefreshPathParityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mlqt-refresh-" + Guid.NewGuid().ToString("N"));
    private string PackagePath => Path.Combine(_dir, "package.mo");

    // P.A is public and unreferenced (an unused-public-class finding); P.C uses P.B so B is alive.
    private const string Source = """
        within;
        package P "p"
          model A "unreferenced"
          end A;
          model B "used"
          end B;
          model C "uses B"
            B b;
          end C;
        end P;
        """;

    public RefreshPathParityTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PackagePath, Source);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static StyleCheckingSettings Settings() => new()
    {
        ClassHasDescription = true,
        CheckUnusedPublicClass = true,
    };

    private sealed class Fixture
    {
        public LibraryDataService Libraries { get; } = new();
        public CodeReviewService CodeReview { get; } = new();
        public RepositoryService Repositories { get; }
        public StyleCheckingService StyleChecking { get; }

        public Fixture()
        {
            var settingsService = new InMemorySettingsService();
            Repositories = new RepositoryService(Libraries, settingsService, new FileMonitoringService());
            StyleChecking = new StyleCheckingService(
                Libraries, Repositories, settingsService,
                new CustomDictionaryService(), new DictionaryManagerService(), CodeReview);
            // MainLayout routes findings into CodeReviewService; mirror that here.
            StyleChecking.OnFindingsFound += v => CodeReview.AddLogMessages(v);
        }

        public void WaitForChecking()
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            Thread.Sleep(100);
            while (StyleChecking.IsRunning && DateTime.UtcNow < deadline)
                Thread.Sleep(50);
            Thread.Sleep(700);   // final flush
        }

        public int IssueCount => CodeReview.LogMessages.Count;

        public Dictionary<string, int> ByRule() => CodeReview.LogMessages
            .GroupBy(m => m.RuleId ?? m.Summary)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>Registers the directory as a repository and runs the startup sequence over it.</summary>
    private async Task<Fixture> LoadAndCheckAsync()
    {
        var f = new Fixture();
        var added = await f.Repositories.AddRepositoryAsync(_dir, startMonitoring: false);
        Assert.True(added.Success, added.ErrorMessage);
        added.Repository!.StyleSettings = Settings();

        await f.Repositories.LoadLibrariesAsync(
            added.Repository.Id, added.DiscoveredLibraries.Select(d => d.RelativePath));

        // What MainLayout's startup does before checking.
        PackageCodeTrimmer.TrimStandaloneChildren(f.Libraries.CombinedGraph);
        await f.Libraries.EnsureDependenciesAnalyzedAsync();

        f.StyleChecking.StartBackgroundCheckingForRepositories(f.Repositories.Repositories);
        f.WaitForChecking();
        return f;
    }

    /// <summary>Replays what MainLayout.RefreshLibrariesAsync does after a file changes on disk.</summary>
    private async Task RefreshAsync(Fixture f, bool reanalyseDependencies)
    {
        var affected = await f.Libraries.UpdateChangedFilesAsync([PackagePath], _dir);

        // The reload dropped the affected models' edges. Restoring them is not optional: the graph
        // still reports itself as analysed, so an analyzer that needs edges runs either way.
        if (reanalyseDependencies && f.Libraries.CombinedGraph.DependenciesAnalyzed)
        {
            await GraphBuilder.AnalyzeDependenciesForModelsAsync(
                f.Libraries.CombinedGraph, affected, f.Libraries.GetLibraryInfos());
            f.Libraries.CombinedGraph.ReconcileDependencyEdges();
        }

        f.CodeReview.RemoveLogMessagesForModels(affected);
        await f.StyleChecking.CheckModelsAsync(affected, f.Libraries.CombinedGraph);
        f.WaitForChecking();
    }

    [Fact]
    public async Task RefreshAfterAnExternalFileChange_MatchesAFreshLoad()
    {
        // A branch switch outside the app, then Refresh: the result must equal a fresh load, which is
        // also what the CLI reports.
        var fresh = await LoadAndCheckAsync();
        var expected = fresh.ByRule();
        Assert.NotEmpty(expected);

        var refreshed = await LoadAndCheckAsync();
        await RefreshAsync(refreshed, reanalyseDependencies: true);

        Assert.Equal(expected, refreshed.ByRule());
    }

    [Fact]
    public async Task ReloadWithoutReanalysingDependencies_InventsUnusedClasses()
    {
        // Why the re-analysis above is mandatory rather than an optimisation. Skipping it leaves the
        // graph claiming to be analysed while the reloaded models have no edges, so classes that ARE
        // referenced look dead. This is what made the GUI's Refresh button report more issues than a
        // restart; the guard is now the graph's own DependenciesAnalyzed flag rather than a UI flag
        // that only the deferred pipeline ever set.
        var f = await LoadAndCheckAsync();
        var expected = f.IssueCount;

        await RefreshAsync(f, reanalyseDependencies: false);

        Assert.True(f.IssueCount > expected,
            $"expected the skipped re-analysis to over-report (was {expected}, got {f.IssueCount}); " +
            "if this no longer holds, check before deleting the guard it justifies");
    }

    [Fact]
    public async Task GraphStaysMarkedAnalysedAcrossAReload()
    {
        // The refresh path keys its decision off this, so pin the behaviour down: a reload replaces
        // nodes but does not un-analyse the graph, which is exactly why the edges must be rebuilt.
        var f = await LoadAndCheckAsync();
        Assert.True(f.Libraries.CombinedGraph.DependenciesAnalyzed);

        await f.Libraries.UpdateChangedFilesAsync([PackagePath], _dir);

        Assert.True(f.Libraries.CombinedGraph.DependenciesAnalyzed);
    }
}
