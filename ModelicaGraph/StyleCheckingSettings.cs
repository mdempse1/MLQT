namespace ModelicaGraph;

public class StyleCheckingSettings
{
    // Commit message requirements
    public bool CommitRequiresIssueNumber { get; set; } = false;
    public bool IssueNumberAtEnd { get; set; } = false;

    // Code formatting settings
    public bool ApplyFormattingRules { get; set; } = false;
    public bool ImportStatementsFirst { get; set; } = false;
    public bool ComponentsBeforeClasses { get; set; } = false;
    public bool OneOfEachSection { get; set; } = false;
    public bool DontMixEquationAndAlgorithm { get; set; } = false;
    public bool DontMixConnections { get; set; } = false;
    public bool InitialEQAlgoFirst { get; set; } = false;
    public bool InitialEQAlgoLast { get; set; } = false;

    // Models excluded from formatting (by fully qualified model ID)
    public List<string> FormattingExcludedModels { get; set; } = new();

    public bool IsModelExcludedFromFormatting(string modelId)
        => FormattingExcludedModels.Contains(modelId, StringComparer.Ordinal);

    // Style guidelines
    public bool ClassHasDescription { get; set; } = false;
    public bool ClassHasDocumentationInfo { get; set; } = false;
    public bool ClassHasDocumentationRevisions { get; set; } = false;
    public bool ClassHasIcon { get; set; } = false;
    public bool ParameterHasDescription { get; set; } = false;
    public bool ConstantHasDescription { get; set; } = false;

    public bool FollowNamingConvention { get; set; } = false;
    public NamingConventionSettings NamingConvention { get; set; } = new();

    public bool SpellCheckDescription { get; set; } = false;
    public bool SpellCheckDocumentation { get; set; } = false;

    /// <summary>
    /// Language codes for spell checking dictionaries (e.g. "en_US", "en_GB").
    /// Includes both bundled and imported dictionaries.
    /// When empty, defaults to all bundled dictionaries.
    /// </summary>
    public List<string> SpellCheckLanguages { get; set; } = ["en_US", "en_GB"];

    // Reference validation
    public bool ValidateModelReferences { get; set; } = false;

    /// <summary>
    /// SVN branch directory names used when listing branches, extracting the current branch,
    /// and creating new branches. The first entry is treated as the trunk equivalent.
    /// Defaults to standard SVN layout: trunk, branches, tags.
    /// </summary>
    public List<string> SvnBranchDirectories { get; set; } = ["trunk", "branches", "tags"];

    /// <summary>
    /// Returns true if any style checking rule is enabled that would produce violations.
    /// Used to skip the entire style checking pipeline when no rules are active.
    /// </summary>
    public bool HasAnyStyleRuleEnabled =>
        ParameterHasDescription || ConstantHasDescription ||
        ImportStatementsFirst ||
        InitialEQAlgoFirst || InitialEQAlgoLast ||
        OneOfEachSection || DontMixEquationAndAlgorithm ||
        DontMixConnections ||
        ClassHasDescription || ClassHasDocumentationInfo ||
        ClassHasDocumentationRevisions || ClassHasIcon ||
        FollowNamingConvention ||
        ValidateModelReferences ||
        SpellCheckDescription || SpellCheckDocumentation;
}
