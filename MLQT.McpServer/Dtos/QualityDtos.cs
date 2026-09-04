using ModelicaGraph;
using ModelicaParser.StyleRules;

namespace MLQT.McpServer.Dtos;

/// <summary>
/// The set of style/spell rules to apply, exposed as a flat object of on/off toggles plus the
/// spell-check dictionary languages.
///
/// <para><b>Every toggle is optional.</b> A rule you do not mention is left exactly as it was when
/// saving with <c>set_style_settings</c> — so enabling one rule does not switch off the rest — and
/// counts as off for a one-off run through a check tool, which starts from a blank settings object.
/// Read the current values with <c>get_style_settings</c>, change what you want, and pass back
/// either the whole object or just the keys you are changing.</para>
///
/// <para>The nested naming-convention configuration is not exposed here and is preserved untouched
/// when you save.</para>
/// </summary>
public sealed class StyleSettingsInput
{
    public bool? ImportStatementsFirst { get; set; }
    public bool? ComponentsBeforeClasses { get; set; }
    public bool? OneOfEachSection { get; set; }
    public bool? DontMixEquationAndAlgorithm { get; set; }
    public bool? DontMixConnections { get; set; }
    public bool? InitialEQAlgoFirst { get; set; }
    public bool? InitialEQAlgoLast { get; set; }
    public bool? ClassHasDescription { get; set; }
    public bool? ClassHasDocumentationInfo { get; set; }
    public bool? ClassHasDocumentationRevisions { get; set; }
    public bool? ClassHasIcon { get; set; }
    public bool? ParameterHasDescription { get; set; }
    public bool? ConstantHasDescription { get; set; }
    public bool? FollowNamingConvention { get; set; }
    public bool? SpellCheckDescription { get; set; }
    public bool? SpellCheckDocumentation { get; set; }
    public bool? ValidateModelReferences { get; set; }

    // Wave-1 analyses (Phase 6). Structure/uses/unused-class are graph analyses; they only produce
    // findings from check_library after analyze_dependencies has run (except package.order).
    public bool? CheckDuplicateDeclarations { get; set; }
    public bool? CheckDuplicateImports { get; set; }
    public bool? CheckMissingUnits { get; set; }
    public bool? CheckUnusedImports { get; set; }
    public bool? CheckPackageOrder { get; set; }
    public bool? CheckUsesUndeclared { get; set; }
    public bool? CheckUsesDeclaredUnused { get; set; }
    public bool? CheckUnusedClass { get; set; }
    public bool? CheckUnusedPublicClass { get; set; }
    public bool? CheckShadowing { get; set; }
    public bool? CheckUnusedMembers { get; set; }

    /// <summary>Spell-check dictionary language codes, e.g. ["en_US", "en_GB"]. Bundled codes are
    /// en_US and en_GB; other codes must have been imported as Hunspell dictionaries. When null or
    /// empty on save, the existing languages are kept.</summary>
    public IReadOnlyList<string>? SpellCheckLanguages { get; set; }

    /// <summary>
    /// One toggle, bound to the rule it sets. <see cref="ApplyTo"/> and <see cref="From"/> both walk
    /// this list rather than repeating the property names, so a rule cannot be readable and not
    /// writable (or the reverse), and <c>StyleSettingsCoverageTests</c> holds the list to
    /// <see cref="RuleCatalog.Configurable"/> — a rule added to the catalog with no entry here would
    /// otherwise be documented, gateable from CI and invisible to an agent, with nothing failing.
    /// </summary>
    private sealed record Toggle(
        string RuleId, Func<StyleSettingsInput, bool?> Read, Action<StyleSettingsInput, bool> Write);

    private static readonly Toggle[] _toggles =
    [
        new(RuleIds.ImportStatementsFirst, i => i.ImportStatementsFirst, (i, v) => i.ImportStatementsFirst = v),
        new(RuleIds.OneOfEachSection, i => i.OneOfEachSection, (i, v) => i.OneOfEachSection = v),
        new(RuleIds.DontMixEquationAndAlgorithm, i => i.DontMixEquationAndAlgorithm, (i, v) => i.DontMixEquationAndAlgorithm = v),
        new(RuleIds.DontMixConnections, i => i.DontMixConnections, (i, v) => i.DontMixConnections = v),
        new(RuleIds.InitialEqAlgoFirst, i => i.InitialEQAlgoFirst, (i, v) => i.InitialEQAlgoFirst = v),
        new(RuleIds.InitialEqAlgoLast, i => i.InitialEQAlgoLast, (i, v) => i.InitialEQAlgoLast = v),
        new(RuleIds.ClassDescription, i => i.ClassHasDescription, (i, v) => i.ClassHasDescription = v),
        new(RuleIds.ClassDocumentationInfo, i => i.ClassHasDocumentationInfo, (i, v) => i.ClassHasDocumentationInfo = v),
        new(RuleIds.ClassDocumentationRevisions, i => i.ClassHasDocumentationRevisions, (i, v) => i.ClassHasDocumentationRevisions = v),
        new(RuleIds.ClassIcon, i => i.ClassHasIcon, (i, v) => i.ClassHasIcon = v),
        new(RuleIds.ParameterDescription, i => i.ParameterHasDescription, (i, v) => i.ParameterHasDescription = v),
        new(RuleIds.ConstantDescription, i => i.ConstantHasDescription, (i, v) => i.ConstantHasDescription = v),
        new(RuleIds.NamingConvention, i => i.FollowNamingConvention, (i, v) => i.FollowNamingConvention = v),
        new(RuleIds.SpellingDescription, i => i.SpellCheckDescription, (i, v) => i.SpellCheckDescription = v),
        new(RuleIds.SpellingDocumentation, i => i.SpellCheckDocumentation, (i, v) => i.SpellCheckDocumentation = v),
        new(RuleIds.ModelReferences, i => i.ValidateModelReferences, (i, v) => i.ValidateModelReferences = v),
        new(RuleIds.DuplicateDeclaration, i => i.CheckDuplicateDeclarations, (i, v) => i.CheckDuplicateDeclarations = v),
        new(RuleIds.DuplicateImport, i => i.CheckDuplicateImports, (i, v) => i.CheckDuplicateImports = v),
        new(RuleIds.MissingUnit, i => i.CheckMissingUnits, (i, v) => i.CheckMissingUnits = v),
        new(RuleIds.UnusedImport, i => i.CheckUnusedImports, (i, v) => i.CheckUnusedImports = v),
        new(RuleIds.PackageOrder, i => i.CheckPackageOrder, (i, v) => i.CheckPackageOrder = v),
        new(RuleIds.UsesUndeclared, i => i.CheckUsesUndeclared, (i, v) => i.CheckUsesUndeclared = v),
        new(RuleIds.UsesDeclaredUnused, i => i.CheckUsesDeclaredUnused, (i, v) => i.CheckUsesDeclaredUnused = v),
        new(RuleIds.UnusedClass, i => i.CheckUnusedClass, (i, v) => i.CheckUnusedClass = v),
        new(RuleIds.UnusedPublicClass, i => i.CheckUnusedPublicClass, (i, v) => i.CheckUnusedPublicClass = v),
        new(RuleIds.ShadowingInheritedMember, i => i.CheckShadowing, (i, v) => i.CheckShadowing = v),
        new(RuleIds.UnusedMember, i => i.CheckUnusedMembers, (i, v) => i.CheckUnusedMembers = v),
    ];

    /// <summary>The rules this input can set, for the test that holds it to <see cref="RuleCatalog"/>.</summary>
    public static IReadOnlyList<string> SettableRuleIds { get; } = _toggles.Select(t => t.RuleId).ToList();

    /// <summary>
    /// Copies the toggles that were supplied onto an existing settings object, leaving everything
    /// else — the rules not mentioned, the naming convention, SVN branch dirs, excluded models,
    /// commit rules — untouched. This is the merge used when persisting.
    ///
    /// <para>A null toggle means "not mentioned", not "off". This is load-bearing: the settings
    /// object here is a repository's own, read from and written back to a committed
    /// <c>.mlqt/settings.json</c>, so treating an absent key as <c>false</c> turned enabling one rule
    /// into switching off every other one.</para>
    /// </summary>
    public void ApplyTo(StyleCheckingSettings s)
    {
        foreach (var toggle in _toggles)
        {
            if (toggle.Read(this) is { } enabled)
                s.SetRuleEnabled(toggle.RuleId, enabled);
        }

        // Not a rule — a formatter flag, so it is not in the toggle table.
        if (ComponentsBeforeClasses is { } componentsFirst)
            s.ComponentsBeforeClasses = componentsFirst;

        if (SpellCheckLanguages is { Count: > 0 })
            s.SpellCheckLanguages = SpellCheckLanguages.ToList();
    }

    /// <summary>A fresh, full settings object from this input (default naming/SVN config). Rules the
    /// input does not mention stay off, which is what a one-off check of an explicit rule set means.</summary>
    public StyleCheckingSettings ToSettings()
    {
        var s = new StyleCheckingSettings();
        ApplyTo(s);
        return s;
    }

    /// <summary>
    /// The settings as an input object, every toggle populated.
    ///
    /// <para>A rule reports whether it is <em>switched on</em>, not whether it would currently run:
    /// the ordering rules are inert while <c>OneOfEachSection</c> is off, and reporting those as
    /// <c>false</c> would mean a read-modify-write round trip through <c>set_style_settings</c>
    /// silently discarded them. Same distinction, and the same reason, as the settings dialog's —
    /// see <see cref="StyleCheckingSettings.IsRuleSwitchedOn"/>.</para>
    /// </summary>
    public static StyleSettingsInput From(StyleCheckingSettings s)
    {
        var input = new StyleSettingsInput
        {
            ComponentsBeforeClasses = s.ComponentsBeforeClasses,
            SpellCheckLanguages = s.SpellCheckLanguages?.ToList(),
        };

        foreach (var toggle in _toggles)
            toggle.Write(input, s.IsRuleSwitchedOn(toggle.RuleId));

        return input;
    }
}

public sealed record StyleSettingsResult(
    string? RepositoryId,
    string? Repository,
    string Source,
    StyleSettingsInput Settings);

public sealed record SetStyleSettingsResult(
    string RepositoryId,
    string Repository,
    bool Persisted,
    string? Path,
    string? Note,
    StyleSettingsInput Settings);

public sealed record StyleFindingDto(
    string ModelName,
    string Severity,
    int Line,
    string Summary,
    string Details,
    string Source);

public sealed record CheckResult(
    int ModelsChecked,
    int FindingCount,
    IReadOnlyList<StyleFindingDto> Findings,
    bool Truncated);

/// <summary>
/// One finding, with both line numbers it has. <see cref="Line"/> is the line in
/// <see cref="FilePath"/> — the pair to use when editing the file. <see cref="ModelLine"/> is the
/// same finding's line within the class's own source, for a caller that fetched the class by id.
///
/// <para>They are separate fields because they are different numbers: a class nested a thousand
/// lines down a <c>package.mo</c> has findings whose two lines are a thousand apart. The names match
/// the CLI's JSON report (<c>Line</c>, <c>ModelLine</c>, <c>File</c>) so the two surfaces cannot be
/// read as meaning different things.</para>
/// </summary>
public sealed record FindingItem(
    string ModelId,
    string Category,
    string Severity,
    int Line,
    int ModelLine,
    string Summary,
    string Details,
    string Source,
    string? FilePath);

public sealed record FindingsResult(
    int Total,
    int Offset,
    int Count,
    IReadOnlyList<FindingItem> Items);

public sealed record FormatCodeResult(
    string Source);

public sealed record FormatClassResult(
    string Id,
    bool PreviewOnly,
    bool Changed,
    string? FilePath,
    string Source);

public sealed record SpellSuggestionsResult(
    string Word,
    bool IsCorrect,
    IReadOnlyList<string> Suggestions,
    string? Note = null);

/// <summary>
/// The misspellings found, plus a note when this machine has no dictionary for a language the
/// settings ask for — in which case the results are not the ones the settings describe. The CLI
/// warns about the same gap on stderr; an agent gets it here.
/// </summary>
public sealed record SpellCheckResult(
    IReadOnlyList<StyleFindingDto> Findings,
    string? Note = null);

public sealed record UpdateClassSourceResult(
    string ClassId,
    string? FilePath,
    bool PreviewOnly,
    bool Changed,
    int AffectedModelCount,
    string? NewFileContent);

public sealed record CreateClassResult(
    string NewClassId,
    string FilePath,
    string Storage,
    bool PreviewOnly,
    bool Created,
    string? NewFileContent);

public sealed record DeleteClassResult(
    string DeletedClassId,
    string FilePath,
    string Storage,
    bool PreviewOnly,
    bool Deleted,
    bool DependenciesChecked,
    IReadOnlyList<string> DanglingReferences,
    string? Note);

public sealed record MoveClassResult(
    string OldClassId,
    string NewClassId,
    string Storage,
    bool PreviewOnly,
    bool Moved,
    int ReferencesRequalified,
    int FilesChanged,
    IReadOnlyList<string> BrokenReferencesInMovedClass,
    string Note);

/// <summary>One operation in a batch_edit. Op names the surgical edit; the other fields carry its
/// arguments (only those relevant to the chosen Op are used).</summary>
public sealed class BatchOperation
{
    public string Op { get; set; } = string.Empty;
    public string ClassId { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? Modifier { get; set; }
    public string? Description { get; set; }
    public string? BaseType { get; set; }
    public string? Import { get; set; }
    public string? Equation { get; set; }
    public string? Statement { get; set; }
    public string? PortA { get; set; }
    public string? PortB { get; set; }
    public string? Comment { get; set; }
    public string? Visibility { get; set; }
    public string? Prefix { get; set; }
    public string? ConstrainedBy { get; set; }
    public string? Condition { get; set; }
}

public sealed record BatchFileChange(string FilePath, string? NewContent);

public sealed record BatchEditResult(
    bool PreviewOnly,
    int OperationsApplied,
    IReadOnlyList<BatchFileChange> Files);

public sealed record StructureEditResult(
    string ClassId,
    string FilePath,
    bool PreviewOnly,
    bool Changed,
    int AffectedModelCount,
    string? NewFileContent,
    string? Note);

public sealed record RenameFileChange(
    string FilePath,
    int Replacements,
    string? NewContent);

public sealed record RenameClassResult(
    string OldClassId,
    string NewClassId,
    bool PreviewOnly,
    bool Changed,
    int FilesChanged,
    int TotalReplacements,
    IReadOnlyList<RenameFileChange> Changes,
    string Note);

public sealed record CorrectSpellingResult(
    string ClassId,
    string? FilePath,
    int Replacements,
    bool Changed,
    bool PreviewOnly,
    string? Source);
