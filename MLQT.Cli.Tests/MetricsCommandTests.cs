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
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "mlqt-metrics-test-" + Guid.NewGuid().ToString("N"));

        public TempLibrary() => Directory.CreateDirectory(Path);

        public TempLibrary WithModel(string fileName, string content)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, fileName), content);
            return this;
        }

        public TempLibrary WithSettings(string json)
        {
            var dir = System.IO.Path.Combine(Path, ".mlqt");
            Directory.CreateDirectory(dir);
            File.WriteAllText(System.IO.Path.Combine(dir, "settings.json"), json);
            return this;
        }

        public string MetricsPath => System.IO.Path.Combine(Path, ".mlqt", "metrics-history.json");

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    private static (int code, string stdout, string stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CliEntry.RunAsync(args, stdout, stderr).GetAwaiter().GetResult();
        return (code, stdout.ToString(), stderr.ToString());
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
        Run("check", lib.Path, "--no-color");

        Assert.False(File.Exists(lib.MetricsPath));
    }

    [Fact]
    public void Metrics_WritesAPointToTheSharedMlqtFile()
    {
        using var lib = Fixture();
        Run("check", lib.Path, "--metrics", "--no-color");

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
        Run("check", lib.Path, "--metrics", "--no-color");

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
        Run("check", lib.Path, "--metrics", "--no-color");

        var point = Assert.Single(Points(lib.MetricsPath));
        Assert.Equal("", point.GetProperty("Scope").GetString());
    }

    [Fact]
    public void Metrics_EachLibraryScopeCountsOnlyItsOwnClasses()
    {
        using var lib = TwoLibraries();
        Run("check", lib.Path, "--metrics", "--no-color");

        var whole = Assert.Single(Points(lib.MetricsPath), p => p.GetProperty("Scope").GetString() == "");
        var one = Assert.Single(Points(lib.MetricsPath), p => p.GetProperty("Scope").GetString() == "One");

        Assert.True(one.GetProperty("TotalClasses").GetInt32() < whole.GetProperty("TotalClasses").GetInt32());
    }

    [Fact]
    public void Metrics_RecordsTheViolationCount()
    {
        // One undescribed class => one finding, and the trend should show it.
        using var lib = Fixture(TwoClassesPlusUndocumented);
        Run("check", lib.Path, "--metrics", "--no-color");

        var point = Assert.Single(Points(lib.MetricsPath), p => p.GetProperty("Scope").GetString() == "");
        Assert.Equal(1, point.GetProperty("Violations").GetInt32());
    }

    [Fact]
    public void MetricsOut_RedirectsAwayFromTheRepository()
    {
        // The "collect as a CI artifact instead of committing" route.
        using var lib = Fixture();
        var outside = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mlqt-hist-{Guid.NewGuid():N}.json");
        try
        {
            Run("check", lib.Path, "--metrics-out", outside, "--no-color");

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
        Run("check", lib.Path, "--metrics", "--no-color");
        var afterFirst = File.ReadAllText(lib.MetricsPath);

        var (_, _, stderr) = Run("check", lib.Path, "--metrics", "--no-color");

        Assert.Equal(afterFirst, File.ReadAllText(lib.MetricsPath));
        Assert.Contains("unchanged", stderr);
    }

    [Fact]
    public void MetricsForce_RecordsEvenWhenUnchanged()
    {
        using var lib = Fixture();
        Run("check", lib.Path, "--metrics", "--no-color");

        Run("check", lib.Path, "--metrics-force", "--no-color");

        // Every scope is re-recorded, so the count doubles.
        Assert.Equal(2, Points(lib.MetricsPath).Length);
    }

    [Fact]
    public void ChangedLibrary_RecordsANewPoint()
    {
        using var lib = Fixture();
        Run("check", lib.Path, "--metrics", "--no-color");

        lib.WithModel("Lib.mo", TwoClassesPlusUndocumented);   // coverage drops
        Run("check", lib.Path, "--metrics", "--no-color");

        Assert.Equal(2, Points(lib.MetricsPath).Count(p => p.GetProperty("Scope").GetString() == ""));
    }

    [Fact]
    public void RecordingIsIndependentOfTheGateResult()
    {
        // A failing build is exactly the one whose numbers you want on the trend.
        using var lib = Fixture(TwoClassesPlusUndocumented);

        var (code, _, _) = Run("check", lib.Path, "--metrics", "--fail-on", "warning", "--no-color");

        Assert.Equal(1, code);
        Assert.NotEmpty(Points(lib.MetricsPath));
    }

    [Fact]
    public void UnwritableMetricsPath_WarnsButDoesNotChangeTheExitCode()
    {
        // Recording is an extra, not the job.
        using var lib = Fixture();
        var badPath = System.IO.Path.Combine(lib.Path, "no-such-dir\0bad", "history.json");

        var (code, _, stderr) = Run("check", lib.Path, "--metrics-out", badPath, "--no-color");

        Assert.Equal(0, code);
        Assert.Contains("could not record metrics", stderr);
    }
}
