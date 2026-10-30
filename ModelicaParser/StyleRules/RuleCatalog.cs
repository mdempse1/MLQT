using ModelicaParser.DataTypes;

namespace ModelicaParser.StyleRules;

/// <summary>Metadata for a single rule (built-in or, later, custom).</summary>
/// <param name="RequiresRule">
/// A rule that must also be enabled for this one to do anything, or null. When the prerequisite is
/// off this rule resolves to <see cref="RuleSeverity.Off"/> however it is configured.
///
/// <para>It exists because of one real dependency rather than as a general mechanism.
/// <c>ModelicaRenderer</c> reorders a class only inside its one-of-each-section branch: with
/// <c>MLQT.Style.OneOfEachSection</c> off it writes the composition in source order and moves
/// nothing. So the other layout rules on their own would report an ordering the formatter is not able
/// to establish — findings nobody can clear by pressing Format, on a setting that looks enabled. The
/// dependency is enforced rather than documented, and the settings dialog greys the switches out.</para>
/// </param>
/// <param name="SeverityFollowsFormatter">
/// True when the rule's <em>level</em> is decided by whether the formatter is maintaining the layout
/// it checks, rather than stored: <see cref="RuleSeverity.Error"/> when it is, and
/// <see cref="RuleSeverity.Warning"/> when it is not. Such a rule is still switched on and off in the
/// usual way — its entry in the severity map records that — but the value there is not read.
///
/// <para>The reasoning is that the two situations mean different things. With formatting off, the
/// rule is advice about how code should be laid out, and advice is a warning. With formatting on, the
/// formatter rewrites the class on every save specifically to satisfy this rule, so a violation that
/// survives is not a matter of taste at all: something is wrong — a file saved outside MLQT, an
/// exclusion nobody remembers adding — and it deserves to stop a build.</para>
/// </param>
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
    string? GovernedBy = null,
    bool SeverityFollowsFormatter = false,
    string? RequiresRule = null,
    bool EnabledByDefault = false);

/// <summary>
/// Catalog of the built-in rules. This is the extensibility seam: the severity map, suppression
/// matching, SARIF rule metadata, and the dashboard's category grouping all resolve against it,
/// and custom rules (a later phase) will register additional definitions.
///
/// Note: <see cref="RuleDefinition.DefaultSeverity"/> is the severity a rule carries WHEN ENABLED —
/// it is not whether the rule is enabled by default. Enablement default is "off" (a rule id absent
/// from <c>StyleCheckingSettings.RuleSeverities</c> is disabled), matching the historical booleans.
/// No rule sets <see cref="RuleDefinition.EnabledByDefault"/> since 2026-10-29, and
/// <c>RuleDefaultsTests.NoRuleIsOnByDefault</c> holds that; the mechanism stays for consumers. A rule
/// that was on by default had to be turned off explicitly, so its <c>Off</c> was stored rather than
/// removed — see <c>StyleCheckingSettings.SetRuleSeverity</c>.
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
    /// True if the rule's level is derived from whether the formatter maintains it rather than stored.
    /// See <see cref="RuleDefinition.SeverityFollowsFormatter"/>.
    /// </summary>
    public static bool SeverityFollowsFormatter(string ruleId) =>
        _builtIn.TryGetValue(ruleId, out var def) && def.SeverityFollowsFormatter;

    /// <summary>
    /// The rule that must also be enabled for <paramref name="ruleId"/> to have any effect, or null.
    /// See <see cref="RuleDefinition.RequiresRule"/>.
    /// </summary>
    public static string? RequiredRuleFor(string ruleId) =>
        _builtIn.TryGetValue(ruleId, out var def) ? def.RequiresRule : null;

    /// <summary>
    /// True when the rule is on unless a repository turns it off, rather than off until one turns it
    /// on. See <see cref="RuleDefinition.EnabledByDefault"/>.
    /// </summary>
    public static bool IsEnabledByDefault(string ruleId) =>
        _builtIn.TryGetValue(ruleId, out var def) && def.EnabledByDefault;

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
            new RuleDefinition(RuleIds.ImportStatementsFirst, "Imports first", "Ordering", RuleSeverity.Warning, "Import statements must appear before the rest of the class definition.", SeverityFollowsFormatter: true, RequiresRule: RuleIds.OneOfEachSection),
            // Governed by ImportStatementsFirst: the two describe one ordering convention ("imports
            // first, extends next"), the formatter applies them together, and the settings page has
            // always offered them as one switch. Keeping a separate id is still right — a finding
            // about a misplaced extends should say so, and be suppressible on its own — but there is
            // no second switch behind it, and now nothing pretends otherwise.
            new RuleDefinition(RuleIds.ExtendsAtTop, "Extends clauses at top", "Ordering", RuleSeverity.Warning, "Extends clauses must appear at the top of the class. Enabled and configured through MLQT.Style.ImportStatementsFirst, which is the same convention seen from the other end.", GovernedBy: RuleIds.ImportStatementsFirst),
            new RuleDefinition(RuleIds.InitialEqAlgoFirst, "Initial sections first", "Ordering", RuleSeverity.Warning, "Initial equation/algorithm sections must appear before regular ones.", SeverityFollowsFormatter: true, RequiresRule: RuleIds.OneOfEachSection),
            new RuleDefinition(RuleIds.InitialEqAlgoLast, "Initial sections last", "Ordering", RuleSeverity.Warning, "Initial equation/algorithm sections must appear after regular ones.", SeverityFollowsFormatter: true, RequiresRule: RuleIds.OneOfEachSection),
            new RuleDefinition(RuleIds.OneOfEachSection, "One of each section", "Ordering", RuleSeverity.Warning, "A class must not contain more than one of each section type.", SeverityFollowsFormatter: true),
            // Requires ImportStatementsFirst rather than OneOfEachSection, unlike its neighbours: the
            // renderer reads this option only inside the branch imports-first selects, so with that
            // off it reorders nothing and the rule would report an arrangement formatting cannot
            // reach. The prerequisite chains — imports-first itself requires one-of-each-section — so
            // naming the nearer one states the real dependency without restating the far one.
            new RuleDefinition(RuleIds.ComponentsBeforeClasses, "Components before classes", "Ordering", RuleSeverity.Warning, "Component declarations must appear before nested class definitions within a section.", SeverityFollowsFormatter: true, RequiresRule: RuleIds.ImportStatementsFirst),
            // Requires ComponentsBeforeClasses, which is the boundary this extends from one to
            // four. The renderer reads the finer order only inside the branch that option selects,
            // so with it off nothing is reordered and the rule would report an arrangement the
            // formatter cannot reach — the same reason its prerequisite names imports-first rather
            // than one-of-each-section. The chain resolves transitively.
            new RuleDefinition(RuleIds.DeclarationOrder, "Declarations in order", "Ordering", RuleSeverity.Warning, "Declarations must be written in the order inputs and outputs, constants, parameters, variables, components.", SeverityFollowsFormatter: true, RequiresRule: RuleIds.ComponentsBeforeClasses),
            new RuleDefinition(RuleIds.DontMixEquationAndAlgorithm, "Don't mix equation and algorithm", "Ordering", RuleSeverity.Warning, "A class must not mix equation and algorithm sections."),
            new RuleDefinition(RuleIds.DontMixConnections, "Don't mix connections and equations", "Ordering", RuleSeverity.Warning, "An equation section must not mix connect statements and equations."),
            new RuleDefinition(RuleIds.ClassDescription, "Class has description", "Documentation", RuleSeverity.Warning, "A class must have a description string."),
            new RuleDefinition(RuleIds.ClassDocumentationInfo, "Class has Documentation info", "Documentation", RuleSeverity.Warning, "A class must have a Documentation(info=...) annotation."),
            new RuleDefinition(RuleIds.ClassDocumentationRevisions, "Class has Documentation revisions", "Documentation", RuleSeverity.Warning, "A class must have a Documentation(revisions=...) annotation."),
            new RuleDefinition(RuleIds.ClassIcon, "Class has Icon", "Documentation", RuleSeverity.Warning, "A class must have an Icon annotation (directly or inherited)."),
            new RuleDefinition(RuleIds.ModelReferences, "Valid model references", "Reference", RuleSeverity.Warning, "modelica:// model references must name a class that exists. Reported only when MLQT can tell: a link into a library that is not loaded, or through a class with a base MLQT cannot read, is not."),
            new RuleDefinition(RuleIds.SpellingDescription, "Spelling in descriptions", "Spelling", RuleSeverity.Warning, "Description strings must be free of spelling mistakes."),
            new RuleDefinition(RuleIds.SpellingDocumentation, "Spelling in documentation", "Spelling", RuleSeverity.Warning, "Documentation strings must be free of spelling mistakes."),
            new RuleDefinition(RuleIds.NamingConvention, "Follows naming convention", "Naming", RuleSeverity.Warning, "Class and element names must follow the configured naming convention."),
            new RuleDefinition(RuleIds.DuplicateDeclaration, "No duplicate declarations", "Correctness", RuleSeverity.Error, "A name must not be declared more than once in the same class."),
            new RuleDefinition(RuleIds.DuplicateImport, "No duplicate imports", "Correctness", RuleSeverity.Warning, "The same name must not be imported more than once in a class."),
            new RuleDefinition(RuleIds.MissingUnit, "Quantity declares a unit", "Units", RuleSeverity.Warning, "A numeric quantity should declare a unit, or use a type that fixes one. A plain Real is always judged; any other type is followed through its alias chain, so an SI type passes and an alias of Real that fixes nothing does not."),
            new RuleDefinition(RuleIds.UnusedImport, "No unused imports", "Unused", RuleSeverity.Warning, "An import must be referenced in the class that declares it, or in a class nested inside it."),
            new RuleDefinition(RuleIds.PackageOrder, "package.order is consistent", "Structure", RuleSeverity.Warning, "package.order entries must match the package's classes/members, and every child class must be listed, once."),
            // Off by default like every rule (decided 2026-10-29). It was the one rule on unless a
            // repository turned it off (B241), because a library drifts into single-file packages
            // without anyone doing anything; every rule now waits to be switched on, so an upgrade
            // never changes what a repository reports.
            new RuleDefinition(RuleIds.SingleFilePackage, "Packages are stored as directories", "Structure", RuleSeverity.Warning, "A package whose classes could each be stored in their own file must be a directory rather than a single .mo file."),
            // An Error when enabled: the file is not where its within clause puts it, so MLQT shows
            // the class somewhere no other Modelica tool will find it, and Format All refuses the
            // library rather than lose the file (B458). Opt-in like the rest, as the user decided.
            new RuleDefinition(RuleIds.WithinClause, "within clause matches the directory", "Structure", RuleSeverity.Error, "A file stored in a package's directory must begin with a within clause naming that package (MLS 13.4.3); a package.mo names the package of the directory above. MLQT places a class by its within clause and other tools by its directory, so a mismatch puts the class where no other tool will find it."),
            new RuleDefinition(RuleIds.UsesUndeclared, "Referenced libraries are declared", "Structure", RuleSeverity.Warning, "A library referenced by the code must be declared in the top-level uses(...) annotation."),
            new RuleDefinition(RuleIds.UsesDeclaredUnused, "No unused uses() dependencies", "Structure", RuleSeverity.Warning, "A library declared in uses(...) must actually be referenced by the code."),
            new RuleDefinition(RuleIds.UnusedClass, "No unused protected classes", "Unused", RuleSeverity.Warning, "A protected nested class that nothing references is dead code. Classes with an experiment(...) annotation are exempt — they are simulation entry points."),
            new RuleDefinition(RuleIds.UnusedPublicClass, "Possibly-unused public classes", "Unused", RuleSeverity.Info, "A public nested class that nothing in the loaded libraries references — lower confidence, as a downstream library you cannot see may use it. Classes with an experiment(...) annotation are exempt. Best on an application library, not a foundational one."),
            new RuleDefinition(RuleIds.ShadowingInheritedMember, "No shadowed inherited members", "Correctness", RuleSeverity.Warning, "A declaration must not silently shadow a same-named member inherited via extends (use redeclare to override intentionally)."),
            new RuleDefinition(RuleIds.UnusedMember, "No unused protected members", "Unused", RuleSeverity.Warning, "A protected component/parameter/constant in a class that nothing extends should be referenced."),
            // An Error when enabled, as a duplicate declaration is: a name that resolves to nothing
            // is a model that will not translate. Reported only where every scope the lookup passes
            // through is readable source - a base, import or library MLQT cannot see could declare
            // the name, so a miss there says nothing (the roadmap's three-state rule).
            new RuleDefinition(RuleIds.UndeclaredName, "Names are declared", "Correctness", RuleSeverity.Error, "Every name used in a declaration, modification, equation or algorithm must be declared where it is used: in the class, inherited, imported, a constant of an enclosing class, or a class that lookup reaches (MLS §5.3). A member named after a dot, and a modification's target, must exist in that component's or class's type. Reported only when every scope the lookup passes through is readable source, so a name that a library MLQT cannot see might declare is never reported."),
            // Phase 2. Off by default, and ships off, as every phase 2 rule does (decided 2026-10-29;
            // roadmap, "Before the next release"). A reference into a library that is not loaded is never reported: it
            // cannot be resolved, so it says nothing about the library being checked.
            new RuleDefinition(RuleIds.MissingResource, "Referenced resources exist", "Resources", RuleSeverity.Error, "A file or directory the code refers to - through loadResource, a modelica:// URI, a loadSelector parameter, an external function's Include header or an IncludeDirectory, LibraryDirectory or SourceDirectory annotation - must exist. A model that reads a missing file fails when it runs, and one whose header or source directory is missing does not compile. Reported only for a reference that resolves into a loaded library or to a path on disk; one into a library that is not loaded says nothing either way."),
            new RuleDefinition(RuleIds.AbsoluteResourcePath, "Resource paths are portable", "Resources", RuleSeverity.Warning, "A resource must be referred to by a modelica:// URI or a path relative to the file, not an absolute file-system path, which exists only on the machine it was written on."),
            // Phase 2, off by default like the rest of it. A Warning: packages that depend on each
            // other are valid Modelica, and the cost is to the library's structure, not its results.
            new RuleDefinition(RuleIds.DependencyCycle, "No dependency cycles between packages", "Structure", RuleSeverity.Warning, "Packages that are children of the same package must not depend on each other in a cycle - Components using Interfaces while Interfaces uses Components - since then neither can be understood, tested or moved on its own. Classes that refer to each other are ordinary Modelica; only the packages they are in are compared, at the level where their names part. Reported once per cycle, on the package whose children are in it, with one route round the cycle and the class that makes each step. Only dependencies within the libraries being checked are followed."),
            // Phase 2, off by default. An incompatible connection is an Error: the model will not
            // translate. A duplicate is a Warning: the second is redundant, not wrong.
            new RuleDefinition(RuleIds.IncompatibleConnection, "Connected connectors are compatible", "Connections", RuleSeverity.Error, "Both ends of a connect must be connectors of the same structure: the same members with the same flow and stream prefixes, or, for a connector built from a value such as RealInput, the same value type. Reported only when MLQT can tell - both ports resolve and neither connector is encrypted, expandable, replaceable or built on a base MLQT cannot read."),
            new RuleDefinition(RuleIds.DuplicateConnection, "No duplicate connections", "Connections", RuleSeverity.Warning, "The same two ports must not be connected twice in a class, either way round, under the same for, if or when branches. The same pair in different branches of an if is not a duplicate."),
            // Phase 2, off by default. A Warning: an obsolete class still works today; the cost is
            // the day its library removes it.
            new RuleDefinition(RuleIds.ObsoleteClass, "No uses of obsolete classes", "Correctness", RuleSeverity.Warning, "A class must not use a class its library marks obsolete with annotation(obsolete=\"...\"), or one inside an obsolete package: as a component type, a base class, a function called, an import or a constant. A class that is itself obsolete, or inside an obsolete one, may use others."),
            new RuleDefinition(RuleIds.GrammarConformance, "Strict Modelica 3.7 syntax", "Conformance", RuleSeverity.Warning, "Code must parse under the concrete syntax of the Modelica Language Specification 3.7 as written. MLQT's grammar also accepts what Dymola accepts beyond it - a class annotation before the elements, a modification written with ':=', escapes MLS does not define, 'time' as an identifier and the rest listed in the documentation - and each place a class relies on one of those is reported, since another tool may refuse it."),
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
