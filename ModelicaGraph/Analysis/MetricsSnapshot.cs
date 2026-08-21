namespace ModelicaGraph.Analysis;

/// <summary>Raw compliant/eligible counts for one coverage dimension, kept alongside the percentage so
/// snapshots from several repositories can be aggregated exactly (sum the counts, not average the
/// percentages). Older snapshots predate this and carry no counts.</summary>
public sealed record CoverageCount(int Compliant, int Eligible);

/// <summary>
/// A point-in-time record of the coverage numbers, for the debt-burndown trend. Stores the coverage
/// percentages by dimension for display, plus the raw compliant/eligible <see cref="Counts"/> so a
/// multi-repository "all libraries" view can combine per-repo snapshots exactly. Timestamps are
/// supplied by the caller (UTC) so the record itself is deterministic.
/// </summary>
public sealed record MetricsSnapshot(
    DateTime TimestampUtc,
    string? Scope,
    int TotalClasses,
    Dictionary<string, double> Coverage,
    Dictionary<string, CoverageCount>? Counts = null,
    int? Violations = null,
    string? Revision = null,
    string? Branch = null)
{
    /// <summary>
    /// True if this snapshot records the same numbers as <paramref name="other"/> — the timestamp,
    /// revision and branch are metadata about when it was taken, not part of the measurement.
    /// Used to avoid appending a point that would draw flat, which is what stops a CI job that commits
    /// its own history file from re-triggering itself forever.
    /// </summary>
    public bool HasSameMetricsAs(MetricsSnapshot? other)
    {
        if (other is null)
            return false;
        if (TotalClasses != other.TotalClasses || Violations != other.Violations)
            return false;
        if (Coverage.Count != other.Coverage.Count)
            return false;

        foreach (var (dimension, percent) in Coverage)
        {
            if (!other.Coverage.TryGetValue(dimension, out var otherPercent) || percent != otherPercent)
                return false;

            // Percentages round to one decimal, so compare the raw counts too when both carry them —
            // otherwise a small real change inside the same rounded percentage looks like no change.
            var mine = Counts?.GetValueOrDefault(dimension);
            var theirs = other.Counts?.GetValueOrDefault(dimension);
            if (mine is not null && theirs is not null && mine != theirs)
                return false;
        }

        return true;
    }

    /// <summary>Build a snapshot from a computed <see cref="LibraryMetrics"/> for a scope (a package id
    /// like "Modelica.Blocks", or "" for a whole library / all loaded libraries) at the given time.
    /// <paramref name="violations"/> is the number of active (unsuppressed) rule findings in scope, or
    /// null when style checking has not been run so the count is unknown.</summary>
    public static MetricsSnapshot From(
        LibraryMetrics metrics, string scope, DateTime timestampUtc, int? violations = null,
        string? revision = null, string? branch = null)
        => new(timestampUtc, scope ?? string.Empty, metrics.TotalClasses,
            metrics.Coverage.ToDictionary(c => c.Dimension, c => c.Percent, StringComparer.Ordinal),
            metrics.Coverage.ToDictionary(c => c.Dimension, c => new CoverageCount(c.Compliant, c.Eligible), StringComparer.Ordinal),
            violations, revision, branch);

    /// <summary>
    /// Collapse snapshots that share a timestamp into one combined snapshot each, summing class counts
    /// and — per dimension — the raw compliant/eligible counts to recompute an exact combined
    /// percentage. When a contributing snapshot predates <see cref="Counts"/>, the dimension falls back
    /// to a class-count-weighted average of the stored percentages. Used to aggregate the per-repository
    /// snapshots written for an "all libraries" save into a single trend line. Returns oldest-first.
    /// </summary>
    public static List<MetricsSnapshot> AggregateByTimestamp(IEnumerable<MetricsSnapshot> snapshots)
    {
        var result = new List<MetricsSnapshot>();
        foreach (var group in snapshots.GroupBy(s => s.TimestampUtc))
        {
            var members = group.ToList();
            if (members.Count == 1)
            {
                result.Add(members[0]);
                continue;
            }

            var totalClasses = members.Sum(s => s.TotalClasses);
            var violations = members.All(s => s.Violations.HasValue)
                ? members.Sum(s => s.Violations!.Value)
                : (int?)null;
            var dims = members.SelectMany(s => s.Coverage.Keys).Distinct(StringComparer.Ordinal);
            var coverage = new Dictionary<string, double>(StringComparer.Ordinal);
            var counts = new Dictionary<string, CoverageCount>(StringComparer.Ordinal);
            var exactForAll = true;

            foreach (var dim in dims)
            {
                var contributors = members.Where(s => s.Coverage.ContainsKey(dim)).ToList();
                if (contributors.All(s => s.Counts is not null && s.Counts.ContainsKey(dim)))
                {
                    var compliant = contributors.Sum(s => s.Counts![dim].Compliant);
                    var eligible = contributors.Sum(s => s.Counts![dim].Eligible);
                    counts[dim] = new CoverageCount(compliant, eligible);
                    coverage[dim] = eligible == 0 ? 100.0 : Math.Round(100.0 * compliant / eligible, 1);
                }
                else
                {
                    exactForAll = false;
                    double weight = contributors.Sum(s => s.TotalClasses);
                    coverage[dim] = weight <= 0
                        ? Math.Round(contributors.Average(s => s.Coverage[dim]), 1)
                        : Math.Round(contributors.Sum(s => s.Coverage[dim] * s.TotalClasses) / weight, 1);
                }
            }

            // Members of one "all libraries" save share a run, so they share a revision unless the
            // repositories genuinely differ — in which case no single revision describes the point.
            var revision = members.Select(m => m.Revision).Distinct().Count() == 1 ? members[0].Revision : null;
            var branch = members.Select(m => m.Branch).Distinct().Count() == 1 ? members[0].Branch : null;

            result.Add(new MetricsSnapshot(group.Key, members[0].Scope, totalClasses, coverage,
                exactForAll ? counts : null, violations, revision, branch));
        }

        return result.OrderBy(s => s.TimestampUtc).ToList();
    }
}
