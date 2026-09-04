using ModelicaParser.DataTypes;

namespace ModelicaParser.StyleRules;

/// <summary>Metadata for a single rule (built-in or, later, custom).</summary>
/// <param name="GovernedBy">
/// The rule whose setting decides this one, or null when the rule is configured in its own right.
/// A governed rule has a stable id — it is reported, fingerprinted, baselined and suppressed like
/// any other — but it has no switch: it runs when its governor runs, and takes its governor's
/// severity. Recording that here is what stops the id being <em>almost</em> configurable, which is
/// what it was: a `.mlqt/settings.json` could name it, the file loaded without complaint, and the
/// value did nothing.
/// </param>
public sealed record RuleDefinition(
    string Id,
    string Title,
    string Category,
    RuleSeverity DefaultSeverity,
    string Description,
    string? GovernedBy = null);

/// <summary>
/// Catalog of the built-in rules. This is the extensibility seam: the severity map, suppression
/// matching, SARIF rule metadata, and the dashboard's category grouping all resolve against it,
/// and custom rules (a later phase) will register additional definitions.
///
/// Note: <see cref="RuleDefinition.DefaultSeverity"/> is the severity a rule carries WHEN ENABLED —
/// it is not whether the rule is enabled by default. Enablement default is "off" (a rule id absent
/// from <c>StyleCheckingSettings.RuleSeverities</c> is disabled), matching the historical booleans.
/// </summary>
public static class RuleCatalog
{
    private static readonly Dictionary<string, RuleDefinition> _builtIn = BuildBuiltIn();

    public static IReadOnlyDictionary<string, RuleDefinition> BuiltIn => _builtIn;

    public static bool IsKnown(string ruleId) => _builtIn.ContainsKey(ruleId);

    /// <summary>
    /// The rule whose setting decides <paramref name="ruleId"/>, or null when it is configured in its
    /// own right. See <see cref="RuleDefinition.GovernedBy"/>.
    /// </summary>
    public static string? GovernorOf(string ruleId) =>
        _builtIn.TryGetValue(ruleId, out var def) ? def.GovernedBy : null;

    /// <summary>
    /// True if the rule has a setting of its own — i.e. it is not a diagnostic and not governed by
    /// another rule. This is the set a settings UI must offer and a settings file may name.
    /// </summary>
    public static bool IsConfigurable(string ruleId) =>
        _builtIn.TryGetValue(ruleId, out var def)
        && def.GovernedBy is null
        && !RuleIds.IsDiagnostic(ruleId);

    /// <summary>Every rule that has a setting of its own, in catalog order.</summary>
    public static IEnumerable<RuleDefinition> Configurable =>
        _builtIn.Values.Where(d => IsConfigurable(d.Id));

    /// <summary>Severity a rule carries when enabled. Warning for all built-ins (parity with the
    /// historical single "Style warning" level). Unknown ids fall back to Warning.</summary>
    public static RuleSeverity DefaultSeverityFor(string ruleId) =>
        _builtIn.TryGetValue(ruleId, out var def) ? def.DefaultSeverity : RuleSeverity.Warning;

    private static Dictionary<string, RuleDefinition> BuildBuiltIn()
    {
        var defs = new[]
        {
            new RuleDefinition(RuleIds.ParameterDescription, "Parameter has description", "Documentation", RuleSeverity.Warning, "Public parameters must have a description string."),
            new RuleDefinition(RuleIds.ConstantDescription, "Constant has description", "Documentation", RuleSeverity.Warning, "Public constants must have a description string."),
            new RuleDefinition(RuleIds.ImportStatementsFirst, "Imports first", "Ordering", RuleSeverity.Warning, "Import statements must appear before the rest of the class definition."),
            // Governed by ImportStatementsFirst: the two describe one ordering convention ("imports
            // first, extends next"), the formatter applies them together, and the settings page has
            // always offered them as one switch. Keeping a separate id is still right — a finding
            // about a misplaced extends should say so, and be suppressible on its own — but there is
            // no second switch behind it, and now nothing pretends otherwise.
            new RuleDefinition(RuleIds.ExtendsAtTop, "Extends clauses at top", "Ordering", RuleSeverity.Warning, "Extends clauses must appear at the top of the class. Enabled and configured through MLQT.Style.ImportStatementsFirst, which is the same convention seen from the other end.", GovernedBy: RuleIds.ImportStatementsFirst),
            new RuleDefinition(RuleIds.InitialEqAlgoFirst, "Initial sections first", "Ordering", RuleSeverity.Warning, "Initial equation/algorithm sections must appear before regular ones."),
            new RuleDefinition(RuleIds.InitialEqAlgoLast, "Initial sections last", "Ordering", RuleSeverity.Warning, "Initial equation/algorithm sections must appear after regular ones."),
            new RuleDefinition(RuleIds.OneOfEachSection, "One of each section", "Ordering", RuleSeverity.Warning, "A class must not contain more than one of each section type."),
            new RuleDefinition(RuleIds.DontMixEquationAndAlgorithm, "Don't mix equation and algorithm", "Ordering", RuleSeverity.Warning, "A class must not mix equation and algorithm sections."),
            new RuleDefinition(RuleIds.DontMixConnections, "Don't mix connections and equations", "Ordering", RuleSeverity.Warning, "An equation section must not mix connect statements and equations."),
            new RuleDefinition(RuleIds.ClassDescription, "Class has description", "Documentation", RuleSeverity.Warning, "A class must have a description string."),
            new RuleDefinition(RuleIds.ClassDocumentationInfo, "Class has Documentation info", "Documentation", RuleSeverity.Warning, "A class must have a Documentation(info=...) annotation."),
            new RuleDefinition(RuleIds.ClassDocumentationRevisions, "Class has Documentation revisions", "Documentation", RuleSeverity.Warning, "A class must have a Documentation(revisions=...) annotation."),
            new RuleDefinition(RuleIds.ClassIcon, "Class has Icon", "Documentation", RuleSeverity.Warning, "A class must have an Icon annotation (directly or inherited)."),
            new RuleDefinition(RuleIds.ModelReferences, "Valid model references", "Reference", RuleSeverity.Warning, "modelica:// model references must resolve to a loaded model."),
            new RuleDefinition(RuleIds.SpellingDescription, "Spelling in descriptions", "Spelling", RuleSeverity.Warning, "Description strings must be free of spelling mistakes."),
            new RuleDefinition(RuleIds.SpellingDocumentation, "Spelling in documentation", "Spelling", RuleSeverity.Warning, "Documentation strings must be free of spelling mistakes."),
            new RuleDefinition(RuleIds.NamingConvention, "Follows naming convention", "Naming", RuleSeverity.Warning, "Class and element names must follow the configured naming convention."),
            new RuleDefinition(RuleIds.DuplicateDeclaration, "No duplicate declarations", "Correctness", RuleSeverity.Error, "A name must not be declared more than once in the same class."),
            new RuleDefinition(RuleIds.DuplicateImport, "No duplicate imports", "Correctness", RuleSeverity.Warning, "The same name must not be imported more than once in a class."),
            new RuleDefinition(RuleIds.MissingUnit, "Quantity declares a unit", "Units", RuleSeverity.Warning, "A numeric quantity should declare a unit, or use a type that fixes one. A plain Real is always judged; any other type is followed through its alias chain, so an SI type passes and an alias of Real that fixes nothing does not."),
            new RuleDefinition(RuleIds.UnusedImport, "No unused imports", "Unused", RuleSeverity.Warning, "An import must be referenced in the class that declares it, or in a class nested inside it."),
            new RuleDefinition(RuleIds.PackageOrder, "package.order is consistent", "Structure", RuleSeverity.Warning, "package.order entries must match the package's classes/members, and every child class must be listed."),
            new RuleDefinition(RuleIds.UsesUndeclared, "Referenced libraries are declared", "Structure", RuleSeverity.Warning, "A library referenced by the code must be declared in the top-level uses(...) annotation."),
            new RuleDefinition(RuleIds.UsesDeclaredUnused, "No unused uses() dependencies", "Structure", RuleSeverity.Warning, "A library declared in uses(...) must actually be referenced by the code."),
            new RuleDefinition(RuleIds.UnusedClass, "No unused protected classes", "Unused", RuleSeverity.Warning, "A protected nested class that nothing references is dead code. Classes with an experiment(...) annotation are exempt — they are simulation entry points."),
            new RuleDefinition(RuleIds.UnusedPublicClass, "Possibly-unused public classes", "Unused", RuleSeverity.Info, "A public nested class that nothing in the loaded libraries references — lower confidence, as a downstream library you cannot see may use it. Classes with an experiment(...) annotation are exempt. Best on an application library, not a foundational one."),
            new RuleDefinition(RuleIds.ShadowingInheritedMember, "No shadowed inherited members", "Correctness", RuleSeverity.Warning, "A declaration must not silently shadow a same-named member inherited via extends (use redeclare to override intentionally)."),
            new RuleDefinition(RuleIds.UnusedMember, "No unused protected members", "Unused", RuleSeverity.Warning, "A protected component/parameter/constant in a class that nothing extends should be referenced."),
            // Diagnostics are catalogued for SARIF metadata and suppression-id validation only. They
            // are never enabled or disabled, and never baselined — see RuleIds.IsDiagnostic. The
            // "Parse" and "Check" categories are deliberately outside the settings UI's rule
            // categories so they cannot be switched off.
            new RuleDefinition(RuleIds.SyntaxError, "Source parses cleanly", "Parse", RuleSeverity.Error, "The file contains a syntax error. The parser recovered, so the class still loaded, but part of it may have been misread and every other rule under-reports on it."),
            new RuleDefinition(RuleIds.ParseFailure, "Source could be parsed", "Parse", RuleSeverity.Error, "The file could not be parsed at all. No classes were extracted from it and nothing in it has been checked."),
            // Its own category, not "Parse": nothing failed to parse here. Reading the file worked;
            // MLQT threw afterwards. Filing it under Parse made the one predicate that matters look
            // like it covered this id when it did not.
            new RuleDefinition(RuleIds.CheckFailed, "Class could be checked", "Check", RuleSeverity.Error, "Checking this class threw, so its findings are missing from the results. A defect in MLQT or an unworkable setting (for example a naming pattern that cannot be evaluated), not a problem with the code."),
        };

        return defs.ToDictionary(d => d.Id, StringComparer.Ordinal);
    }
}
