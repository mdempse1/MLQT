using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;

namespace ModelicaGraph;

public class StyleCheckingSettings
{
    // Commit message requirements
    public bool CommitRequiresIssueNumber { get; set; } = false;
    public bool IssueNumberAtEnd { get; set; } = false;

    // Code formatting settings (formatter flags — NOT style-check rules; consumed by ModelicaRenderer)
    public bool ApplyFormattingRules { get; set; } = false;
    public bool ComponentsBeforeClasses { get; set; } = false;

    // ---------------------------------------------------------------------------------------------
    // Per-rule severity map (the source of truth for rule enablement/severity).
    //
    // The named bool properties below are backward-compatible facades over this map: enabling a rule
    // stores its "when enabled" severity (see RuleCatalog), disabling removes it. A rule id absent
    // from the map is disabled, matching the historical default of `= false`.
    //
    // The map is serialized as the authoritative store (Phase 4). The bool facades are kept for
    // backward compatibility: an old `.mlqt/settings.json` (bools only) still loads, and the setters
    // populate the map. Reconciliation is order-independent — enabling a rule only sets its default
    // severity when the map has no entry yet, so an explicit `RuleSeverities` value (e.g. "Error")
    // is never clobbered by a bool facade regardless of JSON property order.
    // ---------------------------------------------------------------------------------------------
    // Populate (merge into) the existing dictionary on deserialize rather than replacing it, so
    // bool-derived entries and explicit map entries combine instead of one clobbering the other.
    //
    // Sorted, because this is written to .mlqt/settings.json and that file is committed. A plain
    // Dictionary enumerates in insertion order until a removal frees a slot, after which the order
    // depends on the sequence of rules the user happened to toggle — so saving a repository whose
    // settings had not changed still produced a diff, with every rule moved and nothing to review.
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public SortedDictionary<string, RuleSeverity> RuleSeverities { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The layout the formatter should write with, for these settings.
    ///
    /// <para>The one place the rule switches are translated into renderer options, so the app, the
    /// MCP server and the save path cannot disagree about what "formatted" means for a repository.
    /// <see cref="ApplyFormattingRules"/> is deliberately not consulted: it decides <em>whether</em>
    /// to reformat, which is the caller's question, not what the layout is.</para>
    /// </summary>
    public FormattingOptions ToFormattingOptions() => new(
        OneOfEachSection: OneOfEachSection,
        ImportsFirst: ImportStatementsFirst,
        ComponentsBeforeClasses: ComponentsBeforeClasses,
        // The two initial-section rules are mutually exclusive in the settings UI, so this reads
        // "last if the repository asked for last, otherwise first".
        InitialSectionsLast: InitialEQAlgoLast);

    /// <summary>
    /// Resolves the configured severity for a rule id (Off when disabled/absent).
    ///
    /// <para>A <b>governed</b> rule resolves through its governor instead of through its own entry
    /// (see <see cref="RuleDefinition.GovernedBy"/>): <c>MLQT.Style.ExtendsAtTop</c> is the
    /// "imports first, extends next" convention seen from the other end, and answers with whatever
    /// <c>MLQT.Style.ImportStatementsFirst</c> is set to. Before this, its own entry was consulted,
    /// always missing, and always Off — so the checker had to paper over the answer by falling back
    /// to the catalog default, and a settings file that set the id to <c>"Off"</c> was read, accepted,
    /// and ignored. See <see cref="IgnoredRuleKeys"/>, which is how that file gets told.</para>
    /// </summary>
    public RuleSeverity SeverityFor(string ruleId)
    {
        if (RuleCatalog.GovernorOf(ruleId) is { } governor)
            return SeverityFor(governor);

        // A rule whose prerequisite is off does nothing, so it reads as off rather than as enabled
        // and quietly inert. See RuleDefinition.RequiresRule: the ordering rules need
        // OneOfEachSection, because without it the formatter never reorders and they would report an
        // arrangement no one can reach by formatting.
        if (RuleCatalog.RequiredRuleFor(ruleId) is { } prerequisite && !IsRuleEnabled(prerequisite))
            return RuleSeverity.Off;

        if (!RuleSeverities.TryGetValue(ruleId, out var stored))
            return RuleSeverity.Off;

        // A layout rule the formatter maintains is not judged at a level somebody typed: it is a
        // warning while it is advice, and an error once the formatter is rewriting every class on save
        // to satisfy it, because a violation that survives that is not a matter of taste.
        return RuleCatalog.SeverityFollowsFormatter(ruleId)
            ? (ApplyFormattingRules ? RuleSeverity.Error : RuleSeverity.Warning)
            : stored;
    }

    /// <summary>
    /// Keys in <see cref="RuleSeverities"/> that are not doing what the file says, each with the
    /// reason: a rule governed by another, a diagnostic, an id no catalog rule matches (a typo, or a
    /// rule from a newer MLQT), or a rule sitting behind a prerequisite that is off.
    ///
    /// <para>A quality gate configured by a file nobody validates is a gate that can be switched off
    /// by a spelling mistake and never say so. The caller decides what to do about it — the CLI warns
    /// on stderr — but the answer is computed here so every surface asks the same question.</para>
    /// </summary>
    public IReadOnlyList<(string RuleId, string Reason)> IgnoredRuleKeys() =>
        RuleSeverities.Keys
            .Select(id => (RuleId: id, Reason: ReasonIgnored(id)))
            .Where(entry => entry.Reason is not null)
            .OrderBy(entry => entry.RuleId, StringComparer.Ordinal)
            .Select(entry => (entry.RuleId, entry.Reason!))
            .ToList();

    /// <summary>Why this key is not doing what it says, or null when it is.</summary>
    private string? ReasonIgnored(string ruleId)
    {
        if (RuleCatalog.GovernorOf(ruleId) is { } governor)
            return $"'{ruleId}' is governed by '{governor}' and has no setting of its own — set that instead";

        if (RuleIds.IsDiagnostic(ruleId))
            return $"'{ruleId}' is a diagnostic, not a rule: it is always reported and cannot be configured";

        if (!RuleCatalog.IsKnown(ruleId))
            return $"'{ruleId}' is not a known rule id — check the spelling against settings-reference.md";

        if (RuleCatalog.RequiredRuleFor(ruleId) is { } prerequisite && !IsRuleEnabled(prerequisite))
            return $"'{ruleId}' does nothing while '{prerequisite}' is off: the formatter only reorders " +
                   "a class when that rule is on, so this one would report an arrangement it cannot produce";

        return null;
    }

    /// <summary>True if the rule is enabled (severity != Off). Public so a data-driven settings UI
    /// can render a toggle per rule id from the catalog.</summary>
    public bool IsRuleEnabled(string ruleId) => SeverityFor(ruleId) != RuleSeverity.Off;

    /// <summary>Enable a rule at its catalog default severity, or disable it. Public so a data-driven
    /// settings UI can bind a toggle to a rule id.</summary>
    public void SetRuleEnabled(string ruleId, bool enabled)
    {
        if (enabled)
        {
            // Don't overwrite an explicit severity (e.g. Error) — only seed the default when absent.
            // This keeps bool/map reconciliation order-independent during deserialization.
            if (!RuleSeverities.ContainsKey(ruleId))
                RuleSeverities[ruleId] = RuleCatalog.DefaultSeverityFor(ruleId);
        }
        else
        {
            RuleSeverities.Remove(ruleId);
        }
    }

    /// <summary>Set an explicit severity for a rule. <see cref="RuleSeverity.Off"/> disables it
    /// (removes it from the map). Public so a data-driven settings UI can offer a per-rule
    /// Off/Info/Warning/Error selector rather than a plain on/off toggle.</summary>
    public void SetRuleSeverity(string ruleId, RuleSeverity severity)
    {
        if (severity == RuleSeverity.Off)
            RuleSeverities.Remove(ruleId);
        else
            RuleSeverities[ruleId] = severity;
    }

    // Code formatting style rules
    public bool ImportStatementsFirst
    {
        get => IsRuleEnabled(RuleIds.ImportStatementsFirst);
        set => SetRuleEnabled(RuleIds.ImportStatementsFirst, value);
    }
    public bool OneOfEachSection
    {
        get => IsRuleEnabled(RuleIds.OneOfEachSection);
        set => SetRuleEnabled(RuleIds.OneOfEachSection, value);
    }
    public bool DontMixEquationAndAlgorithm
    {
        get => IsRuleEnabled(RuleIds.DontMixEquationAndAlgorithm);
        set => SetRuleEnabled(RuleIds.DontMixEquationAndAlgorithm, value);
    }
    public bool DontMixConnections
    {
        get => IsRuleEnabled(RuleIds.DontMixConnections);
        set => SetRuleEnabled(RuleIds.DontMixConnections, value);
    }
    public bool InitialEQAlgoFirst
    {
        get => IsRuleEnabled(RuleIds.InitialEqAlgoFirst);
        set => SetRuleEnabled(RuleIds.InitialEqAlgoFirst, value);
    }
    public bool InitialEQAlgoLast
    {
        get => IsRuleEnabled(RuleIds.InitialEqAlgoLast);
        set => SetRuleEnabled(RuleIds.InitialEqAlgoLast, value);
    }

    // Models excluded from formatting (by fully qualified model ID)
    public List<string> FormattingExcludedModels { get; set; } = new();

    public bool IsModelExcludedFromFormatting(string modelId)
        => FormattingExcludedModels.Contains(modelId, StringComparer.Ordinal);

    /// <summary>
    /// Top-level library names whose classes are not reported on — typically the test-case and example
    /// libraries that sit in the same repository as the libraries under development, where the same
    /// rules are not wanted. Matched case-insensitively against the first segment of a class id, and
    /// <c>*</c> is a wildcard, so <c>"*_Tests"</c> covers <c>Foo_Tests</c> and <c>Bar_Tests</c>.
    ///
    /// An excluded library is still LOADED and still counts as a user of everything it references —
    /// so a test library keeps the classes it exercises from looking unused. Only the reporting is
    /// suppressed. Parse errors are still reported, because they say the file could not be read at
    /// all rather than expressing an opinion about its style.
    /// </summary>
    public List<string> ExcludedLibraries { get; set; } = new();

    // Compiled form of ExcludedLibraries, rebuilt when the list changes. Checking runs this per class,
    // so it must not recompile a regex each time.
    private string[]? _excludedLibrariesSource;
    private Regex[]? _excludedLibraryPatterns;

    /// <summary>
    /// True if the class belongs to an excluded library. <paramref name="modelId"/> is a fully
    /// qualified class id; only its first segment (the library name) is considered.
    /// </summary>
    public bool IsLibraryExcluded(string? modelId)
    {
        if (string.IsNullOrEmpty(modelId) || ExcludedLibraries.Count == 0)
            return false;

        var patterns = EnsureExcludedLibraryPatterns();
        if (patterns.Length == 0)
            return false;

        var dot = modelId.IndexOf('.');
        var library = dot < 0 ? modelId : modelId[..dot];

        foreach (var pattern in patterns)
            if (pattern.IsMatch(library))
                return true;
        return false;
    }

    private Regex[] EnsureExcludedLibraryPatterns()
    {
        // Cheap identity check on the current entries — the settings object is mutated in place by the
        // UI, so a cached compilation has to notice an edit.
        var current = ExcludedLibraries.Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
        if (_excludedLibraryPatterns is not null && _excludedLibrariesSource is not null &&
            current.SequenceEqual(_excludedLibrariesSource, StringComparer.Ordinal))
            return _excludedLibraryPatterns;

        _excludedLibrariesSource = current;
        _excludedLibraryPatterns = current
            .Select(name => new Regex(
                "^" + string.Join(".*", name.Trim().Split('*').Select(Regex.Escape)) + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToArray();
        return _excludedLibraryPatterns;
    }

    // Style guidelines
    public bool ClassHasDescription
    {
        get => IsRuleEnabled(RuleIds.ClassDescription);
        set => SetRuleEnabled(RuleIds.ClassDescription, value);
    }
    public bool ClassHasDocumentationInfo
    {
        get => IsRuleEnabled(RuleIds.ClassDocumentationInfo);
        set => SetRuleEnabled(RuleIds.ClassDocumentationInfo, value);
    }
    public bool ClassHasDocumentationRevisions
    {
        get => IsRuleEnabled(RuleIds.ClassDocumentationRevisions);
        set => SetRuleEnabled(RuleIds.ClassDocumentationRevisions, value);
    }
    public bool ClassHasIcon
    {
        get => IsRuleEnabled(RuleIds.ClassIcon);
        set => SetRuleEnabled(RuleIds.ClassIcon, value);
    }
    public bool ParameterHasDescription
    {
        get => IsRuleEnabled(RuleIds.ParameterDescription);
        set => SetRuleEnabled(RuleIds.ParameterDescription, value);
    }
    public bool ConstantHasDescription
    {
        get => IsRuleEnabled(RuleIds.ConstantDescription);
        set => SetRuleEnabled(RuleIds.ConstantDescription, value);
    }

    public bool FollowNamingConvention
    {
        get => IsRuleEnabled(RuleIds.NamingConvention);
        set => SetRuleEnabled(RuleIds.NamingConvention, value);
    }
    public NamingConventionSettings NamingConvention { get; set; } = new();

    public bool SpellCheckDescription
    {
        get => IsRuleEnabled(RuleIds.SpellingDescription);
        set => SetRuleEnabled(RuleIds.SpellingDescription, value);
    }
    public bool SpellCheckDocumentation
    {
        get => IsRuleEnabled(RuleIds.SpellingDocumentation);
        set => SetRuleEnabled(RuleIds.SpellingDocumentation, value);
    }

    /// <summary>
    /// Language codes for spell checking dictionaries (e.g. "en_US", "en_GB").
    /// Includes both bundled and imported dictionaries.
    /// When empty, defaults to all bundled dictionaries.
    /// </summary>
    public List<string> SpellCheckLanguages { get; set; } = ["en_US", "en_GB"];

    // Reference validation
    public bool ValidateModelReferences
    {
        get => IsRuleEnabled(RuleIds.ModelReferences);
        set => SetRuleEnabled(RuleIds.ModelReferences, value);
    }

    // Wave-1 analyses (Phase 6)
    public bool CheckDuplicateDeclarations
    {
        get => IsRuleEnabled(RuleIds.DuplicateDeclaration);
        set => SetRuleEnabled(RuleIds.DuplicateDeclaration, value);
    }
    public bool CheckDuplicateImports
    {
        get => IsRuleEnabled(RuleIds.DuplicateImport);
        set => SetRuleEnabled(RuleIds.DuplicateImport, value);
    }
    public bool CheckMissingUnits
    {
        get => IsRuleEnabled(RuleIds.MissingUnit);
        set => SetRuleEnabled(RuleIds.MissingUnit, value);
    }
    public bool CheckUnusedImports
    {
        get => IsRuleEnabled(RuleIds.UnusedImport);
        set => SetRuleEnabled(RuleIds.UnusedImport, value);
    }
    public bool CheckPackageOrder
    {
        get => IsRuleEnabled(RuleIds.PackageOrder);
        set => SetRuleEnabled(RuleIds.PackageOrder, value);
    }
    public bool CheckUsesUndeclared
    {
        get => IsRuleEnabled(RuleIds.UsesUndeclared);
        set => SetRuleEnabled(RuleIds.UsesUndeclared, value);
    }
    public bool CheckUsesDeclaredUnused
    {
        get => IsRuleEnabled(RuleIds.UsesDeclaredUnused);
        set => SetRuleEnabled(RuleIds.UsesDeclaredUnused, value);
    }
    public bool CheckUnusedClass
    {
        get => IsRuleEnabled(RuleIds.UnusedClass);
        set => SetRuleEnabled(RuleIds.UnusedClass, value);
    }
    public bool CheckUnusedPublicClass
    {
        get => IsRuleEnabled(RuleIds.UnusedPublicClass);
        set => SetRuleEnabled(RuleIds.UnusedPublicClass, value);
    }
    public bool CheckShadowing
    {
        get => IsRuleEnabled(RuleIds.ShadowingInheritedMember);
        set => SetRuleEnabled(RuleIds.ShadowingInheritedMember, value);
    }
    public bool CheckUnusedMembers
    {
        get => IsRuleEnabled(RuleIds.UnusedMember);
        set => SetRuleEnabled(RuleIds.UnusedMember, value);
    }

    /// <summary>
    /// SVN branch directory names used when listing branches, extracting the current branch,
    /// and creating new branches. The first entry is treated as the trunk equivalent.
    /// Defaults to standard SVN layout: trunk, branches, tags.
    /// </summary>
    public List<string> SvnBranchDirectories { get; set; } = ["trunk", "branches", "tags"];

    /// <summary>
    /// Returns true if any style checking rule is enabled that would produce findings.
    /// Used to skip the entire style checking pipeline when no rules are active. Map-driven so new
    /// rules are counted automatically (the severity map holds only enabled rules; formatter flags
    /// such as ApplyFormattingRules/ComponentsBeforeClasses are plain bools and never appear here).
    /// </summary>
    /// <summary>
    /// True when at least one rule will actually run. Asks <see cref="SeverityFor"/> rather than
    /// reading the stored values, because the two can disagree: a rule whose prerequisite is off is
    /// in the map and does nothing, and a settings file holding only those would otherwise announce
    /// that rules are enabled and then report nothing, which is the least debuggable outcome there is.
    /// </summary>
    public bool HasAnyStyleRuleEnabled => RuleSeverities.Keys.Any(IsRuleEnabled);
}
