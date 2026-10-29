using System.Text.Json;
using ModelicaGraph;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;

namespace ModelicaGraph.Tests;

/// <summary>
/// Every rule is off until a repository switches it on (decided 2026-10-29), so an upgrade never
/// changes what a repository reports.
///
/// <para><c>MLQT.Structure.SingleFilePackage</c> was on by default from B241 until then, and settings
/// files written in that time carry it: Apply recorded it at its running severity (B244), and
/// switching it off stored <c>Off</c>, since an absent key meant on. Both lines keep meaning what they
/// said, which is most of what is below.</para>
/// </summary>
public class RuleDefaultsTests
{
    private const string Rule = RuleIds.SingleFilePackage;

    [Fact]
    public void NoRuleIsOnByDefault()
    {
        // Stated as an assertion because the default is a decision, not a convenience: a rule
        // arriving with EnabledByDefault set changes what every existing repository reports the next
        // time it is opened.
        var settings = new StyleCheckingSettings();

        Assert.Empty(RuleCatalog.Configurable.Where(d => d.EnabledByDefault).Select(d => d.Id));
        Assert.Empty(RuleCatalog.Configurable.Where(d => settings.IsRuleEnabled(d.Id)).Select(d => d.Id));
    }

    [Fact]
    public void ARepositoryWithNoSettings_HasNoRuleEnabled()
    {
        var settings = new StyleCheckingSettings();

        Assert.False(settings.HasAnyStyleRuleEnabled);
        Assert.Equal(RuleSeverity.Off, settings.SeverityFor(Rule));
    }

    [Fact]
    public void ASettingsFileThatRecordedTheOldDefault_KeepsTheRuleOn()
    {
        // B244 wrote the default down on Apply, so the repository's file says Warning, and that is
        // the repository's answer now.
        var settings = JsonSerializer.Deserialize<StyleCheckingSettings>(
            $$"""{ "RuleSeverities": { "{{Rule}}": "Warning" } }""")!;

        Assert.True(settings.IsRuleEnabled(Rule));
        Assert.Equal(RuleSeverity.Warning, settings.SeverityFor(Rule));
    }

    [Fact]
    public void ASettingsFileThatSwitchedItOff_StillReadsOff_AndIsNotToldTheLineDoesNothing()
    {
        // Off was the only way to switch it off while it was on by default. It is redundant now, and
        // harmless; warning about it would send users through every repository to delete a line.
        var settings = JsonSerializer.Deserialize<StyleCheckingSettings>(
            $$"""{ "RuleSeverities": { "{{Rule}}": "Off" } }""")!;

        Assert.False(settings.IsRuleEnabled(Rule));
        Assert.False(settings.HasAnyStyleRuleEnabled);
        Assert.DoesNotContain(settings.IgnoredRuleKeys(), k => k.RuleId == Rule);
    }

    [Fact]
    public void SwitchingItOnAndOff_SurvivesASaveAndReload()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleEnabled(Rule, true);

        var on = JsonSerializer.Deserialize<StyleCheckingSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(RuleSeverity.Warning, on.SeverityFor(Rule));

        on.SetRuleSeverity(Rule, RuleSeverity.Off);
        var off = JsonSerializer.Deserialize<StyleCheckingSettings>(JsonSerializer.Serialize(on))!;
        Assert.False(off.IsRuleEnabled(Rule));
    }

    [Fact]
    public void AnExplicitSeverityIsHonoured()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(Rule, RuleSeverity.Error);

        Assert.Equal(RuleSeverity.Error, settings.SeverityFor(Rule));
    }

    [Fact]
    public void RecordDefaults_HasNothingToRecord()
    {
        // It writes down rules that are on without being asked for, and there are none, so Apply
        // adds nothing to a repository's file.
        var settings = new StyleCheckingSettings();

        settings.RecordDefaults();

        Assert.Empty(settings.RuleSeverities);
    }

    [Fact]
    public void NothingEnabled_IsAFreshObject()
    {
        Assert.Empty(StyleCheckingSettings.NothingEnabled().RuleSeverities);
        Assert.False(StyleCheckingSettings.NothingEnabled().HasAnyStyleRuleEnabled);
    }
}
