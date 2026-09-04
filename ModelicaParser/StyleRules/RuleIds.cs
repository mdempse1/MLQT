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

    /// <summary>
    /// A class MLQT failed to check. Not a judgement on the code: the checking itself threw, so this
    /// class's findings — however many there would have been — are missing from the results. Reported
    /// because the alternative, which is what MLQT used to do, is a class quietly absent from the run
    /// and a total nobody can reconcile.
    /// </summary>
    public const string CheckFailed = "MLQT.Check.Failed";

    /// <summary>
    /// True for the two diagnostics the <b>parser</b> produces. Narrower than
    /// <see cref="IsDiagnostic"/> on purpose: this is the question "did this come from reading the
    /// file", which decides whether a finding is projected as a parser message (source
    /// <c>"Parser"</c>) or a style one. Use <see cref="IsDiagnostic"/> to ask whether a finding is
    /// configurable or baselineable — <see cref="CheckFailed"/> is neither, and is not a parse error.
    /// </summary>
    public static bool IsParseDiagnostic(string ruleId) =>
        ruleId is SyntaxError or ParseFailure;

    /// <summary>
    /// True for every finding that is a <b>diagnostic rather than a rule</b>: the two parse errors
    /// and <see cref="CheckFailed"/>. None of them is enabled, configured, suppressed or written to a
    /// baseline, and none counts as style debt in the metrics trend.
    ///
    /// <para>They share one property that decides all of that: each one says <em>the results you are
    /// reading are incomplete</em>. A baseline records debt a team chose to live with, and "this
    /// class was never checked" is not something a gate should be able to accept — accepting it hides
    /// the very fact that the total cannot be trusted, permanently and invisibly. <c>CheckFailed</c>
    /// used to be baselineable purely because this predicate stopped one id short of it.</para>
    /// </summary>
    public static bool IsDiagnostic(string ruleId) =>
        ruleId is SyntaxError or ParseFailure or CheckFailed;
}
