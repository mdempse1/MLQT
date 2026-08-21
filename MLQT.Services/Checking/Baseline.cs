using System.Text.Json;
using System.Text.Json.Serialization;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;

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

    /// <summary>When this baseline's content was generated (UTC), or null for a file written before
    /// the field existed. Refreshed by <c>baseline update</c> and <c>baseline prune</c>, because both
    /// rewrite the content — the timestamp describes the snapshot, not the file's first creation.</summary>
    public DateTime? CreatedUtc { get; }

    /// <summary>The VCS revision the library was at when this baseline was generated, so a reviewer
    /// can diff from there to see what has changed since. Null when the library is not in a working
    /// copy, or for a file written before the field existed.</summary>
    public string? Revision { get; }

    /// <summary>The branch the baseline was generated on. Null in the same cases as
    /// <see cref="Revision"/>.</summary>
    public string? Branch { get; }

    public Baseline(
        IReadOnlyList<BaselineEntry> entries,
        DateTime? createdUtc = null,
        string? revision = null,
        string? branch = null)
    {
        Entries = entries;
        _fingerprints = entries.Select(e => e.Fingerprint).ToHashSet(StringComparer.Ordinal);
        CreatedUtc = createdUtc;
        Revision = revision;
        Branch = branch;
    }

    /// <summary>
    /// True if the finding is recorded as accepted debt. Parse diagnostics never are: a baseline is a
    /// record of style debt someone chose to live with, and code that does not parse is not something
    /// a gate should be able to accept — every other rule under-reports on it.
    /// </summary>
    public bool Contains(Finding finding) =>
        !RuleIds.IsParseDiagnostic(finding.RuleId) && _fingerprints.Contains(finding.Fingerprint);

    /// <summary>Snapshots the current findings into a baseline (deduped by fingerprint, sorted).
    /// Parse diagnostics are excluded — see <see cref="Contains"/>.</summary>
    /// <param name="createdUtc">Generation time, supplied by the caller so the result is deterministic
    /// and testable rather than reading the clock here.</param>
    /// <param name="stamp">The revision the library was at, for matching the baseline to a commit.</param>
    public static Baseline FromFindings(
        IEnumerable<Finding> findings, DateTime? createdUtc = null, VcsStamp? stamp = null)
    {
        var entries = findings
            .Where(f => !RuleIds.IsParseDiagnostic(f.RuleId))
            .GroupBy(f => f.Fingerprint, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(f => new BaselineEntry(f.Fingerprint, f.RuleId, f.ModelId, f.ElementPath, f.Message));
        return new Baseline(Sort(entries), createdUtc, stamp?.Revision, stamp?.Branch);
    }

    /// <summary>Baseline entries whose finding no longer appears (i.e. fixed) — candidates for prune.</summary>
    public IReadOnlyList<BaselineEntry> StaleEntries(IEnumerable<Finding> current)
    {
        var currentFingerprints = current.Select(f => f.Fingerprint).ToHashSet(StringComparer.Ordinal);
        return Entries.Where(e => !currentFingerprints.Contains(e.Fingerprint)).ToList();
    }

    /// <summary>A copy with fixed (stale) entries removed. Never adds entries. Prune rewrites the
    /// content, so the caller re-stamps it with the time and revision of the prune.</summary>
    public Baseline WithoutStale(
        IEnumerable<Finding> current, DateTime? createdUtc = null, VcsStamp? stamp = null)
    {
        var currentFingerprints = current.Select(f => f.Fingerprint).ToHashSet(StringComparer.Ordinal);
        return new Baseline(
            Sort(Entries.Where(e => currentFingerprints.Contains(e.Fingerprint))),
            createdUtc ?? CreatedUtc,
            stamp?.Revision ?? Revision,
            stamp?.Branch ?? Branch);
    }

    // --- persistence ---------------------------------------------------------------------------

    // Version 2 added createdUtc/revision/branch. A version-1 file (no metadata) still loads — the
    // fields are simply null — so an existing committed baseline keeps working untouched.
    private const int CurrentVersion = 2;

    private sealed record BaselineFile(
        int Version,
        IReadOnlyList<BaselineEntry> Findings,
        DateTime? CreatedUtc = null,
        string? Revision = null,
        string? Branch = null);

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
        return new Baseline(file.Findings ?? [], file.CreatedUtc, file.Revision, file.Branch);
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Always sort on write so regenerating identical findings at the same revision yields a
        // byte-identical file — that is what lets CI skip a no-op commit.
        var file = new BaselineFile(CurrentVersion, Sort(Entries), CreatedUtc, Revision, Branch);
        File.WriteAllText(path, JsonSerializer.Serialize(file, Options));
    }

    private static List<BaselineEntry> Sort(IEnumerable<BaselineEntry> entries) => entries
        .OrderBy(e => e.Model, StringComparer.Ordinal)
        .ThenBy(e => e.RuleId, StringComparer.Ordinal)
        .ThenBy(e => e.Element ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(e => e.Fingerprint, StringComparer.Ordinal)
        .ToList();
}
