using MLQT.Services;
using MLQT.Services.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// What happens when two repositories watch the same directory (B168).
///
/// <para><b>They do, routinely.</b> Every repository monitors its <c>VcsRootPath</c> rather than its
/// own folder — deliberately, so that a VCS operation anywhere in the working copy is seen — so two
/// libraries checked out of one working copy are two repositories over one directory. The same
/// library reached both as a project repository and through the reference-library paths is the same
/// situation again, and that is how it was reported.</para>
///
/// <para><b>What was wrong.</b> Watchers were keyed by repository id, so each got its own
/// <c>FileSystemWatcher</c> over the same tree. That wastes OS handles — this project has already
/// exhausted Linux's inotify instances once — and it made the two watchers interfere, because the
/// debounce is keyed by <i>path</i> alone: the first watcher's event recorded the entry, and the
/// second watcher's identical event for the same file was then discarded as a duplicate. One of the
/// two repositories was never told its file had changed.</para>
///
/// <para>These tests drive the service's own API rather than the filesystem: a real
/// <c>FileSystemWatcher</c> event is timing-dependent and would make them flaky. What is asserted is
/// the registry the fix is about — how many watchers exist, and who is still monitored after a
/// stop.</para>
/// </summary>
public class SharedWatchPathTests : IDisposable
{
    private readonly string _root;
    private readonly FileMonitoringService _service = new();

    public SharedWatchPathTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mlqt-b168-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The fix itself, and the only assertion here that fails against what came before: two
    /// repositories over one directory must cost one watcher, not two.
    ///
    /// <para>Everything else in this class passed before the change as well — each repository had its
    /// own watcher, so each was independently startable and stoppable. Those tests guard the
    /// restructure; this one is the defect.</para>
    /// </summary>
    [Fact]
    public void TwoRepositoriesOnOnePath_ShareOneWatcher()
    {
        _service.StartMonitoring("repo-a", _root);
        _service.StartMonitoring("repo-b", _root);

        Assert.Equal(1, _service.WatchedPathCount);
    }

    [Fact]
    public void RepositoriesOnDifferentPaths_StillGetAWatcherEach()
    {
        // The counterpart, so the fix cannot be "open one watcher and hope".
        var other = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(other);

        _service.StartMonitoring("repo-a", _root);
        _service.StartMonitoring("repo-b", other);

        Assert.Equal(2, _service.WatchedPathCount);
    }

    [Fact]
    public void TwoRepositoriesOnOnePath_ReleaseTheWatcherOnlyWhenBothStop()
    {
        _service.StartMonitoring("repo-a", _root);
        _service.StartMonitoring("repo-b", _root);

        _service.StopMonitoring("repo-a");
        Assert.Equal(1, _service.WatchedPathCount);

        _service.StopMonitoring("repo-b");
        Assert.Equal(0, _service.WatchedPathCount);
    }

    [Fact]
    public void TwoRepositoriesOnOnePath_AreBothMonitored()
    {
        _service.StartMonitoring("repo-a", _root);
        _service.StartMonitoring("repo-b", _root);

        Assert.True(_service.IsMonitoringRepository("repo-a"));
        Assert.True(_service.IsMonitoringRepository("repo-b"));
    }

    [Fact]
    public void StoppingOneRepository_LeavesTheOtherMonitored()
    {
        // The regression this restructure could most easily have caused, and the reason the watcher
        // is reference-counted rather than simply shared: one library of a working copy pausing its
        // monitoring must not blind the others. Every VCS operation does exactly this.
        _service.StartMonitoring("repo-a", _root);
        _service.StartMonitoring("repo-b", _root);

        _service.StopMonitoring("repo-a");

        Assert.False(_service.IsMonitoringRepository("repo-a"));
        Assert.True(_service.IsMonitoringRepository("repo-b"));
        Assert.True(_service.IsMonitoring);
    }

    [Fact]
    public void StoppingTheLastRepository_StopsMonitoringAltogether()
    {
        _service.StartMonitoring("repo-a", _root);
        _service.StartMonitoring("repo-b", _root);

        _service.StopMonitoring("repo-a");
        _service.StopMonitoring("repo-b");

        Assert.False(_service.IsMonitoring);
        Assert.False(_service.IsMonitoringRepository("repo-b"));
    }

    [Fact]
    public void TheSamePathSpeltTwoWays_IsOneWatcher()
    {
        // A repository's VcsRootPath comes from whatever discovered it, so a trailing separator or a
        // relative segment is a real way for one directory to arrive under two spellings.
        _service.StartMonitoring("repo-a", _root);
        _service.StartMonitoring("repo-b", _root + Path.DirectorySeparatorChar);

        Assert.Equal(1, _service.WatchedPathCount);

        _service.StopMonitoring("repo-a");

        // Still watched, because both spellings registered against one entry rather than two.
        Assert.True(_service.IsMonitoringRepository("repo-b"));
        Assert.True(_service.IsMonitoring);
    }

    [Fact]
    public void ARepositoryMovedToAnotherPath_LeavesTheOldOneBehind()
    {
        // StartMonitoring doubles as "re-point this repository", and the old path's watcher has to be
        // released when nothing is left on it.
        var other = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(other);

        _service.StartMonitoring("repo-a", _root);
        _service.StartMonitoring("repo-a", other);

        Assert.True(_service.IsMonitoringRepository("repo-a"));

        _service.StopMonitoring("repo-a");
        Assert.False(_service.IsMonitoring);
    }

    [Fact]
    public void ARepositoryMovedAway_DoesNotTakeASharedWatcherWithIt()
    {
        var other = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(other);

        _service.StartMonitoring("repo-a", _root);
        _service.StartMonitoring("repo-b", _root);

        _service.StartMonitoring("repo-a", other);

        Assert.True(_service.IsMonitoringRepository("repo-b"));
    }

    [Fact]
    public void StopAllMonitoring_StopsEveryRepositorySharingAPath()
    {
        // StopAllMonitoring used to iterate the watcher keys, which were repository ids. They are
        // paths now, so iterating them would have stopped one repository per path and left the rest
        // registered.
        _service.StartMonitoring("repo-a", _root);
        _service.StartMonitoring("repo-b", _root);
        _service.StartMonitoring("repo-c", _root);

        _service.StopAllMonitoring();

        Assert.False(_service.IsMonitoring);
        Assert.False(_service.IsMonitoringRepository("repo-a"));
        Assert.False(_service.IsMonitoringRepository("repo-b"));
        Assert.False(_service.IsMonitoringRepository("repo-c"));
    }

    [Fact]
    public void APathThatIsNotThere_LeavesTheRepositoryUnmonitored()
    {
        // The rollback in the failure path: a repository must not read as monitored when nothing is
        // watching for it.
        _service.StartMonitoring("repo-a", Path.Combine(_root, "no-such-directory"));

        Assert.False(_service.IsMonitoringRepository("repo-a"));
        Assert.False(_service.IsMonitoring);
    }

    [Fact]
    public void ClearingOneRepositorysChanges_LeavesTheOthers()
    {
        // Pending changes stay per repository, which is what the fan-out preserves: two repositories
        // sharing a directory each get their own record of an edit, and clearing one does not clear
        // the other. Asserted on the records now - this used to say so and check only that the
        // other was still monitored (B325).
        _service.StartMonitoring("repo-a", _root);
        _service.StartMonitoring("repo-b", _root);
        _service.OnFileSystemEvent(_root, Path.Combine(_root, "Shared.mo"), FileChangeType.Modified);

        Assert.Single(_service.GetPendingChangesForRepository("repo-a"));
        Assert.Single(_service.GetPendingChangesForRepository("repo-b"));

        _service.ClearPendingChanges("repo-a");

        Assert.Empty(_service.GetPendingChangesForRepository("repo-a"));
        Assert.Single(_service.GetPendingChangesForRepository("repo-b"));
    }

    // ---- Whose change it is (B325) ----------------------------------------------------------------
    //
    // Two libraries checked out in one tree share one watcher. The change used to be offered to both
    // and recorded against one - whichever was offered it last, because the record was looked up by
    // path alone - so library B's edit could be formatted with library A's settings, and A's
    // formatter writes became B's pending changes. With each repository's own folder as its scope,
    // a change is recorded for the library it is in.

    private string Library(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void AChangeIsRecordedForTheLibraryItIsIn_AndNoOther()
    {
        _service.SetRepositoryScope("repo-a", Library("LibA"));
        _service.SetRepositoryScope("repo-b", Library("LibB"));
        _service.StartMonitoring("repo-a", _root);
        _service.StartMonitoring("repo-b", _root);

        _service.OnFileSystemEvent(_root, Path.Combine(_root, "LibB", "Pump.mo"), FileChangeType.Modified);
        _service.OnFileSystemEvent(_root, Path.Combine(_root, "LibA", "Valve.mo"), FileChangeType.Modified);

        Assert.Equal([Path.Combine(_root, "LibA", "Valve.mo")],
            _service.GetPendingChangesForRepository("repo-a").Select(c => c.FilePath));
        Assert.Equal([Path.Combine(_root, "LibB", "Pump.mo")],
            _service.GetPendingChangesForRepository("repo-b").Select(c => c.FilePath));
    }

    [Fact]
    public void AChangeOutsideEveryLibrary_IsNobodysPendingChange_ButIsStillActivity()
    {
        _service.SetRepositoryScope("repo-a", Library("LibA"));
        _service.SetRepositoryScope("repo-b", Library("LibB"));
        _service.StartMonitoring("repo-a", _root);
        _service.StartMonitoring("repo-b", _root);
        var activity = new List<string>();
        _service.OnRepositoryFileActivity += activity.Add;

        _service.OnFileSystemEvent(_root, Path.Combine(_root, "Scripts", "build.mo"), FileChangeType.Added);

        Assert.Empty(_service.PendingChanges);
        Assert.Equal(["repo-a", "repo-b"], activity.Order());
    }

    [Fact]
    public void ALibraryInsideAnotherRepositorysFolder_OwnsItsOwnChanges()
    {
        // A repository registered at the checkout root and one at a library below it: the library's
        // file is the library's, and a file elsewhere in the tree is the outer repository's.
        _service.SetRepositoryScope("repo-root", _root);
        _service.SetRepositoryScope("repo-b", Library("LibB"));
        _service.StartMonitoring("repo-root", _root);
        _service.StartMonitoring("repo-b", _root);

        _service.OnFileSystemEvent(_root, Path.Combine(_root, "LibB", "Pump.mo"), FileChangeType.Modified);
        _service.OnFileSystemEvent(_root, Path.Combine(_root, "Other.mo"), FileChangeType.Modified);

        Assert.Equal([Path.Combine(_root, "Other.mo")],
            _service.GetPendingChangesForRepository("repo-root").Select(c => c.FilePath));
        Assert.Equal([Path.Combine(_root, "LibB", "Pump.mo")],
            _service.GetPendingChangesForRepository("repo-b").Select(c => c.FilePath));
    }

    [Fact]
    public void AClassMovedBetweenLibraries_IsAChangeToBoth()
    {
        _service.SetRepositoryScope("repo-a", Library("LibA"));
        _service.SetRepositoryScope("repo-b", Library("LibB"));
        _service.StartMonitoring("repo-a", _root);
        _service.StartMonitoring("repo-b", _root);

        _service.OnFileSystemRenamedEvent(_root,
            Path.Combine(_root, "LibA", "Pump.mo"), Path.Combine(_root, "LibB", "Pump.mo"));

        Assert.Single(_service.GetPendingChangesForRepository("repo-a"));
        Assert.Single(_service.GetPendingChangesForRepository("repo-b"));
    }

    [Fact]
    public void AScopeOutlivesAPause()
    {
        // StopMonitoring is how every VCS operation pauses a repository; the scope must still be
        // there when it starts again, or the first edit after an update goes to both libraries.
        _service.SetRepositoryScope("repo-a", Library("LibA"));
        _service.SetRepositoryScope("repo-b", Library("LibB"));
        _service.StartMonitoring("repo-a", _root);
        _service.StartMonitoring("repo-b", _root);

        _service.StopMonitoring("repo-a");
        _service.StopMonitoring("repo-b");
        _service.StartMonitoring("repo-a", _root);
        _service.StartMonitoring("repo-b", _root);
        _service.OnFileSystemEvent(_root, Path.Combine(_root, "LibB", "Pump.mo"), FileChangeType.Modified);

        Assert.Empty(_service.GetPendingChangesForRepository("repo-a"));
        Assert.Single(_service.GetPendingChangesForRepository("repo-b"));
    }
}
