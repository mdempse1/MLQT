using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// A rule with an id but no switch behind it.
///
/// <para><c>MLQT.Style.ExtendsAtTop</c> is the "imports first, extends next" convention seen from the
/// other end: the formatter applies the two together and the settings page has always offered them as
/// one switch. It still needs its own id — a finding about a misplaced <c>extends</c> should say so,
/// and be waivable on its own — but it has never had a setting. What it had instead was the
/// appearance of one: <c>.mlqt/settings.json</c> could name it, the file loaded without complaint,
/// and the value did nothing. `"Off"` in particular read as "this rule is disabled" and disabled
/// nothing.</para>
/// </summary>
public class GovernedRuleTests
{
    [Fact]
    public void AGovernedRuleTakesItsGovernorsSeverity()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(RuleIds.ImportStatementsFirst, RuleSeverity.Error);

        Assert.Equal(RuleSeverity.Error, settings.SeverityFor(RuleIds.ExtendsAtTop));
    }

    [Fact]
    public void AGovernedRuleIsOffWhenItsGovernorIs()
    {
        var settings = new StyleCheckingSettings();

        Assert.Equal(RuleSeverity.Off, settings.SeverityFor(RuleIds.ImportStatementsFirst));
        Assert.Equal(RuleSeverity.Off, settings.SeverityFor(RuleIds.ExtendsAtTop));
    }

    /// <summary>
    /// The case that made this a defect rather than a curiosity: the settings file said Off and the
    /// rule reported anyway, because the checker papered over the Off with the catalog default.
    /// </summary>
    [Fact]
    public void SettingAGovernedRuleDirectly_DoesNotOverrideItsGovernor()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(RuleIds.ImportStatementsFirst, RuleSeverity.Warning);
        settings.SetRuleSeverity(RuleIds.ExtendsAtTop, RuleSeverity.Off);

        // Still Warning: the governor decides, and the direct value is reported as ignored below
        // rather than half-honoured.
        Assert.Equal(RuleSeverity.Warning, settings.SeverityFor(RuleIds.ExtendsAtTop));
    }

    [Fact]
    public void AKeyThatCannotBeSet_IsReportedAsIgnored()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(RuleIds.ExtendsAtTop, RuleSeverity.Error);

        var ignored = settings.IgnoredRuleKeys();

        Assert.Contains(RuleIds.ExtendsAtTop, ignored);
        Assert.Contains("governed by", StyleCheckingSettings.WhyIgnored(RuleIds.ExtendsAtTop));
        Assert.Contains(RuleIds.ImportStatementsFirst, StyleCheckingSettings.WhyIgnored(RuleIds.ExtendsAtTop));
    }

    [Fact]
    public void AMisspeltRuleId_IsReportedAsIgnored()
    {
        // The failure mode this protects against: a gate switched off by a typo, silently.
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity("MLQT.Doc.ClassDescriptions", RuleSeverity.Error);

        Assert.Contains("MLQT.Doc.ClassDescriptions", settings.IgnoredRuleKeys());
        Assert.Contains("not a known rule id", StyleCheckingSettings.WhyIgnored("MLQT.Doc.ClassDescriptions"));
    }

    [Fact]
    public void ADiagnostic_IsReportedAsIgnored()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(RuleIds.SyntaxError, RuleSeverity.Info);

        Assert.Contains(RuleIds.SyntaxError, settings.IgnoredRuleKeys());
        Assert.Contains("always reported", StyleCheckingSettings.WhyIgnored(RuleIds.SyntaxError));
    }

    [Fact]
    public void ARuleWithItsOwnSetting_IsNotReportedAsIgnored()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(RuleIds.ClassDescription, RuleSeverity.Error);

        Assert.Empty(settings.IgnoredRuleKeys());
    }

    /// <summary>
    /// The catalog's own account of itself. Every id is exactly one of: configurable, governed by
    /// another rule, or a diagnostic — and the settings UI, the settings file and the documentation
    /// each key off that split, so an id in none of the three would fall through all of them.
    /// </summary>
    [Fact]
    public void EveryCatalogRuleIsConfigurableGovernedOrADiagnostic()
    {
        foreach (var id in RuleCatalog.BuiltIn.Keys)
        {
            var kinds = new[]
            {
                RuleCatalog.IsConfigurable(id),
                RuleCatalog.GovernorOf(id) is not null,
                RuleIds.IsDiagnostic(id),
            };
            Assert.True(kinds.Count(k => k) == 1, $"{id} is {kinds.Count(k => k)} of the three kinds");
        }
    }

    [Fact]
    public void AGovernorIsItselfConfigurable()
    {
        // Otherwise a governed rule resolves to something nobody can set either, and the chain ends
        // nowhere. Also rules out a cycle, since SeverityFor recurses through it.
        foreach (var id in RuleCatalog.BuiltIn.Keys)
        {
            if (RuleCatalog.GovernorOf(id) is not { } governor)
                continue;

            Assert.True(RuleCatalog.IsConfigurable(governor),
                $"{id} is governed by {governor}, which has no setting of its own");
        }
    }

    /// <summary>
    /// The settings dialog offers the formatting rules as plain on/off switches rather than the
    /// four-button severity control the other rules get, because they interlock and drive the
    /// formatter. Switching one on gives it the catalog default, which for every built-in style rule
    /// is Warning — and now gives the same to the rule it governs.
    /// </summary>
    [Fact]
    public void TheGuiSwitchEnablesAtTheCatalogDefault()
    {
        var settings = new StyleCheckingSettings();

        settings.ImportStatementsFirst = true;   // what the MudSwitch binds to

        Assert.Equal(RuleSeverity.Warning, settings.SeverityFor(RuleIds.ImportStatementsFirst));
        Assert.Equal(RuleSeverity.Warning, settings.SeverityFor(RuleIds.ExtendsAtTop));
    }

    [Fact]
    public void SwitchingOnDoesNotOverwriteASeverityAlreadySet()
    {
        // A repository that sets Error in .mlqt/settings.json must not have it demoted just because
        // the dialog rendered the switch and wrote back the value it read.
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(RuleIds.ImportStatementsFirst, RuleSeverity.Error);

        settings.ImportStatementsFirst = true;

        Assert.Equal(RuleSeverity.Error, settings.SeverityFor(RuleIds.ImportStatementsFirst));
        Assert.Equal(RuleSeverity.Error, settings.SeverityFor(RuleIds.ExtendsAtTop));
    }

    /// <summary>
    /// KNOWN DEFECT, tracked as B36 — this test records what happens today, not what should.
    ///
    /// <para>Switching a rule off removes its entry outright, so an explicit <c>Error</c> is not
    /// remembered: switching it back on re-seeds the catalog default and the repository's severity is
    /// gone, with the dialog looking exactly as it did before. It only bites the rules the dialog
    /// shows as a switch rather than as a severity picker, but those are the ones a CI gate is most
    /// likely to have raised to Error. When B36 is fixed this test should assert Error on both lines.</para>
    /// </summary>
    [Fact]
    public void SwitchingOffAndOnAgainCurrentlyLosesAnExplicitSeverity()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(RuleIds.ImportStatementsFirst, RuleSeverity.Error);

        settings.ImportStatementsFirst = false;
        settings.ImportStatementsFirst = true;

        Assert.Equal(RuleSeverity.Warning, settings.SeverityFor(RuleIds.ImportStatementsFirst));
        Assert.Equal(RuleSeverity.Warning, settings.SeverityFor(RuleIds.ExtendsAtTop));
    }

    /// <summary>
    /// The coverage dimensions ask about their own rule, not about its governor. Naming the governor
    /// there would state the same coupling a second time, in a file nobody would edit when it changed.
    /// </summary>
    [Fact]
    public void TheExtendsCoverageDimensionFollowsTheSameSwitch()
    {
        var settings = new StyleCheckingSettings();
        Assert.False(CoverageDimensions.TrackedFor(settings).HasFlag(CoverageDimension.ExtendsAtTop));

        settings.ImportStatementsFirst = true;

        Assert.True(CoverageDimensions.TrackedFor(settings).HasFlag(CoverageDimension.ExtendsAtTop));
    }
}
