using System.Text.Json;
using System.Text.Json.Serialization;
using ModelicaParser.DataTypes;
using ModelicaGraph;
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

    /// <summary>
    /// The rules that were in force when this baseline was generated (rule id → severity, enabled
    /// rules only), and the libraries that were excluded. Null for a file written before these were
    /// recorded, in which case drift cannot be detected and is not reported.
    ///
    /// Kept so a check can warn when the configuration has moved on: a rule enabled after baselining
    /// reports its pre-existing violations as NEW, which looks like a regression the change did not
    /// cause.
    /// </summary>
    public IReadOnlyDictionary<string, RuleSeverity>? Rules { get; }

    /// <inheritdoc cref="Rules"/>
    public IReadOnlyList<string>? ExcludedLibraries { get; }

    /// <summary>
    /// The libraries loaded purely so references would resolve (MSL and friends), by name rather than
    /// by path — the path differs between a developer's machine and a CI agent, the set of libraries
    /// does not. Checking without a dependency the baseline had resolves fewer references, which
    /// surfaces as a pile of findings the change did not cause.
    /// </summary>
    public IReadOnlyList<string>? Dependencies { get; }

    public Baseline(
        IReadOnlyList<BaselineEntry> entries,
        DateTime? createdUtc = null,
        string? revision = null,
        string? branch = null,
        IReadOnlyDictionary<string, RuleSeverity>? rules = null,
        IReadOnlyList<string>? excludedLibraries = null,
        IReadOnlyList<string>? dependencies = null)
    {
        Entries = entries;
        _fingerprints = entries.Select(e => e.Fingerprint).ToHashSet(StringComparer.Ordinal);
        CreatedUtc = createdUtc;
        Revision = revision;
        Branch = branch;
        Rules = rules;
        ExcludedLibraries = excludedLibraries;
        Dependencies = dependencies;
    }

    /// <summary>
    /// How the current settings differ from the ones this baseline was generated with. Empty when
    /// they match, or when the baseline predates rule recording (see <see cref="Rules"/>) — there is
    /// nothing to compare against, and guessing would be worse than staying quiet.
    /// </summary>
    public RuleSetDrift DriftFrom(
        StyleCheckingSettings current, IReadOnlyList<string>? currentDependencies = null)
    {
        if (Rules is null)
            return RuleSetDrift.NotComparable;

        var enabledNow = current.RuleSeverities
            .Where(kv => kv.Value != RuleSeverity.Off)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        var enabledSince = enabledNow.Keys.Where(id => !Rules.ContainsKey(id))
            .OrderBy(id => id, StringComparer.Ordinal).ToList();
        var disabledSince = Rules.Keys.Where(id => !enabledNow.ContainsKey(id))
            .OrderBy(id => id, StringComparer.Ordinal).ToList();
        var severityChanged = enabledNow
            .Where(kv => Rules.TryGetValue(kv.Key, out var was) && was != kv.Value)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (RuleId: kv.Key, Was: Rules[kv.Key], Now: kv.Value))
            .ToList();

        var wasExcluded = ExcludedLibraries ?? [];
        var excludedNow = current.ExcludedLibraries;
        var exclusionsChanged =
            !wasExcluded.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(excludedNow.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        // Compared as a set of names: order and path are irrelevant, presence is not.
        var wasDependencies = (Dependencies ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nowDependencies = (currentDependencies ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new RuleSetDrift(
            true, enabledSince, disabledSince, severityChanged, exclusionsChanged,
            wasDependencies.Except(nowDependencies).OrderBy(n => n, StringComparer.Ordinal).ToList(),
            nowDependencies.Except(wasDependencies).OrderBy(n => n, StringComparer.Ordinal).ToList());
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
        IEnumerable<Finding> findings, DateTime? createdUtc = null, VcsStamp? stamp = null,
        StyleCheckingSettings? settings = null, IReadOnlyList<string>? dependencies = null)
    {
        var entries = findings
            .Where(f => !RuleIds.IsParseDiagnostic(f.RuleId))
            .GroupBy(f => f.Fingerprint, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(f => new BaselineEntry(f.Fingerprint, f.RuleId, f.ModelId, f.ElementPath, f.Message));
        return new Baseline(
            Sort(entries), createdUtc, stamp?.Revision, stamp?.Branch,
            RulesOf(settings), settings?.ExcludedLibraries.ToList(), dependencies);
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
        IEnumerable<Finding> current, DateTime? createdUtc = null, VcsStamp? stamp = null,
        StyleCheckingSettings? settings = null, IReadOnlyList<string>? dependencies = null)
    {
        var currentFingerprints = current.Select(f => f.Fingerprint).ToHashSet(StringComparer.Ordinal);
        return new Baseline(
            Sort(Entries.Where(e => currentFingerprints.Contains(e.Fingerprint))),
            createdUtc ?? CreatedUtc,
            stamp?.Revision ?? Revision,
            stamp?.Branch ?? Branch,
            RulesOf(settings) ?? Rules,
            settings?.ExcludedLibraries.ToList() ?? ExcludedLibraries,
            dependencies ?? Dependencies);
    }

    /// <summary>The enabled rules of <paramref name="settings"/>, or null when none were supplied.</summary>
    private static Dictionary<string, RuleSeverity>? RulesOf(StyleCheckingSettings? settings) =>
        settings?.RuleSeverities
            .Where(kv => kv.Value != RuleSeverity.Off)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    // --- persistence ---------------------------------------------------------------------------

    // Version 2 added createdUtc/revision/branch; version 3 added the rules the baseline was
    // generated with. Older files still load — the added fields are simply null — so an existing
    // committed baseline keeps working untouched, it just cannot report rule drift.
    private const int CurrentVersion = 3;

    private sealed record BaselineFile(
        int Version,
        IReadOnlyList<BaselineEntry> Findings,
        DateTime? CreatedUtc = null,
        string? Revision = null,
        string? Branch = null,
        IReadOnlyDictionary<string, RuleSeverity>? Rules = null,
        IReadOnlyList<string>? ExcludedLibraries = null,
        IReadOnlyList<string>? Dependencies = null);

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
        return new Baseline(
            file.Findings ?? [], file.CreatedUtc, file.Revision, file.Branch,
            file.Rules, file.ExcludedLibraries, file.Dependencies);
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Always sort on write so regenerating identical findings at the same revision yields a
        // byte-identical file — that is what lets CI skip a no-op commit.
        var file = new BaselineFile(
            CurrentVersion, Sort(Entries), CreatedUtc, Revision, Branch,
            Rules,
            ExcludedLibraries is { Count: > 0 } ? ExcludedLibraries : null,
            Dependencies is { Count: > 0 } ? Dependencies : null);
        File.WriteAllText(path, JsonSerializer.Serialize(file, Options));
    }

    private static List<BaselineEntry> Sort(IEnumerable<BaselineEntry> entries) => entries
        .OrderBy(e => e.Model, StringComparer.Ordinal)
        .ThenBy(e => e.RuleId, StringComparer.Ordinal)
        .ThenBy(e => e.Element ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(e => e.Fingerprint, StringComparer.Ordinal)
        .ToList();
}

/// <summary>
/// How the rules in force now differ from the ones a baseline was generated with.
///
/// This matters because the two failure modes are silent. A rule enabled after baselining reports its
/// pre-existing violations as NEW, so a change looks like it caused a regression it had nothing to do
/// with. A rule disabled since leaves entries in the baseline that can never match again.
/// </summary>
public sealed record RuleSetDrift(
    bool IsComparable,
    IReadOnlyList<string> EnabledSince,
    IReadOnlyList<string> DisabledSince,
    IReadOnlyList<(string RuleId, RuleSeverity Was, RuleSeverity Now)> SeverityChanged,
    bool ExclusionsChanged,
    IReadOnlyList<string> DependenciesMissing,
    IReadOnlyList<string> DependenciesAdded)
{
    /// <summary>A baseline written before the rule set was recorded — nothing to compare.</summary>
    public static readonly RuleSetDrift NotComparable = new(false, [], [], [], false, [], []);

    public bool HasDrifted =>
        IsComparable &&
        (EnabledSince.Count > 0 || DisabledSince.Count > 0 || SeverityChanged.Count > 0 ||
         ExclusionsChanged || DependenciesMissing.Count > 0 || DependenciesAdded.Count > 0);

    /// <summary>Lines describing the drift, for a warning. Empty when nothing has changed.</summary>
    public IEnumerable<string> Describe()
    {
        if (!HasDrifted)
            yield break;

        if (EnabledSince.Count > 0)
            yield return $"enabled since: {string.Join(", ", EnabledSince)}";
        if (DisabledSince.Count > 0)
            yield return $"disabled since: {string.Join(", ", DisabledSince)}";
        foreach (var (ruleId, was, now) in SeverityChanged)
            yield return $"severity changed: {ruleId} ({was} -> {now})";
        if (ExclusionsChanged)
            yield return "the set of excluded libraries has changed";
        if (DependenciesMissing.Count > 0)
            yield return
                $"not loaded this time: {string.Join(", ", DependenciesMissing)} " +
                "— references into them will not resolve";
        if (DependenciesAdded.Count > 0)
            yield return $"loaded this time but not when baselined: {string.Join(", ", DependenciesAdded)}";
    }
}
