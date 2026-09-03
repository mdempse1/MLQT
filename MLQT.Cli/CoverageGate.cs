using ModelicaGraph.Analysis;

namespace MLQT.Cli;

/// <summary>What a coverage requirement is measured against.</summary>
internal enum CoverageRequirement
{
    /// <summary>A percentage the user asked for.</summary>
    Threshold,

    /// <summary>The last recorded snapshot — the ratchet, applied to coverage.</summary>
    Previous
}

/// <summary>One dimension's verdict.</summary>
internal sealed record CoverageGateResult(
    string Dimension, double Percent, double Required, CoverageRequirement Requirement)
{
    public bool Passed => Percent >= Required - Tolerance;

    /// <summary>
    /// Percentages are rounded to one decimal on both sides, so a hair of floating-point difference
    /// is not a reason to fail a build that has not moved.
    /// </summary>
    private const double Tolerance = 0.05;

    public string Describe() =>
        Requirement == CoverageRequirement.Threshold
            ? $"{Dimension} {Percent:0.#}% is below the required {Required:0.#}%"
            : $"{Dimension} {Percent:0.#}% is below the last recorded {Required:0.#}%";
}

/// <summary>
/// Gating on the coverage numbers rather than on findings.
///
/// <para><c>--fail-on</c> asks "did this change introduce findings"; this asks "is the library
/// documented well enough", and the two fail for different reasons. A threshold states where a team
/// has decided to be, and the ratchet — no dimension below the last recorded snapshot — states only
/// that it will not go backwards, which is the version a legacy library can adopt on day one.</para>
/// </summary>
internal sealed record CoverageGate(
    double? MinimumForAll,
    IReadOnlyDictionary<string, double> MinimumByDimension,
    bool Ratchet)
{
    public bool IsActive => MinimumForAll is not null || MinimumByDimension.Count > 0 || Ratchet;

    /// <summary>
    /// Judges the measured coverage. <paramref name="previous"/> is the last recorded snapshot for
    /// the checked set, or null when there is none — nothing to ratchet against is not a failure, it
    /// is the first run.
    /// </summary>
    public List<CoverageGateResult> Evaluate(LibraryMetrics metrics, MetricsSnapshot? previous)
    {
        var results = new List<CoverageGateResult>();

        foreach (var metric in metrics.Coverage)
        {
            // A named dimension overrides the blanket figure: "80% everywhere, 95% on descriptions"
            // is the shape a team's policy actually takes.
            if (MinimumByDimension.TryGetValue(Normalize(metric.Dimension), out var specific))
                results.Add(new CoverageGateResult(
                    metric.Dimension, metric.Percent, specific, CoverageRequirement.Threshold));
            else if (MinimumForAll is { } all)
                results.Add(new CoverageGateResult(
                    metric.Dimension, metric.Percent, all, CoverageRequirement.Threshold));

            if (Ratchet && previous?.Coverage.TryGetValue(metric.Dimension, out var before) == true)
                results.Add(new CoverageGateResult(
                    metric.Dimension, metric.Percent, before, CoverageRequirement.Previous));
        }

        return results;
    }

    /// <summary>
    /// The dimensions named in <c>--min-coverage</c> that the run does not measure — a rule the
    /// repository has switched off, or a typo. Either way the requirement is silently doing nothing,
    /// which is the failure mode a quality gate can least afford.
    /// </summary>
    public List<string> UnmatchedDimensions(LibraryMetrics metrics)
    {
        var measured = metrics.Coverage.Select(c => Normalize(c.Dimension)).ToHashSet(StringComparer.Ordinal);
        return MinimumByDimension.Keys.Where(d => !measured.Contains(d)).ToList();
    }

    /// <summary>
    /// Resolves a dimension as a user would write it — <c>class-description</c>,
    /// <c>ClassDescription</c>, <c>"Class description"</c> — to the name the report uses, or null if
    /// no dimension matches.
    /// </summary>
    public static string? ResolveDimension(string text)
    {
        var wanted = Normalize(text);
        foreach (var (dimension, name) in CoverageDimensions.Ordered)
        {
            if (Normalize(name) == wanted || Normalize(dimension.ToString()) == wanted)
                return name;
        }
        return null;
    }

    /// <summary>The names a user might type differ only in case, spaces and punctuation.</summary>
    public static string Normalize(string text) =>
        new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>Every dimension name, for an error message that says what was expected.</summary>
    public static string KnownDimensions() =>
        string.Join(", ", CoverageDimensions.Ordered.Select(d => Normalize(d.Name)));
}
