using ModelicaGraph;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// Holds <see cref="StyleSettingsInput"/> to the rule catalog, and pins the merge semantics that
/// stop <c>set_style_settings</c> destroying a repository's configuration.
/// </summary>
public class StyleSettingsCoverageTests
{
    private const string Package = """
        within;
        package TestLib "Test library"
          model Base "Base model"
            Real b "state";
          equation
            b = time;
          end Base;
        end TestLib;
        """;

    private static async Task<(StyleTools style, string repoId)> LoadRepo(TestHost host)
    {
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["TestLib/package.mo"] = Package });
        var res = await host.Repositories.AddRepositoryAsync(dir, startMonitoring: false);
        await host.Repositories.LoadLibrariesAsync(res.Repository!.Id);
        var style = new StyleTools(
            host.Libraries, host.CodeReview, host.Repositories,
            host.CustomDictionary, host.DictionaryManager, host.Session);
        return (style, res.Repository.Id);
    }

    // ---- the catalog guard (B38) --------------------------------------------------------------

    [Fact]
    public void EveryConfigurableRuleCanBeSetThroughTheTool()
    {
        var settable = StyleSettingsInput.SettableRuleIds.ToHashSet(StringComparer.Ordinal);

        var missing = RuleCatalog.Configurable
            .Select(d => d.Id)
            .Where(id => !settable.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "These rules have a setting of their own but no toggle on StyleSettingsInput, so an agent " +
            "cannot enable them and get_style_settings cannot report them: " + string.Join(", ", missing));
    }

    [Fact]
    public void TheToolOffersNothingThatIsNotAConfigurableRule()
    {
        var configurable = RuleCatalog.Configurable.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

        var strays = StyleSettingsInput.SettableRuleIds
            .Where(id => !configurable.Contains(id))
            .ToList();

        // A governed rule or a diagnostic offered here would accept a value nothing reads - the same
        // shape as the settings file keys IgnoredRuleKeys() warns about.
        Assert.True(strays.Count == 0, "Not configurable rules: " + string.Join(", ", strays));
    }

    [Fact]
    public void NoRuleIsBoundTwice()
    {
        Assert.Equal(
            StyleSettingsInput.SettableRuleIds.Distinct(StringComparer.Ordinal).Count(),
            StyleSettingsInput.SettableRuleIds.Count);
    }

    [Fact]
    public void EveryToggleReadsBackWhatItWrote()
    {
        // Catches a toggle wired to one rule for reading and another for writing, which no
        // round-trip through a single property would notice.
        foreach (var ruleId in StyleSettingsInput.SettableRuleIds)
        {
            var settings = new StyleCheckingSettings();
            settings.SetRuleEnabled(ruleId, true);

            var input = StyleSettingsInput.From(settings);
            var round = input.ToSettings();

            Assert.True(round.IsRuleSwitchedOn(ruleId), $"{ruleId} did not survive From/ToSettings");

            var others = StyleSettingsInput.SettableRuleIds.Where(id => id != ruleId);
            Assert.DoesNotContain(others, id => round.IsRuleSwitchedOn(id));
        }
    }

    // ---- the merge (B37) ----------------------------------------------------------------------

    [Fact]
    public void ApplyTo_LeavesRulesTheInputDoesNotMention()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleEnabled(RuleIds.ClassDescription, true);
        settings.SetRuleEnabled(RuleIds.ClassIcon, true);

        new StyleSettingsInput { CheckMissingUnits = true }.ApplyTo(settings);

        Assert.True(settings.IsRuleSwitchedOn(RuleIds.ClassDescription));
        Assert.True(settings.IsRuleSwitchedOn(RuleIds.ClassIcon));
        Assert.True(settings.IsRuleSwitchedOn(RuleIds.MissingUnit));
    }

    [Fact]
    public void ApplyTo_StillSwitchesOffWhenAskedTo()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleEnabled(RuleIds.ClassDescription, true);

        new StyleSettingsInput { ClassHasDescription = false }.ApplyTo(settings);

        Assert.False(settings.IsRuleSwitchedOn(RuleIds.ClassDescription));
    }

    [Fact]
    public void ApplyTo_KeepsAnExplicitSeverityWhenTheRuleStaysOn()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(RuleIds.ClassDescription, RuleSeverity.Error);

        new StyleSettingsInput { ClassHasDescription = true }.ApplyTo(settings);

        Assert.Equal(RuleSeverity.Error, settings.SeverityFor(RuleIds.ClassDescription));
    }

    [Fact]
    public void ToSettings_TreatsAnUnmentionedRuleAsOff()
    {
        // A check tool builds from a blank settings object, so "not mentioned" and "off" coincide
        // there - which is what makes the nullable toggles safe for check_style/check_class.
        var settings = new StyleSettingsInput { ClassHasDescription = true }.ToSettings();

        Assert.True(settings.IsRuleSwitchedOn(RuleIds.ClassDescription));
        Assert.False(settings.IsRuleSwitchedOn(RuleIds.ClassIcon));
    }

    [Fact]
    public async Task SetStyleSettings_EnablingOneRuleDoesNotSwitchOffTheRest()
    {
        using var host = new TestHost();
        var (style, repoId) = await LoadRepo(host);
        var repo = host.Repositories.GetRepository(repoId)!;

        await style.SetStyleSettings(new StyleSettingsInput { ClassHasDescription = true, ClassHasIcon = true });
        await style.SetStyleSettings(new StyleSettingsInput { CheckMissingUnits = true });

        // The settings file is committed with the library. Enabling one rule used to write `false`
        // over the other twenty-eight and persist that, which is a gate narrowed to one rule and a
        // large diff nobody asked for.
        Assert.True(repo.StyleSettings!.IsRuleSwitchedOn(RuleIds.ClassDescription));
        Assert.True(repo.StyleSettings.IsRuleSwitchedOn(RuleIds.ClassIcon));
        Assert.True(repo.StyleSettings.IsRuleSwitchedOn(RuleIds.MissingUnit));
    }

    [Fact]
    public async Task GetThenSet_IsAFaithfulRoundTrip_EvenForAnInertOrderingRule()
    {
        using var host = new TestHost();
        var (style, repoId) = await LoadRepo(host);
        var repo = host.Repositories.GetRepository(repoId)!;

        // Switched on, but inert: the ordering rules need OneOfEachSection, which is off.
        repo.StyleSettings!.SetRuleEnabled(RuleIds.ImportStatementsFirst, true);
        Assert.False(repo.StyleSettings.IsRuleEnabled(RuleIds.ImportStatementsFirst));

        var read = ToolAssert.Ok<StyleSettingsResult>(style.GetStyleSettings());
        await style.SetStyleSettings(read.Settings);

        // Reporting the effective answer here would have written the rule off on the way back,
        // silently, from a round trip that changed nothing.
        Assert.True(repo.StyleSettings.IsRuleSwitchedOn(RuleIds.ImportStatementsFirst));
    }
}
