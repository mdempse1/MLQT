using ModelicaGraph;

namespace MLQT.McpServer.Dtos;

/// <summary>
/// The set of style/spell rules to apply, exposed as a flat object of on/off toggles plus the
/// spell-check dictionary languages. Every rule defaults to OFF. Use get_style_settings to read the
/// current values (from the repository's .mlqt/settings.json), modify, and pass back to
/// set_style_settings (to persist) or a check tool (for a one-off run). The nested naming-convention
/// configuration is not exposed here and is preserved untouched when you save.
/// </summary>
public sealed class StyleSettingsInput
{
    public bool ImportStatementsFirst { get; init; }
    public bool ComponentsBeforeClasses { get; init; }
    public bool OneOfEachSection { get; init; }
    public bool DontMixEquationAndAlgorithm { get; init; }
    public bool DontMixConnections { get; init; }
    public bool InitialEQAlgoFirst { get; init; }
    public bool InitialEQAlgoLast { get; init; }
    public bool ClassHasDescription { get; init; }
    public bool ClassHasDocumentationInfo { get; init; }
    public bool ClassHasDocumentationRevisions { get; init; }
    public bool ClassHasIcon { get; init; }
    public bool ParameterHasDescription { get; init; }
    public bool ConstantHasDescription { get; init; }
    public bool FollowNamingConvention { get; init; }
    public bool SpellCheckDescription { get; init; }
    public bool SpellCheckDocumentation { get; init; }
    public bool ValidateModelReferences { get; init; }

    /// <summary>Spell-check dictionary language codes, e.g. ["en_US", "en_GB"]. Bundled codes are
    /// en_US and en_GB; other codes must have been imported as Hunspell dictionaries. When null or
    /// empty on save, the existing languages are kept.</summary>
    public IReadOnlyList<string>? SpellCheckLanguages { get; init; }

    /// <summary>Copies the rule toggles (and the spell languages, when provided) onto an existing
    /// settings object, leaving all other fields (naming convention, SVN branch dirs, excluded
    /// models, commit rules) untouched. This is the merge used when persisting.</summary>
    public void ApplyTo(StyleCheckingSettings s)
    {
        s.ImportStatementsFirst = ImportStatementsFirst;
        s.ComponentsBeforeClasses = ComponentsBeforeClasses;
        s.OneOfEachSection = OneOfEachSection;
        s.DontMixEquationAndAlgorithm = DontMixEquationAndAlgorithm;
        s.DontMixConnections = DontMixConnections;
        s.InitialEQAlgoFirst = InitialEQAlgoFirst;
        s.InitialEQAlgoLast = InitialEQAlgoLast;
        s.ClassHasDescription = ClassHasDescription;
        s.ClassHasDocumentationInfo = ClassHasDocumentationInfo;
        s.ClassHasDocumentationRevisions = ClassHasDocumentationRevisions;
        s.ClassHasIcon = ClassHasIcon;
        s.ParameterHasDescription = ParameterHasDescription;
        s.ConstantHasDescription = ConstantHasDescription;
        s.FollowNamingConvention = FollowNamingConvention;
        s.SpellCheckDescription = SpellCheckDescription;
        s.SpellCheckDocumentation = SpellCheckDocumentation;
        s.ValidateModelReferences = ValidateModelReferences;
        if (SpellCheckLanguages is { Count: > 0 })
            s.SpellCheckLanguages = SpellCheckLanguages.ToList();
    }

    /// <summary>A fresh, full settings object from this input (default naming/SVN config).</summary>
    public StyleCheckingSettings ToSettings()
    {
        var s = new StyleCheckingSettings();
        ApplyTo(s);
        return s;
    }

    public static StyleSettingsInput From(StyleCheckingSettings s) => new()
    {
        ImportStatementsFirst = s.ImportStatementsFirst,
        ComponentsBeforeClasses = s.ComponentsBeforeClasses,
        OneOfEachSection = s.OneOfEachSection,
        DontMixEquationAndAlgorithm = s.DontMixEquationAndAlgorithm,
        DontMixConnections = s.DontMixConnections,
        InitialEQAlgoFirst = s.InitialEQAlgoFirst,
        InitialEQAlgoLast = s.InitialEQAlgoLast,
        ClassHasDescription = s.ClassHasDescription,
        ClassHasDocumentationInfo = s.ClassHasDocumentationInfo,
        ClassHasDocumentationRevisions = s.ClassHasDocumentationRevisions,
        ClassHasIcon = s.ClassHasIcon,
        ParameterHasDescription = s.ParameterHasDescription,
        ConstantHasDescription = s.ConstantHasDescription,
        FollowNamingConvention = s.FollowNamingConvention,
        SpellCheckDescription = s.SpellCheckDescription,
        SpellCheckDocumentation = s.SpellCheckDocumentation,
        ValidateModelReferences = s.ValidateModelReferences,
        SpellCheckLanguages = s.SpellCheckLanguages?.ToList(),
    };
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

public sealed record StyleViolationDto(
    string ModelName,
    string Severity,
    int Line,
    string Summary,
    string Details,
    string Source);

public sealed record CheckResult(
    int ModelsChecked,
    int ViolationCount,
    IReadOnlyList<StyleViolationDto> Violations,
    bool Truncated);

public sealed record IssueItem(
    string ModelId,
    string Category,
    string Severity,
    int Line,
    string Summary,
    string Details,
    string Source,
    string? FilePath);

public sealed record IssuesResult(
    int Total,
    int Offset,
    int Count,
    IReadOnlyList<IssueItem> Items);

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
    IReadOnlyList<string> Suggestions);

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
