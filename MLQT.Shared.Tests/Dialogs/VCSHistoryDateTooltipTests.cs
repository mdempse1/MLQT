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

        Assert.Equal($"Committed {Rebased.ToString(VCSHistory.DateFormat)} - the date the filter uses", tooltip);
    }

    [Fact]
    public void ACommitAmendedAMinuteLater_ShowsWhenItWasCommitted()
    {
        var amended = Authored.AddMinutes(1);

        Assert.Contains(amended.ToString(VCSHistory.DateFormat),
            VCSHistory.DateTooltipFor(new VcsLogEntry { Date = Authored, CommitterDate = amended }));
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
