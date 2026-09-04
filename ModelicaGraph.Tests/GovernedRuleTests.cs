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
        // Whatever the governor resolves to — and for this pair that is itself derived from the
        // formatter, so the assertion is that the two agree rather than that either is a fixed value.
        var advisory = new StyleCheckingSettings { OneOfEachSection = true, ImportStatementsFirst = true };
        Assert.Equal(
            advisory.SeverityFor(RuleIds.ImportStatementsFirst),
            advisory.SeverityFor(RuleIds.ExtendsAtTop));

        var maintained = new StyleCheckingSettings
        {
            ImportStatementsFirst = true, OneOfEachSection = true, ApplyFormattingRules = true,
        };
        Assert.Equal(
            maintained.SeverityFor(RuleIds.ImportStatementsFirst),
            maintained.SeverityFor(RuleIds.ExtendsAtTop));
        Assert.NotEqual(
            advisory.SeverityFor(RuleIds.ExtendsAtTop),
            maintained.SeverityFor(RuleIds.ExtendsAtTop));
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
        var settings = new StyleCheckingSettings { OneOfEachSection = true, ImportStatementsFirst = true };
        settings.SetRuleSeverity(RuleIds.ExtendsAtTop, RuleSeverity.Off);

        // Still on: the governor decides, and the direct value is reported as ignored below rather
        // than half-honoured.
        Assert.NotEqual(RuleSeverity.Off, settings.SeverityFor(RuleIds.ExtendsAtTop));
    }

    private static string ReasonFor(StyleCheckingSettings settings, string ruleId) =>
        settings.IgnoredRuleKeys().Single(entry => entry.RuleId == ruleId).Reason;

    [Fact]
    public void AKeyThatCannotBeSet_IsReportedAsIgnored()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(RuleIds.ExtendsAtTop, RuleSeverity.Error);

        var reason = ReasonFor(settings, RuleIds.ExtendsAtTop);

        Assert.Contains("governed by", reason);
        Assert.Contains(RuleIds.ImportStatementsFirst, reason);
    }

    [Fact]
    public void AMisspeltRuleId_IsReportedAsIgnored()
    {
        // The failure mode this protects against: a gate switched off by a typo, silently.
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity("MLQT.Doc.ClassDescriptions", RuleSeverity.Error);

        Assert.Contains("not a known rule id", ReasonFor(settings, "MLQT.Doc.ClassDescriptions"));
    }

    [Fact]
    public void ADiagnostic_IsReportedAsIgnored()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(RuleIds.SyntaxError, RuleSeverity.Info);

        Assert.Contains("always reported", ReasonFor(settings, RuleIds.SyntaxError));
    }

    [Fact]
    public void ARuleWithItsOwnSetting_IsNotReportedAsIgnored()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(RuleIds.ClassDescription, RuleSeverity.Error);

        Assert.Empty(settings.IgnoredRuleKeys());
    }

    /// <summary>
    /// The case a hand-edited settings file falls into. The dialog greys these switches out while
    /// One of each section is off; a file has no such protection, so `mlqt check` says it.
    /// </summary>
    [Fact]
    public void ALayoutRuleWithoutItsPrerequisite_IsReportedAsIgnored()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(RuleIds.ImportStatementsFirst, RuleSeverity.Warning);

        var reason = ReasonFor(settings, RuleIds.ImportStatementsFirst);

        Assert.Contains(RuleIds.OneOfEachSection, reason);
        Assert.Contains("does nothing", reason);
    }

    [Fact]
    public void ALayoutRuleWithItsPrerequisite_IsNotReportedAsIgnored()
    {
        var settings = new StyleCheckingSettings { OneOfEachSection = true, ImportStatementsFirst = true };

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
        var settings = new StyleCheckingSettings { OneOfEachSection = true };

        settings.ImportStatementsFirst = true;   // what the MudSwitch binds to

        Assert.Equal(RuleSeverity.Warning, settings.SeverityFor(RuleIds.ImportStatementsFirst));
        Assert.Equal(RuleSeverity.Warning, settings.SeverityFor(RuleIds.ExtendsAtTop));
    }

    [Fact]
    public void SwitchingOnDoesNotOverwriteASeverityAlreadySet()
    {
        // A repository that sets Error in .mlqt/settings.json must not have it demoted just because
        // the dialog rendered the switch and wrote back the value it read. Uses a rule whose level is
        // stored rather than derived — the formatting rules no longer keep one to lose.
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(RuleIds.ClassDescription, RuleSeverity.Error);

        settings.ClassHasDescription = true;

        Assert.Equal(RuleSeverity.Error, settings.SeverityFor(RuleIds.ClassDescription));
    }

    /// <summary>
    /// KNOWN DEFECT, tracked as B36 — this test records what happens today, not what should.
    ///
    /// <para>Switching a rule off removes its entry outright, so an explicit <c>Error</c> is not
    /// remembered: switching it back on re-seeds the catalog default and the repository's severity is
    /// gone, with the dialog looking exactly as it did before. It only bites the rules the dialog
    /// shows as a switch rather than as a severity picker, but those are the ones a CI gate is most
    /// likely to have raised to Error. When B36 is fixed this test should assert Error.</para>
    ///
    /// <para>It used to cover the four formatting rules too; their level is derived now, so they
    /// keep nothing to lose and the remaining exposure is spelling and naming.</para>
    /// </summary>
    [Fact]
    public void SwitchingOffAndOnAgainCurrentlyLosesAnExplicitSeverity()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(RuleIds.SpellingDescription, RuleSeverity.Error);

        settings.SpellCheckDescription = false;
        settings.SpellCheckDescription = true;

        Assert.Equal(RuleSeverity.Warning, settings.SeverityFor(RuleIds.SpellingDescription));
    }

    /// <summary>
    /// Off when the switch is off, a warning while the rule is only advice, and an error once the
    /// formatter is rewriting every class on save to satisfy it.
    /// </summary>
    [Fact]
    public void ALayoutRuleIsOffWhenItsSwitchIs()
    {
        var settings = new StyleCheckingSettings { ApplyFormattingRules = true, OneOfEachSection = true };

        Assert.Equal(RuleSeverity.Off, settings.SeverityFor(RuleIds.ImportStatementsFirst));
        Assert.Equal(RuleSeverity.Off, settings.SeverityFor(RuleIds.ExtendsAtTop));
    }

    [Fact]
    public void ALayoutRuleIsAWarningWhileItIsOnlyAdvice()
    {
        var settings = new StyleCheckingSettings { ImportStatementsFirst = true, OneOfEachSection = true };

        Assert.Equal(RuleSeverity.Warning, settings.SeverityFor(RuleIds.ImportStatementsFirst));
        Assert.Equal(RuleSeverity.Warning, settings.SeverityFor(RuleIds.OneOfEachSection));
    }

    [Fact]
    public void ALayoutRuleIsAnErrorOnceTheFormatterMaintainsIt()
    {
        var settings = new StyleCheckingSettings
        {
            ApplyFormattingRules = true, OneOfEachSection = true, ImportStatementsFirst = true,
            InitialEQAlgoFirst = true,
        };

        Assert.Equal(RuleSeverity.Error, settings.SeverityFor(RuleIds.OneOfEachSection));
        Assert.Equal(RuleSeverity.Error, settings.SeverityFor(RuleIds.ImportStatementsFirst));
        Assert.Equal(RuleSeverity.Error, settings.SeverityFor(RuleIds.ExtendsAtTop));
        Assert.Equal(RuleSeverity.Error, settings.SeverityFor(RuleIds.InitialEqAlgoFirst));
    }

    /// <summary>
    /// Without One of each section the ordering rules do not run at all, whatever else is set.
    /// <c>ModelicaRenderer</c> only reorders inside its one-of-each-section branch, so on their own
    /// they would report an arrangement the formatter cannot produce — findings nobody can clear by
    /// pressing Format, on a setting that looks enabled.
    /// </summary>
    [Fact]
    public void WithoutOneOfEachSection_TheOrderingRulesDoNotRun()
    {
        var settings = new StyleCheckingSettings { ApplyFormattingRules = true, ImportStatementsFirst = true };

        Assert.Equal(RuleSeverity.Off, settings.SeverityFor(RuleIds.ImportStatementsFirst));
        Assert.Equal(RuleSeverity.Off, settings.SeverityFor(RuleIds.ExtendsAtTop));
        Assert.False(settings.ImportStatementsFirst);   // the facade agrees, so the checker skips it
    }

    /// <summary>
    /// And the setting is not destroyed by that — turning the prerequisite back on restores it, which
    /// is what makes the greyed-out switch in the dialog safe to leave alone.
    /// </summary>
    [Fact]
    public void EnablingThePrerequisiteRestoresTheRule()
    {
        var settings = new StyleCheckingSettings { ImportStatementsFirst = true };
        Assert.Equal(RuleSeverity.Off, settings.SeverityFor(RuleIds.ImportStatementsFirst));

        settings.OneOfEachSection = true;

        Assert.Equal(RuleSeverity.Warning, settings.SeverityFor(RuleIds.ImportStatementsFirst));
    }

    [Fact]
    public void ARuleWhoseOnlyEntryIsInert_DoesNotCountAsRulesEnabled()
    {
        // Otherwise a run announces that rules are enabled and then reports nothing, which is the
        // least debuggable outcome available.
        var settings = new StyleCheckingSettings { ImportStatementsFirst = true };

        Assert.False(settings.HasAnyStyleRuleEnabled);
    }

    [Fact]
    public void ARuleTheFormatterDoesNotTouch_KeepsTheLevelItWasGiven()
    {
        // The formatter cannot merge an equation section with an algorithm one, so this rule is not
        // in the derived set and the level it was given stands however formatting is configured.
        var settings = new StyleCheckingSettings { ApplyFormattingRules = true, OneOfEachSection = true };
        settings.SetRuleSeverity(RuleIds.DontMixEquationAndAlgorithm, RuleSeverity.Info);

        Assert.Equal(RuleSeverity.Info, settings.SeverityFor(RuleIds.DontMixEquationAndAlgorithm));
    }

    /// <summary>
    /// The coverage dimensions ask about their own rule, not about its governor. Naming the governor
    /// there would state the same coupling a second time, in a file nobody would edit when it changed.
    /// </summary>
    [Fact]
    public void TheExtendsCoverageDimensionFollowsTheSameSwitch()
    {
        var settings = new StyleCheckingSettings { OneOfEachSection = true };
        Assert.False(CoverageDimensions.TrackedFor(settings).HasFlag(CoverageDimension.ExtendsAtTop));

        settings.ImportStatementsFirst = true;

        Assert.True(CoverageDimensions.TrackedFor(settings).HasFlag(CoverageDimension.ExtendsAtTop));
    }

    /// <summary>
    /// What a settings dialog asks, as against what the checker asks.
    ///
    /// <para>Turning off One of each section makes the ordering rules inert, and the dialog used to
    /// bind its switches to the rule facades — so three of the four redrew themselves as unticked
    /// while the fourth, a plain bool with nothing to mask it, stayed on. Four disabled, three
    /// apparently switched off, and MLQT looking like it had silently changed the user's settings.
    /// Nothing had changed: the entries were all still there.</para>
    /// </summary>
    [Fact]
    public void SwitchedOnIsWhatIsConfigured_NotWhatCurrentlyTakesEffect()
    {
        var settings = new StyleCheckingSettings { OneOfEachSection = true, ImportStatementsFirst = true };

        settings.OneOfEachSection = false;

        Assert.True(settings.IsRuleSwitchedOn(RuleIds.ImportStatementsFirst));   // still configured
        Assert.False(settings.IsRuleEnabled(RuleIds.ImportStatementsFirst));     // but not in effect
    }

    [Fact]
    public void AGovernedRuleReportsItsGovernorsSwitch()
    {
        // ExtendsAtTop has no switch of its own, so "is it switched on" is a question about the one
        // it is governed by — the same indirection SeverityFor makes.
        var settings = new StyleCheckingSettings { OneOfEachSection = true, ImportStatementsFirst = true };

        Assert.True(settings.IsRuleSwitchedOn(RuleIds.ExtendsAtTop));

        settings.ImportStatementsFirst = false;

        Assert.False(settings.IsRuleSwitchedOn(RuleIds.ExtendsAtTop));
    }

    [Fact]
    public void ARuleWithNothingBlockingIt_IsSwitchedOnAndEnabledAlike()
    {
        var settings = new StyleCheckingSettings { ClassHasDescription = true };

        Assert.True(settings.IsRuleSwitchedOn(RuleIds.ClassDescription));
        Assert.True(settings.IsRuleEnabled(RuleIds.ClassDescription));
    }
}
