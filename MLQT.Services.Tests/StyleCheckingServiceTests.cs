using MLQT.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// Unit tests for the StyleCheckingService class.
/// </summary>
public class StyleCheckingServiceTests
{
    private LibraryDataService _libraryDataService = null!;
    private RepositoryService _repositoryService = null!;
    private InMemorySettingsService _settingsService = null!;

    private StyleCheckingService CreateService()
    {
        _libraryDataService = new LibraryDataService();
        _settingsService = new InMemorySettingsService();
        var fileMonitoringService = new FileMonitoringService();
        _repositoryService = new RepositoryService(_libraryDataService, _settingsService, fileMonitoringService);
        var customDictionaryService = new StubCustomDictionaryService();
        var dictionaryManagerService = new StubDictionaryManagerService();
        return new StyleCheckingService(_libraryDataService, _repositoryService, _settingsService, customDictionaryService, dictionaryManagerService);
    }

    private class StubCustomDictionaryService : ICustomDictionaryService
    {
        public IReadOnlyCollection<string> CustomWords => Array.Empty<string>();
#pragma warning disable CS0067
        public event Action? OnDictionaryChanged;
#pragma warning restore CS0067
        public Task AddWordAsync(string word) => Task.CompletedTask;
        public Task RemoveWordAsync(string word) => Task.CompletedTask;
        public Task ImportAsync(string filePath) => Task.CompletedTask;
        public Task ExportAsync(string filePath) => Task.CompletedTask;
        public Task MergeAsync(string filePath) => Task.CompletedTask;
        public Task LoadAsync() => Task.CompletedTask;
    }

    private class StubDictionaryManagerService : IDictionaryManagerService
    {
        public IReadOnlyList<DictionaryInfo> GetAvailableDictionaries() => new List<DictionaryInfo>
        {
            new("en_US", "English (US)", IsBundled: true),
            new("en_GB", "English (UK)", IsBundled: true)
        };
        public Task<string?> ImportDictionaryAsync(string affFilePath, string dicFilePath) => Task.FromResult<string?>(null);
        public Task<bool> RemoveImportedDictionaryAsync(string languageCode) => Task.FromResult(false);
        public ModelicaParser.SpellChecking.DictionarySource? GetImportedDictionaryPaths(string languageCode) => null;
#pragma warning disable CS0067
        public event Action? OnDictionariesChanged;
#pragma warning restore CS0067
    }

    /// <summary>
    /// Helper to create a repository with models loaded into LibraryDataService.
    /// </summary>
    private async Task<Repository> CreateRepositoryWithModelsAsync(
        StyleCheckingSettings? settings = null,
        params (string id, string code)[] models)
    {
        var repo = new Repository
        {
            Name = "TestRepo",
            StyleSettings = settings ?? new StyleCheckingSettings()
        };

        foreach (var (id, code) in models)
        {
            var library = await _libraryDataService.AddLibraryFromFileAsync($"{id}.mo", code);
            library.RepositoryId = repo.Id;
        }

        return repo;
    }

    [Fact]
    public void IsRunning_InitiallyFalse()
    {
        var service = CreateService();

        Assert.False(service.IsRunning);
    }

    [Fact]
    public void QueuedCount_InitiallyZero()
    {
        var service = CreateService();

        Assert.Equal(0, service.QueuedCount);
    }

    [Fact]
    public async Task CheckModelAsync_ReturnsViolationsForBadModel()
    {
        var service = CreateService();
        var settings = new StyleCheckingSettings { ClassHasDescription = true };

        // Model without description string
        var model = new ModelDefinition("TestModel", @"model TestModel
  Real x;
end TestModel;");

        var violations = await service.CheckModelAsync(model, settings);

        // Should have violation for missing description
        Assert.NotEmpty(violations);
    }

    [Fact]
    public async Task CheckModelAsync_ReturnsEmptyForGoodModel()
    {
        var service = CreateService();
        var settings = new StyleCheckingSettings
        {
            ClassHasDescription = false,
            ClassHasDocumentationInfo = false,
            ClassHasDocumentationRevisions = false,
            ClassHasIcon = false,
            ParameterHasDescription = false,
            ConstantHasDescription = false,
            ImportStatementsFirst = false,
            ComponentsBeforeClasses = false,
            OneOfEachSection = false,
            DontMixEquationAndAlgorithm = false,
            DontMixConnections = false,
            InitialEQAlgoFirst = false,
            InitialEQAlgoLast = false,
            FollowNamingConvention = false,
            SpellCheckDescription = false,
            SpellCheckDocumentation = false
        };

        var model = new ModelDefinition("TestModel", @"model TestModel
  Real x;
end TestModel;");

        var violations = await service.CheckModelAsync(model, settings);

        Assert.Empty(violations);
    }

    [Fact]
    public async Task CheckModelAsync_SetsStyleRulesChecked()
    {
        var service = CreateService();
        var settings = new StyleCheckingSettings { ClassHasDescription = false };

        var model = new ModelDefinition("TestModel", "model TestModel end TestModel;");

        Assert.False(model.StyleRulesChecked);

        await service.CheckModelAsync(model, settings);

        Assert.True(model.StyleRulesChecked);
    }

    [Fact]
    public async Task StartBackgroundCheckingAsync_FiresOnProgressChangedEvent()
    {
        var service = CreateService();
        var eventFired = false;
        service.OnProgressChanged += (_) => eventFired = true;

        var repo = await CreateRepositoryWithModelsAsync(
            new StyleCheckingSettings { ClassHasDescription = true },
            ("TestModel", "model TestModel end TestModel;"));

        await service.StartBackgroundCheckingAsync(repo);

        // Wait for background processing to complete
        await WaitForCompletionAsync(service);

        Assert.True(eventFired);
    }

    [Fact]
    public async Task StartBackgroundCheckingAsync_FiresOnViolationsFoundEvent()
    {
        var service = CreateService();
        var settings = new StyleCheckingSettings { ClassHasDescription = true };

        var violationsReceived = new List<LogMessage>();
        service.OnViolationsFound += v => violationsReceived.AddRange(v);

        var repo = await CreateRepositoryWithModelsAsync(
            settings,
            ("TestModel", "model TestModel Real x; end TestModel;"));

        await service.StartBackgroundCheckingAsync(repo);

        // Wait for background processing and flush to complete
        await WaitForCompletionAsync(service);

        Assert.NotEmpty(violationsReceived);
    }

    [Fact]
    public async Task StartBackgroundCheckingAsync_UsesRepositorySettings()
    {
        var service = CreateService();
        var violationsReceived = new List<LogMessage>();
        service.OnViolationsFound += v => violationsReceived.AddRange(v);

        // Create repo with description checking disabled - should find no violations
        var repo = await CreateRepositoryWithModelsAsync(
            new StyleCheckingSettings { ClassHasDescription = false },
            ("TestModel", "model TestModel Real x; end TestModel;"));

        await service.StartBackgroundCheckingAsync(repo);
        await WaitForCompletionAsync(service);

        Assert.Empty(violationsReceived);
    }

    [Fact]
    public async Task CheckModelsAsync_ReChecksSpecificModels()
    {
        var service = CreateService();
        var violationsReceived = new List<LogMessage>();
        service.OnViolationsFound += v => violationsReceived.AddRange(v);

        var settings = new StyleCheckingSettings { ClassHasDescription = true };

        // Store settings so CheckModelsAsync can find them via the fallback path
        await _settingsService.SetAsync("StyleChecking", settings);

        var repo = await CreateRepositoryWithModelsAsync(
            settings,
            ("TestModel", "model TestModel Real x; end TestModel;"));

        // Mark models as already checked
        var graph = _libraryDataService.CombinedGraph;
        foreach (var modelNode in graph.ModelNodes)
        {
            modelNode.Definition.StyleRulesChecked = true;
        }

        // CheckModelsAsync should reset the flag and re-check
        var modelIds = graph.ModelNodes.Select(m => m.Id).ToList();
        await service.CheckModelsAsync(modelIds, graph);
        await WaitForCompletionAsync(service);

        Assert.NotEmpty(violationsReceived);
    }

    [Fact]
    public async Task CheckModelsAsync_WithEmptyList_ReturnsEarly()
    {
        var service = CreateService();
        var violationsReceived = new List<LogMessage>();
        service.OnViolationsFound += v => violationsReceived.AddRange(v);

        await service.CheckModelsAsync(new List<string>(), _libraryDataService.CombinedGraph);

        Assert.Empty(violationsReceived);
    }

    [Fact]
    public async Task StartBackgroundChecking_Sync_FiresOnProgressChangedEvent()
    {
        var service = CreateService();
        var eventFired = false;
        service.OnProgressChanged += (_) => eventFired = true;

        var repo = await CreateRepositoryWithModelsAsync(
            new StyleCheckingSettings { ClassHasDescription = true },
            ("TestModel", "model TestModel end TestModel;"));

        service.StartBackgroundChecking(repo);

        await WaitForCompletionAsync(service);

        Assert.True(eventFired);
    }

    [Fact]
    public async Task StartBackgroundCheckingAsync_WithNullStyleSettings_UsesDefaults()
    {
        var service = CreateService();

        // Create repo without style settings - service should use defaults from settings service
        var repo = new Repository { Name = "TestRepo", StyleSettings = null };
        var library = await _libraryDataService.AddLibraryFromFileAsync("test.mo", "model TestModel end TestModel;");
        library.RepositoryId = repo.Id;

        // Should not throw even with null style settings
        await service.StartBackgroundCheckingAsync(repo);
        await WaitForCompletionAsync(service);

        Assert.NotNull(repo.StyleSettings); // Should have been set during processing
    }

    [Fact]
    public void StartBackgroundChecking_Sync_WithNullStyleSettings_DoesNotThrow()
    {
        var service = CreateService();
        var repo = new Repository { Name = "TestRepo", StyleSettings = null };

        // Should not throw
        service.StartBackgroundChecking(repo);
    }

    [Fact]
    public async Task StartBackgroundCheckingAsync_NoRulesEnabled_SkipsProcessing()
    {
        var service = CreateService();
        bool? completionSignal = null;
        service.OnProgressChanged += (allComplete) => completionSignal = allComplete;

        var repo = await CreateRepositoryWithModelsAsync(
            new StyleCheckingSettings(), // all rules disabled
            ("TestModel", "model TestModel end TestModel;"));

        await service.StartBackgroundCheckingAsync(repo);

        // With no rules enabled, should return immediately without starting workers
        Assert.False(service.IsRunning);
        Assert.Equal(0, service.QueuedCount);
        // Should signal completion so the UI knows style checking is done
        Assert.True(completionSignal);
    }

    [Fact]
    public async Task StartBackgroundChecking_Sync_NoRulesEnabled_SkipsProcessing()
    {
        var service = CreateService();
        bool? completionSignal = null;
        service.OnProgressChanged += (allComplete) => completionSignal = allComplete;

        var repo = await CreateRepositoryWithModelsAsync(
            new StyleCheckingSettings(), // all rules disabled
            ("TestModel", "model TestModel end TestModel;"));

        service.StartBackgroundChecking(repo);

        // With no rules enabled, should return immediately without starting workers
        Assert.False(service.IsRunning);
        Assert.Equal(0, service.QueuedCount);
        // Should signal completion so the UI knows style checking is done
        Assert.True(completionSignal);
    }

    [Fact]
    public async Task StartBackgroundCheckingForRepositories_WithMultipleRepos_ProcessesAll()
    {
        var service = CreateService();
        var violationsReceived = new List<LogMessage>();
        service.OnViolationsFound += v => violationsReceived.AddRange(v);

        var settings = new StyleCheckingSettings { ClassHasDescription = true };

        var repo1 = await CreateRepositoryWithModelsAsync(settings,
            ("Model1", "model Model1 Real x; end Model1;"));
        var repo2 = new Repository
        {
            Name = "TestRepo2",
            StyleSettings = new StyleCheckingSettings { ClassHasDescription = true }
        };
        var lib2 = await _libraryDataService.AddLibraryFromFileAsync("Model2.mo", "model Model2 Real y; end Model2;");
        lib2.RepositoryId = repo2.Id;

        service.StartBackgroundCheckingForRepositories(new List<Repository> { repo1, repo2 });
        await WaitForCompletionAsync(service);

        Assert.NotEmpty(violationsReceived);
    }

    [Fact]
    public async Task StartBackgroundCheckingForRepositories_AllSkipped_SignalsCompletion()
    {
        var service = CreateService();
        bool? completionSignal = null;
        service.OnProgressChanged += (allComplete) => completionSignal = allComplete;

        var repo1 = await CreateRepositoryWithModelsAsync(new StyleCheckingSettings(),
            ("Model1", "model Model1 end Model1;"));
        var repo2 = await CreateRepositoryWithModelsAsync(new StyleCheckingSettings(),
            ("Model2", "model Model2 end Model2;"));

        service.StartBackgroundCheckingForRepositories(new List<Repository> { repo1, repo2 });

        Assert.True(completionSignal);
    }

    [Fact]
    public async Task StartBackgroundCheckingForRepositories_NullSettings_LoadsDefaults()
    {
        var service = CreateService();

        var repo = new Repository { Name = "TestRepo", StyleSettings = null };
        var library = await _libraryDataService.AddLibraryFromFileAsync("test.mo", "model TestModel end TestModel;");
        library.RepositoryId = repo.Id;

        // Should not throw
        service.StartBackgroundCheckingForRepositories(new List<Repository> { repo });
        await WaitForCompletionAsync(service);

        Assert.NotNull(repo.StyleSettings);
    }

    [Fact]
    public void GetSpellChecker_InitiallyNull()
    {
        var service = CreateService();
        Assert.Null(service.GetSpellChecker());
    }

    [Fact]
    public void EnsureSpellChecker_CreatesSpellChecker()
    {
        var service = CreateService();
        var checker = service.EnsureSpellChecker();
        Assert.NotNull(checker);
    }

    [Fact]
    public void EnsureSpellChecker_ReturnsSameInstanceOnSecondCall()
    {
        var service = CreateService();
        var checker1 = service.EnsureSpellChecker();
        var checker2 = service.EnsureSpellChecker();
        Assert.Same(checker1, checker2);
    }

    [Fact]
    public void EnsureSpellChecker_WithCustomWords_CreatesChecker()
    {
        var service = CreateService();
        var checker = service.EnsureSpellChecker(new[] { "customword" });
        Assert.NotNull(checker);
    }

    [Fact]
    public void ReloadSpellChecker_CreatesNewInstance()
    {
        var service = CreateService();
        var checker1 = service.EnsureSpellChecker();
        service.ReloadSpellChecker();
        var checker2 = service.GetSpellChecker();
        Assert.NotNull(checker2);
        Assert.NotSame(checker1, checker2);
    }

    [Fact]
    public void ReloadSpellChecker_WithCustomWords_Works()
    {
        var service = CreateService();
        service.EnsureSpellChecker();
        service.ReloadSpellChecker(new[] { "testword" });
        Assert.NotNull(service.GetSpellChecker());
    }

    [Fact]
    public async Task CheckModelsAsync_GroupsModelsByRepository()
    {
        var service = CreateService();
        var violationsReceived = new List<LogMessage>();
        service.OnViolationsFound += v => violationsReceived.AddRange(v);

        var settings = new StyleCheckingSettings { ClassHasDescription = true };

        // Store settings so CheckModelsAsync uses them via the fallback path
        // (repos aren't registered in RepositoryService, so it falls back to settings service)
        await _settingsService.SetAsync("StyleChecking", settings);

        // Create a library with a model missing a description
        await _libraryDataService.AddLibraryFromFileAsync("Model1.mo", "model Model1 Real x; end Model1;");

        var graph = _libraryDataService.CombinedGraph;
        var modelIds = graph.ModelNodes.Select(m => m.Id).ToList();
        await service.CheckModelsAsync(modelIds, graph);
        await WaitForCompletionAsync(service);

        // Model1 should have violations (description check enabled via fallback settings)
        Assert.NotEmpty(violationsReceived);
    }

    [Fact]
    public async Task StartBackgroundCheckingAsync_WithSpellCheckEnabled_CreatesSpellChecker()
    {
        var service = CreateService();
        var settings = new StyleCheckingSettings { SpellCheckDescription = true };

        var repo = await CreateRepositoryWithModelsAsync(settings,
            ("TestModel", "model TestModel \"A good description\" Real x; end TestModel;"));

        await service.StartBackgroundCheckingAsync(repo);
        await WaitForCompletionAsync(service);

        Assert.NotNull(service.GetSpellChecker());
    }

    /// <summary>
    /// Waits for the style checking service to finish processing.
    /// </summary>
    private static async Task WaitForCompletionAsync(StyleCheckingService service, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        // Wait for processing to start
        await Task.Delay(100);
        // Then wait for it to finish
        while (service.IsRunning && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }
        // Extra delay for final flush
        await Task.Delay(600);
    }
}
