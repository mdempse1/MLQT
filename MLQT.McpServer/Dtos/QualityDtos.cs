using ModelicaGraph;

namespace MLQT.McpServer.Dtos;

/// <summary>
/// The set of style/spell rules to apply, exposed as a flat object of on/off toggles. Every rule
/// defaults to OFF (matching StyleCheckingSettings), so a check runs exactly the rules you enable.
/// Use get_style_settings to see the current values, modify, and pass the whole object back to a
/// check tool to re-check with different rules. The nested naming-convention configuration is not
/// exposed here; FollowNamingConvention uses the default convention.
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

    public StyleCheckingSettings ToSettings() => new()
    {
        ImportStatementsFirst = ImportStatementsFirst,
        ComponentsBeforeClasses = ComponentsBeforeClasses,
        OneOfEachSection = OneOfEachSection,
        DontMixEquationAndAlgorithm = DontMixEquationAndAlgorithm,
        DontMixConnections = DontMixConnections,
        InitialEQAlgoFirst = InitialEQAlgoFirst,
        InitialEQAlgoLast = InitialEQAlgoLast,
        ClassHasDescription = ClassHasDescription,
        ClassHasDocumentationInfo = ClassHasDocumentationInfo,
        ClassHasDocumentationRevisions = ClassHasDocumentationRevisions,
        ClassHasIcon = ClassHasIcon,
        ParameterHasDescription = ParameterHasDescription,
        ConstantHasDescription = ConstantHasDescription,
        FollowNamingConvention = FollowNamingConvention,
        SpellCheckDescription = SpellCheckDescription,
        SpellCheckDocumentation = SpellCheckDocumentation,
        ValidateModelReferences = ValidateModelReferences,
        // SpellCheckLanguages left at its default (en_US, en_GB); the spell checker built via the
        // service interface uses the bundled dictionaries regardless.
    };

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
    };
}

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

public sealed record CorrectSpellingResult(
    string ClassId,
    string? FilePath,
    int Replacements,
    bool Changed,
    bool PreviewOnly,
    string? Source);
