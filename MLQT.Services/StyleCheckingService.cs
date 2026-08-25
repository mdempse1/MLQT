using ModelicaGraph;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.SpellChecking;
using MLQT.Services.Helpers;
using MLQT.Services.DataTypes;
using MLQT.Services.Checking;
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

    // Number of graph-analysis passes currently in flight. The completion signal waits on these as
    // well as on the per-model workers, so the count the UI shows when checking reports "complete"
    // is the final count rather than a partial one that grows a moment later.
    private int _graphAnalysesRunning;

    // Bumped whenever a run is cancelled. A graph-analysis pass captures the value before it awaits
    // dependency analysis (which can take minutes) and abandons its findings if the run it belongs
    // to has since been superseded.
    private int _runGeneration;

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
        EndRun();   // whoever was waiting on the cancelled run is not waiting for anything any more
        Interlocked.Increment(ref _runGeneration);
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

    // Completed when the run started by the last Start* call has finished — every worker done, every
    // graph analysis done, every violation flushed. IsRunning cannot be polled instead: it is set by
    // the flush loop, which starts on a thread-pool thread, so a caller that checks it immediately
    // after queuing work can see false before the loop has begun and conclude the run is over.
    private TaskCompletionSource<bool>? _completion;
    private readonly object _completionLock = new();

    /// <inheritdoc/>
    public Task WaitForCompletionAsync()
    {
        lock (_completionLock)
            return _completion?.Task ?? Task.CompletedTask;
    }

    /// <summary>
    /// Arms the completion signal, immediately before the flush loop that will release it is started.
    ///
    /// <para>If a loop is already running it absorbs this run's workers too — they go in the same list
    /// — and releases this signal when it drains, which is the right moment either way. Two runs
    /// starting within a few milliseconds of each other can still let a waiter return a moment early,
    /// no worse than not waiting at all, which is what callers did before this existed.</para>
    /// </summary>
    private void BeginRun()
    {
        lock (_completionLock)
        {
            if (_completion is null || _completion.Task.IsCompleted)
                _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Releases anything waiting on the run, whether it finished or was cancelled.</summary>
    private void EndRun()
    {
        lock (_completionLock)
            _completion?.TrySetResult(true);
    }

    private readonly ILibraryDataService _libraryDataService;
    private readonly IRepositoryService _repositoryService;
    private readonly ISettingsService _settingsService;
    private readonly ICustomDictionaryService _customDictionaryService;
    private readonly IDictionaryManagerService _dictionaryManagerService;
    private readonly ICodeReviewService _codeReviewService;
    // One spell checker per repository, because the accepted words now live with the repository.
    // A single shared checker would hand one repository's words to another — which is the sort of
    // cross-contamination the move to per-repository lists exists to prevent.
    private readonly Dictionary<string, (SpellChecker Checker, List<string>? Languages)> _spellCheckers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _spellCheckerLock = new();


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

        // Rebuild on the next request rather than eagerly: a word added mid-session should apply to
        // the repository it was added for, and to no other.
        _customDictionaryService.OnDictionaryChanged += repositoryRoot =>
        {
            lock (_spellCheckerLock)
                _spellCheckers.Remove(repositoryRoot);
        };
        _dictionaryManagerService.OnDictionariesChanged += () =>
        {
            lock (_spellCheckerLock)
                _spellCheckers.Clear();
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
    public SpellChecker EnsureSpellChecker(string? repositoryRoot, IEnumerable<string>? languages = null)
    {
        var wanted = languages?.ToList();

        // No repository means no accepted words — not no checker. The language dictionaries are
        // installed per machine and belong to nobody, so a class outside a repository can still be
        // offered corrections; only recording a word needs somewhere to record it. Keyed by the empty
        // string, which is not a path any repository has.
        repositoryRoot ??= string.Empty;

        // Ask for the words before looking in the cache. That re-reads the repository's list if it has
        // changed on disk since it was last read, and a change drops any checker built from the old
        // list — which is what lets an updated list take effect without restarting the app.
        var words = _customDictionaryService.WordsFor(repositoryRoot);

        lock (_spellCheckerLock)
        {
            if (_spellCheckers.TryGetValue(repositoryRoot, out var existing)
                && (wanted is null || LanguagesMatch(wanted, existing.Languages)))
            {
                return existing.Checker;
            }
        }

        // Built outside the lock: loading the language dictionaries from disk is slow.
        Info("StyleCheckingService",
            $"Building spell checker for {repositoryRoot} with {words.Count} accepted word(s) from " +
            _customDictionaryService.PathFor(repositoryRoot));
        var checker = CreateSpellChecker(wanted, words);

        lock (_spellCheckerLock)
        {
            _spellCheckers[repositoryRoot] = (checker, wanted);
            return checker;
        }
    }

    /// <summary>
    /// Creates a SpellChecker through the same factory the CLI and MCP use.
    ///
    /// <para>This used to be a second implementation of the same construction, and the two had drifted:
    /// an empty language list meant "no dictionaries at all" here — every word misspelled, no
    /// suggestions — while the factory read it as the setting documents it, "all bundled
    /// dictionaries". Deselecting every language in the settings therefore produced a different answer
    /// in the app from the one CI gave for the same library.</para>
    /// </summary>
    private SpellChecker CreateSpellChecker(List<string>? languages, IEnumerable<string>? customWords) =>
        SpellCheckerFactory.Build(
            languages, customWords?.ToList() ?? [], _dictionaryManagerService);

    private static bool LanguagesMatch(List<string>? a, List<string>? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.Count != b.Count) return false;
        return a.SequenceEqual(b, StringComparer.OrdinalIgnoreCase);
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
        EnsureTrimmedForChecking(ModelIdsFor(repository));

        //Create worker for this repository
        int queuedModels = 0;
        var spellChecker = GetSpellCheckerIfNeeded(repository);
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
        _ = StartGraphAnalyses([repository], removeStaleFirst: false);
        BeginRun();
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
        EnsureTrimmedForChecking(ModelIdsFor(repository));

        //Create worker for this repository
        int queuedModels = 0;
        var spellChecker = GetSpellCheckerIfNeeded(repository);
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
        _ = StartGraphAnalyses([repository], removeStaleFirst: false);
        BeginRun();
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
            EnsureTrimmedForChecking(ModelIdsFor(repository));

            var spellChecker = GetSpellCheckerIfNeeded(repository);
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
            // Whole-graph analyses run alongside the per-model workers and deliver through the same
            // violation buffer. Registered before the flush loop starts so completion waits for them.
            _ = StartGraphAnalyses(repositories, removeStaleFirst: false);
            BeginRun();
            _ = Task.Run(() => ProcessQueueAsync());
        }
        else
        {
            // Every repository was skipped, so there is nothing to wait for: CancelExistingWorkers
            // above already released the previous run's signal.
            OnProgressChanged?.Invoke(true);
        }
    }

    // The rule ids owned by the whole-graph analyzers. Used to clear stale graph findings before an
    // incremental re-run (they can attach to any model in the repo, not just the ones edited).
    private static readonly HashSet<string> GraphRuleIds =
        GraphAnalysisRunner.BuiltIn.SelectMany(a => a.RuleIds).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Runs the whole-graph analyses (package.order, uses hygiene, unused classes/members, shadowing)
    /// per repository and buffers their findings for delivery. Dependency-requiring analyzers only
    /// produce findings once dependency analysis has populated the graph edges (detected here);
    /// package.order needs none. When <paramref name="removeStaleFirst"/> is set (incremental re-check),
    /// prior graph findings for each repository are cleared before fresh ones are emitted, because a
    /// graph finding can sit on a model the edit did not touch (e.g. a package.order finding on the
    /// package node) and so would not be removed by the caller's per-model cleanup.
    /// </summary>
    /// <inheritdoc/>
    public Task RunGraphAnalysesForRepositoriesAsync(IReadOnlyList<Repository> repositories)
        => StartGraphAnalyses(repositories, removeStaleFirst: true);

    /// <summary>
    /// Brings the packages about to be checked into the trimmed representation every checking surface
    /// uses (the CLI does this in <c>CheckPipeline</c>, MCP in <c>StyleTools</c>).
    ///
    /// Startup trims once, but a file reload — the Refresh button, a VCS operation, a revert, saving
    /// an edit — replaces the node with the full source read back from disk. Without re-trimming here,
    /// those paths checked a different representation from startup and the CLI and reported a
    /// different number of findings. Already-trimmed packages are skipped, so this is cheap to repeat.
    /// </summary>
    private void EnsureTrimmedForChecking(IReadOnlySet<string>? modelIds = null)
    {
        try
        {
            PackageCodeTrimmer.TrimStandaloneChildren(_libraryDataService.CombinedGraph, modelIds);
        }
        catch (Exception ex)
        {
            // Trimming is a representation tidy-up; failing it must not stop the check running.
            Error("StyleCheckingService", "Trimming package source before checking failed", ex);
        }
    }

    /// <summary>
    /// Starts a graph-analysis pass in the background and registers it with the completion signal.
    /// Every entry point that starts checking goes through here, so a single-repository check
    /// (Apply in repository settings, adding a repository) produces the same rules as a
    /// whole-project check — previously only the multi-repository path ran the graph analyzers,
    /// which is why the two reported different totals for the same library and settings.
    /// </summary>
    /// <returns>The running pass, so a caller that must not proceed without its findings can await
    /// it. Background callers ignore it.</returns>
    private Task StartGraphAnalyses(IReadOnlyList<Repository> repositories, bool removeStaleFirst)
    {
        Interlocked.Increment(ref _graphAnalysesRunning);
        return Task.Run(async () =>
        {
            try
            {
                await RunGraphAnalysesAsync(repositories, removeStaleFirst);
            }
            finally
            {
                Interlocked.Decrement(ref _graphAnalysesRunning);
            }
        });
    }

    /// <summary>
    /// Ensures the dependency edges the graph analyzers need are present, then runs the analyses.
    ///
    /// The ordering is the point. <c>UnusedClassAnalyzer</c> and <c>UsesHygieneAnalyzer</c> are
    /// skipped outright when the edges are missing, so running this concurrently with dependency
    /// analysis — as the startup path used to — made the finding count depend on which task won a
    /// race. Awaiting <see cref="ILibraryDataService.EnsureDependenciesAnalyzedAsync"/> both fixes
    /// the ordering and covers the paths that never ran dependency analysis at all.
    /// </summary>
    private async Task RunGraphAnalysesAsync(IReadOnlyList<Repository> repositories, bool removeStaleFirst)
    {
        var generation = Volatile.Read(ref _runGeneration);

        if (repositories.Any(r => r.StyleSettings is not null
                && GraphAnalysisRunner.RequiresDependencyAnalysis(r.StyleSettings)))
        {
            try
            {
                await _libraryDataService.EnsureDependenciesAnalyzedAsync(
                    msg => LogProcessStart("StyleCheckingService", msg));
            }
            catch (Exception ex)
            {
                // Fall through and run the analyzers that don't need edges rather than losing them all.
                Error("StyleCheckingService", "Dependency analysis for graph analyses failed", ex);
            }
        }

        // A newer run superseded this one while we waited — its own pass will deliver the findings.
        if (Volatile.Read(ref _runGeneration) != generation)
            return;

        RunGraphAnalyses(repositories, removeStaleFirst);
    }

    private void RunGraphAnalyses(IReadOnlyList<Repository> repositories, bool removeStaleFirst = false)
    {
        try
        {
            var graph = _libraryDataService.CombinedGraph;
            var emitted = false;

            foreach (var repository in repositories)
            {
                var settings = repository.StyleSettings;
                if (settings is null)
                    continue;
                // Nothing to do unless a graph rule is enabled for this repository.
                if (!GraphAnalysisRunner.BuiltIn.Any(a => a.RuleIds.Any(settings.IsRuleEnabled)))
                    continue;

                var models = _libraryDataService.Libraries
                    .Where(l => l.RepositoryId == repository.Id)
                    .SelectMany(l => l.ModelIds)
                    .Select(id => graph.GetNode<ModelNode>(id))
                    .Where(m => m is not null && !m.IsParseFailurePlaceholder)
                    .Cast<ModelNode>()
                    .ToList();
                if (models.Count == 0)
                    continue;

                if (removeStaleFirst)
                {
                    var repoModelIds = models.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
                    _codeReviewService.RemoveLogMessagesByPredicate(m =>
                        m.RuleId is not null && GraphRuleIds.Contains(m.RuleId) && repoModelIds.Contains(m.ModelName));
                }

                // Whether the edges exist is answered by the graph itself (DirectedGraph.DependenciesAnalyzed),
                // not inferred from whether some model happens to have edges — that inference read true for a
                // partly-built graph and changed the finding count between runs.
                var context = new GraphAnalysisContext(graph, settings, models);
                var findings = GraphAnalysisRunner.Run(context);
                if (findings.Count > 0)
                {
                    ViolationsFound(this, findings.Select(f => f.ToLogMessage()).ToList());
                    emitted = true;
                }
            }

            if (emitted)
                FlushPendingViolations();
        }
        catch (Exception ex)
        {
            Error("StyleCheckingService", "Graph analyses failed", ex);
        }
    }

    /// <summary>
    /// Returns the spell checker if spell checking is enabled in the given settings,
    /// ensuring it is created if needed with the correct language dictionaries.
    /// Public so the deferred combined dependency+style pass can build the same spell-checking
    /// context the background workers use, keeping violation counts consistent between the two paths.
    /// </summary>
    public SpellChecker? GetSpellCheckerIfNeeded(Repository repository)
    {
        var settings = repository.StyleSettings;
        if (settings is null || !(settings.SpellCheckDescription || settings.SpellCheckDocumentation))
            return null;

        // Keyed on the working copy, which is where .mlqt/dictionary.txt lives.
        return EnsureSpellChecker(repository.LocalPath, settings.SpellCheckLanguages);
    }

    /// <summary>The ids of every model in the repository's libraries.</summary>
    private IReadOnlySet<string> ModelIdsFor(Repository repository) =>
        _libraryDataService.Libraries
            .Where(l => l.RepositoryId == repository.Id)
            .SelectMany(l => l.ModelIds)
            .ToHashSet(StringComparer.Ordinal);

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

        EnsureTrimmedForChecking(modelIdList.ToHashSet(StringComparer.Ordinal));

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

            // No repository means no word list to apply — the classes are not in one, so there is
            // nowhere their accepted spellings could have been stored.
            var spellChecker = repo is not null ? GetSpellCheckerIfNeeded(repo) : null;
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

        // Whole-graph analyses are repository-wide, not per-model, so re-run them for every repository
        // touched by this incremental check — otherwise their findings (package.order, uses hygiene,
        // unused, shadowing) would reflect the pre-edit graph until the next full load. Stale graph
        // findings are cleared per repo inside the run. Fire-and-forget, alongside the per-model workers.
        var affectedRepos = modelsByRepo.Keys
            .Select(id => _repositoryService.Repositories.FirstOrDefault(r => r.Id == id))
            .Where(r => r is not null)
            .Cast<Repository>()
            .ToList();
        if (affectedRepos.Count > 0)
            _ = StartGraphAnalyses(affectedRepos, removeStaleFirst: true);

        LogProcessStart("StyleCheckingService", $"Style checking {modelIdList.Count} model(s)");
        OnProgressChanged?.Invoke(false);

        // Start the flush loop (fire-and-forget). Must NOT be awaited because
        // FlushPendingViolations fires OnViolationsFound which calls InvokeAsync
        // to marshal to the render thread — awaiting would deadlock if the caller
        // is already on the render thread.
        BeginRun();
        _ = Task.Run(ProcessQueueAsync);
    }

    private async Task ProcessQueueAsync()
    {
        if (_isRunning)
            return;
        _isRunning = true;

        try
        {
            // Keep flushing while there are active workers, in-flight graph analyses, or pending
            // violations. Graph analyses are included so completion is not signalled before their
            // findings land — otherwise the total the UI shows on completion grows afterwards.
            while (!_stopRequested)
            {
                bool hasWorkers;
                lock (_workerLock)
                {
                    hasWorkers = _workers.Count > 0;
                }

                if (!hasWorkers && Volatile.Read(ref _graphAnalysesRunning) == 0)
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
            EndRun();
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
