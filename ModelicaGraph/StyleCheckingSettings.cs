using System.Text.Json.Serialization;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;

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
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, RuleSeverity> RuleSeverities { get; } = new(StringComparer.Ordinal);

    /// <summary>Resolves the configured severity for a rule id (Off when disabled/absent).</summary>
    public RuleSeverity SeverityFor(string ruleId)
        => RuleSeverities.TryGetValue(ruleId, out var s) ? s : RuleSeverity.Off;

    private bool IsRuleEnabled(string ruleId) => SeverityFor(ruleId) != RuleSeverity.Off;

    private void SetRuleEnabled(string ruleId, bool enabled)
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

    /// <summary>
    /// SVN branch directory names used when listing branches, extracting the current branch,
    /// and creating new branches. The first entry is treated as the trunk equivalent.
    /// Defaults to standard SVN layout: trunk, branches, tags.
    /// </summary>
    public List<string> SvnBranchDirectories { get; set; } = ["trunk", "branches", "tags"];

    /// <summary>
    /// Returns true if any style checking rule is enabled that would produce violations.
    /// Used to skip the entire style checking pipeline when no rules are active. Map-driven so new
    /// rules are counted automatically (the severity map holds only enabled rules; formatter flags
    /// such as ApplyFormattingRules/ComponentsBeforeClasses are plain bools and never appear here).
    /// </summary>
    public bool HasAnyStyleRuleEnabled => RuleSeverities.Values.Any(s => s != RuleSeverity.Off);
}
