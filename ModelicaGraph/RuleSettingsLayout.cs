using ModelicaParser.StyleRules;

namespace ModelicaGraph;

/// <summary>How a rule is offered in the repository-settings dialog.</summary>
public enum RuleControl
{
    /// <summary>An Off/Info/Warning/Error picker, rendered from this declaration. Adding a rule to
    /// such a section is all it takes for the control to exist.</summary>
    SeverityPicker,

    /// <summary>
    /// A control written by hand, because the rule interlocks with something else: the formatting
    /// switches are mutually exclusive and drive the formatter, naming opens a whole sub-panel, and
    /// spelling reveals the dictionary picker. Named here so a rule cannot claim a home that has no
    /// control behind it — <c>RuleSettingsLayoutTests</c> checks the razor for the binding.
    /// </summary>
    Bespoke
}

/// <param name="RuleId">The rule this row sets.</param>
/// <param name="Section">The heading it appears under.</param>
/// <param name="Control">How it is presented.</param>
/// <param name="Label">The wording, for a picker row. Null for a bespoke control, which carries its own.</param>
/// <param name="Binding">
/// For a bespoke control, the settings member the razor binds to. This is the string the layout test
/// looks for in the markup, so it is what proves the control exists.
/// </param>
public sealed record RuleSettingsRow(
    string RuleId,
    string Section,
    RuleControl Control,
    string? Label = null,
    string? Binding = null);

/// <summary>
/// The declaration of what the repository-settings dialog offers, and the answer to "where is this
/// rule set?"
///
/// <para>It exists because the dialog used to answer that question in three unrelated places: a
/// catalog-driven list for four categories, a hand-written array for eight documentation rules, one
/// row of hard-coded markup for reference validation, and bool-bound switches for the rest. A rule
/// added outside all of them was documented, gateable from CI, and invisible in the app — with
/// nothing failing. <c>settings-reference.md</c> has had a test holding it to the catalog since the
/// alert links started pointing at it; this is the same test pointed at the UI.</para>
///
/// <para>Adding a rule to <see cref="RuleCatalog"/> now fails <c>RuleSettingsLayoutTests</c> until it
/// has a home here. Giving it one under <see cref="RuleControl.SeverityPicker"/> is enough — the
/// dialog renders those rows from this list.</para>
/// </summary>
public static class RuleSettingsLayout
{
    public const string Formatting = "Code formatting";
    public const string Spelling = "Spell checking";
    public const string Naming = "Naming";
    public const string StyleGuidelines = "Style guidelines";
    public const string ReferenceValidation = "Reference validation";

    private static readonly RuleSettingsRow[] _rows =
    [
        // Formatting. Bespoke because the checker and the formatter read the same switches, and
        // several of them are mutually exclusive — a plain severity picker would let a repository
        // ask for "initial sections first" and "initial sections last" at once.
        new(RuleIds.OneOfEachSection, Formatting, RuleControl.Bespoke, Binding: "SelectedSettings.OneOfEachSection"),
        new(RuleIds.ImportStatementsFirst, Formatting, RuleControl.Bespoke, Binding: "SelectedSettings.ImportStatementsFirst"),
        new(RuleIds.InitialEqAlgoFirst, Formatting, RuleControl.Bespoke, Binding: "SelectedSettings.InitialEQAlgoFirst"),
        new(RuleIds.InitialEqAlgoLast, Formatting, RuleControl.Bespoke, Binding: "SelectedSettings.InitialEQAlgoLast"),

        // Spelling. Bespoke because switching either on reveals the dictionary picker.
        new(RuleIds.SpellingDescription, Spelling, RuleControl.Bespoke, Binding: "SelectedSettings.SpellCheckDescription"),
        new(RuleIds.SpellingDocumentation, Spelling, RuleControl.Bespoke, Binding: "SelectedSettings.SpellCheckDocumentation"),

        // Naming. Bespoke because it opens the preset/style sub-panel.
        new(RuleIds.NamingConvention, Naming, RuleControl.Bespoke, Binding: "SelectedSettings.FollowNamingConvention"),

        new(RuleIds.ClassDescription, StyleGuidelines, RuleControl.SeverityPicker, "Every class must have a description"),
        new(RuleIds.ClassDocumentationInfo, StyleGuidelines, RuleControl.SeverityPicker, "Every class must have documentation info"),
        new(RuleIds.ClassDocumentationRevisions, StyleGuidelines, RuleControl.SeverityPicker, "Every class must have documentation revisions"),
        new(RuleIds.ClassIcon, StyleGuidelines, RuleControl.SeverityPicker, "Every class must have an icon"),
        new(RuleIds.ParameterDescription, StyleGuidelines, RuleControl.SeverityPicker, "Every public parameter must have a description"),
        new(RuleIds.ConstantDescription, StyleGuidelines, RuleControl.SeverityPicker, "Every public constant must have a description"),
        new(RuleIds.DontMixEquationAndAlgorithm, StyleGuidelines, RuleControl.SeverityPicker, "A class may only have either an equation or algorithm section, not both"),
        new(RuleIds.DontMixConnections, StyleGuidelines, RuleControl.SeverityPicker, "Do not mix connections and equations in the same class"),

        new(RuleIds.ModelReferences, ReferenceValidation, RuleControl.SeverityPicker,
            "Validate modelica:// model references point to existing models"),
    ];

    /// <summary>
    /// The rules the dialog offers outside the catalog-driven "Static analysis" section, in the order
    /// they appear.
    /// </summary>
    public static IReadOnlyList<RuleSettingsRow> Rows => _rows;

    /// <summary>The picker rows for one section, which the dialog renders directly.</summary>
    public static IEnumerable<RuleSettingsRow> PickersIn(string section) =>
        _rows.Where(r => r.Section == section && r.Control == RuleControl.SeverityPicker);

    /// <summary>
    /// The categories rendered from <see cref="RuleCatalog"/> under "Static analysis". A rule in one
    /// of these needs no entry above: the section grows with the catalog on its own.
    /// </summary>
    public static readonly IReadOnlySet<string> CatalogDrivenCategories =
        new HashSet<string>(StringComparer.Ordinal) { "Correctness", "Units", "Unused", "Structure" };

    /// <summary>
    /// Configurable rules the dialog offers no way to set: not declared above, and not in a
    /// catalog-driven category. Empty is the only acceptable answer, which is what the layout test
    /// asserts — this method is the reason a new rule cannot land invisible.
    /// </summary>
    public static IReadOnlyList<string> UnreachableRules()
    {
        var declared = _rows.Select(r => r.RuleId).ToHashSet(StringComparer.Ordinal);

        return RuleCatalog.Configurable
            .Where(d => !declared.Contains(d.Id) && !CatalogDrivenCategories.Contains(d.Category))
            .Select(d => d.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }
}
