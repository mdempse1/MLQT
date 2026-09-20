using MLQT.Shared.Pages;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// What the findings table says it is showing (B247).
///
/// <para>Four things narrow that list — the class scope, the rule, the search box and the baseline
/// toggle — and the heading counted none of them. It reported the whole ledger however much of it
/// was on screen, so <b>a filter that matched nothing and one that matched three read the same</b>:
/// rows scrolled out of sight and a number above them that had not moved.</para>
///
/// <para>The counting is arithmetic; what is worth testing is which of six wordings applies, and
/// that every one of them is true of the numbers it was given. Hence a pure function and a test that
/// never renders anything.</para>
/// </summary>
public class CodeReviewFindingsHeadingTests
{
    private static string Heading(int shown, int total, int? changed = null, bool changesOnly = false) =>
        CodeReview.FindingsHeadingText(shown, total, changed, changesOnly);

    // ── no baseline: the plain case, and the one most users are in ────────────────

    [Fact]
    public void WithNothingFiltered_ItReadsAsItAlwaysHas()
    {
        // The unfiltered wording is deliberately unchanged. It is what the journeys look for and
        // what a screenshot shows, and there is nothing to report when nothing is hidden.
        Assert.Equal("40 Findings to review", Heading(shown: 40, total: 40));
    }

    [Fact]
    public void WithSomeFiltered_ItNamesBoth()
    {
        Assert.Equal("3 of 40 findings", Heading(shown: 3, total: 40));
    }

    [Fact]
    public void AFilterThatMatchedNothingSaysSo()
    {
        // The case the report was about. Before, this said "40 Findings to review" over an empty
        // table — which reads as the table being broken rather than the filter being narrow.
        Assert.Equal("0 of 40 findings", Heading(shown: 0, total: 40));
    }

    [Fact]
    public void NoFindingsAtAllIsNotAFilteredZero()
    {
        // "0 of 0 findings" would invite the user to go looking for a filter to clear.
        Assert.Equal("0 Findings to review", Heading(shown: 0, total: 0));
    }

    // ── with a baseline, and the toggle off ───────────────────────────────────────

    [Fact]
    public void TheBaselineCountIsReportedAlongsideTheTotal()
    {
        Assert.Equal("40 Findings to review (7 changed vs baseline)",
            Heading(shown: 40, total: 40, changed: 7));
    }

    [Fact]
    public void TheBaselineCountSurvivesTheOtherFilters()
    {
        // "changed" counts a property of the findings, not of what is on screen, so it does not
        // move when the search box does. Both numbers are about the whole set; only `shown` is not.
        Assert.Equal("3 of 40 findings (7 changed vs baseline)",
            Heading(shown: 3, total: 40, changed: 7));
    }

    // ── with a baseline, and the toggle on ────────────────────────────────────────

    [Fact]
    public void TheToggleAloneKeepsItsOwnWording()
    {
        // Unchanged from before B247: with only the toggle narrowing, this is already the sentence
        // that says what happened.
        Assert.Equal("7 changed of 40 findings",
            Heading(shown: 7, total: 40, changed: 7, changesOnly: true));
    }

    [Fact]
    public void FilteringWithinTheChangedOnesCountsAgainstThoseAndNotTheWhole()
    {
        // The denominator is the toggle's own count, not the ledger. "2 of 40" would credit the
        // search box with hiding the 33 findings the toggle hid, which is the same confusion B247
        // is about, one level in.
        Assert.Equal("2 of 7 changed findings",
            Heading(shown: 2, total: 40, changed: 7, changesOnly: true));
    }

    [Fact]
    public void TheToggleHidingEverythingIsDistinguishableFromASearchThatDid()
    {
        Assert.Equal("0 changed of 40 findings",
            Heading(shown: 0, total: 40, changed: 0, changesOnly: true));

        Assert.Equal("0 of 7 changed findings",
            Heading(shown: 0, total: 40, changed: 7, changesOnly: true));
    }

    // ── the property every wording has to have ────────────────────────────────────

    [Theory]
    [InlineData(0, 0, null, false)]
    [InlineData(0, 40, null, false)]
    [InlineData(3, 40, null, false)]
    [InlineData(40, 40, null, false)]
    [InlineData(40, 40, 7, false)]
    [InlineData(3, 40, 7, false)]
    [InlineData(7, 40, 7, true)]
    [InlineData(2, 40, 7, true)]
    [InlineData(0, 40, 0, true)]
    public void TheShownCountIsAlwaysTheFirstNumberInTheHeading(
        int shown, int total, int? changed, bool changesOnly)
    {
        // The one thing a reader takes from this line without parsing it: the first number is how
        // many rows are underneath. Every wording above has to keep that, and a seventh added later
        // has to as well — which is why this is a property over all of them rather than nine more
        // string comparisons.
        var heading = Heading(shown, total, changed, changesOnly);
        var firstNumber = new string(heading.TakeWhile(char.IsDigit).ToArray());

        Assert.Equal(shown.ToString(), firstNumber);
    }
}
