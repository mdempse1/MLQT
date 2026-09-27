using System.Text.Json;
using ModelicaGraph;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;

namespace ModelicaGraph.Tests;

/// <summary>
/// <c>MLQT.Structure.SingleFilePackage</c> is the one rule that is on unless a repository turns it
/// off, and this is what that has to mean.
///
/// <para>Every other rule waits to be discovered, which is right for a matter of taste. This one
/// reports a library drifting away from the layout MLQT itself maintains, and the drift happens
/// without anyone doing anything: another tool saves a new package as a single file, and the
/// incremental formatter — which never moves a class between files — reformats it in place and
/// leaves it that way. A user who has not heard of the rule is exactly the user who needs it.</para>
///
/// <para><b>The trap this has to avoid is B238's.</b> "Absent from the map" means off for every
/// other rule, so a default-on rule that is switched off by removing its key would come back on the
/// next time the settings were read. Its Off is stored instead, and the tests below are mostly about
/// that.</para>
/// </summary>
public class DefaultOnRuleTests
{
    private const string Rule = RuleIds.SingleFilePackage;

    [Fact]
    public void ARepositoryThatHasNeverHeardOfItGetsIt()
    {
        var settings = new StyleCheckingSettings();

        Assert.True(settings.IsRuleEnabled(Rule));
        Assert.Equal(RuleSeverity.Warning, settings.SeverityFor(Rule));
    }

    [Fact]
    public void ItIsTheOnlyRuleOnByDefault()
    {
        // Stated as an assertion because the default is a decision, not a convenience: a second rule
        // arriving with EnabledByDefault set is a change to what every existing repository reports
        // the next time it is opened, and that should be deliberate.
        var settings = new StyleCheckingSettings();

        var onByDefault = RuleCatalog.Configurable
            .Where(d => settings.IsRuleEnabled(d.Id))
            .Select(d => d.Id)
            .ToList();

        Assert.Equal([Rule], onByDefault);
    }

    [Fact]
    public void TurningItOffSurvivesASaveAndReload()
    {
        // The B238 shape. Removing the key would read as "not configured", which for this rule means
        // on — so the user's decision would last until the next reload and then quietly undo itself.
        var settings = new StyleCheckingSettings();
        settings.SetRuleEnabled(Rule, false);

        Assert.False(settings.IsRuleEnabled(Rule));
        Assert.False(settings.IsRuleSwitchedOn(Rule));

        var back = JsonSerializer.Deserialize<StyleCheckingSettings>(
            JsonSerializer.Serialize(settings))!;

        Assert.False(back.IsRuleEnabled(Rule));
    }

    [Fact]
    public void TurningItOffThroughTheSeverityPickerAlsoSticks()
    {
        // The settings dialog sets a severity rather than a boolean, so Off arrives by this route.
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(Rule, RuleSeverity.Off);

        var back = JsonSerializer.Deserialize<StyleCheckingSettings>(
            JsonSerializer.Serialize(settings))!;

        Assert.False(back.IsRuleEnabled(Rule));
    }

    [Fact]
    public void TheStoredOffIsWrittenToTheFile()
    {
        // ...and visibly, because .mlqt/settings.json is committed and read by people. A rule that is
        // off by its absence tells a reviewer nothing; this one says so.
        var settings = new StyleCheckingSettings();
        settings.SetRuleEnabled(Rule, false);

        var json = JsonSerializer.Serialize(settings);

        Assert.Contains(Rule, json, StringComparison.Ordinal);
    }

    [Fact]
    public void TurningItBackOnRestoresTheDefaultSeverity()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleEnabled(Rule, false);

        settings.SetRuleEnabled(Rule, true);

        Assert.Equal(RuleSeverity.Warning, settings.SeverityFor(Rule));
    }

    [Fact]
    public void AnExplicitSeverityIsStillHonoured()
    {
        // A repository that wants it to fail a build says so, and the default has no opinion about
        // that.
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(Rule, RuleSeverity.Error);

        Assert.Equal(RuleSeverity.Error, settings.SeverityFor(Rule));
    }

    [Fact]
    public void AnOffRuleIsNotReportedAsAnIgnoredKey()
    {
        // IgnoredRuleKeys warns about keys that do nothing. A stored Off does something — it is the
        // only way this rule can be switched off — so complaining about it would send the user to
        // remove the very line that records their decision.
        var settings = new StyleCheckingSettings();
        settings.SetRuleEnabled(Rule, false);

        Assert.DoesNotContain(settings.IgnoredRuleKeys(), k => k.RuleId == Rule);
    }

    [Fact]
    public void RecordDefaults_WritesDownWhatWasImplicit()
    {
        // A settings file written before the rule existed does not mention it. The rule runs anyway,
        // which is the intent — but the file then hides a gate from everyone reading the repository,
        // and a user who opens the settings dialog, sees Warning and presses Apply is entitled to
        // find it in the file afterwards (B244).
        var settings = new StyleCheckingSettings();
        Assert.False(settings.RuleSeverities.ContainsKey(Rule));

        settings.RecordDefaults();

        Assert.Equal(RuleSeverity.Warning, settings.RuleSeverities[Rule]);
    }

    [Fact]
    public void RecordDefaults_LeavesADecisionAlone()
    {
        // It only ever adds. Turning the rule off, or setting it to Error, is the repository's
        // answer and recording defaults must not overwrite it.
        var off = new StyleCheckingSettings();
        off.SetRuleEnabled(Rule, false);
        off.RecordDefaults();
        Assert.Equal(RuleSeverity.Off, off.RuleSeverities[Rule]);

        var error = new StyleCheckingSettings();
        error.SetRuleSeverity(Rule, RuleSeverity.Error);
        error.RecordDefaults();
        Assert.Equal(RuleSeverity.Error, error.RuleSeverities[Rule]);
    }

    [Fact]
    public void RecordDefaults_TouchesNothingElse()
    {
        // Only the rules that are on without being asked for. A rule that is off by default stays
        // absent, or every settings file would grow an entry for all forty of them.
        var settings = new StyleCheckingSettings();

        settings.RecordDefaults();

        Assert.Equal([Rule], settings.RuleSeverities.Keys);
    }

    [Fact]
    public void ASettingsFileWrittenBeforeTheRuleExisted_GainsItOnSave()
    {
        // End to end through the serializer, which is what the repository actually writes.
        const string Legacy = """{ "RuleSeverities": { "MLQT.Doc.ClassDescription": "Warning" } }""";
        var settings = JsonSerializer.Deserialize<StyleCheckingSettings>(Legacy)!;

        settings.RecordDefaults();
        var json = JsonSerializer.Serialize(settings);

        Assert.Contains(Rule, json, StringComparison.Ordinal);
        Assert.Contains(RuleIds.ClassDescription, json, StringComparison.Ordinal);
    }

    [Fact]
    public void ARepositoryWithNoSettingsStillCountsAsHavingARuleEnabled()
    {
        // HasAnyStyleRuleEnabled short-circuits the per-class checker, and it used to read the map's
        // keys alone — so a rule with no key would have been enabled and never run.
        Assert.True(new StyleCheckingSettings().HasAnyStyleRuleEnabled);
    }

    [Fact]
    public void ARepositoryThatSwitchedItOffHasNoRulesEnabled()
    {
        // The other side of that: switching off the only rule that was on must leave nothing on,
        // rather than leaving the short-circuit permanently open.
        var settings = new StyleCheckingSettings();
        settings.SetRuleEnabled(Rule, false);

        Assert.False(settings.HasAnyStyleRuleEnabled);
    }
}
