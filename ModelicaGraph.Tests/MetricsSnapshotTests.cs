using System;
using System.Collections.Generic;
using System.Linq;
using ModelicaGraph.Analysis;
using Xunit;

namespace ModelicaGraph.Tests;

public class MetricsSnapshotTests
{
    private static readonly DateTime T = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static MetricsSnapshot Snap(DateTime t, int classes, int compliant, int eligible)
        => new(t, "", classes,
            new Dictionary<string, double> { ["Description"] = eligible == 0 ? 100.0 : Math.Round(100.0 * compliant / eligible, 1) },
            new Dictionary<string, CoverageCount> { ["Description"] = new(compliant, eligible) });

    [Fact]
    public void AggregateByTimestamp_SameTimestamp_SumsCountsExactly()
    {
        var a = Snap(T, 4, 3, 4);   // 75%
        var b = Snap(T, 6, 1, 6);   // ~16.7%

        var combined = Assert.Single(MetricsSnapshot.AggregateByTimestamp(new[] { a, b }));

        Assert.Equal(10, combined.TotalClasses);
        Assert.Equal(40.0, combined.Coverage["Description"]);           // 4 compliant / 10 eligible, not (75+16.7)/2
        Assert.Equal(4, combined.Counts!["Description"].Compliant);
        Assert.Equal(10, combined.Counts["Description"].Eligible);
    }

    [Fact]
    public void AggregateByTimestamp_DifferentTimestamps_NotCombined()
    {
        var a = Snap(T, 4, 3, 4);
        var b = Snap(T.AddDays(1), 6, 1, 6);

        Assert.Equal(2, MetricsSnapshot.AggregateByTimestamp(new[] { a, b }).Count);
    }

    [Fact]
    public void AggregateByTimestamp_MissingCounts_FallsBackToClassWeightedAverage()
    {
        var withCounts = Snap(T, 10, 8, 10);   // 80%
        var legacy = new MetricsSnapshot(T, "", 30,
            new Dictionary<string, double> { ["Description"] = 40.0 }, null);   // pre-counts snapshot

        var combined = Assert.Single(MetricsSnapshot.AggregateByTimestamp(new[] { withCounts, legacy }));

        // weighted by TotalClasses: (80*10 + 40*30) / 40 = 50
        Assert.Equal(50.0, combined.Coverage["Description"]);
        Assert.Null(combined.Counts);   // not exact, so counts are dropped
    }

    [Fact]
    public void AggregateByTimestamp_SingleSnapshot_PassesThrough()
    {
        var a = Snap(T, 4, 3, 4);
        var only = Assert.Single(MetricsSnapshot.AggregateByTimestamp(new[] { a }));
        Assert.Same(a, only);
    }
}
