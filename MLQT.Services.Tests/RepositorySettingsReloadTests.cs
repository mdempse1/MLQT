using MLQT.Services.DataTypes;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// A repository's <c>.mlqt/settings.json</c> read again, for a process that outlives a change to it -
/// the MCP server across a session, where a reload re-read every library and none of the settings.
/// What changed on disk is taken up; what did not change leaves the settings in use alone, and a
/// file that no longer parses is not a reason to fall back to the defaults.
/// </summary>
public class RepositorySettingsReloadTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mlqt-settings-reload", Guid.NewGuid().ToString("N"));

    public RepositorySettingsReloadTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private const string Committed = "{\n  \"ClassHasDescription\": true\n}\n";

    private async Task<(RepositoryService Service, Repository Repository, string SettingsPath)> Load(string? settingsJson)
    {
        var root = Path.Combine(_dir, "Repo");
        var lib = Path.Combine(root, "P");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"), "within;\npackage P\n  model A\n  end A;\nend P;\n");
        File.WriteAllText(Path.Combine(lib, "package.order"), "A\n");
        var settingsPath = Path.Combine(root, ".mlqt", "settings.json");
        if (settingsJson is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, settingsJson);
        }

        var service = new RepositoryService(new LibraryDataService(), new InMemorySettingsService(), new FileMonitoringService());
        var added = await service.AddRepositoryAsync(root, startMonitoring: false);
        return (service, added.Repository!, settingsPath);
    }

    [Fact]
    public async Task AFileChangedOnDisk_IsTakenUp()
    {
        var (service, repository, path) = await Load(Committed);

        File.WriteAllText(path, "{\n  \"ClassHasDescription\": false,\n  \"OneOfEachSection\": true\n}\n");

        Assert.Equal(SettingsReloadOutcome.Changed, service.ReloadRepositorySettings(repository.Id));
        Assert.False(repository.StyleSettings!.ClassHasDescription);
        Assert.True(repository.StyleSettings.OneOfEachSection);
    }

    [Fact]
    public async Task AFileThatHasNotChanged_LeavesTheSettingsInUse()
    {
        var (service, repository, _) = await Load(Committed);
        var inUse = repository.StyleSettings;

        Assert.Equal(SettingsReloadOutcome.Unchanged, service.ReloadRepositorySettings(repository.Id));
        Assert.Same(inUse, repository.StyleSettings);
    }

    [Fact]
    public async Task AChangeMadeThisSession_IsNotUndoneByAFileThatHasNotChanged()
    {
        // A change that could not be saved - a read-only file - lives in memory only; re-reading the
        // same file is not a reason to drop it.
        var (service, repository, _) = await Load(Committed);
        repository.StyleSettings!.OneOfEachSection = true;

        Assert.Equal(SettingsReloadOutcome.Unchanged, service.ReloadRepositorySettings(repository.Id));
        Assert.True(repository.StyleSettings.OneOfEachSection);
    }

    [Fact]
    public async Task AFileThatNoLongerParses_KeepsTheSettingsInUse()
    {
        var (service, repository, path) = await Load(Committed);

        File.WriteAllText(path, "{ not json");

        Assert.Equal(SettingsReloadOutcome.Unreadable, service.ReloadRepositorySettings(repository.Id));
        Assert.True(repository.StyleSettings!.ClassHasDescription);
    }

    [Fact]
    public async Task AFileThatHasGone_PutsTheDefaultsBack_AsAFirstLoadWould()
    {
        var (service, repository, path) = await Load(Committed);

        File.Delete(path);

        Assert.Equal(SettingsReloadOutcome.Changed, service.ReloadRepositorySettings(repository.Id));
        Assert.False(repository.StyleSettings!.ClassHasDescription);
        Assert.Null(repository.SettingsOnDisk);
    }

    [Fact]
    public async Task ARepositoryThatNeverHadAFile_IsUnchanged()
    {
        var (service, repository, _) = await Load(settingsJson: null);

        Assert.Equal(SettingsReloadOutcome.Unchanged, service.ReloadRepositorySettings(repository.Id));
    }

    [Fact]
    public async Task ARepositoryThatAppearsAfterTheFirstReload_IsReadToo()
    {
        var (service, repository, path) = await Load(settingsJson: null);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Committed);

        Assert.Equal(SettingsReloadOutcome.Changed, service.ReloadRepositorySettings(repository.Id));
        Assert.True(repository.StyleSettings!.ClassHasDescription);
    }

    [Fact]
    public async Task AnUnknownRepository_IsNotFound()
    {
        var (service, _, _) = await Load(Committed);

        Assert.Equal(SettingsReloadOutcome.NotFound, service.ReloadRepositorySettings("no-such-repository"));
    }
}
