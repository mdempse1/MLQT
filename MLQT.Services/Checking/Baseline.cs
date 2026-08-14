using System.Text.Json;
using System.Text.Json.Serialization;
using ModelicaParser.DataTypes;

namespace MLQT.Services.Checking;

/// <summary>One accepted-debt entry. Only <see cref="Fingerprint"/> is load-bearing for matching;
/// the rest makes the baseline file a greppable, reviewable debt ledger.</summary>
public sealed record BaselineEntry(string Fingerprint, string RuleId, string Model, string? Element, string Message);

/// <summary>
/// A version-controlled ledger of accepted (pre-existing) findings, keyed by fingerprint. Loaded
/// from / saved to <c>.mlqt/baseline.json</c>. Membership is fingerprint identity, so it survives
/// reformatting and line shifts (the fingerprint deliberately excludes position — see Phase 1).
/// </summary>
public sealed class Baseline
{
    private readonly HashSet<string> _fingerprints;

    public IReadOnlyList<BaselineEntry> Entries { get; }

    public Baseline(IReadOnlyList<BaselineEntry> entries)
    {
        Entries = entries;
        _fingerprints = entries.Select(e => e.Fingerprint).ToHashSet(StringComparer.Ordinal);
    }

    public bool Contains(Finding finding) => _fingerprints.Contains(finding.Fingerprint);

    /// <summary>Snapshots the current findings into a baseline (deduped by fingerprint, sorted).</summary>
    public static Baseline FromFindings(IEnumerable<Finding> findings)
    {
        var entries = findings
            .GroupBy(f => f.Fingerprint, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(f => new BaselineEntry(f.Fingerprint, f.RuleId, f.ModelId, f.ElementPath, f.Message));
        return new Baseline(Sort(entries));
    }

    /// <summary>Baseline entries whose finding no longer appears (i.e. fixed) — candidates for prune.</summary>
    public IReadOnlyList<BaselineEntry> StaleEntries(IEnumerable<Finding> current)
    {
        var currentFingerprints = current.Select(f => f.Fingerprint).ToHashSet(StringComparer.Ordinal);
        return Entries.Where(e => !currentFingerprints.Contains(e.Fingerprint)).ToList();
    }

    /// <summary>A copy with fixed (stale) entries removed. Never adds entries.</summary>
    public Baseline WithoutStale(IEnumerable<Finding> current)
    {
        var currentFingerprints = current.Select(f => f.Fingerprint).ToHashSet(StringComparer.Ordinal);
        return new Baseline(Sort(Entries.Where(e => currentFingerprints.Contains(e.Fingerprint))));
    }

    // --- persistence ---------------------------------------------------------------------------

    private sealed record BaselineFile(int Version, IReadOnlyList<BaselineEntry> Findings);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static Baseline Load(string path)
    {
        var file = JsonSerializer.Deserialize<BaselineFile>(File.ReadAllText(path), Options)
            ?? throw new InvalidOperationException($"could not parse baseline '{path}'");
        return new Baseline(file.Findings ?? []);
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Always sort on write so regenerating identical findings yields a byte-identical file.
        var file = new BaselineFile(1, Sort(Entries));
        File.WriteAllText(path, JsonSerializer.Serialize(file, Options));
    }

    private static List<BaselineEntry> Sort(IEnumerable<BaselineEntry> entries) => entries
        .OrderBy(e => e.Model, StringComparer.Ordinal)
        .ThenBy(e => e.RuleId, StringComparer.Ordinal)
        .ThenBy(e => e.Element ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(e => e.Fingerprint, StringComparer.Ordinal)
        .ToList();
}
