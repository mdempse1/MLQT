using MLQT.Shared.Dialogs;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Dialogs;

/// <summary>
/// The rules the merge and rebase dialogs share.
/// </summary>
/// <remarks>
/// These were three byte-identical private copies before 7a-3, one per dialog, and none of them was
/// reachable by a test. The tests are written against the behaviour a user would notice — an
/// operation allowed to start over a dirty working copy, a resolved file quietly going back to
/// unresolved, a commit button live before the list has loaded — rather than against the shape of
/// the code, because the shape is what just changed.
/// </remarks>
public class VcsConflictRulesTests
{
    private static VcsWorkingCopyFile File(string path, VcsFileStatus status) =>
        new() { Path = path, Status = status };

    // ---- BlockingChanges -------------------------------------------------------------------

    [Theory]
    [InlineData(VcsFileStatus.Modified)]
    [InlineData(VcsFileStatus.Added)]
    [InlineData(VcsFileStatus.Deleted)]
    [InlineData(VcsFileStatus.Untracked)]
    [InlineData(VcsFileStatus.Conflicted)]
    public void AChangeThatWouldBeLostOrCollide_BlocksTheOperation(VcsFileStatus status)
    {
        var blocking = VcsConflictRules.BlockingChanges([File("a.mo", status)]);

        Assert.Single(blocking);
    }

    [Fact]
    public void EveryStatusTheVcsCanReport_Blocks()
    {
        // The guard that would have caught B106, and the reason the rule lists its statuses instead
        // of returning everything: GetWorkingCopyChanges yields only files with real changes, so a
        // status it can produce and this rule does not name is a change the dialog walks past. All
        // three dialogs walked past Renamed. Adding a member to VcsFileStatus now fails here, which
        // forces the decision rather than defaulting it to "does not block".
        foreach (var status in Enum.GetValues<VcsFileStatus>())
        {
            Assert.True(
                VcsConflictRules.BlockingChanges([File("a.mo", status)]).Count == 1,
                $"{status} does not block a merge or rebase. GetWorkingCopyChanges can report it, so "
                + "the dialog would call the working copy clean and start an operation over it.");
        }
    }

    [Fact]
    public void AStagedRename_Blocks()
    {
        // B106 named on its own, because the general guard above would pass again the moment someone
        // "simplified" the rule back to the five statuses it used to list. git refuses to rebase with
        // a staged rename in the index; before this the dialog said "ready to rebase" and let git
        // deliver the news.
        Assert.Single(VcsConflictRules.BlockingChanges([File("renamed.mo", VcsFileStatus.Renamed)]));
    }

    [Fact]
    public void TheFilesAreReturnedInOrder()
    {
        // The dialog shows this list and asks the user to deal with it, so the order it was
        // reported in is the order they read.
        var blocking = VcsConflictRules.BlockingChanges(
        [
            File("dirty.mo", VcsFileStatus.Modified),
            File("new.mo", VcsFileStatus.Untracked),
        ]);

        Assert.Equal(["dirty.mo", "new.mo"], blocking.Select(f => f.Path));
    }

    [Fact]
    public void NoChangesAtAll_BlocksNothing()
    {
        Assert.Empty(VcsConflictRules.BlockingChanges([]));
    }

    // ---- CarryForward ----------------------------------------------------------------------

    [Fact]
    public void AFreshConflictList_StartsUnresolved()
    {
        var states = VcsConflictRules.CarryForward(["a.mo", "b.mo"], new Dictionary<string, ConflictFileState>());

        Assert.Equal(2, states.Count);
        Assert.All(states.Values, s => Assert.Equal(ConflictFileState.Unresolved, s));
    }

    [Fact]
    public void AFileTheUserAlreadyResolved_StaysResolved()
    {
        // The rebase case, and the reason this is not a plain ToDictionary. A rebase replays commits
        // one at a time and reports a fresh conflict list for each, so rebuilding the map would send
        // a file the user has already dealt with back to the top of the list.
        var existing = new Dictionary<string, ConflictFileState>
        {
            ["a.mo"] = ConflictFileState.Resolved,
        };

        var states = VcsConflictRules.CarryForward(["a.mo", "b.mo"], existing);

        Assert.Equal(ConflictFileState.Resolved, states["a.mo"]);
        Assert.Equal(ConflictFileState.Unresolved, states["b.mo"]);
    }

    [Fact]
    public void AFileBeingEditedExternally_KeepsThatState()
    {
        // Not a VCS state - it is the dialog remembering the user opened the file elsewhere. Losing
        // it swaps the "I have fixed it" button back for the three resolution choices while the
        // user is still in their editor.
        var existing = new Dictionary<string, ConflictFileState>
        {
            ["a.mo"] = ConflictFileState.EditingExternally,
        };

        var states = VcsConflictRules.CarryForward(["a.mo"], existing);

        Assert.Equal(ConflictFileState.EditingExternally, states["a.mo"]);
    }

    [Fact]
    public void AFileNoLongerInConflict_DropsOut()
    {
        // Otherwise the count says "1 of 2 resolved" for a file that is no longer in the operation,
        // and the commit button never enables.
        var existing = new Dictionary<string, ConflictFileState>
        {
            ["gone.mo"] = ConflictFileState.Resolved,
        };

        var states = VcsConflictRules.CarryForward(["still-here.mo"], existing);

        Assert.Equal(["still-here.mo"], states.Keys);
    }

    // ---- AllResolved -----------------------------------------------------------------------

    [Fact]
    public void NothingIsResolved_WhenThereAreNoConflictsYet()
    {
        // The one that needs stating: All() on an empty dictionary is true, so without the count
        // check the commit button enables itself in the moment before the conflict list arrives.
        Assert.False(VcsConflictRules.AllResolved(new Dictionary<string, ConflictFileState>()));
    }

    [Fact]
    public void EveryConflictResolved_AllowsTheCommit()
    {
        Assert.True(VcsConflictRules.AllResolved(new Dictionary<string, ConflictFileState>
        {
            ["a.mo"] = ConflictFileState.Resolved,
            ["b.mo"] = ConflictFileState.Resolved,
        }));
    }

    [Theory]
    [InlineData(ConflictFileState.Unresolved)]
    [InlineData(ConflictFileState.EditingExternally)]
    public void OneFileOutstanding_BlocksTheCommit(ConflictFileState outstanding)
    {
        // EditingExternally counts as outstanding. The user has the file open somewhere else and has
        // not said they are finished; committing now would commit the conflict markers.
        Assert.False(VcsConflictRules.AllResolved(new Dictionary<string, ConflictFileState>
        {
            ["a.mo"] = ConflictFileState.Resolved,
            ["b.mo"] = outstanding,
        }));
    }

    // ---- RelativeTo ------------------------------------------------------------------------

    [Fact]
    public void AFileInTheWorkingCopy_IsShownRelativeToIt()
    {
        Assert.Equal(
            Path.Combine("Sub", "Model.mo"),
            VcsConflictRules.RelativeTo(Path.Combine("C:", "repo"), Path.Combine("C:", "repo", "Sub", "Model.mo")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoWorkingCopyPath_ShowsTheFullPath(string? localPath)
    {
        // The repository lookup can come back null mid-operation. Showing the full path is odd;
        // throwing in the middle of a merge is worse.
        var full = Path.Combine("C:", "repo", "Model.mo");

        Assert.Equal(full, VcsConflictRules.RelativeTo(localPath, full));
    }

    [Fact]
    public void APathThatCannotBeRelated_FallsBackToTheFullPath()
    {
        // GetRelativePath throws only for genuinely unrelatable input, not for a path that walks up
        // - that comes back as "..\..\x" and is returned as-is, which is the same choice
        // ReportLocation documents.
        var result = VcsConflictRules.RelativeTo(Path.Combine("C:", "repo"), "\0invalid");

        Assert.Equal("\0invalid", result);
    }
}
