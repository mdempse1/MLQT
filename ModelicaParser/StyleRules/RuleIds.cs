namespace ModelicaParser.StyleRules;

/// <summary>
/// Stable identifiers for the built-in style rules. These are load-bearing: they feed the
/// finding fingerprint (baseline), the severity map, suppression matching, and SARIF metadata,
/// so existing values must not change once shipped.
/// </summary>
public static class RuleIds
{
    public const string ParameterDescription = "MLQT.Doc.ParameterDescription";
    public const string ConstantDescription = "MLQT.Doc.ConstantDescription";
    public const string ImportStatementsFirst = "MLQT.Style.ImportStatementsFirst";
    public const string ExtendsAtTop = "MLQT.Style.ExtendsAtTop";
    public const string InitialEqAlgoFirst = "MLQT.Style.InitialEqAlgoFirst";
    public const string InitialEqAlgoLast = "MLQT.Style.InitialEqAlgoLast";
    public const string OneOfEachSection = "MLQT.Style.OneOfEachSection";
    public const string DontMixEquationAndAlgorithm = "MLQT.Style.DontMixEquationAndAlgorithm";
    public const string DontMixConnections = "MLQT.Style.DontMixConnections";
    public const string ClassDescription = "MLQT.Doc.ClassDescription";
    public const string ClassDocumentationInfo = "MLQT.Doc.ClassDocumentationInfo";
    public const string ClassDocumentationRevisions = "MLQT.Doc.ClassDocumentationRevisions";
    public const string ClassIcon = "MLQT.Doc.ClassIcon";
    public const string ModelReferences = "MLQT.Reference.ModelReferences";
    public const string SpellingDescription = "MLQT.Spelling.Description";
    public const string SpellingDocumentation = "MLQT.Spelling.Documentation";
    public const string NamingConvention = "MLQT.Naming.Convention";

    // Wave-1 analyses (Phase 6). Disabled by default like every rule; a library opts in.
    public const string DuplicateDeclaration = "MLQT.Duplicate.Declaration";
    public const string DuplicateImport = "MLQT.Duplicate.Import";
    public const string MissingUnit = "MLQT.Units.MissingUnit";
    public const string UnusedImport = "MLQT.Unused.Import";
    public const string PackageOrder = "MLQT.Structure.PackageOrder";
    public const string UsesUndeclared = "MLQT.Structure.UsesUndeclared";
    public const string UsesDeclaredUnused = "MLQT.Structure.UsesDeclaredUnused";
    public const string UnusedClass = "MLQT.Unused.Class";
    public const string UnusedPublicClass = "MLQT.Unused.PublicClass";
    public const string ShadowingInheritedMember = "MLQT.Shadowing.InheritedMember";
    public const string UnusedMember = "MLQT.Unused.Member";

    // Parse diagnostics. These are NOT style rules: they are always reported regardless of the
    // severity map and cannot be suppressed or baselined, because code that does not parse cannot
    // be meaningfully checked — every other rule silently under-reports on it.
    public const string SyntaxError = "MLQT.Parse.SyntaxError";
    public const string ParseFailure = "MLQT.Parse.Failure";

    /// <summary>True for the always-on parse diagnostics above.</summary>
    public static bool IsParseDiagnostic(string ruleId) =>
        ruleId is SyntaxError or ParseFailure;
}
