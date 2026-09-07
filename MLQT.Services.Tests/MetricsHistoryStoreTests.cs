using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModelicaGraph.Analysis;
using MLQT.Services.Helpers;
using Xunit;

namespace MLQT.Services.Tests;

public class MetricsHistoryStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"mlqt-metrics-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private static MetricsSnapshot Snap(DateTime t, double desc)
        => new(t, "All", 10, new Dictionary<string, double> { ["Class description"] = desc });

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
        => Assert.Empty(MetricsHistoryStore.Load(_path));

    [Fact]
    public void RepoPath_PointsAtSharedMlqtFile()
    {
        var p = MetricsHistoryStore.RepoPath(Path.Combine("C:", "libs", "MyLib"));
        Assert.EndsWith(Path.Combine(".mlqt", "metrics-history.json"), p);
        Assert.StartsWith(Path.Combine("C:", "libs", "MyLib"), p);
    }

    [Fact]
    public void Append_ThenLoad_RoundTrips()
    {
        MetricsHistoryStore.Append(_path, Snap(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 40));
        MetricsHistoryStore.Append(_path, Snap(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), 55));

        var history = MetricsHistoryStore.Load(_path);
        Assert.Equal(2, history.Count);
        Assert.Equal(40, history[0].Coverage["Class description"]);
        Assert.Equal(55, history[1].Coverage["Class description"]);   // appended in order, oldest first
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmpty()
    {
        File.WriteAllText(_path, "{ not json");
        Assert.Empty(MetricsHistoryStore.Load(_path));
    }

    [Fact]
    public void From_ProjectsCoveragePercentages()
    {
        var metrics = new LibraryMetrics(5, new Dictionary<string, int>(), 0,
            new[] { new CoverageMetric("Class description", 3, 4) });   // 75%
        var snap = MetricsSnapshot.From(metrics, "Modelica.Blocks", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("Modelica.Blocks", snap.Scope);
        Assert.Equal(5, snap.TotalClasses);
        Assert.Equal(75.0, snap.Coverage["Class description"]);
    }

    // --- AppendIfChanged ------------------------------------------------------------------------
    // The safety valve for recording from CI: a job that commits the history file triggers a build of
    // its own commit, and appending an identical point there would commit again, forever.

    private static MetricsSnapshot SnapRev(DateTime t, double desc, string? revision)
        => new(t, "All", 10, new Dictionary<string, double> { ["Class description"] = desc }, null, null, revision);

    [Fact]
    public void AppendIfChanged_FirstPoint_IsAppended()
    {
        var (outcome, history) = MetricsHistoryStore.AppendIfChanged(_path, SnapRev(new DateTime(2026, 1, 1), 50, "r1"));

        Assert.Equal(MetricsHistoryStore.AppendOutcome.Appended, outcome);
        Assert.Single(history);
    }

    [Fact]
    public void AppendIfChanged_SameRevision_IsSkipped()
    {
        MetricsHistoryStore.AppendIfChanged(_path, SnapRev(new DateTime(2026, 1, 1), 50, "r1"));

        // Rebuilding the same commit must not stack a second point on it.
        var (outcome, history) = MetricsHistoryStore.AppendIfChanged(_path, SnapRev(new DateTime(2026, 1, 2), 60, "r1"));

        Assert.Equal(MetricsHistoryStore.AppendOutcome.RevisionAlreadyRecorded, outcome);
        Assert.Single(history);
    }

    [Fact]
    public void AppendIfChanged_NewRevisionButSameNumbers_IsSkipped()
    {
        // This is the loop breaker: CI's own commit changes the revision but not the metrics.
        MetricsHistoryStore.AppendIfChanged(_path, SnapRev(new DateTime(2026, 1, 1), 50, "r1"));

        var (outcome, history) = MetricsHistoryStore.AppendIfChanged(_path, SnapRev(new DateTime(2026, 1, 2), 50, "r2"));

        Assert.Equal(MetricsHistoryStore.AppendOutcome.Unchanged, outcome);
        Assert.Single(history);
    }

    [Fact]
    public void AppendIfChanged_NumbersMoved_IsAppended()
    {
        MetricsHistoryStore.AppendIfChanged(_path, SnapRev(new DateTime(2026, 1, 1), 50, "r1"));

        var (outcome, history) = MetricsHistoryStore.AppendIfChanged(_path, SnapRev(new DateTime(2026, 1, 2), 51, "r2"));

        Assert.Equal(MetricsHistoryStore.AppendOutcome.Appended, outcome);
        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void AppendIfChanged_ComparesAgainstTheLatestPointOfTheSameScopeOnly()
    {
        // A per-package snapshot must not make a whole-library one look unchanged.
        MetricsHistoryStore.Append(_path, new MetricsSnapshot(
            new DateTime(2026, 1, 1), "Some.Package", 10, new Dictionary<string, double> { ["Class description"] = 50 }));

        var (outcome, _) = MetricsHistoryStore.AppendIfChanged(_path, SnapRev(new DateTime(2026, 1, 2), 50, "r1"));

        Assert.Equal(MetricsHistoryStore.AppendOutcome.Appended, outcome);
    }

    [Fact]
    public void AppendIfChanged_NoRevision_StillSkipsAnUnchangedPoint()
    {
        // A library outside a working copy has no revision to compare; the numbers still gate.
        MetricsHistoryStore.AppendIfChanged(_path, SnapRev(new DateTime(2026, 1, 1), 50, null));

        var (outcome, _) = MetricsHistoryStore.AppendIfChanged(_path, SnapRev(new DateTime(2026, 1, 2), 50, null));

        Assert.Equal(MetricsHistoryStore.AppendOutcome.Unchanged, outcome);
    }

}
