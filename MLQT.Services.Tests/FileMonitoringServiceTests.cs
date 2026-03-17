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
}
