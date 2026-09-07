using MLQT.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.Services.Tests;

/// <summary>
/// Unit tests for the FileMonitoringService class.
/// Tests focus on the pending changes management and consolidation logic
/// that can be exercised without a real FileSystemWatcher.
/// </summary>
public class FileMonitoringServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileMonitoringService _service;

    public FileMonitoringServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mlqt-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _service = new FileMonitoringService();
    }

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void IsMonitoring_InitiallyFalse()
    {
        Assert.False(_service.IsMonitoring);
    }

    [Fact]
    public void PendingChanges_InitiallyEmpty()
    {
        Assert.Empty(_service.PendingChanges);
    }

    [Fact]
    public void GetPendingChangesSummary_InitiallyAllZero()
    {
        var summary = _service.GetPendingChangesSummary();

        Assert.Equal(0, summary.AddedFiles);
        Assert.Equal(0, summary.ModifiedFiles);
        Assert.Equal(0, summary.DeletedFiles);
        Assert.Equal(0, summary.RenamedFiles);
        Assert.Equal(0, summary.AddedDirectories);
        Assert.Equal(0, summary.DeletedDirectories);
        Assert.False(summary.HasChanges);
    }

    [Fact]
    public void StartMonitoring_NonExistentDirectory_DoesNotThrow()
    {
        // Should log warning and return without throwing
        _service.StartMonitoring("repo1", "C:/NonExistent/Path/That/Does/Not/Exist");

        Assert.False(_service.IsMonitoring);
    }

    [Fact]
    public void StartMonitoring_ExistingDirectory_BecomesMonitoring()
    {
        _service.StartMonitoring("repo1", _tempDir);

        Assert.True(_service.IsMonitoring);
    }

    [Fact]
    public void StopMonitoring_AfterStart_StopsMonitoring()
    {
        _service.StartMonitoring("repo1", _tempDir);

        _service.StopMonitoring("repo1");

        Assert.False(_service.IsMonitoring);
    }

    [Fact]
    public void StopMonitoring_NotStarted_DoesNotThrow()
    {
        // Should not throw even if not monitoring
        _service.StopMonitoring("nonexistent-repo");
    }

    [Fact]
    public void StopAllMonitoring_StopsAllWatchers()
    {
        var tempDir2 = Path.Combine(Path.GetTempPath(), "mlqt-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir2);
        try
        {
            _service.StartMonitoring("repo1", _tempDir);
            _service.StartMonitoring("repo2", tempDir2);

            _service.StopAllMonitoring();

            Assert.False(_service.IsMonitoring);
        }
        finally
        {
            Directory.Delete(tempDir2, recursive: true);
        }
    }

    [Fact]
    public void StartMonitoring_CalledTwiceForSameRepo_ReplacesWatcher()
    {
        _service.StartMonitoring("repo1", _tempDir);
        _service.StartMonitoring("repo1", _tempDir);

        Assert.True(_service.IsMonitoring);

        _service.StopMonitoring("repo1");
        Assert.False(_service.IsMonitoring);
    }

    [Fact]
    public void GetPendingChangesForRepository_EmptyWhenNoChanges()
    {
        var changes = _service.GetPendingChangesForRepository("repo1");

        Assert.Empty(changes);
    }

    [Fact]
    public void ClearPendingChanges_AllChanges_FiresEvent()
    {
        var eventFired = false;
        _service.OnPendingChangesUpdated += () => eventFired = true;

        _service.ClearPendingChanges();

        Assert.True(eventFired);
    }

    [Fact]
    public void ClearPendingChanges_ForRepository_FiresEvent()
    {
        var eventFired = false;
        _service.OnPendingChangesUpdated += () => eventFired = true;

        _service.ClearPendingChanges("repo1");

        Assert.True(eventFired);
    }

    [Fact]
    public async Task FileCreation_TriggersChange_WhenMonitoring()
    {
        var changeReceived = false;
        var tcs = new TaskCompletionSource<bool>();
        _service.OnFileChanged += change =>
        {
            if (change.FilePath.EndsWith(".mo"))
            {
                changeReceived = true;
                tcs.TrySetResult(true);
            }
        };

        _service.StartMonitoring("repo1", _tempDir);

        // Create a .mo file to trigger the watcher
        var testFilePath = Path.Combine(_tempDir, "TestModel.mo");
        await File.WriteAllTextAsync(testFilePath, "model TestModel end TestModel;");

        // Wait for the event with a timeout
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.True(changeReceived, "Expected OnFileChanged to fire for .mo file creation");
    }

    [Fact]
    public async Task FileCreation_NonMoFile_DoesNotTriggerChange()
    {
        var changeReceived = false;
        _service.OnFileChanged += change =>
        {
            if (change.FilePath.EndsWith(".txt"))
                changeReceived = true;
        };

        _service.StartMonitoring("repo1", _tempDir);

        var testFilePath = Path.Combine(_tempDir, "readme.txt");
        await File.WriteAllTextAsync(testFilePath, "test");

        // Wait briefly - no event should fire for .txt files
        await Task.Delay(1000);

        Assert.False(changeReceived, "Non-.mo files should not trigger changes");
    }

    [Fact]
    public async Task PackageOrderFileCreation_TriggersChange_WhenMonitoring()
    {
        var changeReceived = false;
        var tcs = new TaskCompletionSource<bool>();
        _service.OnFileChanged += change =>
        {
            if (change.FilePath.EndsWith("package.order", StringComparison.OrdinalIgnoreCase))
            {
                changeReceived = true;
                tcs.TrySetResult(true);
            }
        };

        _service.StartMonitoring("repo1", _tempDir);

        var testFilePath = Path.Combine(_tempDir, "package.order");
        await File.WriteAllTextAsync(testFilePath, "Model1\nModel2");

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.True(changeReceived, "Expected OnFileChanged to fire for package.order creation");
    }

    [Fact]
    public async Task GetPendingChangesSummary_AfterFileCreation_ReflectsChange()
    {
        _service.StartMonitoring("repo1", _tempDir);

        var testFilePath = Path.Combine(_tempDir, "TestModel.mo");
        await File.WriteAllTextAsync(testFilePath, "model TestModel end TestModel;");

        // Wait for the event to be processed
        await Task.Delay(1500);

        var summary = _service.GetPendingChangesSummary();
        Assert.True(summary.HasChanges);
    }

    [Fact]
    public async Task GetPendingChangesForRepository_AfterFileCreation_ReturnsChange()
    {
        _service.StartMonitoring("repo1", _tempDir);

        var testFilePath = Path.Combine(_tempDir, "TestModel.mo");
        await File.WriteAllTextAsync(testFilePath, "model TestModel end TestModel;");

        await Task.Delay(1500);

        var changes = _service.GetPendingChangesForRepository("repo1");
        Assert.NotEmpty(changes);
    }

    [Fact]
    public async Task ClearPendingChanges_AfterFileCreation_ClearsAllChanges()
    {
        _service.StartMonitoring("repo1", _tempDir);

        var testFilePath = Path.Combine(_tempDir, "TestModel.mo");
        await File.WriteAllTextAsync(testFilePath, "model TestModel end TestModel;");

        await Task.Delay(1500);

        _service.ClearPendingChanges();

        Assert.Empty(_service.PendingChanges);
        var summary = _service.GetPendingChangesSummary();
        Assert.False(summary.HasChanges);
    }

    [Fact]
    public async Task ClearPendingChanges_ForRepository_ClearsOnlyThatRepository()
    {
        var tempDir2 = Path.Combine(Path.GetTempPath(), "mlqt-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir2);
        try
        {
            _service.StartMonitoring("repo1", _tempDir);
            _service.StartMonitoring("repo2", tempDir2);

            await File.WriteAllTextAsync(Path.Combine(_tempDir, "Model1.mo"), "model Model1 end Model1;");
            await File.WriteAllTextAsync(Path.Combine(tempDir2, "Model2.mo"), "model Model2 end Model2;");

            await Task.Delay(1500);

            _service.ClearPendingChanges("repo1");

            var repo1Changes = _service.GetPendingChangesForRepository("repo1");
            var repo2Changes = _service.GetPendingChangesForRepository("repo2");

            Assert.Empty(repo1Changes);
            Assert.NotEmpty(repo2Changes);
        }
        finally
        {
            _service.StopMonitoring("repo2");
            Directory.Delete(tempDir2, recursive: true);
        }
    }

    [Fact]
    public void Dispose_StopsAllMonitoring()
    {
        var tempService = new FileMonitoringService();
        tempService.StartMonitoring("repo1", _tempDir);
        Assert.True(tempService.IsMonitoring);

        tempService.Dispose();

        Assert.False(tempService.IsMonitoring);
    }

    // ---- the handlers nothing asked for by name ------------------------------------------------
    //
    // Rename and delete had no test of their own, and were reached only when Windows happened to
    // report a write as one — which it does sometimes, because a text write is a temp file and a
    // rename underneath. That is why this class's measured coverage moved several points between two
    // runs of the same code, straddling its bar and turning the build into a coin flip. A handler
    // covered by accident is a handler nobody is checking; these wait for the specific change and
    // assert it arrived, so they either cover those lines or fail.

    /// <summary>
    /// Waits for a change of a given kind, or gives up. Generous, because the wait is on the OS
    /// delivering a watcher event under whatever load the machine is under, and a short timeout here
    /// is the flakiness this section exists to remove.
    /// </summary>
    private async Task<FileChangeInfo?> WaitForChangeAsync(
        FileChangeType type, Func<FileChangeInfo, bool>? where = null)
    {
        var tcs = new TaskCompletionSource<FileChangeInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(FileChangeInfo change)
        {
            if (change.ChangeType == type && (where is null || where(change)))
                tcs.TrySetResult(change);
        }

        _service.OnFileChanged += Handler;
        try
        {
            return await Task.WhenAny(tcs.Task, Task.Delay(10_000)) == tcs.Task ? tcs.Task.Result : null;
        }
        finally
        {
            _service.OnFileChanged -= Handler;
        }
    }

    [Fact]
    public async Task RenamingAMoFile_ReportsARename_CarryingBothNames()
    {
        var original = Path.Combine(_tempDir, "Before.mo");
        await File.WriteAllTextAsync(original, "model Before end Before;");
        _service.StartMonitoring("repo1", _tempDir);

        var renamed = Path.Combine(_tempDir, "After.mo");
        var waiting = WaitForChangeAsync(
            FileChangeType.Renamed, c => c.FilePath.EndsWith("After.mo", StringComparison.OrdinalIgnoreCase));
        File.Move(original, renamed);

        var change = await waiting;

        Assert.NotNull(change);
        Assert.Equal(renamed, change!.FilePath);
        Assert.Equal(original, change.OldFilePath);   // the old name is what makes it a rename
        Assert.Equal("repo1", change.RepositoryId);
        Assert.False(change.IsDirectory);
    }

    [Fact]
    public async Task RenamingToANonMoName_IsStillReported()
    {
        // Tracked because it is how a .mo file leaves the library: the old name was one MLQT cared
        // about, so the change is one it has to hear about even though the new name is not.
        var original = Path.Combine(_tempDir, "Leaving.mo");
        await File.WriteAllTextAsync(original, "model Leaving end Leaving;");
        _service.StartMonitoring("repo1", _tempDir);

        var waiting = WaitForChangeAsync(
            FileChangeType.Renamed, c => c.OldFilePath?.EndsWith("Leaving.mo", StringComparison.OrdinalIgnoreCase) == true);
        File.Move(original, Path.Combine(_tempDir, "Leaving.bak"));

        Assert.NotNull(await waiting);
    }

    [Fact]
    public async Task RenamingAFileNobodyTracks_IsNotReported()
    {
        var original = Path.Combine(_tempDir, "notes.txt");
        await File.WriteAllTextAsync(original, "notes");
        _service.StartMonitoring("repo1", _tempDir);

        var seen = false;
        void Handler(FileChangeInfo change) => seen = true;
        _service.OnFileChanged += Handler;
        try
        {
            File.Move(original, Path.Combine(_tempDir, "notes2.txt"));
            await Task.Delay(1500);
        }
        finally { _service.OnFileChanged -= Handler; }

        Assert.False(seen, "neither name is one MLQT tracks");
    }

    [Fact]
    public async Task DeletingAMoFile_ReportsADeletion()
    {
        var path = Path.Combine(_tempDir, "Doomed.mo");
        await File.WriteAllTextAsync(path, "model Doomed end Doomed;");
        _service.StartMonitoring("repo1", _tempDir);

        var waiting = WaitForChangeAsync(
            FileChangeType.Deleted, c => c.FilePath.EndsWith("Doomed.mo", StringComparison.OrdinalIgnoreCase));
        File.Delete(path);

        var change = await waiting;

        Assert.NotNull(change);
        Assert.Equal(1, _service.GetPendingChangesSummary().DeletedFiles);
    }

    [Fact]
    public async Task ARenameIsCountedAsOneInTheSummary()
    {
        // The summary is what the refresh prompt reads, so its counts are the user-facing half of
        // these handlers.
        var original = Path.Combine(_tempDir, "Counted.mo");
        await File.WriteAllTextAsync(original, "model Counted end Counted;");
        _service.StartMonitoring("repo1", _tempDir);

        var waiting = WaitForChangeAsync(FileChangeType.Renamed);
        File.Move(original, Path.Combine(_tempDir, "CountedNow.mo"));
        Assert.NotNull(await waiting);

        Assert.Equal(1, _service.GetPendingChangesSummary().RenamedFiles);
    }

    [Fact]
    public async Task StopMonitoring_StopsTheEvents()
    {
        _service.StartMonitoring("repo1", _tempDir);
        _service.StopMonitoring("repo1");

        var seen = false;
        void Handler(FileChangeInfo change) => seen = true;
        _service.OnFileChanged += Handler;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(_tempDir, "Ignored.mo"), "model Ignored end Ignored;");
            await Task.Delay(1500);
        }
        finally { _service.OnFileChanged -= Handler; }

        Assert.False(seen);
        Assert.False(_service.IsMonitoring);
    }

    [Fact]
    public void NotifyFileActivity_RaisesTheActivityEvent()
    {
        string? seen = null;
        _service.OnRepositoryFileActivity += id => seen = id;

        _service.NotifyFileActivity("repo1");

        Assert.Equal("repo1", seen);
    }

    [Fact]
    public void StartMonitoring_APathThatIsNotThere_DoesNotThrowOrStartMonitoring()
    {
        // The catch around the watcher setup: a repository whose working copy has been moved or
        // deleted since the project was saved, which is an ordinary thing to happen between sessions.
        _service.StartMonitoring("gone", Path.Combine(_tempDir, "no-such-directory"));

        Assert.False(_service.IsMonitoring);
    }

    [Fact]
    public void StopMonitoring_ARepositoryThatWasNeverStarted_IsHarmless()
    {
        _service.StopMonitoring("never-started");

        Assert.False(_service.IsMonitoring);
    }
}
