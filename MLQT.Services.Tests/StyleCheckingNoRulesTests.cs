using MLQT.Services.DataTypes;
using ModelicaGraph;
using MLQT.Services;
using MLQT.Services.Checking;
using Xunit;
using Xunit.Abstractions;

namespace MLQT.Services.Tests;

/// <summary>
/// Style checking must announce that it has finished even when it finds nothing to do.
///
/// <para>The desktop app shows a modal, non-dismissable progress dialog while a run is in flight
/// and closes it on the completion event. A run that quietly does nothing and never signals leaves
/// the application wedged with no way out and nothing further in the log — so "no rules enabled"
/// has to reach the same completion signal as a full run.</para>
/// </summary>
public class StyleCheckingNoRulesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-norules", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string WriteRepo(string name)
    {
        var repo = Path.Combine(_root, name);
        var lib = Path.Combine(repo, name);
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"),
            $"package {name}\n  model Widget\n  end Widget;\nend {name};\n");
        File.WriteAllText(Path.Combine(lib, "package.order"), "Widget\n");
        return repo;
    }

    private sealed record Fixture(StyleCheckingService Service, List<Repository> Repositories);

    private async Task<Fixture> LoadTwoRepositoriesWithNoRulesAsync()
    {
        var libraries = new LibraryDataService();
        var monitor = new FileMonitoringService();
        var settingsService = new InMemorySettingsService();
        var repos = new RepositoryService(libraries, settingsService, monitor);
        var service = new StyleCheckingService(
            libraries, repos, settingsService,
            new CustomDictionaryService(), new DictionaryManagerService(), new CodeReviewService());

        var loaded = new List<Repository>();
        foreach (var name in new[] { "MSL", "ExternData" })
        {
            var added = await repos.AddRepositoryAsync(WriteRepo(name), checkoutPath: null, startMonitoring: false);
            Assert.True(added.Success, added.ErrorMessage);
            await repos.LoadLibrariesAsync(added.Repository!.Id);

            added.Repository.StyleSettings = new StyleCheckingSettings();
            Assert.False(added.Repository.StyleSettings.HasAnyStyleRuleEnabled);
            loaded.Add(added.Repository);
        }

        return new Fixture(service, loaded);
    }

    [Fact]
    public async Task EveryRepositorySkipped_StillSignalsCompletion()
    {
        var fixture = await LoadTwoRepositoriesWithNoRulesAsync();

        var completions = 0;
        fixture.Service.OnProgressChanged += done => { if (done) Interlocked.Increment(ref completions); };

        fixture.Service.StartBackgroundCheckingForRepositories(fixture.Repositories);

        Assert.Equal(1, Volatile.Read(ref completions));
        Assert.False(fixture.Service.IsRunning);
    }

    [Fact]
    public async Task ASingleSkippedRepository_StillSignalsCompletion()
    {
        // The settings-applied path uses this one, and it is the path a user hits by turning every
        // rule off and pressing Apply.
        var fixture = await LoadTwoRepositoriesWithNoRulesAsync();

        var completions = 0;
        fixture.Service.OnProgressChanged += done => { if (done) Interlocked.Increment(ref completions); };

        fixture.Service.StartBackgroundChecking(fixture.Repositories[0]);

        Assert.Equal(1, Volatile.Read(ref completions));
    }

    [Fact]
    public async Task NoRepositoriesAtAll_StillSignalsCompletion()
    {
        var fixture = await LoadTwoRepositoriesWithNoRulesAsync();

        var completions = 0;
        fixture.Service.OnProgressChanged += done => { if (done) Interlocked.Increment(ref completions); };

        fixture.Service.StartBackgroundCheckingForRepositories([]);

        Assert.Equal(1, Volatile.Read(ref completions));
    }
}
