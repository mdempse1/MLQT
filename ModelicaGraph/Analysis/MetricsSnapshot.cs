namespace ModelicaGraph.Analysis;

/// <summary>
/// A point-in-time record of the coverage numbers, for the debt-burndown trend. Stores the coverage
/// percentages by dimension (not the raw compliant/eligible) so the history file stays small and
/// stable. Timestamps are supplied by the caller (UTC) so the record itself is deterministic.
/// </summary>
public sealed record MetricsSnapshot(
    DateTime TimestampUtc,
    int TotalClasses,
    Dictionary<string, double> Coverage)
{
    /// <summary>Build a snapshot from a computed <see cref="LibraryMetrics"/> at the given time.</summary>
    public static MetricsSnapshot From(LibraryMetrics metrics, DateTime timestampUtc)
        => new(timestampUtc, metrics.TotalClasses,
            metrics.Coverage.ToDictionary(c => c.Dimension, c => c.Percent, StringComparer.Ordinal));
}
