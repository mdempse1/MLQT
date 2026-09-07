using MLQT.Shared.Helpers;
using MudBlazor;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Helpers;

/// <summary>
/// The one table that says how a working-copy status looks.
/// </summary>
/// <remarks>
/// <para>It became the only table in 7a-3. The three VCS dialogs each held a private copy that had
/// drifted from this one — an untracked file was a green "new" badge in the library browser and a
/// grey question mark in a merge dialog, a deleted file was amber in one place and red in another,
/// and a renamed file had no glyph at all in the dialogs. Those are the same file, in the same
/// working copy, in two windows of the same application.</para>
///
/// <para>The tests are mostly totality: what matters is not which icon a status gets but that every
/// status gets one, and that the choice was made rather than inherited from a <c>_</c> arm.</para>
/// </remarks>
public class VcsStatusHelperTests
{
    public static TheoryData<VcsFileStatus> EveryStatus()
    {
        var data = new TheoryData<VcsFileStatus>();
        foreach (var status in Enum.GetValues<VcsFileStatus>())
            data.Add(status);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void EveryStatusHasItsOwnIcon(VcsFileStatus status)
    {
        // The fallback arm exists for a status that does not exist yet. A real status reaching it is
        // the bug: a question mark beside a file whose state the application knows perfectly well.
        Assert.NotEqual(Icons.Material.Filled.QuestionMark, VcsStatusHelper.GetStatusIcon(status));
    }

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void EveryStatusHasItsOwnLetter(VcsFileStatus status)
    {
        Assert.NotEqual("?", VcsStatusHelper.GetStatusText(status));
    }

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void EveryStatusHasADeliberateColour(VcsFileStatus status)
    {
        Assert.NotEqual(Color.Default, VcsStatusHelper.GetStatusColor(status));
    }

    [Fact]
    public void TheLettersAreDistinct()
    {
        // They are shown in a chip a few pixels wide with no tooltip, so two statuses sharing a
        // letter are two statuses the user cannot tell apart at all.
        var letters = Enum.GetValues<VcsFileStatus>().Select(VcsStatusHelper.GetStatusText).ToList();

        Assert.Equal(letters.Count, letters.Distinct().Count());
    }

    [Fact]
    public void TheIconsAreDistinct()
    {
        var icons = Enum.GetValues<VcsFileStatus>().Select(VcsStatusHelper.GetStatusIcon).ToList();

        Assert.Equal(icons.Count, icons.Distinct().Count());
    }

    [Fact]
    public void OnlyAConflictIsShownAsAnError()
    {
        // Everything else in the list is an ordinary edit the user meant to make. Colouring a
        // modified file red makes a normal working copy look broken, which is how the dialogs'
        // private copies read - they had Deleted as an error.
        foreach (var status in Enum.GetValues<VcsFileStatus>())
        {
            if (status == VcsFileStatus.Conflicted)
                Assert.Equal(Color.Error, VcsStatusHelper.GetStatusColor(status));
            else
                Assert.NotEqual(Color.Error, VcsStatusHelper.GetStatusColor(status));
        }
    }

    [Fact]
    public void AStatusOutsideTheEnum_StillRenders()
    {
        // The arms exist; this asserts they are reachable rather than dead, and that an unknown
        // status degrades to a question mark instead of throwing in the middle of a merge.
        var unknown = (VcsFileStatus)999;

        Assert.Equal(Icons.Material.Filled.QuestionMark, VcsStatusHelper.GetStatusIcon(unknown));
        Assert.Equal("?", VcsStatusHelper.GetStatusText(unknown));
        Assert.Equal(Color.Default, VcsStatusHelper.GetStatusColor(unknown));
    }
}
