using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.SpellChecking;
using MLQT.Services.Helpers;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using static MLQT.Services.LoggingService;

namespace MLQT.Services;

/// <summary>
/// Singleton service that manages style checking of Modelica models.
/// Handles background processing with queue management and batched UI updates.
/// </summary>
public class StyleCheckingService : IStyleCheckingService
{
    private readonly object _pendingViolationsLock = new();
    private readonly List<LogMessage> _pendingViolations = new();
    private readonly object _workerLock = new();
    private List<StyleCheckingWorker> _workers = new();

    private bool _isRunning;
    private bool _stopRequested;
    private long _lastProgressTicks = 0;

    /// <summary>
    /// Cancels any in-flight workers, clears pending violations, and removes
    /// previously delivered style checking messages from CodeReviewService so a
    /// new checking run starts from a clean slate. Must be called at the top of
    /// every public entry point that starts style checking.
    /// </summary>
    private void CancelExistingWorkers()
    {
        CancelRunningWorkers();
        // Remove style checking violations already delivered to CodeReviewService
        // so they don't duplicate when the new run re-produces them
        _codeReviewService.RemoveLogMessagesByPredicate(m => m.Source == "StyleChecking");
    }

    /// <summary>
    /// Cancels any running workers and clears unflushed violations, but does NOT
    /// remove already-delivered violations from CodeReviewService. Used by targeted
    /// checks (CheckModelsAsync) where the caller handles removal for specific models.
    /// </summary>
    private void CancelRunningWorkers()
    {
        lock (_workerLock)
        {
            foreach (var worker in _workers)
                worker.CancelProcessing();
            _workers.Clear();
        }
        lock (_pendingViolationsLock)
        {
            _pendingViolations.Clear();
        }
    }

    /// <inheritdoc/>
    public event Action<bool>? OnProgressChanged;

    /// <inheritdoc/>
    public event Action<List<LogMessage>>? OnViolationsFound;

    /// <inheritdoc/>
    public bool IsRunning => _isRunning;

    private readonly ILibraryDataService _libraryDataService;
    private readonly IRepositoryService _repositoryService;
    private readonly ISettingsService _settingsService;
    private readonly ICustomDictionaryService _customDictionaryService;
    private readonly IDictionaryManagerService _dictionaryManagerService;
    private readonly ICodeReviewService _codeReviewService;
    private SpellChecker? _spellChecker;
    private bool _dictionaryLoaded;
    private List<string>? _lastLanguages;


    public StyleCheckingService(
        ILibraryDataService libraryDataService,
        IRepositoryService repositoryService,
        ISettingsService settingsService,
        ICustomDictionaryService customDictionaryService,
        IDictionaryManagerService dictionaryManagerService,
        ICodeReviewService codeReviewService)
    {
        _libraryDataService = libraryDataService;
        _repositoryService = repositoryService;
        _settingsService = settingsService;
        _customDictionaryService = customDictionaryService;
        _dictionaryManagerService = dictionaryManagerService;
        _codeReviewService = codeReviewService;

        // Reload the spell checker when the custom dictionary or available dictionaries change
        _customDictionaryService.OnDictionaryChanged += () =>
        {
            if (_spellChecker != null)
                ReloadSpellChecker(_lastLanguages);
        };
        _dictionaryManagerService.OnDictionariesChanged += () =>
        {
            if (_spellChecker != null)
                ReloadSpellChecker(_lastLanguages);
        };
    }

    /// <inheritdoc/>
    public int QueuedCount
    {
        get
        {
            int result = 0;
            foreach(var worker in _workers)
            {
                result += worker.QueuedCount;
            }
            return result;
        }
    }

    /// <inheritdoc/>
    public SpellChecker? GetSpellChecker() => _spellChecker;

    /// <inheritdoc/>
    public SpellChecker EnsureSpellChecker(IEnumerable<string>? customWords = null)
    {
        return EnsureSpellChecker(null, customWords);
    }

    /// <summary>
    /// Ensures a spell checker exists with the specified languages.
    /// If languages differ from the last creation, the spell checker is recreated.
    /// </summary>
    private SpellChecker EnsureSpellChecker(List<string>? languages, IEnumerable<string>? customWords = null)
    {
        // Invalidate if language selection has changed
        if (_spellChecker != null && languages != null && !LanguagesMatch(languages, _lastLanguages))
            _spellChecker = null;

        if (_spellChecker == null)
        {
            // Ensure the custom dictionary is loaded from disk before first use
            if (!_dictionaryLoaded)
            {
                _customDictionaryService.LoadAsync().GetAwaiter().GetResult();
                _dictionaryLoaded = true;
            }

            _lastLanguages = languages;
            _spellChecker = CreateSpellChecker(languages, customWords);
        }
        return _spellChecker;
    }

    /// <inheritdoc/>
    public void ReloadSpellChecker(IEnumerable<string>? customWords = null)
    {
        ReloadSpellChecker(null, customWords);
    }

    private void ReloadSpellChecker(List<string>? languages, IEnumerable<string>? customWords = null)
    {
        _lastLanguages = languages ?? _lastLanguages;
        _spellChecker = CreateSpellChecker(_lastLanguages, customWords);
    }

    /// <summary>
    /// Creates a new SpellChecker with the given languages and custom words,
    /// separating bundled from imported dictionaries.
    /// </summary>
    private SpellChecker CreateSpellChecker(List<string>? languages, IEnumerable<string>? customWords)
    {
        var allCustomWords = GetMergedCustomWords(customWords);

        // Separate bundled from imported language codes
        var bundledCodes = new List<string>();
        var additionalDicts = new List<DictionarySource>();

        var codes = languages ?? ["en_US", "en_GB"];
        foreach (var code in codes)
        {
            var imported = _dictionaryManagerService.GetImportedDictionaryPaths(code);
            if (imported != null)
                additionalDicts.Add(imported);
            else
                bundledCodes.Add(code);
        }

        return SpellChecker.Create(
            languageCodes: bundledCodes,
            customWords: allCustomWords,
            additionalDictionaries: additionalDicts.Count > 0 ? additionalDicts : null);
    }

    private static bool LanguagesMatch(List<string>? a, List<string>? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.Count != b.Count) return false;
        return a.SequenceEqual(b, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Merges explicitly provided custom words with the custom dictionary service words.
    /// </summary>
    private IEnumerable<string>? GetMergedCustomWords(IEnumerable<string>? additionalWords)
    {
        var dictionaryWords = _customDictionaryService.CustomWords;
        if (dictionaryWords.Count == 0 && additionalWords == null)
            return null;

        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in dictionaryWords)
            merged.Add(word);
        if (additionalWords != null)
        {
            foreach (var word in additionalWords)
                merged.Add(word);
        }
        return merged;
    }

    /// <inheritdoc/>
    public Task<List<LogMessage>> CheckModelAsync(ModelDefinition model, StyleCheckingSettings settings)
    {
        return Task.FromResult(StyleChecking.RunStyleChecking(model, settings));
    }

    /// <inheritdoc/>
    public async Task StartBackgroundCheckingAsync(Repository repository)
    {
        CancelExistingWorkers();
        _stopRequested = false;

        //Check that the repository style settings exist
        if (repository.StyleSettings == null)
        {
            //Copy the tool defaults
            repository.StyleSettings = await _settingsService.GetAsync("StyleChecking", new StyleCheckingSettings());
        }

        // Skip entirely if no style rules are enabled — avoids queuing and parsing all models
        if (!repository.StyleSettings.HasAnyStyleRuleEnabled)
        {
            LogProcessStart("StyleCheckingService", $"Skipping style checking for {repository.Name} — no rules enabled");
            SignalCompleteIfNoWorkers();
            return;
        }

        //Reset StyleRulesChecked flag so models get re-checked with current settings
        ResetStyleRulesChecked(repository);

        //Create worker for this repository
        int queuedModels = 0;
        var spellChecker = GetSpellCheckerIfNeeded(repository.StyleSettings);
        var worker = new StyleCheckingWorker(_libraryDataService.CombinedGraph, repository.StyleSettings, repository.Name, spellChecker);
        worker.OnViolationFound += ViolationsFound;
        worker.OnProgressChanged += ProgressChanged;
        worker.OnWorkCompleted += WorkerCompletedChecks;
        lock (_workerLock) {
            _workers.Add(worker);
        }
        foreach (var library in _libraryDataService.Libraries)
        {
            if (library.RepositoryId == repository.Id)
            {
                foreach(var model in library.ModelIds)
                {
                    worker.AddToQueue(model);
                    queuedModels++;
                }
            }
        }
        //Ensure the worker is processing the queue and that the UI update task is running
        worker.StartProcessing();
        _ = ProcessQueueAsync();

        LogProcessStart("StyleCheckingService", $"Background style checking ({repository.Name} models)");
        OnProgressChanged?.Invoke(false);
    }

    /// <inheritdoc/>
    public void StartBackgroundChecking(Repository repository)
    {
        CancelExistingWorkers();
        _stopRequested = false;

        //Check that the repository style settings exist
        if (repository.StyleSettings == null)
        {
            //Copy the tool defaults
            repository.StyleSettings = _settingsService.GetAsync("StyleChecking", new StyleCheckingSettings()).GetAwaiter().GetResult();
        }

        // Skip entirely if no style rules are enabled — avoids queuing and parsing all models
        if (!repository.StyleSettings.HasAnyStyleRuleEnabled)
        {
            LogProcessStart("StyleCheckingService", $"Skipping style checking for {repository.Name} — no rules enabled");
            SignalCompleteIfNoWorkers();
            return;
        }

        //Reset StyleRulesChecked flag so models get re-checked with current settings
        ResetStyleRulesChecked(repository);

        //Create worker for this repository
        int queuedModels = 0;
        var spellChecker = GetSpellCheckerIfNeeded(repository.StyleSettings);
        var worker = new StyleCheckingWorker(_libraryDataService.CombinedGraph, repository.StyleSettings, repository.Name, spellChecker);
        worker.OnViolationFound += ViolationsFound;
        worker.OnProgressChanged += ProgressChanged;
        worker.OnWorkCompleted += WorkerCompletedChecks;
        lock (_workerLock) {
            _workers.Add(worker);
        }
        foreach (var library in _libraryDataService.Libraries)
        {
            if (library.RepositoryId == repository.Id)
            {
                foreach(var model in library.ModelIds)
                {
                    worker.AddToQueue(model);
                    queuedModels++;
                }
            }
        }
        //Ensure the worker is processing the queue and that the UI update task is running
        worker.StartProcessing();
        _ = Task.Run(()=>ProcessQueueAsync());

        LogProcessStart("StyleCheckingService", $"Background style checking ({repository.Name} models)");
        OnProgressChanged?.Invoke(false);
    }

    /// <inheritdoc/>
    public void StartBackgroundCheckingForRepositories(IReadOnlyList<Repository> repositories)
    {
        CancelExistingWorkers();
        _stopRequested = false;

        bool anyWorkerStarted = false;

        foreach (var repository in repositories)
        {
            repository.StyleSettings ??= _settingsService.GetAsync("StyleChecking", new StyleCheckingSettings()).GetAwaiter().GetResult();

            if (!repository.StyleSettings.HasAnyStyleRuleEnabled)
            {
                LogProcessStart("StyleCheckingService", $"Skipping style checking for {repository.Name} — no rules enabled");
                continue;
            }

            ResetStyleRulesChecked(repository);

            var spellChecker = GetSpellCheckerIfNeeded(repository.StyleSettings);
            var worker = new StyleCheckingWorker(_libraryDataService.CombinedGraph, repository.StyleSettings, repository.Name, spellChecker);
            worker.OnViolationFound += ViolationsFound;
            worker.OnProgressChanged += ProgressChanged;
            worker.OnWorkCompleted += WorkerCompletedChecks;
            lock (_workerLock)
            {
                _workers.Add(worker);
            }

            foreach (var library in _libraryDataService.Libraries)
            {
                if (library.RepositoryId == repository.Id)
                {
                    foreach (var model in library.ModelIds)
                    {
                        worker.AddToQueue(model);
                    }
                }
            }

            worker.StartProcessing();
            LogProcessStart("StyleCheckingService", $"Background style checking ({repository.Name} models)");
            anyWorkerStarted = true;
        }

        if (anyWorkerStarted)
        {
            OnProgressChanged?.Invoke(false);
            _ = Task.Run(() => ProcessQueueAsync());
        }
        else
        {
            // All repositories were skipped — signal completion immediately
            OnProgressChanged?.Invoke(true);
        }
    }

    /// <summary>
    /// Returns the spell checker if spell checking is enabled in the given settings,
    /// ensuring it is created if needed with the correct language dictionaries.
    /// </summary>
    private SpellChecker? GetSpellCheckerIfNeeded(StyleCheckingSettings settings)
    {
        if (settings.SpellCheckDescription || settings.SpellCheckDocumentation)
            return EnsureSpellChecker(settings.SpellCheckLanguages);
        return null;
    }

    private void ResetStyleRulesChecked(Repository repository)
    {
        foreach (var library in _libraryDataService.Libraries)
        {
            if (library.RepositoryId == repository.Id)
            {
                foreach (var modelId in library.ModelIds)
                {
                    var node = _libraryDataService.CombinedGraph.GetNode<ModelNode>(modelId);
                    if (node != null)
                        node.Definition.StyleRulesChecked = false;
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task CheckModelsAsync(IEnumerable<string> modelIds, DirectedGraph graph)
    {
        // Cancel running workers but preserve already-delivered violations.
        // The caller is responsible for removing old violations for the targeted
        // models via CodeReviewService.RemoveLogMessagesForModels before calling.
        CancelRunningWorkers();
        _stopRequested = false;

        var modelIdList = modelIds.ToList();
        if (modelIdList.Count == 0)
            return;

        // Reset the StyleRulesChecked flag for these models so they get re-checked
        foreach (var modelId in modelIdList)
        {
            var node = graph.GetNode<ModelNode>(modelId);
            if (node != null)
            {
                node.Definition.StyleRulesChecked = false;
            }
        }

        // Group models by repository so each gets the correct style settings
        var modelsByRepo = new Dictionary<string, List<string>>();
        foreach (var modelId in modelIdList)
        {
            var library = _libraryDataService.Libraries.FirstOrDefault(l => l.ModelIds.Contains(modelId));
            var repoId = library?.RepositoryId ?? "";
            if (!modelsByRepo.TryGetValue(repoId, out var list))
            {
                list = [];
                modelsByRepo[repoId] = list;
            }
            list.Add(modelId);
        }

        // Create a worker for each repository's affected models
        foreach (var kvp in modelsByRepo)
        {
            var repoId = kvp.Key;
            var repoModelIds = kvp.Value;

            // Get the style settings for this repository
            StyleCheckingSettings settings;
            var repo = _repositoryService.Repositories.FirstOrDefault(r => r.Id == repoId);
            if (repo?.StyleSettings != null)
            {
                settings = repo.StyleSettings;
            }
            else
            {
                settings = await _settingsService.GetAsync("StyleChecking", new StyleCheckingSettings());
            }

            var workerName = repo?.Name ?? "unknown";
            var spellChecker = GetSpellCheckerIfNeeded(settings);
            var worker = new StyleCheckingWorker(graph, settings, workerName, spellChecker);
            worker.OnViolationFound += ViolationsFound;
            worker.OnProgressChanged += ProgressChanged;
            worker.OnWorkCompleted += WorkerCompletedChecks;
            lock (_workerLock)
            {
                _workers.Add(worker);
            }

            foreach (var modelId in repoModelIds)
            {
                worker.AddToQueue(modelId);
            }

            worker.StartProcessing();
        }

        LogProcessStart("StyleCheckingService", $"Style checking {modelIdList.Count} model(s)");
        OnProgressChanged?.Invoke(false);

        // Start the flush loop (fire-and-forget). Must NOT be awaited because
        // FlushPendingViolations fires OnViolationsFound which calls InvokeAsync
        // to marshal to the render thread — awaiting would deadlock if the caller
        // is already on the render thread.
        _ = Task.Run(ProcessQueueAsync);
    }

    private async Task ProcessQueueAsync()
    {
        if (_isRunning)
            return;
        _isRunning = true;

        try
        {
            // Keep flushing while there are active workers or pending violations
            while (!_stopRequested)
            {
                bool hasWorkers;
                lock (_workerLock)
                {
                    hasWorkers = _workers.Count > 0;
                }

                if (!hasWorkers)
                    break;

                await Task.Delay(500); // Batch updates every 500ms
                FlushPendingViolations();
            }
            // Final flush when done
            FlushPendingViolations();
        }
        finally
        {
            _isRunning = false;
            LogProcessEnd("StyleCheckingService", "All background style checking completed");
            OnProgressChanged?.Invoke(true);
        }
    }

    private void ViolationsFound(object? sender, List<LogMessage> violations)
    {
        if (violations.Count > 0)
        {
            lock (_pendingViolationsLock)
            {
                _pendingViolations.AddRange(violations);
            }
        }
    }

    private void ProgressChanged()
    {
        // Throttle progress updates to at most once per second to reduce UI overhead
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastProgressTicks);
        if (now - last >= 1000)
        {
            Interlocked.Exchange(ref _lastProgressTicks, now);
            OnProgressChanged?.Invoke(false);
        }
    }

    /// <summary>
    /// Fires OnProgressChanged(allComplete: true) if there are no active workers.
    /// Called when a repository is skipped to ensure the UI knows processing is done.
    /// </summary>
    private void SignalCompleteIfNoWorkers()
    {
        bool hasWorkers;
        lock (_workerLock)
        {
            hasWorkers = _workers.Count > 0;
        }
        if (!hasWorkers)
        {
            OnProgressChanged?.Invoke(true);
        }
    }

    private void WorkerCompletedChecks(object? sender, string repositoryName)
    {
        if (sender != null) {
            var worker = (StyleCheckingWorker)sender;
            lock (_workerLock) {
                _workers.Remove(worker);
            }
            // Flush any remaining violations from this worker
            FlushPendingViolations();
            LogProcessEnd("StyleCheckingService", $"Background style checking completed for ({repositoryName})");
        }
    }

    private void FlushPendingViolations()
    {
        List<LogMessage> violationsToProcess;

        lock (_pendingViolationsLock)
        {
            if (_pendingViolations.Count == 0)
                return;

            violationsToProcess = new List<LogMessage>(_pendingViolations);
            _pendingViolations.Clear();
        }

        OnViolationsFound?.Invoke(violationsToProcess);
    }
}
