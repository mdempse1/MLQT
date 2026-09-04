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
}
