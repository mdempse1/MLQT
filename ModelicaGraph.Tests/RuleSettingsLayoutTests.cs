using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// Every rule a repository can configure has to be settable somewhere in the app.
///
/// <para>This is the counterpart to the test that holds <c>settings-reference.md</c> to the catalog.
/// That one exists because a SARIF alert's "learn more" link went to a page that named eleven of
/// thirty-one rules; this one exists because the settings dialog had the same shape of hole and no
/// test at all. Four of nine categories were rendered from the catalog and the rest from hand-written
/// lists, so a rule added under Documentation, Ordering, Spelling, Naming or Reference — or under a
/// category nobody thought to add — was documented, gateable from CI, and invisible in the app.</para>
/// </summary>
public class RuleSettingsLayoutTests
{
    /// <summary>
    /// The repository's <c>MLQT.Shared</c> directory, found by walking up from the test binary. Null
    /// when the tests run from somewhere the sources are not, which is not a failure — the source
    /// check simply has nothing to read.
    /// </summary>
    private static string? SharedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "MLQT.Shared");
            if (File.Exists(Path.Combine(candidate, "Components", "SettingsRepositories.razor")))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static string? SettingsMarkup() => Markup("SettingsRepositories.razor");

    private static string? Markup(string component)
    {
        var shared = SharedDirectory();
        return shared is null ? null : File.ReadAllText(Path.Combine(shared, "Components", component));
    }

    [Fact]
    public void EveryConfigurableRuleCanBeSetSomewhere()
    {
        var unreachable = RuleSettingsLayout.UnreachableRules();

        Assert.True(unreachable.Count == 0,
            "These rules are in the catalog and configurable, but the settings dialog offers no way " +
            "to set them. Add each to RuleSettingsLayout (a SeverityPicker row is enough — the dialog " +
            "renders those itself), or give it a category the dialog builds from the catalog. " +
            "Missing: " + string.Join(", ", unreachable));
    }

    [Fact]
    public void NoRuleIsPlacedTwice()
    {
        var duplicates = RuleSettingsLayout.Rows
            .GroupBy(r => r.RuleId, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "A rule placed in two sections gets two controls that disagree the moment one is used: " +
            string.Join(", ", duplicates));
    }

    [Fact]
    public void NothingIsDeclaredTwiceOverTheCatalogDrivenSection()
    {
        // A row for a rule the "Static analysis" section already renders would put the same rule in
        // two places on one page.
        var doubled = RuleSettingsLayout.Rows
            .Where(r => RuleCatalog.BuiltIn.TryGetValue(r.RuleId, out var def)
                        && RuleSettingsLayout.CatalogDrivenCategories.Contains(def.Category))
            .Select(r => r.RuleId)
            .ToList();

        Assert.Empty(doubled);
    }

    [Fact]
    public void OnlyConfigurableRulesArePlaced()
    {
        // A diagnostic or a governed rule has no setting, so a control for it would write a value
        // nothing reads — the defect this and the governed-rule work were both about.
        var wrong = RuleSettingsLayout.Rows
            .Where(r => !RuleCatalog.IsConfigurable(r.RuleId))
            .Select(r => r.RuleId)
            .ToList();

        Assert.True(wrong.Count == 0,
            "These are placed in the settings dialog but cannot be set: " + string.Join(", ", wrong));
    }

    [Fact]
    public void EveryPickerRowHasWording()
    {
        Assert.All(
            RuleSettingsLayout.Rows.Where(r => r.Control == RuleControl.SeverityPicker),
            r => Assert.False(string.IsNullOrWhiteSpace(r.Label), $"{r.RuleId} has no label"));
    }

    /// <summary>
    /// A bespoke row is a claim that the dialog has a control for the rule — the one claim this
    /// layout cannot make true by itself, since the dialog writes those by hand. So it is checked
    /// against the markup, the same way the documentation test is checked against the page.
    /// </summary>
    [Fact]
    public void EveryBespokeRuleIsActuallyBoundInTheDialog()
    {
        if (SettingsMarkup() is not { } markup)
            return;

        var missing = RuleSettingsLayout.Rows
            .Where(r => r.Control == RuleControl.Bespoke)
            .Where(r => r.Binding is null || !markup.Contains(r.Binding, StringComparison.Ordinal))
            .Select(r => $"{r.RuleId} (expected a control bound to {r.Binding ?? "nothing"})")
            .ToList();

        Assert.True(missing.Count == 0,
            "RuleSettingsLayout says the dialog has a hand-written control for these, and it does " +
            "not: " + string.Join("; ", missing));
    }

    /// <summary>
    /// And the reverse for the picker rows: the dialog must render them from this list rather than
    /// from a copy, or the two drift and only one of them is tested.
    /// </summary>
    [Fact]
    public void ThePickerSectionsAreRenderedFromThisList()
    {
        if (SettingsMarkup() is not { } dialog)
            return;

        foreach (var section in RuleSettingsLayout.Rows
                     .Where(r => r.Control == RuleControl.SeverityPicker)
                     .Select(r => r.Section)
                     .Distinct(StringComparer.Ordinal))
        {
            var name = SectionConstantName(section);
            Assert.True(
                dialog.Contains($"RuleSettingsLayout.PickersIn(RuleSettingsLayout.{name})", StringComparison.Ordinal),
                $"section '{section}' declares picker rows, and the dialog does not render them");
            Assert.True(
                RuleSettingsLayout.PickersIn(section).Any(),
                $"section '{section}' declares picker rows that nothing renders");
        }
    }

    /// <summary>
    /// Every rule row goes through <c>RuleSeverityRow</c>, and none is written out by hand.
    ///
    /// <para>Not tidiness — the row's hover highlight is scoped CSS, and Blazor stamps a component's
    /// scope attribute onto that component's own elements only. A hand-written
    /// <c>&lt;div class="mlqt-rule-row"&gt;</c> in the dialog carries the dialog's scope and
    /// <c>RuleSeverityRow.razor.css</c> never reaches it, so the row renders correctly and silently
    /// stops highlighting. That is what happened in reverse when the row first moved out: the four
    /// list sections lost their highlight and the Static analysis rows, still inline, kept theirs,
    /// which is a difference nothing but a person looking at the dialog would have caught.</para>
    /// </summary>
    [Fact]
    public void NoRuleRowIsWrittenOutByHand()
    {
        if (SettingsMarkup() is not { } dialog)
            return;

        Assert.Contains("<RuleSeverityRow ", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("mlqt-rule-row", dialog, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the row's stylesheet sits beside the row, for the same reason.
    /// </summary>
    [Fact]
    public void TheRowStyleLivesWithTheRow()
    {
        if (SharedDirectory() is not { } shared)
            return;

        var css = Path.Combine(shared, "Components", "RuleSeverityRow.razor.css");
        Assert.True(File.Exists(css),
            "RuleSeverityRow.razor.css is where the row's scoped styles have to live; a stylesheet "
            + "beside any other component cannot reach the row's div.");
        Assert.Contains("mlqt-rule-row", File.ReadAllText(css), StringComparison.Ordinal);
    }

    /// <summary>The name of the <see cref="RuleSettingsLayout"/> constant holding a section heading —
    /// which is what the markup names, rather than the heading text.</summary>
    private static string SectionConstantName(string section) =>
        typeof(RuleSettingsLayout)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Single(f => (string?)f.GetRawConstantValue() == section)
            .Name;
}
