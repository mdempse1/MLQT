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
            new Dictionary<string, double> { ["Class description"] = eligible == 0 ? 100.0 : Math.Round(100.0 * compliant / eligible, 1) },
            new Dictionary<string, CoverageCount> { ["Class description"] = new(compliant, eligible) });

    [Fact]
    public void AggregateByTimestamp_SameTimestamp_SumsCountsExactly()
    {
        var a = Snap(T, 4, 3, 4);   // 75%
        var b = Snap(T, 6, 1, 6);   // ~16.7%

        var combined = Assert.Single(MetricsSnapshot.AggregateByTimestamp(new[] { a, b }));

        Assert.Equal(10, combined.TotalClasses);
        Assert.Equal(40.0, combined.Coverage["Class description"]);           // 4 compliant / 10 eligible, not (75+16.7)/2
        Assert.Equal(4, combined.Counts!["Class description"].Compliant);
        Assert.Equal(10, combined.Counts["Class description"].Eligible);
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
            new Dictionary<string, double> { ["Class description"] = 40.0 }, null);   // pre-counts snapshot

        var combined = Assert.Single(MetricsSnapshot.AggregateByTimestamp(new[] { withCounts, legacy }));

        // weighted by TotalClasses: (80*10 + 40*30) / 40 = 50
        Assert.Equal(50.0, combined.Coverage["Class description"]);
        Assert.Null(combined.Counts);   // not exact, so counts are dropped
    }

    [Fact]
    public void AggregateByTimestamp_SingleSnapshot_PassesThrough()
    {
        var a = Snap(T, 4, 3, 4);
        var only = Assert.Single(MetricsSnapshot.AggregateByTimestamp(new[] { a }));
        Assert.Same(a, only);
    }

    [Fact]
    public void AggregateByTimestamp_SumsFindings_WhenAllPresent()
    {
        var a = new MetricsSnapshot(T, "", 4, new() { ["Class description"] = 75 }, null, 10);
        var b = new MetricsSnapshot(T, "", 6, new() { ["Class description"] = 50 }, null, 5);

        var combined = Assert.Single(MetricsSnapshot.AggregateByTimestamp(new[] { a, b }));

        Assert.Equal(15, combined.Findings);
    }

    [Fact]
    public void AggregateByTimestamp_NullFindings_WhenAnyMissing()
    {
        var a = new MetricsSnapshot(T, "", 4, new() { ["Class description"] = 75 }, null, 10);
        var b = new MetricsSnapshot(T, "", 6, new() { ["Class description"] = 50 }, null, null);

        var combined = Assert.Single(MetricsSnapshot.AggregateByTimestamp(new[] { a, b }));

        Assert.Null(combined.Findings);
    }

    // --- HasSameMetricsAs -----------------------------------------------------------------------
    // Identity of the measurement, not of the record: timestamp/revision are metadata about when it
    // was taken. Used to decide whether a new point would say anything.

    private static MetricsSnapshot S(double desc, int classes = 10, int? findings = 3,
        (int compliant, int eligible)? counts = null)
        => new(new DateTime(2026, 1, 1), "", classes,
            new Dictionary<string, double> { ["Class description"] = desc },
            counts is null ? null : new Dictionary<string, CoverageCount> { ["Class description"] = new(counts.Value.compliant, counts.Value.eligible) },
            findings);

    [Fact]
    public void HasSameMetricsAs_IdenticalNumbers_True()
        => Assert.True(S(50).HasSameMetricsAs(S(50) with { TimestampUtc = new DateTime(2027, 5, 5), Revision = "other" }));

    [Fact]
    public void HasSameMetricsAs_Null_False()
        => Assert.False(S(50).HasSameMetricsAs(null));

    [Fact]
    public void HasSameMetricsAs_DifferentCoverage_False()
        => Assert.False(S(50).HasSameMetricsAs(S(51)));

    [Fact]
    public void HasSameMetricsAs_DifferentFindings_False()
        => Assert.False(S(50).HasSameMetricsAs(S(50, findings: 4)));

    [Fact]
    public void HasSameMetricsAs_DifferentClassCount_False()
        => Assert.False(S(50).HasSameMetricsAs(S(50, classes: 11)));

    [Fact]
    public void HasSameMetricsAs_SameRoundedPercentButDifferentCounts_False()
    {
        // 500/1000 and 501/1002 both round to 50.0; the underlying library did change.
        var a = S(50.0, counts: (500, 1000));
        var b = S(50.0, counts: (501, 1002));

        Assert.False(a.HasSameMetricsAs(b));
    }

    [Fact]
    public void HasSameMetricsAs_MissingDimension_False()
    {
        var a = S(50);
        var b = new MetricsSnapshot(new DateTime(2026, 1, 1), "", 10,
            new Dictionary<string, double> { ["Icon"] = 50 }, null, 3);

        Assert.False(a.HasSameMetricsAs(b));
    }

}
