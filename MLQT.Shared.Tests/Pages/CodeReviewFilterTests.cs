using MLQT.Shared.Pages;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// The findings filter (B187), which had two defects in one control: no way to narrow to a rule, and
/// two words in the box <em>widened</em> the result instead of narrowing it.
///
/// <para>The second could not simply be swapped from OR to AND, and that is the part worth keeping:
/// the class scope was <b>smuggled through the search string</b> — appended as
/// <c>_searchString + " " + NavState.ModelID</c> and stripped back out inside the filter — so
/// requiring every term to match would have excluded every finding whose text did not also contain
/// the class name, which is all of them. The scope had to become a parameter first.</para>
/// </summary>
public class CodeReviewFilterTests
{
    private static LogMessage Finding(
        string model = "Lib.Thing", string summary = "The class is missing a description string",
        string severity = "Style warning", string rule = RuleIds.ClassDescription, string details = "") =>
        new(model, severity, 1, summary, details) { RuleId = rule };

    // ── every word has to match ───────────────────────────────────────────────────

    [Fact]
    public void ASecondWordNarrowsRatherThanWidens()
    {
        // The defect, stated as a test: under OR, "missing" alone and "missing nonsense" returned
        // the same finding, so adding a word could never help.
        var finding = Finding();

        Assert.True(CodeReview.Matches(finding, "missing", null, null));
        Assert.False(CodeReview.Matches(finding, "missing nonsense", null, null));
    }

    [Fact]
    public void WordsMayMatchDifferentFields()
    {
        // What AND across terms has to allow, and the case the backlog named: a partial class name
        // *and* a keyword. Requiring both in one field would make two words nearly useless.
        Assert.True(CodeReview.Matches(Finding(), "Thing description", null, null));
    }

    [Theory]
    [InlineData("lib.thing")]
    [InlineData("MISSING")]
    [InlineData("style")]
    [InlineData("mlqt.doc")]
    public void AWordMatchesAnyFieldAndIgnoresCase(string term) =>
        Assert.True(CodeReview.Matches(Finding(), term, null, null));

    [Fact]
    public void ExtraSpacesAreNotEmptyTerms()
    {
        // An empty term matches nothing, so splitting naively would make a trailing space hide
        // every finding - which is what a user gets for typing a word and pausing.
        Assert.True(CodeReview.Matches(Finding(), "  missing   ", null, null));
    }

    [Fact]
    public void AnEmptySearchKeepsEverything()
    {
        Assert.True(CodeReview.Matches(Finding(), "", null, null));
        Assert.True(CodeReview.Matches(Finding(), null, null, null));
        Assert.True(CodeReview.Matches(Finding(), "   ", null, null));
    }

    // ── the scope, which is no longer a word ──────────────────────────────────────

    [Fact]
    public void ScopingToAClassKeepsOnlyThatClass()
    {
        Assert.True(CodeReview.Matches(Finding(model: "Lib.Thing"), "", "Lib.Thing", null));
        Assert.False(CodeReview.Matches(Finding(model: "Lib.Other"), "", "Lib.Thing", null));
    }

    [Fact]
    public void ScopingAndSearchingNarrowTogether()
    {
        // The combination that the old smuggling made impossible: the scope is not a term, so it
        // does not have to be found in the text as well.
        var finding = Finding(model: "Lib.Thing", summary: "Misspelled word 'efficiancy' in description");

        Assert.True(CodeReview.Matches(finding, "efficiancy", "Lib.Thing", null));
        Assert.False(CodeReview.Matches(finding, "efficiancy", "Lib.Other", null));
    }

    // ── the rule filter ───────────────────────────────────────────────────────────

    [Fact]
    public void NarrowingToARuleKeepsOnlyThatRule()
    {
        Assert.True(CodeReview.Matches(Finding(rule: RuleIds.ClassDescription), "", null, RuleIds.ClassDescription));
        Assert.False(CodeReview.Matches(Finding(rule: RuleIds.NamingConvention), "", null, RuleIds.ClassDescription));
    }

    [Fact]
    public void TheRuleFilterIsExactRatherThanASearch()
    {
        // Ids share prefixes - MLQT.Doc.ClassDescription and MLQT.Doc.ClassDocumentationInfo - so a
        // "contains" match here would quietly select a family rather than a rule.
        Assert.False(CodeReview.Matches(
            Finding(rule: RuleIds.ClassDocumentationInfo), "", null, RuleIds.ClassDescription));
    }

    [Fact]
    public void AllThreeNarrowingsApplyTogether()
    {
        var finding = Finding(model: "Lib.Thing", rule: RuleIds.ClassDescription);

        Assert.True(CodeReview.Matches(finding, "missing", "Lib.Thing", RuleIds.ClassDescription));
        Assert.False(CodeReview.Matches(finding, "missing", "Lib.Thing", RuleIds.NamingConvention));
        Assert.False(CodeReview.Matches(finding, "missing", "Lib.Other", RuleIds.ClassDescription));
        Assert.False(CodeReview.Matches(finding, "absent", "Lib.Thing", RuleIds.ClassDescription));
    }

    [Fact]
    public void NoRuleChosenKeepsEveryRule()
    {
        Assert.True(CodeReview.Matches(Finding(rule: RuleIds.NamingConvention), "", null, null));
        Assert.True(CodeReview.Matches(Finding(rule: RuleIds.NamingConvention), "", null, ""));
    }
}
