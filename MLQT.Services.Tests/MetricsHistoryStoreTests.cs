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
        => new(t, "All", 10, new Dictionary<string, double> { ["Description"] = desc });

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
        => Assert.Empty(MetricsHistoryStore.Load(_path));

    [Fact]
    public void Append_ThenLoad_RoundTrips()
    {
        MetricsHistoryStore.Append(_path, Snap(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 40));
        MetricsHistoryStore.Append(_path, Snap(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), 55));

        var history = MetricsHistoryStore.Load(_path);
        Assert.Equal(2, history.Count);
        Assert.Equal(40, history[0].Coverage["Description"]);
        Assert.Equal(55, history[1].Coverage["Description"]);   // appended in order, oldest first
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
            new[] { new CoverageMetric("Description", 3, 4) });   // 75%
        var snap = MetricsSnapshot.From(metrics, "Modelica.Blocks", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("Modelica.Blocks", snap.Scope);
        Assert.Equal(5, snap.TotalClasses);
        Assert.Equal(75.0, snap.Coverage["Description"]);
    }
}
