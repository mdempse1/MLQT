using System.Text.Json;
using MLQT.Cli;

namespace MLQT.Cli.Tests;

/// <summary>
/// `mlqt check --metrics` records a coverage point into the same history file the desktop Coverage
/// dashboard reads, so CI builds the burndown instead of it depending on someone pressing a button.
/// </summary>
public class MetricsCommandTests
{
    private const string TwoClasses = """
        model A "described"
          parameter Real x = 1.0 "described";
        end A;
        """;

    private const string TwoClassesPlusUndocumented = """
        model A "described"
          parameter Real x = 1.0 "described";
        end A;

        model B
          parameter Real y = 2.0;
        end B;
        """;

    private sealed class TempLibrary : IDisposable
    {
        private readonly TempWorkspace _workspace = new("mlqt-metrics");

        public string Path => _workspace.Root;
        public string MetricsPath => _workspace.PathTo(".mlqt", "metrics-history.json");

        public TempLibrary WithModel(string fileName, string content)
        {
            _workspace.Write(fileName, content);
            return this;
        }

        public TempLibrary WithSettings(string json)
        {
            _workspace.WithSettings(json);
            return this;
        }

        public void Dispose() => _workspace.Dispose();
    }

    private static JsonElement[] Points(string path)
        => JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateArray().ToArray();

    private static TempLibrary Fixture(string content = TwoClasses) =>
        new TempLibrary()
            .WithModel("Lib.mo", content)
            .WithSettings("""{ "ClassHasDescription": true }""");

    [Fact]
    public void WithoutTheFlag_NoHistoryIsWritten()
    {
        using var lib = Fixture();
        Cli.Run("check", lib.Path, "--no-color");

        Assert.False(File.Exists(lib.MetricsPath));
    }

    [Fact]
    public void Metrics_WritesAPointToTheSharedMlqtFile()
    {
        using var lib = Fixture();
        Cli.Run("check", lib.Path, "--metrics", "--no-color");

        // Scope "" is the whole checked set — what the dashboard's "all libraries" view reads.
        var point = Assert.Single(Points(lib.MetricsPath), p => p.GetProperty("Scope").GetString() == "");
        Assert.True(point.GetProperty("TotalClasses").GetInt32() > 0);
        Assert.True(point.TryGetProperty("Coverage", out _));
    }

    // A repository holding two real libraries, each a package — the shape the scope filter is for.
    private const string PackageOne = """
        within;
        package One "library one"
          model A "described"
            parameter Real x = 1.0 "described";
          end A;
        end One;
        """;

    private const string PackageTwo = """
        within;
        package Two "library two"
          model B "described"
          end B;
          model C "described"
          end C;
        end Two;
        """;

    private static TempLibrary TwoLibraries() =>
        new TempLibrary()
            .WithModel("One.mo", PackageOne)
            .WithModel("Two.mo", PackageTwo)
            .WithSettings("""{ "ClassHasDescription": true }""");

    [Fact]
    public void Metrics_AlsoWritesAPointPerLibrary()
    {
        // The dashboard's scope filter matches a snapshot's Scope against the selected package id
        // exactly, so without these a library shows current coverage but an empty trend.
        using var lib = TwoLibraries();
        Cli.Run("check", lib.Path, "--metrics", "--no-color");

        var scopes = Points(lib.MetricsPath).Select(p => p.GetProperty("Scope").GetString()).ToList();

        Assert.Contains("", scopes);
        Assert.Contains("One", scopes);
        Assert.Contains("Two", scopes);
    }

    [Fact]
    public void Metrics_OnlyPackagesGetTheirOwnScope()
    {
        // A flat folder of loose classes has no library packages, so only the whole-set point is
        // recorded — a scope per class could never be selected in the dashboard anyway.
        using var lib = Fixture();
        Cli.Run("check", lib.Path, "--metrics", "--no-color");

        var point = Assert.Single(Points(lib.MetricsPath));
        Assert.Equal("", point.GetProperty("Scope").GetString());
    }

    [Fact]
    public void Metrics_EachLibraryScopeCountsOnlyItsOwnClasses()
    {
        using var lib = TwoLibraries();
        Cli.Run("check", lib.Path, "--metrics", "--no-color");

        var whole = Assert.Single(Points(lib.MetricsPath), p => p.GetProperty("Scope").GetString() == "");
        var one = Assert.Single(Points(lib.MetricsPath), p => p.GetProperty("Scope").GetString() == "One");

        Assert.True(one.GetProperty("TotalClasses").GetInt32() < whole.GetProperty("TotalClasses").GetInt32());
    }

    [Fact]
    public void Metrics_RecordsTheFindingCount()
    {
        // One undescribed class => one finding, and the trend should show it.
        using var lib = Fixture(TwoClassesPlusUndocumented);
        Cli.Run("check", lib.Path, "--metrics", "--no-color");

        var point = Assert.Single(Points(lib.MetricsPath), p => p.GetProperty("Scope").GetString() == "");
        Assert.Equal(1, point.GetProperty("Findings").GetInt32());
    }

    [Fact]
    public void MetricsOut_RedirectsAwayFromTheRepository()
    {
        // The "collect as a CI artifact instead of committing" route.
        using var lib = Fixture();
        var outside = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mlqt-hist-{Guid.NewGuid():N}.json");
        try
        {
            Cli.Run("check", lib.Path, "--metrics-out", outside, "--no-color");

            Assert.True(File.Exists(outside));
            Assert.False(File.Exists(lib.MetricsPath));
        }
        finally { if (File.Exists(outside)) File.Delete(outside); }
    }

    [Fact]
    public void SecondRunWithNoChange_DoesNotTouchTheFile()
    {
        // The loop breaker. A CI job that commits this file triggers a build of its own commit; if that
        // build appended an identical point it would commit again, forever.
        using var lib = Fixture();
        Cli.Run("check", lib.Path, "--metrics", "--no-color");
        var afterFirst = File.ReadAllText(lib.MetricsPath);

        var (_, _, stderr) = Cli.Run("check", lib.Path, "--metrics", "--no-color");

        Assert.Equal(afterFirst, File.ReadAllText(lib.MetricsPath));
        Assert.Contains("unchanged", stderr);
    }

    [Fact]
    public void MetricsForce_RecordsEvenWhenUnchanged()
    {
        using var lib = Fixture();
        Cli.Run("check", lib.Path, "--metrics", "--no-color");

        Cli.Run("check", lib.Path, "--metrics-force", "--no-color");

        // Every scope is re-recorded, so the count doubles.
        Assert.Equal(2, Points(lib.MetricsPath).Length);
    }

    [Fact]
    public void ChangedLibrary_RecordsANewPoint()
    {
        using var lib = Fixture();
        Cli.Run("check", lib.Path, "--metrics", "--no-color");

        lib.WithModel("Lib.mo", TwoClassesPlusUndocumented);   // coverage drops
        Cli.Run("check", lib.Path, "--metrics", "--no-color");

        Assert.Equal(2, Points(lib.MetricsPath).Count(p => p.GetProperty("Scope").GetString() == ""));
    }

    [Fact]
    public void RecordingIsIndependentOfTheGateResult()
    {
        // A failing build is exactly the one whose numbers you want on the trend.
        using var lib = Fixture(TwoClassesPlusUndocumented);

        var (code, _, _) = Cli.Run("check", lib.Path, "--metrics", "--fail-on", "warning", "--no-color");

        Assert.Equal(1, code);
        Assert.NotEmpty(Points(lib.MetricsPath));
    }

    [Fact]
    public void UnwritableMetricsPath_WarnsButDoesNotChangeTheExitCode()
    {
        // Recording is an extra, not the job.
        using var lib = Fixture();
        var badPath = System.IO.Path.Combine(lib.Path, "no-such-dir\0bad", "history.json");

        var (code, _, stderr) = Cli.Run("check", lib.Path, "--metrics-out", badPath, "--no-color");

        Assert.Equal(0, code);
        Assert.Contains("could not record metrics", stderr);
    }

    // ---- the history goes where the settings came from, not beside the library (B56) ------------

    [Fact]
    public void ALibraryInASubdirectory_RecordsIntoTheRepositorysHistory()
    {
        // The settings are read from the repository's .mlqt, and the desktop app keeps the history
        // there too. Composed from the library path instead, CI wrote a second file in
        // <repo>/Libraries/Lib/.mlqt that the Metrics tab never opened.
        using var workspace = new TempWorkspace("mlqt-metrics-sub");
        workspace.Write(System.IO.Path.Combine("Libraries", "Lib", "Lib.mo"), TwoClasses);
        workspace.WithSettings("""{ "ClassHasDescription": true }""");
        var library = workspace.PathTo("Libraries", "Lib");

        var (code, _, _) = Cli.Run("check", library, "--metrics", "--no-color");

        Assert.Equal(0, code);
        Assert.True(File.Exists(workspace.PathTo(".mlqt", "metrics-history.json")));
        Assert.False(File.Exists(System.IO.Path.Combine(library, ".mlqt", "metrics-history.json")));
    }

    [Fact]
    public void TheRatchetReadsBackWhatTheSameInvocationWrote()
    {
        // The half of B56 that is a gate rather than a file: --coverage-ratchet loads the history from
        // the same resolved path, so a repository with a perfectly good trend used to be told there
        // was nothing to compare against.
        using var workspace = new TempWorkspace("mlqt-metrics-ratchet");
        workspace.Write(System.IO.Path.Combine("Libraries", "Lib", "Lib.mo"), TwoClasses);
        workspace.WithSettings("""{ "ClassHasDescription": true }""");
        var library = workspace.PathTo("Libraries", "Lib");

        Cli.Run("check", library, "--metrics", "--no-color");
        var (_, _, stderr) = Cli.Run("check", library, "--coverage-ratchet", "--no-color");

        Assert.DoesNotContain("nothing to compare against yet", stderr);
    }

    [Fact]
    public void ALooseLibraryStillRecordsBesideItself()
    {
        // Nothing above it has a .mlqt and it is not in a working copy, so its own directory is the
        // only sensible home — which is also what the path meant before any of this.
        using var lib = Fixture();

        Cli.Run("check", lib.Path, "--metrics", "--no-color");

        Assert.True(File.Exists(lib.MetricsPath));
    }
}
