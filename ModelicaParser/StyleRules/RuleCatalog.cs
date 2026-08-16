using ModelicaParser.DataTypes;

namespace ModelicaParser.StyleRules;

/// <summary>Metadata for a single rule (built-in or, later, custom).</summary>
public sealed record RuleDefinition(
    string Id,
    string Title,
    string Category,
    RuleSeverity DefaultSeverity,
    string Description);

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
            new RuleDefinition(RuleIds.ExtendsAtTop, "Extends clauses at top", "Ordering", RuleSeverity.Warning, "Extends clauses must appear at the top of the class."),
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
            new RuleDefinition(RuleIds.MissingUnit, "Real declares a unit", "Units", RuleSeverity.Warning, "A plain Real variable or parameter should declare a unit (or use an SI type that does)."),
            new RuleDefinition(RuleIds.UnusedImport, "No unused imports", "Unused", RuleSeverity.Warning, "An import must be referenced in the class that declares it."),
            new RuleDefinition(RuleIds.PackageOrder, "package.order is consistent", "Structure", RuleSeverity.Warning, "package.order entries must match the package's classes/members, and every child class must be listed."),
        };

        return defs.ToDictionary(d => d.Id, StringComparer.Ordinal);
    }
}
