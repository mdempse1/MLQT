using MLQT.Shared.Dialogs;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Dialogs;

/// <summary>
/// <see cref="VCSHistory.DateTooltipFor"/> - the History dialog's Date cell shows the author date,
/// while its date filter matches the committer date, so a rebased commit can appear in a range its
/// Date is outside. The tooltip is where the user sees why.
/// </summary>
public class VCSHistoryDateTooltipTests
{
    private static readonly DateTimeOffset Authored = new(2026, 9, 30, 11, 14, 18, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset Rebased = new(2026, 11, 2, 13, 6, 4, TimeSpan.FromHours(1));

    [Fact]
    public void ARebasedCommit_ShowsWhenItWasCommitted()
    {
        var tooltip = VCSHistory.DateTooltipFor(new VcsLogEntry { Date = Authored, CommitterDate = Rebased });

        Assert.Equal($"Committed {Rebased.ToLocalTime().ToString(VCSHistory.DateFormat)} - the date the filter uses", tooltip);
    }

    [Fact]
    public void ACommitAmendedAMinuteLater_ShowsWhenItWasCommitted()
    {
        var amended = Authored.AddMinutes(1);

        Assert.Contains(VCSHistory.ShownDate(amended),
            VCSHistory.DateTooltipFor(new VcsLogEntry { Date = Authored, CommitterDate = amended }));
    }

    /// <summary>
    /// A date is shown in this machine's zone, whatever zone it was recorded in: the same moment
    /// recorded at +01:00 and at UTC reads the same.
    /// </summary>
    [Fact]
    public void AMoment_IsShownTheSame_WhicheverZoneItWasRecordedIn()
    {
        var recorded = new DateTimeOffset(2026, 11, 2, 0, 30, 0, TimeSpan.FromHours(1));

        Assert.Equal(VCSHistory.ShownDate(recorded.ToUniversalTime()), VCSHistory.ShownDate(recorded));
        Assert.Equal(TimeZoneInfo.ConvertTime(recorded, TimeZoneInfo.Local).ToString(VCSHistory.DateFormat),
            VCSHistory.ShownDate(recorded));
    }

    /// <summary>
    /// The day a commit is shown on is a day the filter keeps it for, and the day before is not. Shown
    /// in the zone it was recorded in, 00:30 +01:00 on 2 November read as the 2nd, while to a user on
    /// UTC a filter ending the 1st kept it and one starting the 2nd did not.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(9)]
    public void TheDayACommitIsShownOn_IsTheDayTheFilterKeepsItFor(int offsetHours)
    {
        var committed = new DateTimeOffset(2026, 11, 2, 0, 30, 0, TimeSpan.FromHours(offsetHours));
        var shownDay = DateTime.ParseExact(VCSHistory.ShownDate(committed), VCSHistory.DateFormat,
            System.Globalization.CultureInfo.CurrentCulture).Date;

        var (since, until) = VCSHistory.FilterRange(shownDay, shownDay);
        Assert.InRange(committed, since!.Value, until!.Value);

        var (_, untilTheDayBefore) = VCSHistory.FilterRange(null, shownDay.AddDays(-1));
        Assert.True(committed > untilTheDayBefore!.Value);
    }

    [Fact]
    public void ACommitWithOneDate_HasNoTooltip()
    {
        Assert.Equal("", VCSHistory.DateTooltipFor(new VcsLogEntry { Date = Authored, CommitterDate = Authored }));
    }

    [Fact]
    public void AnSvnEntry_WhichSetsOnlyTheDate_HasNoTooltip()
    {
        Assert.Equal("", VCSHistory.DateTooltipFor(new VcsLogEntry { Date = Authored }));
    }

    [Fact]
    public void ACommitAmendedWithinTheMinute_HasNoTooltip()
    {
        Assert.Equal("", VCSHistory.DateTooltipFor(new VcsLogEntry { Date = Authored, CommitterDate = Authored.AddSeconds(40) }));
    }

    [Fact]
    public void TheSameMomentInAnotherTimeZone_HasNoTooltip()
    {
        Assert.Equal("", VCSHistory.DateTooltipFor(new VcsLogEntry { Date = Authored, CommitterDate = Authored.ToOffset(TimeSpan.Zero) }));
    }
}
