using System.Text.Json;
using ModelicaGraph;
using ModelicaParser.StyleRules;

namespace ModelicaGraph.Tests;

/// <summary>
/// B238 — a rule switched on while its prerequisite is off must survive a save and reload.
///
/// <para>Found while giving components-before-classes a rule id (B181), and it predates that: the
/// four rules with a prerequisite were serialized through a bool facade whose getter answers the
/// <em>effective</em> question. With the prerequisite off that wrote <c>false</c> for a rule the
/// user had switched on, and the facade's setter removes the map entry when given false — so
/// reading the file back deleted the entry the RuleSeverities map ahead of it had just restored.
/// The settings dialog binds to <c>IsRuleSwitchedOn</c> exactly so an inert rule still shows as
/// configured, which is what made the loss invisible: the switch stayed ticked until the next
/// reload.</para>
///
/// <para>The tests below are written against the property, not the mechanism. What matters is that
/// a round trip preserves what the user configured, whichever rule it is and whatever else is
/// switched off at the time.</para>
/// </summary>
public class InertRuleSurvivesSettingsRoundTripTests
{
    /// <summary>Every rule whose effective state can differ from its configured state.</summary>
    public static TheoryData<string> RulesWithAPrerequisite()
    {
        var data = new TheoryData<string>();
        foreach (var rule in RuleCatalog.BuiltIn.Values.Where(d => d.RequiresRule is not null))
            data.Add(rule.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(RulesWithAPrerequisite))]
    public void ARuleSwitchedOnWhileItsPrerequisiteIsOff_SurvivesAJsonRoundTrip(string ruleId)
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleEnabled(ruleId, true);

        // The state this is about: configured on, not currently in effect.
        Assert.True(settings.IsRuleSwitchedOn(ruleId));
        Assert.False(settings.IsRuleEnabled(ruleId));

        var back = JsonSerializer.Deserialize<StyleCheckingSettings>(
            JsonSerializer.Serialize(settings))!;

        Assert.True(back.IsRuleSwitchedOn(ruleId));
    }

    [Theory]
    [MemberData(nameof(RulesWithAPrerequisite))]
    public void SwitchingThePrerequisiteBackOn_BringsTheRuleBack(string ruleId)
    {
        // The other half of the promise the dialog makes by showing an inert rule as ticked: the
        // setting was kept, so ticking the prerequisite restores it rather than needing it re-entered.
        var settings = new StyleCheckingSettings();
        settings.SetRuleEnabled(ruleId, true);

        var back = JsonSerializer.Deserialize<StyleCheckingSettings>(
            JsonSerializer.Serialize(settings))!;

        var prerequisite = RuleCatalog.RequiredRuleFor(ruleId)!;
        back.SetRuleEnabled(prerequisite, true);
        // The chain can be more than one link deep — components-before-classes needs imports-first,
        // which needs one-of-each-section — so enable every prerequisite above it.
        while (RuleCatalog.RequiredRuleFor(prerequisite) is { } next)
        {
            back.SetRuleEnabled(next, true);
            prerequisite = next;
        }

        Assert.True(back.IsRuleEnabled(ruleId));
    }

    [Fact]
    public void TurningARuleOff_StillTurnsItOff()
    {
        // The asymmetry this fix rests on is that a redundant `false` from serialization is not the
        // same as a caller saying "off". A caller saying it must still work.
        var settings = new StyleCheckingSettings { OneOfEachSection = true, ImportStatementsFirst = true };
        Assert.True(settings.IsRuleEnabled(RuleIds.ImportStatementsFirst));

        settings.ImportStatementsFirst = false;

        Assert.False(settings.IsRuleSwitchedOn(RuleIds.ImportStatementsFirst));
        var back = JsonSerializer.Deserialize<StyleCheckingSettings>(
            JsonSerializer.Serialize(settings))!;
        Assert.False(back.IsRuleSwitchedOn(RuleIds.ImportStatementsFirst));
    }

    [Fact]
    public void ASettingsFileFromAnEarlierMlqt_StillLoads()
    {
        // The bool names are what old files carry, and they are unchanged. This is the shape of a
        // file written before the severity map existed at all.
        const string Legacy = """
            {
              "ApplyFormattingRules": true,
              "OneOfEachSection": true,
              "ImportStatementsFirst": true,
              "ComponentsBeforeClasses": true,
              "InitialEQAlgoLast": true
            }
            """;

        var settings = JsonSerializer.Deserialize<StyleCheckingSettings>(Legacy)!;

        Assert.True(settings.ApplyFormattingRules);
        Assert.True(settings.OneOfEachSection);
        Assert.True(settings.ImportStatementsFirst);
        Assert.True(settings.ComponentsBeforeClasses);
        Assert.True(settings.InitialEQAlgoLast);
    }

    [Fact]
    public void TheEffectiveFacadeIsNotAlsoWrittenUnderItsOwnName()
    {
        // Two properties for one rule would be two answers in the file, and the second one read
        // would win. The guard is that only the legacy name appears.
        var settings = new StyleCheckingSettings { OneOfEachSection = true, ImportStatementsFirst = true };

        var json = JsonSerializer.Serialize(settings);

        Assert.DoesNotContain("Configured", json, StringComparison.Ordinal);
        Assert.Contains("\"ImportStatementsFirst\"", json, StringComparison.Ordinal);
    }
}
