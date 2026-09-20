using MLQT.Shared.Pages;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// The Code Review rule filter's list of rules.
///
/// <para><b>There is no list to maintain.</b> It is derived from the findings on screen, so a rule
/// added to MLQT appears the first time it produces one — asked after B241 added
/// <c>MLQT.Structure.SingleFilePackage</c>, and the answer is that nothing needed updating. Two
/// other guards already hold the chain above it: <c>RuleCatalogTests.EveryRuleId_IsRegistered</c>
/// puts every id constant in the catalogue, and <c>RuleSettingsLayout.UnreachableRules()</c> puts
/// every configurable rule in the settings dialog.</para>
///
/// <para>What none of them covers is the last link, which is what these are for: that the filter
/// <em>names</em> a rule through the catalogue. Drop that lookup and the control goes on working
/// while offering <c>MLQT.Structure.SingleFilePackage</c> where it used to say "Packages are stored
/// as directories" — a change nothing would fail on, and nobody would notice until they went
/// looking for a rule by name.</para>
/// </summary>
public class CodeReviewRuleFilterTests
{
    private static LogMessage Finding(string? ruleId) =>
        new("Lib.Thing", "Style warning", 1, "a finding") { RuleId = ruleId };

    public static TheoryData<string, string> EveryConfigurableRule()
    {
        var data = new TheoryData<string, string>();
        foreach (var rule in RuleCatalog.Configurable)
            data.Add(rule.Id, rule.Title);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryConfigurableRule))]
    public void EveryRuleIsOfferedByItsTitleWhenItHasAFinding(string ruleId, string title)
    {
        // Driven off the catalogue, so a rule added tomorrow is covered the day it is added — which
        // is the check worth having, given the filter itself needs no maintenance.
        var offered = CodeReview.RulesIn([Finding(ruleId)]).ToList();

        var entry = Assert.Single(offered);
        Assert.Equal(ruleId, entry.Id);
        Assert.Equal(title, entry.Title);
        Assert.NotEqual(entry.Id, entry.Title);
    }

    [Fact]
    public void ARuleWithNoFindingIsNotOffered()
    {
        // The reason the list is derived rather than taken from the catalogue: offering forty-odd
        // rules, most of which produced nothing here, makes the control something to search rather
        // than something to pick from.
        var offered = CodeReview.RulesIn([Finding(RuleIds.ClassDescription)]).ToList();

        Assert.DoesNotContain(offered, r => r.Id == RuleIds.SingleFilePackage);
    }

    [Fact]
    public void ANewRuleNeedsNoRegistration()
    {
        // Stated directly, because it is the question that prompted these tests: a finding from a
        // rule nobody wired into any list still reaches the filter.
        var offered = CodeReview.RulesIn([Finding(RuleIds.SingleFilePackage)]).ToList();

        var entry = Assert.Single(offered);
        Assert.Equal("Packages are stored as directories", entry.Title);
    }

    [Fact]
    public void AnIdTheCatalogueDoesNotKnowFallsBackToTheId()
    {
        // An external tool's output reaches this list too. Better a row named by its id than a rule
        // the user cannot filter by at all.
        var offered = CodeReview.RulesIn([Finding("Dymola.SomeCheck")]).ToList();

        var entry = Assert.Single(offered);
        Assert.Equal("Dymola.SomeCheck", entry.Title);
    }

    [Fact]
    public void FindingsWithNoRuleAreNotOffered()
    {
        // Parse diagnostics and tool output can arrive without a rule id, and a blank row in the
        // filter selects nothing.
        Assert.Empty(CodeReview.RulesIn([Finding(null), Finding(string.Empty)]));
    }

    [Fact]
    public void EachRuleIsOfferedOnce_HoweverManyFindingsItHas()
    {
        var offered = CodeReview.RulesIn([
            Finding(RuleIds.ClassDescription),
            Finding(RuleIds.ClassDescription),
            Finding(RuleIds.SingleFilePackage)]).ToList();

        Assert.Equal(2, offered.Count);
    }

    [Fact]
    public void TheListIsOrderedByTitle()
    {
        // What the user reads is the title, so that is what it has to be sorted by — ordering by id
        // puts MLQT.Doc before MLQT.Structure and looks arbitrary on screen.
        var offered = CodeReview.RulesIn([
            Finding(RuleIds.SingleFilePackage),
            Finding(RuleIds.ClassDescription),
            Finding(RuleIds.PackageOrder)]).ToList();

        Assert.Equal(
            offered.Select(r => r.Title).OrderBy(t => t, StringComparer.OrdinalIgnoreCase),
            offered.Select(r => r.Title));
    }
}
