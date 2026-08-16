namespace ModelicaGraph.Analysis;

/// <summary>
/// A point-in-time record of the coverage numbers, for the debt-burndown trend. Stores the coverage
/// percentages by dimension (not the raw compliant/eligible) so the history file stays small and
/// stable. Timestamps are supplied by the caller (UTC) so the record itself is deterministic.
/// </summary>
public sealed record MetricsSnapshot(
    DateTime TimestampUtc,
    string? Scope,
    int TotalClasses,
    Dictionary<string, double> Coverage)
{
    /// <summary>Build a snapshot from a computed <see cref="LibraryMetrics"/> for a scope (a package id
    /// like "Modelica.Blocks", or "" for all loaded libraries) at the given time.</summary>
    public static MetricsSnapshot From(LibraryMetrics metrics, string scope, DateTime timestampUtc)
        => new(timestampUtc, scope ?? string.Empty, metrics.TotalClasses,
            metrics.Coverage.ToDictionary(c => c.Dimension, c => c.Percent, StringComparer.Ordinal));
}
