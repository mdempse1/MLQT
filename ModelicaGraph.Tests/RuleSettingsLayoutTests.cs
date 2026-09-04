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

    private static string? SettingsMarkup()
    {
        var shared = SharedDirectory();
        return shared is null
            ? null
            : File.ReadAllText(Path.Combine(shared, "Components", "SettingsRepositories.razor"));
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
        if (SettingsMarkup() is not { } markup)
            return;

        foreach (var section in RuleSettingsLayout.Rows
                     .Where(r => r.Control == RuleControl.SeverityPicker)
                     .Select(r => r.Section)
                     .Distinct(StringComparer.Ordinal))
        {
            Assert.Contains($"RuleSettingsLayout.PickersIn(", markup, StringComparison.Ordinal);
            Assert.True(
                RuleSettingsLayout.PickersIn(section).Any(),
                $"section '{section}' declares picker rows that nothing renders");
        }
    }
}
