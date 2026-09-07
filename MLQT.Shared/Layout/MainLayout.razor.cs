using MLQT.Shared.Pages;
using MLQT.Shared.Theming;
using DymolaInterface;
using OpenModelicaInterface;
using System.IO;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime;
using static MLQT.Services.LoggingService;
using RevisionControl;

namespace MLQT.Shared.Layout;

public partial class MainLayout : IDisposable
{
    [Inject] private AppState NavState { get; set; } = null!;
    [Inject] private ILibraryDataService LibraryDataService { get; set; } = null!;
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;
    [Inject] private IFormattingPipeline FormattingPipeline { get; set; } = null!;
    [Inject] private IFilePickerService FilePickerService { get; set; } = null!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
    [Inject] private ICodeReviewService CodeReviewService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISettingsService SettingsService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStyleCheckingService StyleCheckingService { get; set; } = null!;
    [Inject] private IFileMonitoringService FileMonitoringService { get; set; } = null!;
    [Inject] private IExternalResourceService ExternalResourceService { get; set; } = null!;
    [Inject] private IPowerManagementService PowerManagementService { get; set; } = null!;

    private List<TreeItemData<ModelNode>> TreeItems { get; set; } = new();
    private AppSettings _settings = new();
    private int _pendingChangesCount = 0;
    private bool _isRefreshing = false;
    private bool _startupProcessRunning = false;
    // Set when one or more repositories failed to load, so the startup dialog offers a "Continue to
    // app" escape hatch (the user needs to reach Settings to fix/remove the offending repository).
    private bool _startupHadLoadWarning = false;
    private bool _fullFormatRunning = false;
    private string _fullFormatStatusMessage = "Applying code formatting rules, please wait...";
    private static readonly DialogOptions _nonClosableDialogOptions = new() { CloseOnEscapeKey = false, BackdropClick = false, MaxWidth = MaxWidth.Small, FullWidth = true };
    private bool _styleCheckRunning = false;
    private string _styleCheckStatusMessage = "Running style checking rules on all classes";
    private bool _step1running = false;
    private bool _step2running = false;
    private bool _step3running = false;
    private bool _step4running = false;
    private bool _step5running = false;
    private bool _step6running = false;
    private Color _step1color = Color.Info;
    private Color _step2color = Color.Info;
    private Color _step3color = Color.Info;
    private Color _step4color = Color.Info;
    private Color _step5color = Color.Info;
    private Color _step6color = Color.Info;
    private bool _step3deferred = false;
    private bool _step4deferred = false;
    private bool _step5deferred = false;
    private bool _runningDeferredStep = false;
    private bool _showStyleCheckingCompleteMessage = false;
    /// <summary>Tracks files that have been formatted, keyed by path with the file's LastWriteTimeUtc at format time.</summary>
    private bool _isDarkMode = false;
    private MudTheme _myTheme = MlqtTheme.BuildTheme(MlqtTheme.GetDefaultPaletteLight());
    private string? _currentProjectName = null;
    private bool _showAboutDialog = false;
    private string version = GetAppVersion();


    private static string GetAppVersion()
    {
        var attr = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (attr?.InformationalVersion is { } ver)
        {
            // Strip the "+commithash" suffix if present
            var plusIndex = ver.IndexOf('+');
            return plusIndex > 0 ? ver[..plusIndex] : ver;
        }
        return "dev";
    }

    private async Task OpenGitHub()
    {
        await JSRuntime.InvokeVoidAsync("open", "https://github.com/mdempse1/MLQT", "_blank");
    }

    private async Task OpenDocumentation()
    {
        await JSRuntime.InvokeVoidAsync("open", "https://github.com/mdempse1/MLQT/blob/main/Documentation/getting-started.md", "_blank");
    }

    protected override async Task OnInitializedAsync()
    {
        // Logging is initialised by AddMlqtCore, which every host calls, rather than here - a layout
        // component is not the right owner for it, and a route that does not render this one had none.
        Info("MainLayout", "Application starting");

        _settings.UI = await SettingsService.GetAsync("UI", new UISettings());
        ApplyThemeFromSettings(_settings.UI);

        NavState.OnRepositorySettingsApplied += OnRepositorySettingsApplied;
        NavState.OnVcsFilesChanged += OnVcsFilesChanged;
        NavState.OnVcsModelsChanged += OnVcsModelsChanged;
        NavState.OnProjectSwitchStarting += OnProjectSwitchStarting;
        RepositoryService.OnProjectChanged += OnProjectChanged;
        NavState.OnThemeChanged += OnThemeChangedHandler;
        NavState.OnRunDeferredDependencies += RunDeferredDependenciesOnlyAsync;
        NavState.OnRunDeferredStyleChecking += RunDeferredStyleCheckingFromEventAsync;
        NavState.OnRunDeferredExternalResources += RunDeferredExternalResourcesAsync;
        NavState.OnRunAllDeferredAnalysis += RunAllDeferredAnalysisAsync;
        NavState.OnFormatChangedFilesForCommit += FormatChangedFilesForCommitAsync;

        RepositoryService.OnRepositoriesChanged += OnRepositoriesChanged;
        FileMonitoringService.OnPendingChangesUpdated += OnPendingChangesUpdated;
        StyleCheckingService.OnProgressChanged += OnStyleCheckingProgressChanged;
        // Persist background style-checking findings here, in the always-mounted layout, rather than
        // in the CodeReview page — otherwise findings are dropped whenever checking runs while the
        // user is on another tab (e.g. Metrics auto-triggering it).
        StyleCheckingService.OnFindingsFound += OnStyleFindingsFound;
        StyleCheckingService.OnSpellCheckWarning += OnSpellCheckWarning;

        _ = Task.Run(async () => await RunStartUpAsync());

        await base.OnInitializedAsync();
    }


    private async Task RunStartUpAsync()
    {
        LogProcessStart("MainLayout", "Application startup sequence");
        try
        {
            // Configure snackbar position
            Snackbar.Configuration.PositionClass = Defaults.Classes.Position.BottomRight;

            Thread.Sleep(200);

            // Step 0: Check if we need to show project selection dialog
            var savedSettings = await SettingsService.GetAsync("Repositories", new RepositorySettingsCollection());
            var projectCount = savedSettings.Projects.Count;
            string? selectedProjectId = null;
            // If legacy repos exist but no projects, migration will create 1 project — no dialog needed
            if (projectCount == 0 && savedSettings.Repositories.Count > 0)
                projectCount = 1;

            string? newProjectName = null;
            if (projectCount > 1)
            {
                await InvokeAsync(async () =>
                {
                    var options = new DialogOptions
                    {
                        CloseOnEscapeKey = false,
                        BackdropClick = false,
                        MaxWidth = MaxWidth.Small,
                        FullWidth = true
                    };
                    var dialog = await DialogService.ShowAsync<ProjectSelectionDialog>("Select Project", options);
                    var result = await dialog.Result;
                    if (result != null && !result.Canceled && result.Data is string id)
                    {
                        // Check if this is a new project name (not an existing project ID)
                        if (!savedSettings.Projects.Any(p => p.Id == id))
                            newProjectName = id;
                        else
                            selectedProjectId = id;
                    }
                });
            }

            // If the user requested a new project, load settings first (so existing projects
            // are in memory), then create the new project and use its real ID.
            if (newProjectName != null)
            {
                await RepositoryService.LoadRepositorySettingsAsync();
                var newProject = RepositoryService.CreateProject(newProjectName);
                selectedProjectId = newProject.Id;
            }

            // Check if the selected project has repositories before showing the startup dialog.
            // If it has none, load settings silently (handles migration etc.) and skip the dialog.
            {
                var checkId = selectedProjectId ?? savedSettings.ActiveProjectId ?? savedSettings.Projects.FirstOrDefault()?.Id;
                var checkProject = savedSettings.Projects.FirstOrDefault(p => p.Id == checkId)
                                   ?? savedSettings.Projects.FirstOrDefault();
                bool hasLegacyRepos = savedSettings.Projects.Count == 0 && savedSettings.Repositories.Count > 0;
                if (!hasLegacyRepos && (checkProject == null || checkProject.Repositories.Count == 0))
                {
                    await RepositoryService.LoadRepositorySettingsAsync(selectedProjectId);
                    _currentProjectName = RepositoryService.GetActiveProject()?.Name;
                    await InvokeAsync(StateHasChanged);
                    return;
                }
            }

            // Prevent system sleep during long-running startup analysis
            PowerManagementService.PreventSleep();

            // Show startup progress dialog now that project selection is done
            _startupProcessRunning = true;
            _startupHadLoadWarning = false;
            _step1running = true;
            _step1color = Color.Success;
            await InvokeAsync(StateHasChanged);

            // Step 1: Load saved repositories and libraries
            //
            // The tree is told once, at the end. Every library announces itself as it lands, and every
            // announcement costs each open library tree a working-copy status query and a full
            // rebuild — all of it queued on the UI thread. A project holding a tool's library folder
            // announces a hundred times, and the queue that builds up is then in front of everything
            // else the startup wants to do: the two minutes this used to spend were spent waiting for
            // that queue to drain, not doing the work each step was named for.
            var treeNotifications = LibraryDataService.SuppressTreeDataChanged();
            try
            {
                LogProcessStart("MainLayout", "Loading repositories and libraries");
                await RepositoryService.LoadRepositorySettingsAsync(selectedProjectId);
                var project = RepositoryService.GetActiveProject();
                _currentProjectName = project!.Name;
                LogProcessEnd("MainLayout", "Loading repositories and libraries");

                // Reference libraries fill in what the user's own code refers to but does not contain,
                // so they go in after it and before anything analyses it.
                //
                // The order matters: a tool's library folder ships encrypted builds of libraries the
                // user may have checked out as source, and the source copy must win. Loading
                // references first meant the encrypted build got there first, and for a nested class —
                // which, like a stub, cannot be stored standalone — the graph had no rule that
                // preferred the real source.
                await LoadReferenceLibrariesAsync();
            }
            finally
            {
                treeNotifications.Dispose();   // the one announcement, now the tree is settled
            }

            // Surface any repositories that could not be loaded (e.g. their path no longer exists) as a
            // notification, and reveal the "Continue to app" escape hatch immediately so the user isn't
            // trapped behind the modal while the rest of the (possibly long) startup continues.
            var loadWarnings = RepositoryService.LastLoadWarnings;
            if (loadWarnings.Count > 0)
            {
                _startupHadLoadWarning = true;
                foreach (var warning in loadWarnings)
                    await InvokeAsync(() => Snackbar.Add(warning, Severity.Warning));
                await InvokeAsync(StateHasChanged);
            }

            // Compact the LOH after loading — ExtractModels creates a full ANTLR parse tree
            // per file (all files in parallel) which fragments the LOH significantly.
            // Also trim package ModelicaCode to remove duplicated standalone children source.
            var totalModelCount = LibraryDataService.Libraries.Sum(l => l.ModelIds.Count);
            if (totalModelCount > 0)
            {
                LogProcessStart("MainLayout", "Trimming package source");
                TrimPackageModelicaCode();
                LogProcessEnd("MainLayout", "Trimming package source");

                LogProcessStart("MainLayout", "Compacting the heap");
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                LogProcessEnd("MainLayout", "Compacting the heap");
                Info("MainLayout", $"Memory after loading + trim + GC: {GC.GetTotalMemory(false) / (1024 * 1024)} MB ({totalModelCount} models)");
            }

            // Parser errors are recorded on the nodes during load, so report them as soon as loading
            // finishes rather than waiting for analysis — a syntax error is the one thing the user
            // needs to know about before anything downstream reads the file.
            //
            // Each step logged separately: this stretch of startup used to pass in silence, so time
            // spent in it — three minutes of it, on a large project — could not be attributed to
            // anything. A step that takes no time costs one log line to say so.
            LogProcessStart("MainLayout", "Reporting parser errors");
            SurfaceParserErrors();
            await NotifyParserErrorsIfAnyAsync();
            LogProcessEnd("MainLayout", "Reporting parser errors");

            _step1running = false;

            // Step 2: Format only VCS-modified files (fast — assumes repo is already formatted)
            _step2running = true;
            _step2color = Color.Success;
            LogProcessStart("MainLayout", "Rendering after load");
            await InvokeAsync(StateHasChanged);
            LogProcessEnd("MainLayout", "Rendering after load");

            LogProcessStart("MainLayout", "Formatting modified files");
            await FormatModifiedFilesAsync();
            LogProcessEnd("MainLayout", "Formatting modified files");

            _step2running = false;
            await InvokeAsync(StateHasChanged);

            // Only run analysis when there are repos/models. For empty projects this block
            // is skipped entirely, leaving _step4running=false so the close check below fires.
            if (totalModelCount > 0 || RepositoryService.Repositories.Count > 0)
            {
                // Check if we should defer analysis steps based on model count
                var shouldDefer = totalModelCount > _settings.UI.DeferAnalysisThreshold;
                if (shouldDefer)
                {
                    NavState.EnableDeferredMode();
                    LogProcessStart("MainLayout", $"Deferred mode enabled ({totalModelCount} models > {_settings.UI.DeferAnalysisThreshold} threshold)");
                    _step3deferred = true;
                    _step4deferred = true;
                    _step5deferred = true;
                    _step3color = Color.Default;
                    _step4color = Color.Default;
                    _step5color = Color.Default;
                }
                else
                {
                    _step3running = true;
                    _step3color = Color.Success;
                    _step4running = true;
                    _step4color = Color.Success;
                }
                await InvokeAsync(StateHasChanged);

                if (!shouldDefer)
                {
                    // Steps 3 & 4: Analyze dependencies and start style checking in parallel
                    LogProcessStart("MainLayout", "Analyzing dependencies and starting style checking");

                    // Style checking's graph analyses need these edges, and join this same run rather
                    // than starting a competing one — see ILibraryDataService.EnsureDependenciesAnalyzedAsync.
                    var dependencyTask = LibraryDataService.EnsureDependenciesAnalyzedAsync(
                        progressLog: msg => LogProcessStart("GraphBuilder", msg));

                    StyleCheckingService.StartBackgroundCheckingForRepositories(RepositoryService.Repositories);
                    if (RepositoryService.Repositories.Count == 0)
                        _step4running = false;

                    await dependencyTask;
                    // Record it: the deferred pipeline was the only thing setting this, so on a library
                    // below the defer threshold the app behaved all session as if dependencies had never
                    // been analysed — which also kept the Metrics tab from ever considering the
                    // analysis complete.
                    NavState.DependencyAnalysisCompleted();
                    LogProcessEnd("MainLayout", "Analyzing dependencies");

                    // Compact the LOH after dependency analysis released parse trees
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    Info("MainLayout", $"Memory after dependency analysis + GC: {GC.GetTotalMemory(false) / (1024 * 1024)} MB");

                    _step3running = false;
                    _step5running = true;
                    _step5color = Color.Success;
                    await InvokeAsync(StateHasChanged);

                    // Step 5: Analyze external resource references and start monitoring
                    LogProcessStart("MainLayout", "Analyzing external resources");
                    await ExternalResourceService.AnalyzeResourcesAsync(LibraryDataService.CombinedGraph);
                    ExternalResourceService.StartMonitoringResources();
                    LogProcessEnd("MainLayout", "Analyzing external resources");
                    _step5running = false;
                }

                // Step 6: Start file monitoring
                _step6running = true;
                _step6color = Color.Success;
                await InvokeAsync(StateHasChanged);

                LogProcessEnd("MainLayout", "Application startup sequence (style checking continues in background)");
                RepositoryService.StartMonitoringAllRepositories();
                _step6running = false;
                await InvokeAsync(StateHasChanged);

                LogProcessEnd("MainLayout", "Application startup sequence");
                await Task.Delay(2000).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogProcessFailed("MainLayout", "Application startup sequence", ex);
            await InvokeAsync(() => Snackbar.Add($"Startup error: {ex.Message}", Severity.Error));
        }
        finally
        {
            // Re-enable system sleep now that startup analysis is complete
            // (style checking may still be running but it's not CPU-intensive enough to warrant blocking sleep)
            PowerManagementService.AllowSleep();
        }
        // In deferred mode, keep the dialog open to show deferred steps with Run Now buttons.
        // In normal mode, close when style checking is done (or immediately if it already finished).
        if (NavState.IsDeferredMode)
        {
            // Dialog stays open — user can run deferred steps or close manually
            await InvokeAsync(StateHasChanged);
        }
        else if (!_step4running)
        {
            _startupProcessRunning = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// A spell-check language the settings ask for that this machine has no dictionary for. Shown
    /// rather than logged only: the results are not the ones the settings describe, and a user
    /// comparing them with a colleague's — or with CI, which has always warned about this — would
    /// otherwise have nothing to go on.
    /// </summary>
    private async void OnSpellCheckWarning(string message)
    {
        await InvokeAsync(() => Snackbar.Add($"Spell check: {message}", Severity.Warning));
    }

    private async void OnStyleCheckingProgressChanged(bool allComplete) {
        if (allComplete)
        {
            _step4running = false;
            _styleCheckRunning = false;
            //Check to see if there are pending deferred steps
            if (_step3deferred || _step5deferred) {
                _runningDeferredStep = false;
            }
            else
            {
                //Check to see if this was the last step still running
                if (!_step3running && !_step5running && !_step6running)
                {
                    Thread.Sleep(2000);
                    _startupProcessRunning = false;                
                }
                if (!_startupProcessRunning && _showStyleCheckingCompleteMessage)
                {
                    _showStyleCheckingCompleteMessage = false;
                    await InvokeAsync(() => Snackbar.Add("Style checking complete.", Severity.Success));
                }
            }
            await InvokeAsync(StateHasChanged);
        }
    }

    private async void OnProjectSwitchStarting()
    {
        // Show progress dialog immediately with step 1 active (loading repos/libraries)
        // Set state synchronously BEFORE InvokeAsync to avoid a race with OnProjectChanged:
        // If InvokeAsync is delayed (e.g., by SwitchProjectAsync completing quickly for an
        // empty project), OnProjectChanged could finish and set _startupProcessRunning = false
        // before this InvokeAsync runs, causing the dialog to reopen with stale state.
        ResetStartupSteps();
        _startupProcessRunning = true;
        _step1running = true;
        _step1color = Color.Success;
        await InvokeAsync(StateHasChanged);
    }

    private async void OnProjectChanged(string projectId)
    {
        PowerManagementService.PreventSleep();
        try
        {
            LogProcessStart("MainLayout", $"Project changed to: {projectId}");

            // Clear style checking findings from the previous project
            CodeReviewService.ClearLogMessages();

            // Step 1 (loading repos/libraries) is now complete — move to step 2
            // If OnProjectSwitchStarting wasn't called, ensure dialog is visible
            if (!_startupProcessRunning)
            {
                ResetStartupSteps();
            }
            _step1running = false;
            _step1color = Color.Success;

            // Update the project name in the title bar
            var project = RepositoryService.GetActiveProject();
            _currentProjectName = project?.Name;

            // Compact the LOH + trim packages after loading
            var totalModelCount = LibraryDataService.Libraries.Sum(l => l.ModelIds.Count);
            if (totalModelCount > 0)
            {
                LogProcessStart("MainLayout", "Trimming package source");
                TrimPackageModelicaCode();
                LogProcessEnd("MainLayout", "Trimming package source");

                LogProcessStart("MainLayout", "Compacting the heap");
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                LogProcessEnd("MainLayout", "Compacting the heap");
                Info("MainLayout", $"Memory after loading + trim + GC: {GC.GetTotalMemory(false) / (1024 * 1024)} MB ({totalModelCount} models)");
            }

            // If no repositories and no models, close the startup dialog immediately.
            // Set synchronously before yielding so any pending renders from OnProjectSwitchStarting
            // will see the final false value and not show the dialog.
            if (totalModelCount == 0 && RepositoryService.Repositories.Count == 0)
            {
                _step1running = false;
                _step1color = Color.Success;
                _startupProcessRunning = false;
                await InvokeAsync(StateHasChanged);
                return;
            }

            // Parser errors are recorded on the nodes during load — report them before anything
            // downstream reads the file (see the equivalent call in the startup path).
            LogProcessStart("MainLayout", "Reporting parser errors");
            SurfaceParserErrors();
            await NotifyParserErrorsIfAnyAsync();
            LogProcessEnd("MainLayout", "Reporting parser errors");

            // Step 2: Format only VCS-modified files (fast — assumes repo is already formatted)
            _step2running = true;
            _step2color = Color.Success;
            LogProcessStart("MainLayout", "Rendering after load");
            await InvokeAsync(() =>
            {
                _startupProcessRunning = true;
                StateHasChanged();
            });
            LogProcessEnd("MainLayout", "Rendering after load");

            await FormatModifiedFilesAsync();
            _step2running = false;
            await InvokeAsync(StateHasChanged);

            // Only run analysis when there are repos/models. For empty projects this block
            // is skipped entirely, leaving _step4running=false so the close check below fires.
            if (totalModelCount > 0 || RepositoryService.Repositories.Count > 0)
            {
                // Check if we should defer analysis steps based on model count
                var shouldDefer = totalModelCount > _settings.UI.DeferAnalysisThreshold;
                if (shouldDefer)
                {
                    NavState.EnableDeferredMode();
                    LogProcessStart("MainLayout", $"Deferred mode enabled ({totalModelCount} models > {_settings.UI.DeferAnalysisThreshold} threshold)");
                    _step3deferred = true;
                    _step4deferred = true;
                    _step5deferred = true;
                    _step3color = Color.Default;
                    _step4color = Color.Default;
                    _step5color = Color.Default;
                }
                else
                {
                    _step3running = true;
                    _step3color = Color.Success;
                    _step4running = true;
                    _step4color = Color.Success;
                }
                await InvokeAsync(StateHasChanged);

                if (!shouldDefer)
                {
                    // Steps 3 & 4: Analyze dependencies and start style checking
                    // Style checking's graph analyses need these edges, and join this same run rather
                    // than starting a competing one — see ILibraryDataService.EnsureDependenciesAnalyzedAsync.
                    var dependencyTask = LibraryDataService.EnsureDependenciesAnalyzedAsync(
                        progressLog: msg => LogProcessStart("GraphBuilder", msg));

                    StyleCheckingService.StartBackgroundCheckingForRepositories(RepositoryService.Repositories);
                    if (RepositoryService.Repositories.Count == 0)
                        _step4running = false;

                    await dependencyTask;
                    NavState.DependencyAnalysisCompleted();   // see the startup path's note

                    // Compact the LOH after dependency analysis
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    Info("MainLayout", $"Memory after dependency analysis + GC: {GC.GetTotalMemory(false) / (1024 * 1024)} MB");

                    _step3running = false;
                    _step5running = true;
                    _step5color = Color.Success;
                    await InvokeAsync(StateHasChanged);

                    // Step 5: Analyze external resources
                    await ExternalResourceService.AnalyzeResourcesAsync(LibraryDataService.CombinedGraph);
                    ExternalResourceService.StartMonitoringResources();
                    _step5running = false;
                }

                // Step 6: Start file monitoring
                _step6running = true;
                _step6color = Color.Success;
                await InvokeAsync(StateHasChanged);
                RepositoryService.StartMonitoringAllRepositories();
                _step6running = false;
                await InvokeAsync(StateHasChanged);

                LogProcessEnd("MainLayout", $"Project changed to: {projectId}");
                await Task.Delay(2000).ConfigureAwait(false);
            }

            if (NavState.IsDeferredMode)
            {
                await InvokeAsync(StateHasChanged);
            }
            else if (!_step4running)
            {
                _startupProcessRunning = false;
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception ex)
        {
            _startupProcessRunning = false;
            await InvokeAsync(() =>
            {
                Snackbar.Add($"Project switch error: {ex.Message}", Severity.Error);
                StateHasChanged();
            });
            LogProcessFailed("MainLayout", "Project switch", ex);
        }
        finally
        {
            PowerManagementService.AllowSleep();
        }
    }

    /// <summary>
    /// Resets all startup step flags and colors to their initial state.
    /// </summary>
    private void ResetStartupSteps()
    {
        _step1running = false;
        _step2running = false;
        _step3running = false;
        _step4running = false;
        _step5running = false;
        _step6running = false;
        _step3deferred = false;
        _step4deferred = false;
        _step5deferred = false;
        _runningDeferredStep = false;
        _step1color = Color.Info;
        _step2color = Color.Info;
        _step3color = Color.Info;
        _step4color = Color.Info;
        _step5color = Color.Info;
        _step6color = Color.Info;
        _showStyleCheckingCompleteMessage = false;
        FormattingPipeline.ClearWrittenFileTimestamps();
        NavState.ResetDeferredState();
    }

    /// <summary>
    /// Loads the configured reference libraries read-only, so references out of the user's code
    /// resolve against them. Encrypted libraries are reconstructed from their vendor documentation;
    /// readable ones are loaded normally. Neither is ever reported on or written to.
    ///
    /// <para>Failures here are reported and stepped over rather than allowed to stop startup: a
    /// reference library is an aid to resolution, and a missing or unreadable one should degrade
    /// the check, not prevent the application from opening the user's own work.</para>
    /// </summary>
    private async Task LoadReferenceLibrariesAsync()
    {
        var settings = await SettingsService.GetAsync("ReferenceLibraries", new ReferenceLibrarySettings());
        if (settings.Paths.Count == 0)
            return;

        LogProcessStart("MainLayout", "Loading reference libraries");
        var loaded = 0;
        var classes = 0;

        foreach (var configuredPath in settings.Paths)
        {
            if (string.IsNullOrWhiteSpace(configuredPath) || !Directory.Exists(configuredPath))
            {
                Warn("MainLayout", $"Reference library path not found: {configuredPath}");
                await InvokeAsync(() => Snackbar.Add(
                    $"Reference library path not found: {configuredPath}", Severity.Warning));
                continue;
            }

            foreach (var libraryPath in LibraryDiscovery.DiscoverLibraryPaths(configuredPath))
            {
                try
                {
                    // Whether to load an encrypted library at all is a policy question the user
                    // answers in settings; how to load one is not this loop's business.
                    if (!settings.UseEncryptedLibraryDocumentation &&
                        EncryptedLibraryDetector.IsEncryptedLibraryRoot(libraryPath))
                        continue;

                    // A tool's library folder ships the encrypted build of libraries a user may also
                    // have checked out as source. The source copy is strictly better — it is the code
                    // being worked on — so the encrypted one is not loaded at all rather than loaded
                    // and then overridden class by class. Skipping it also avoids thousands of stub
                    // nodes that would be discarded anyway.
                    var encryptedName = EncryptedLibraryDetector.Detect(libraryPath)?.Name;
                    if (encryptedName is not null &&
                        LibraryDataService.Libraries.Any(l =>
                            l.SourceType != LibrarySourceType.EncryptedDirectory &&
                            string.Equals(l.Name, encryptedName, StringComparison.Ordinal)))
                    {
                        Info("MainLayout",
                            $"Skipping encrypted reference library '{encryptedName}' at {libraryPath}: " +
                            "already loaded from source");
                        continue;
                    }

                    var library = await LibraryDataService.AddLibraryFromPathAsync(libraryPath);

                    // The one place that knows this library is reference material. It has no
                    // repository to carry the fact and — if it is readable rather than encrypted —
                    // no stub nodes either, so without recording it here every consumer had to
                    // re-derive it and the Metrics tab could not.
                    library.IsReferenceOnly = true;

                    if (library.ModelIds.Count == 0)
                    {
                        // An encrypted library with no documentation contributes nothing. Removing it
                        // keeps it out of the tree rather than showing an empty node that suggests the
                        // library is loaded and simply has no classes.
                        LibraryDataService.RemoveLibrary(library.Id);
                        continue;
                    }

                    loaded++;
                    classes += library.ModelIds.Count;
                }
                catch (Exception ex)
                {
                    Warn("MainLayout", $"Failed to load reference library '{libraryPath}': {ex.Message}");
                }
            }
        }

        Info("MainLayout", $"Loaded {loaded} reference libraries ({classes} classes) for reference resolution");
        LogProcessEnd("MainLayout", "Loading reference libraries");
    }

    /// <summary>
    /// Saves all loaded libraries back to their source directories to apply code formatting rules.
    /// This uses the ModelicaSyntaxVisitor to reformat the code according to our standards.
    /// Libraries loaded from Git/SVN repositories are saved to their local checkout location.
    ///
    /// Uses a safe replacement strategy:
    /// 1. Collects all original file paths before saving
    /// 2. Saves new files (which may have different structure due to formatting rules)
    /// 3. Deletes orphaned files that are no longer part of the new structure
    /// 4. Updates FileNodes in the graph with new file paths
    /// </summary>
    private Task SaveAllLibrariesWithFormattingAsync(string? filterRepositoryId = null)
        => FormattingPipeline.SaveAllLibrariesWithFormattingAsync(
            filterRepositoryId,
            onLibraryFailed: (name, ex) =>
                _ = InvokeAsync(() => Snackbar.Add($"Failed to format {name}: {ex.Message}", Severity.Warning)));

    private static bool SkipReferenceOnly(Repository repository, string what)
    {
        if (!repository.IsReferenceOnly)
            return false;

        Debug("MainLayout", $"Skipping {what} for {repository.Name} — reference only");
        return true;
    }

    /// <summary>
    /// Formats only files that have been modified according to VCS status.
    /// This assumes the repository is already properly formatted and only files that
    /// have been modified (or are untracked) since the last commit need formatting.
    /// This is much faster than SaveAllLibrariesWithFormattingAsync for large repositories.
    /// </summary>
    private Task FormatModifiedFilesAsync() => FormattingPipeline.FormatModifiedFilesAsync();

    private HashSet<string> GetModifiedFilePathsFromVcs(Repository repository)
    {
        try
        {
            return VcsChangeResolver.FormattableModelicaFiles(
                repository.LocalPath,
                repository.VcsRootPath,
                RepositoryService.GetWorkingCopyChanges(repository.Id),
                File.Exists);
        }
        catch (Exception ex)
        {
            // Reading VCS status can fail for reasons that are not this pipeline's business — a
            // locked working copy, an svn client that is not there. Losing the targeted list only
            // costs precision: the caller falls back to re-analysing the whole repository.
            Warn("MainLayout", $"Could not read VCS status for repository {repository.Name}: {ex.Message}");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Cleans up empty directories that may have been left behind after deleting orphaned files.
    /// Only removes directories within the library source paths.
    /// Skips hidden directories (e.g., .svn, .git) to avoid corrupting version control metadata.
    /// </summary>
    private void CleanupEmptyDirectories(IReadOnlyList<LoadedLibrary> libraries)
    {
        foreach (var library in libraries)
            EmptyDirectoryCleaner.RemoveEmptyDirectories(library.SourcePath);
    }

    /// <summary>
    /// Trims package ModelicaCode to remove source code that's duplicated in standalone children.
    /// After loading, a package's ModelicaCode contains the full file content including all nested
    /// classes. Each standalone child also has its own ModelicaCode. This causes ~2× memory usage
    /// for all source code. This method re-renders each package excluding standalone children,
    /// replacing the bloated extracted source with the compact package shell.
    /// </summary>
    private void TrimPackageModelicaCode()
    {
        // Trim each package's inline standalone children from its stored source (frees duplicated
        // source for large libraries). Shared with the CLI/MCP so every path checks the same
        // representation; the helper keeps line numbers aligned with the file (no prepended within).
        ModelicaGraph.PackageCodeTrimmer.TrimStandaloneChildren(LibraryDataService.CombinedGraph);
    }

    /// <summary>
    /// Updates the graph after a formatting save to reflect the new file structure.
    /// When formatting is applied, classes may move from a single .mo file into
    /// per-class files within a directory hierarchy. This method:
    ///   1. Creates FileNodes for any new file paths
    ///   2. Removes the model from its old FileNode (edge + ContainedModelIds)
    ///   3. Adds the model to its new FileNode (edge + ContainingFileId)
    ///   4. Removes FileNodes that are now empty (original files that were restructured)
    /// </summary>
    private void UpdateFileNodesAfterSave(Dictionary<string, string> modelIdToFilePath)
    {
        foreach (var modelId in FileNodeReconciler.ReassignModels(LibraryDataService.CombinedGraph, modelIdToFilePath))
            Debug("MainLayout", $"Model {modelId} moved to {modelIdToFilePath[modelId]}");
    }

    // ========== Deferred Analysis Methods ==========

    /// <summary>
    /// Parameterless wrapper for event subscription (events can't bind to methods with optional params).
    /// </summary>
    private Task RunDeferredDependenciesOnlyAsync() => RunDeferredDependenciesAsync(combineStyleChecking: false);

    /// <summary>
    /// Runs deferred dependency analysis. Called via AppState event from pages or startup dialog.
    /// </summary>
    /// <param name="combineStyleChecking">When true, also runs style checking inline during
    /// dependency analysis to avoid re-parsing all models a second time.</param>
    private async Task RunDeferredDependenciesAsync(bool combineStyleChecking = false)
    {
        if (NavState.HasDependencyAnalysisRun) return;
        PowerManagementService.PreventSleep();
        try
        {
            LogProcessStart("MainLayout", combineStyleChecking
                ? "Running deferred dependency analysis + style checking (combined)"
                : "Running deferred dependency analysis");
            await InvokeAsync(() => Snackbar.Add(
                combineStyleChecking ? "Analysing dependencies and checking style..." : "Analysing dependencies...",
                Severity.Normal));

            var libraryInfos = GetLibraryInfos();

            // Build a per-model style checking callback when combining.
            // This runs style rules on each model while the parse tree is still available from
            // dependency analysis, avoiding a separate re-parse pass for style checking.
            Action<ModelNode>? postAnalysisAction = null;
            ConcurrentBag<LogMessage>? combinedFindings = null;

            if (combineStyleChecking && !NavState.HasStyleCheckingRun)
            {
                // Build model-to-settings lookup: each model belongs to a library with a repository
                var modelToSettings = BuildModelToStyleSettingsMap();
                combinedFindings = new ConcurrentBag<LogMessage>();

                // Clear existing findings before re-running
                var allModelIds = LibraryDataService.Libraries.SelectMany(l => l.ModelIds).ToList();
                if (allModelIds.Count > 0)
                {
                    CodeReviewService.RemoveLogMessagesForModels(allModelIds);
                    // The clear above takes the parser findings with it — put them back.
                    SurfaceParserErrors(allModelIds);
                }

                (postAnalysisAction, _) = CombinedStyleCheckPass.Build(
                    LibraryDataService.CombinedGraph,
                    RepositoryService.Repositories,
                    modelToSettings,
                    BuildModelToRepositoryMap(),
                    StyleCheckingService.GetSpellCheckerIfNeeded,
                    combinedFindings);
            }

            // A class that cannot be analysed, or whose style check throws, is reported rather than
            // dropped. Silently skipping it made this pass report fewer findings than the same check
            // run again afterwards, with nothing to say which classes had been left out.
            var analysisFailures = new ConcurrentBag<LogMessage>();
            await Task.Run(async () => await GraphBuilder.AnalyzeDependenciesAsync(
                LibraryDataService.CombinedGraph, libraryInfos,
                progressLog: msg => LogProcessStart("GraphBuilder", msg),
                postAnalysisAction: postAnalysisAction,
                onModelFailed: (model, ex) =>
                {
                    Error("MainLayout", $"Analysing {model.Id} failed", ex);
                    analysisFailures.Add(ModelicaParser.StyleRules.CheckFailure.Message(
                        model.Id, ex,
                        ModelicaParser.StyleRules.CheckFailure.Analysing,
                        alsoMissing: "its dependencies"));
                }));

            if (!analysisFailures.IsEmpty)
                CodeReviewService.AddLogMessages(analysisFailures.ToList());

            // Compact the LOH after dependency analysis released all parse trees
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            Info("MainLayout", $"Memory after dependency analysis + GC: {GC.GetTotalMemory(false) / (1024 * 1024)} MB");

            NavState.DependencyAnalysisCompleted();
            _step3deferred = false;
            _step3color = Color.Success;
            LogProcessEnd("MainLayout", "Running deferred dependency analysis");

            // If style checking was combined, report findings and mark as complete
            if (combineStyleChecking && combinedFindings != null)
            {
                var findingsList = combinedFindings.ToList();
                if (findingsList.Count > 0)
                    CodeReviewService.AddLogMessages(findingsList);

                // The combined pass only runs the per-model rules. Dependency analysis has just
                // finished, so run the whole-graph analyses (package.order, uses hygiene, unused
                // class/member, shadowing) now too, otherwise those findings would be missing entirely
                // from the deferred flow. Delivered via the service's finding pipeline.
                await StyleCheckingService.RunGraphAnalysesForRepositoriesAsync(RepositoryService.Repositories);

                NavState.StyleCheckingCompleted();
                _step4deferred = false;
                _step4color = Color.Success;
                _showStyleCheckingCompleteMessage = true;
                LogProcessEnd("MainLayout", "Running deferred style checking (combined with dependency analysis)");
            }

            await InvokeAsync(() =>
            {
                Snackbar.Add("Dependency analysis complete.", Severity.Success);
                StateHasChanged();
            });
        }
        finally
        {
            PowerManagementService.AllowSleep();
        }
    }

    /// <summary>
    /// Builds a mapping from model ID to the style checking settings for its repository.
    /// </summary>
    private Dictionary<string, StyleCheckingSettings> BuildModelToStyleSettingsMap()
        => ModelScope.ModelToStyleSettings(LibraryDataService.Libraries, RepositoryService.GetRepository);

    /// <summary>
    /// Which repository each model belongs to, so the combined pass can give each class its own
    /// repository's accepted spellings. Models outside a repository are absent — they have no word
    /// list, rather than someone else's.
    /// </summary>
    private Dictionary<string, string> BuildModelToRepositoryMap()
        => ModelScope.ModelToRepository(LibraryDataService.Libraries);

    // True if any loaded repository has a style rule enabled that requires dependency analysis
    // (e.g. unused-class, uses hygiene), so the graph analyses can produce their findings.
    private bool AnyEnabledRuleNeedsDependencies()
        => ModelScope.RequiresDependencyAnalysis(RepositoryService.Repositories);

    /// <summary>
    /// Entry point for the AppState event, which carries no arguments. Pages asking for a run have
    /// no dialog of their own, so this one raises the progress dialog.
    /// </summary>
    private Task RunDeferredStyleCheckingFromEventAsync() => RunDeferredStyleCheckingAsync();

    /// <summary>Runs the deferred style check, on request from a page or from the startup dialog.</summary>
    /// <param name="showOwnDialog">Whether to raise the standalone progress dialog. False when the
    /// startup dialog is driving this, because it already shows the step — and because raising a
    /// second modal there put an undismissable dialog over the one the user was working in, whose
    /// only route to closing was an event fired from a background thread mid-run.</param>
    private async Task RunDeferredStyleCheckingAsync(bool showOwnDialog = true)
    {
        if (NavState.HasStyleCheckingRun) return;
        PowerManagementService.PreventSleep();
        try
        {
            if (showOwnDialog) {
                _styleCheckStatusMessage = "Running style checking rules on all classes";
                _styleCheckRunning = true;
                await InvokeAsync(StateHasChanged);
            }
            LogProcessStart("MainLayout", "Running deferred style checking");
            await InvokeAsync(() => Snackbar.Add("Running style checking...", Severity.Normal));

            // Graph analyses (unused class, uses hygiene) need cross-model dependency edges. If a rule
            // that requires them is enabled and dependency analysis hasn't run, run it first — otherwise
            // those analysers are silently skipped here, making the GUI's finding set differ from the CLI
            // (which always runs dependency analysis when a graph rule needs it).
            if (!NavState.HasDependencyAnalysisRun && AnyEnabledRuleNeedsDependencies())
                await RunDeferredDependenciesAsync();

            // Clear any existing style checking findings before re-running
            // (matches the pattern used by OnRepositorySettingsApplied and OnVcsFilesChanged)
            var allModelIds = LibraryDataService.Libraries.SelectMany(l => l.ModelIds).ToList();
            if (allModelIds.Count > 0)
            {
                CodeReviewService.RemoveLogMessagesForModels(allModelIds);
                // The clear above takes the parser findings with it — put them back.
                SurfaceParserErrors(allModelIds);
            }

            // Offload to a background thread: StartBackgroundCheckingForRepositories performs
            // sync-over-async work (custom dictionary + settings load). When this method is
            // invoked directly on the Blazor UI thread (e.g. the Code Review "run style
            // checking" button rather than the startup dialog's Task.Run path), running it
            // inline would deadlock the renderer sync context.
            await Task.Run(() =>
                StyleCheckingService.StartBackgroundCheckingForRepositories(RepositoryService.Repositories));

            // The call above only queues the work. Wait for it to actually finish before reporting
            // the step complete and letting the dialog go: checking saturates the machine, so a user
            // handed the app back mid-run gets a UI that barely responds and a step that claims to
            // have finished.
            await StyleCheckingService.WaitForCompletionAsync();

            NavState.StyleCheckingCompleted();
            _step4deferred = false;
            _step4color = Color.Success;
            _showStyleCheckingCompleteMessage = true;
            LogProcessEnd("MainLayout", "Running deferred style checking");
            await InvokeAsync(StateHasChanged);
        }
        catch
        {
            // The progress dialog is modal and is only closed by the completion event. If the run
            // never got far enough to produce one, close it here rather than wedging the app.
            _styleCheckRunning = false;
            await InvokeAsync(StateHasChanged);
            throw;
        }
        finally
        {
            // The run is genuinely over by the time we get here, so close the modal rather than
            // leaving it to the completion event — which does not arrive when there was nothing to
            // check, and used to leave the dialog up with no way out.
            _styleCheckRunning = false;
            await InvokeAsync(StateHasChanged);
            PowerManagementService.AllowSleep();
        }
    }

    /// <summary>
    /// Runs deferred external resource analysis. Called via AppState event from pages or startup dialog.
    /// </summary>
    private async Task RunDeferredExternalResourcesAsync()
    {
        if (NavState.HasExternalResourcesAnalyzed) return;

        // External resource analysis reads resource edges from the graph, which are
        // only populated by dependency analysis. Ensure dependencies are analyzed first.
        if (!NavState.HasDependencyAnalysisRun)
        {
            await RunDeferredDependenciesAsync();
        }

        PowerManagementService.PreventSleep();
        try
        {
            LogProcessStart("MainLayout", "Running deferred external resource analysis");
            await InvokeAsync(() => Snackbar.Add("Analysing external resources...", Severity.Normal));

            await ExternalResourceService.AnalyzeResourcesAsync(LibraryDataService.CombinedGraph);
            ExternalResourceService.StartMonitoringResources();

            NavState.ExternalResourcesAnalysisCompleted();
            _step5deferred = false;
            _step5color = Color.Success;
            LogProcessEnd("MainLayout", "Running deferred external resource analysis");
            await InvokeAsync(() =>
            {
                Snackbar.Add("External resource analysis complete.", Severity.Success);
                StateHasChanged();
            });
        }
        finally
        {
            PowerManagementService.AllowSleep();
        }
    }

    /// <summary>
    /// Runs all deferred analysis steps in sequence.
    /// Combines dependency analysis and style checking into a single pass to avoid
    /// re-parsing all models a second time (saves ~40-80 seconds for 27K models).
    /// </summary>
    private async Task RunAllDeferredAnalysisAsync()
    {
        // Combine dependency analysis + style checking: each model is parsed once and
        // both analysis + style checking run before the parse tree is released.
        await RunDeferredDependenciesAsync(combineStyleChecking: true);
        // If style checking wasn't combined (e.g., already completed), run it separately
        await RunDeferredStyleCheckingAsync();
        await RunDeferredExternalResourcesAsync();
        NavState.DisableDeferredMode();
    }

    // ========== Startup Dialog Deferred Button Handlers ==========

    private async Task RunDeferredDependenciesFromDialogAsync()
    {
        _runningDeferredStep = true;
        _step3running = true;
        _step3deferred = false;
        _step3color = Color.Success;
        StateHasChanged();
        await Task.Run(() => RunDeferredDependenciesAsync());
        _step3running = false;
        _runningDeferredStep = false;
        //Check to see if the dialog should stay open
        if (!_step3deferred && !_step4deferred && !_step5deferred)
            _startupProcessRunning = false;
        StateHasChanged();
    }

    private async Task RunDeferredStyleCheckingFromDialogAsync()
    {
        _runningDeferredStep = true;
        _step4running = true;
        _step4deferred = false;
        _step4color = Color.Success;
        StateHasChanged();

        // Settled here rather than left to the completion event, which is what its two siblings
        // already do. Relying on that event alone meant the buttons stayed disabled and the step
        // kept spinning whenever it did not arrive — including when there was nothing to check,
        // which is the one case where the run finishes before the user has let go of the mouse.
        // The call below now waits for the run to finish, so this clears at the true end of it and
        // not, as it once did, the moment the work had been handed to the background workers.
        try
        {
            await Task.Run(() => RunDeferredStyleCheckingAsync(showOwnDialog: false));
        }
        finally
        {
            _step4running = false;
            _runningDeferredStep = false;
            _styleCheckRunning = false;
            if (!_step3deferred && !_step4deferred && !_step5deferred)
                _startupProcessRunning = false;
            StateHasChanged();
        }
    }

    private async Task RunDeferredExternalResourcesFromDialogAsync()
    {
        _runningDeferredStep = true;
        _step5running = true;
        _step5deferred = false;
        _step5color = Color.Success;
        StateHasChanged();
        await Task.Run(RunDeferredExternalResourcesAsync);
        _step5running = false;
        _runningDeferredStep = false;
        //Check to see if the dialog should stay open
        if (!_step3deferred && !_step4deferred && !_step5deferred)
            _startupProcessRunning = false;
        StateHasChanged();
    }

    private async Task RunAllDeferredFromDialogAsync()
    {
        _runningDeferredStep = true;
        StateHasChanged();

        // Combined dependency analysis + style checking: parse each model once for both
        _step3running = true;
        _step3deferred = false;
        _step3color = Color.Success;
        _step4running = true;
        _step4deferred = false;
        _step4color = Color.Success;
        StateHasChanged();
        await Task.Run(() => RunDeferredDependenciesAsync(combineStyleChecking: true));
        _step3running = false;
        _step4running = false;
        StateHasChanged();

        // If style checking wasn't combined (e.g., dependency analysis was already done), run separately
        await RunDeferredStyleCheckingFromDialogAsync();
        await RunDeferredExternalResourcesFromDialogAsync();
        NavState.DisableDeferredMode();
        _runningDeferredStep = false;
        _startupProcessRunning = false;
        StateHasChanged();
    }

    private void CloseStartupDialog()
    {
        _startupProcessRunning = false;
        _startupHadLoadWarning = false;
        _step3deferred = false;
        _step4deferred = false;
        _step5deferred = false;
        StateHasChanged();
    }

    /// <summary>
    /// Formats changed files in a repository before commit.
    /// Gets the working copy changes, identifies affected .mo files, pauses monitoring,
    /// applies formatting, then restarts monitoring.
    /// </summary>
    private async Task FormatChangedFilesForCommitAsync(string repositoryId)
    {
        var repository = RepositoryService.GetRepository(repositoryId);
        if (repository == null || SkipReferenceOnly(repository, "formatting")) return;

        var styleSettings = repository.StyleSettings ?? new StyleCheckingSettings();
        if (!styleSettings.ApplyFormattingRules)
            return;

        var changedFilePaths = GetModifiedFilePathsFromVcs(repository);
        if (changedFilePaths.Count == 0) return;

        // Skip files that have already been formatted and not modified since
        var alreadyFormatted = changedFilePaths
            .Where(f => FormattingPipeline.WrittenFileTimestamps.TryGetValue(f, out var formattedAt)
                        && File.GetLastWriteTimeUtc(f) == formattedAt)
            .ToList();

        if (alreadyFormatted.Count > 0)
        {
            Debug("MainLayout", $"Skipping {alreadyFormatted.Count} already-formatted file(s)");
            changedFilePaths.ExceptWith(alreadyFormatted);
        }

        if (changedFilePaths.Count == 0)
        {
            Info("MainLayout", "All changed files are already formatted — skipping pre-commit formatting");
            return;
        }

        LogProcessStart("MainLayout", $"Pre-commit formatting for {changedFilePaths.Count} file(s)");
        FileMonitoringService.StopMonitoring(repositoryId);
        try
        {
            await SaveChangedFilesWithFormattingAsync(changedFilePaths, styleSettings);
        }
        finally
        {
            FileMonitoringService.ClearPendingChanges(repositoryId);
            if (!string.IsNullOrEmpty(repository.VcsRootPath))
                FileMonitoringService.StartMonitoring(repositoryId, repository.VcsRootPath);
        }
        LogProcessEnd("MainLayout", $"Pre-commit formatting for {changedFilePaths.Count} file(s)");
    }

    public void Dispose()
    {
        NavState.OnThemeChanged -= OnThemeChangedHandler;
        NavState.OnRepositorySettingsApplied -= OnRepositorySettingsApplied;
        NavState.OnProjectSwitchStarting -= OnProjectSwitchStarting;
        NavState.OnVcsFilesChanged -= OnVcsFilesChanged;
        NavState.OnVcsModelsChanged -= OnVcsModelsChanged;
        NavState.OnRunDeferredDependencies -= RunDeferredDependenciesOnlyAsync;
        NavState.OnRunDeferredStyleChecking -= RunDeferredStyleCheckingFromEventAsync;
        NavState.OnRunDeferredExternalResources -= RunDeferredExternalResourcesAsync;
        NavState.OnRunAllDeferredAnalysis -= RunAllDeferredAnalysisAsync;
        NavState.OnFormatChangedFilesForCommit -= FormatChangedFilesForCommitAsync;
        RepositoryService.OnProjectChanged -= OnProjectChanged;
        RepositoryService.OnRepositoriesChanged -= OnRepositoriesChanged;
        FileMonitoringService.OnPendingChangesUpdated -= OnPendingChangesUpdated;
        StyleCheckingService.OnProgressChanged -= OnStyleCheckingProgressChanged;
        StyleCheckingService.OnFindingsFound -= OnStyleFindingsFound;
        StyleCheckingService.OnSpellCheckWarning -= OnSpellCheckWarning;
    }

    // Route background style-checking findings into the persistent CodeReviewService. Lives in the
    // layout (always mounted) so delivery never depends on which tab is currently open.
    private void OnStyleFindingsFound(List<LogMessage> findings)
        => InvokeAsync(() => CodeReviewService.AddLogMessages(findings));

    /// <summary>
    /// Re-derives the Parser-sourced findings for <paramref name="modelIds"/> (all models when null)
    /// from the graph and puts them back in the findings list.
    ///
    /// This lives in the layout, alongside style-finding delivery, for the same reason: it used to
    /// sit on the Code Review page, so parser errors only ever reached the findings list if that page
    /// happened to be mounted when the library loaded. Everything that clears the findings list for a set
    /// of models must call this afterwards — otherwise the tree badge and the Code Review banner keep
    /// reporting "N parser errors" (they read the graph node) while the panel they point the user at
    /// is empty.
    /// </summary>
    private void SurfaceParserErrors(IEnumerable<string>? modelIds = null)
    {
        var models = modelIds is null
            ? LibraryDataService.GetAllModels().ToList()
            : modelIds.Select(LibraryDataService.GetModelById).Where(m => m is not null).Cast<ModelNode>().ToList();
        if (models.Count == 0)
            return;

        // Drop the previous parser findings for exactly these models before re-adding, so repeated
        // re-analysis can neither duplicate them nor leave behind ones the file no longer has.
        var ids = models.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        CodeReviewService.RemoveLogMessagesByPredicate(
            m => m.Source == ParserErrorReporter.SourceName && ids.Contains(m.ModelName));

        var messages = ParserErrorReporter.ToLogMessages(models);
        if (messages.Count > 0)
            CodeReviewService.AddLogMessages(messages);
    }

    /// <summary>
    /// Tells the user up front that something failed to parse. Without this the only signals are a
    /// small icon in the tree and a banner on a page they may never open, so a library that silently
    /// dropped part of a file looks like it loaded cleanly.
    /// </summary>
    private async Task NotifyParserErrorsIfAnyAsync()
    {
        var (fatal, recovered) = ParserErrorReporter.Count(LibraryDataService.GetAllModels());
        if (fatal == 0 && recovered == 0)
            return;

        string message;
        Severity severity;
        if (fatal > 0)
        {
            message = fatal == 1
                ? "1 file could not be parsed. It appears in the library tree as a placeholder — see the Findings panel."
                : $"{fatal} files could not be parsed. They appear in the library tree as placeholders — see the Findings panel.";
            if (recovered > 0)
                message += $" ({recovered} additional recoverable syntax {(recovered == 1 ? "error" : "errors")} also reported.)";
            severity = Severity.Error;
        }
        else
        {
            message = recovered == 1
                ? "1 syntax error found while loading the library. See the Findings panel."
                : $"{recovered} syntax errors found while loading the library. See the Findings panel.";
            severity = Severity.Warning;
        }

        await InvokeAsync(() => Snackbar.Add(message, severity));
    }

    private async void OnRepositoriesChanged()
    {
        await InvokeAsync(StateHasChanged);
    }

    private void OnRepositorySettingsApplied(string repositoryId, bool formattingChanged, bool styleSettingsChanged)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var repository = RepositoryService.GetRepository(repositoryId);
                if (repository == null) return;

                if (formattingChanged)
                {
                    // Clear cached timestamps since formatting rules changed
                    FormattingPipeline.ClearWrittenFileTimestamps();

                    // Show progress dialog — full formatting can take several minutes
                    _fullFormatStatusMessage = $"Formatting all files in {repository.Name}...";
                    _fullFormatRunning = true;
                    await InvokeAsync(StateHasChanged);

                    // Pause monitoring to suppress the thousands of change events that
                    // formatting generates; clear any events that slipped through afterwards
                    FileMonitoringService.StopMonitoring(repositoryId);
                    try
                    {
                        await SaveAllLibrariesWithFormattingAsync(repositoryId);
                    }
                    finally
                    {
                        FileMonitoringService.ClearPendingChanges(repositoryId);
                        if (!string.IsNullOrEmpty(repository.VcsRootPath))
                            FileMonitoringService.StartMonitoring(repositoryId, repository.VcsRootPath);

                        // Invalidate working copy cache and notify the library browser
                        // so it picks up the thousands of files modified by formatting
                        RepositoryService.InvalidateWorkingCopyCache(repositoryId);
                        FileMonitoringService.NotifyFileActivity(repositoryId);

                        _fullFormatRunning = false;
                        await InvokeAsync(StateHasChanged);
                    }

                    await InvokeAsync(() => Snackbar.Add("Code formatting complete.", Severity.Success));
                }

                if (formattingChanged || styleSettingsChanged)
                {
                    var repositoryModelIds = LibraryDataService.Libraries
                        .Where(l => l.RepositoryId == repositoryId)
                        .SelectMany(l => l.ModelIds)
                        .ToHashSet();

                    // Decide there is work before showing the progress dialog, not after. It is
                    // modal and cannot be dismissed, and the only thing that closes it is the
                    // completion event — so opening it on a path that then starts nothing leaves the
                    // application wedged with no way out and nothing further in the log.
                    if (repositoryModelIds.Count > 0)
                    {
                        _styleCheckStatusMessage = $"Running style checking rules on all classes in {repository.Name}...";
                        _styleCheckRunning = true;
                        await InvokeAsync(StateHasChanged);

                        try
                        {
                            CodeReviewService.RemoveLogMessagesForModels(repositoryModelIds);
                            // The clear above takes the parser findings with it — put them back.
                            SurfaceParserErrors(repositoryModelIds);
                            await InvokeAsync(() => Snackbar.Add("Re-running style checking with new rules...", Severity.Normal));
                            _showStyleCheckingCompleteMessage = true;

                            // Signals completion itself even when it finds nothing to do (no rules
                            // enabled), which is what closes the dialog.
                            StyleCheckingService.StartBackgroundChecking(repository);
                        }
                        catch
                        {
                            // Nothing will signal completion now, so close the dialog here rather
                            // than leaving it up for a run that never started.
                            _styleCheckRunning = false;
                            await InvokeAsync(StateHasChanged);
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Error("MainLayout", "Error applying repository settings", ex);
                await InvokeAsync(() => Snackbar.Add($"Error applying settings: {ex.Message}", Severity.Error));
            }
        });
    }

    /// <summary>
    /// Handles VCS operations (pull, switch branch, revert, checkout) that change files on disk.
    ///
    /// The caller is expected to have already:
    ///   1. Stopped the file monitor for the repository (to prevent UI lockup during VCS op)
    ///   2. Performed the VCS operation
    ///   3. Called RefreshRepositoryAsync to reload the graph
    ///
    /// This handler then:
    ///   1. Reads any pending file changes captured before the monitor was paused
    ///   2. If no specific changes detected, falls back to all models in the repository
    ///   3. Pauses the monitor, applies code formatting, then restarts the monitor
    ///   4. Re-runs dependency analysis, style checking, and external resource analysis
    /// </summary>
    private void OnVcsFilesChanged(string repositoryId)
    {
        _ = Task.Run(async () =>
        {
            var repository = RepositoryService.GetRepository(repositoryId);
            if (repository == null || SkipReferenceOnly(repository, "the VCS-change pipeline")) return;

            try
            {
                await InvokeAsync(() => Snackbar.Add("Processing VCS file changes...", Severity.Normal));
                LogProcessStart("MainLayout", $"Processing VCS changes for repository {repository.Name}");

                var graph = LibraryDataService.CombinedGraph;

                // The fallback chain lives in VcsChangeResolver: pending monitor changes, then VCS
                // status, then the whole repository — and the last of those deliberately gives the
                // formatter nothing, so a branch switch does not rewrite the working copy.
                var pendingChanges = FileMonitoringService.GetPendingChangesForRepository(repositoryId);

                var changes = VcsChangeResolver.Resolve(
                    pendingChanges,
                    () => GetModifiedFilePathsFromVcs(repository),
                    () => LibraryDataService.Libraries
                        .Where(l => l.RepositoryId == repositoryId)
                        .SelectMany(l => l.ModelIds),
                    filePath => graph.GetModelsInFile(GraphBuilder.GenerateFileId(filePath)).Select(m => m.Id));

                // Cleared whenever there were any, not only when they answered: pending changes that
                // resolved to nothing are still handled, and leaving them queued makes the Refresh
                // button report work that is already done.
                if (pendingChanges.Count > 0)
                    FileMonitoringService.ClearPendingChanges(repositoryId);

                var affectedModelIds = changes.AffectedModelIds;
                var changedFilePaths = changes.ChangedFilePaths;

                if (affectedModelIds.Count == 0)
                {
                    await InvokeAsync(() => Snackbar.Add("No changes to process.", Severity.Info));
                    if (!string.IsNullOrEmpty(repository.VcsRootPath))
                        FileMonitoringService.StartMonitoring(repositoryId, repository.VcsRootPath);
                    return;
                }

                // Get per-repository style settings for formatting
                var styleSettings = repository.StyleSettings ?? new StyleCheckingSettings();

                // Pause monitor and apply formatting. StopMonitoring is idempotent — safe to call
                // even if the caller already paused it (e.g., for update/checkout operations).
                try
                {
                    FileMonitoringService.StopMonitoring(repositoryId);

                    if (changedFilePaths.Count > 0)
                    {
                        await InvokeAsync(() => Snackbar.Add($"Applying code formatting to {changedFilePaths.Count} changed file(s)...", Severity.Normal));
                        await SaveChangedFilesWithFormattingAsync(changedFilePaths, styleSettings);
                    }
                }
                finally
                {
                    // Clear any change events that slipped through or were generated by formatting,
                    // then restart monitoring so future user edits are tracked again.
                    FileMonitoringService.ClearPendingChanges(repositoryId);
                    if (!string.IsNullOrEmpty(repository.VcsRootPath))
                        FileMonitoringService.StartMonitoring(repositoryId, repository.VcsRootPath);
                }

                // Re-analyse whenever the graph has edges to maintain (see RefreshLibrariesAsync).
                if (LibraryDataService.CombinedGraph.DependenciesAnalyzed)
                {
                    await InvokeAsync(() => Snackbar.Add("Analyzing dependencies...", Severity.Normal));
                    var libraryInfos = GetLibraryInfos();
                    await Task.Run(async () =>
                    {
                        await GraphBuilder.AnalyzeDependenciesForModelsAsync(
                            LibraryDataService.CombinedGraph, affectedModelIds, libraryInfos);
                        LibraryDataService.CombinedGraph.ReconcileDependencyEdges();
                    });
                }

                // Re-run style checking for affected models (skip if deferred and not yet run)
                if (!NavState.IsDeferredMode || NavState.HasStyleCheckingRun)
                {
                    CodeReviewService.RemoveLogMessagesForModels(affectedModelIds);
                    // The clear above takes the parser findings with it — put them back.
                    SurfaceParserErrors(affectedModelIds);
                    await InvokeAsync(() => Snackbar.Add($"Style checking {affectedModelIds.Count} model(s)...", Severity.Normal));
                    await StyleCheckingService.CheckModelsAsync(affectedModelIds, LibraryDataService.CombinedGraph);
                    await InvokeAsync(() => Snackbar.Add("Style checking complete.", Severity.Success));
                }

                // Re-analyze external resources for affected models (skip if deferred and not yet run)
                if (!NavState.IsDeferredMode || NavState.HasExternalResourcesAnalyzed)
                {
                    await ExternalResourceService.AnalyzeResourcesForModelsAsync(
                        affectedModelIds, LibraryDataService.CombinedGraph);
                }

                // If the currently selected model was affected, invalidate its render cache and
                // refresh the model viewer so it re-reads fresh content from the graph.
                if (!string.IsNullOrEmpty(NavState.ModelID) && affectedModelIds.Contains(NavState.ModelID))
                    await InvokeAsync(() =>
                    {
                        NavState.ModelContentChanged(affectedModelIds.ToList());
                        NavState.ChangeModelID(NavState.ModelID);
                    });

                await InvokeAsync(() => Snackbar.Add("VCS changes processed successfully.", Severity.Success));
                LogProcessEnd("MainLayout", $"Processing VCS changes for repository {repository.Name}");
            }
            catch (Exception ex)
            {
                Error("MainLayout", "Error processing VCS changes", ex);
                await InvokeAsync(() => Snackbar.Add($"Error processing VCS changes: {ex.Message}", Severity.Error));
                // Always restart monitor on error so future edits are tracked
                if (!string.IsNullOrEmpty(repository.VcsRootPath))
                    FileMonitoringService.StartMonitoring(repositoryId, repository.VcsRootPath);
            }
        });
    }

    private void OnVcsModelsChanged(string repositoryId, IReadOnlyList<string> modelIds)
    {
        _ = Task.Run(async () =>
        {
            var repository = RepositoryService.GetRepository(repositoryId);
            if (repository == null) return;

            try
            {
                // Clear any pending monitor events captured during the revert so the monitor
                // does not try to re-process the same files as "changed" on the next VCS op.
                FileMonitoringService.ClearPendingChanges(repositoryId);

                var affectedModelIds = new HashSet<string>(modelIds);
                if (affectedModelIds.Count == 0) return;

                // Re-analyse whenever the graph has edges to maintain (see RefreshLibrariesAsync).
                if (LibraryDataService.CombinedGraph.DependenciesAnalyzed)
                {
                    await InvokeAsync(() => Snackbar.Add("Analyzing dependencies...", Severity.Normal));
                    var libraryInfos = GetLibraryInfos();
                    await Task.Run(async () =>
                    {
                        await GraphBuilder.AnalyzeDependenciesForModelsAsync(
                            LibraryDataService.CombinedGraph, affectedModelIds, libraryInfos);
                        LibraryDataService.CombinedGraph.ReconcileDependencyEdges();
                    });
                }

                // Re-run style checking (skip if deferred and not yet run)
                if (!NavState.IsDeferredMode || NavState.HasStyleCheckingRun)
                {
                    CodeReviewService.RemoveLogMessagesForModels(affectedModelIds);
                    // The clear above takes the parser findings with it — put them back.
                    SurfaceParserErrors(affectedModelIds);
                    await InvokeAsync(() => Snackbar.Add($"Style checking {affectedModelIds.Count} model(s)...", Severity.Normal));
                    await StyleCheckingService.CheckModelsAsync(affectedModelIds, LibraryDataService.CombinedGraph);
                }

                // Re-analyze external resources (skip if deferred and not yet run)
                if (!NavState.IsDeferredMode || NavState.HasExternalResourcesAnalyzed)
                {
                    await ExternalResourceService.AnalyzeResourcesForModelsAsync(
                        affectedModelIds, LibraryDataService.CombinedGraph);
                }

                // Refresh model viewer if the selected model was affected
                if (!string.IsNullOrEmpty(NavState.ModelID) && affectedModelIds.Contains(NavState.ModelID))
                    await InvokeAsync(() =>
                    {
                        NavState.ModelContentChanged(affectedModelIds.ToList());
                        NavState.ChangeModelID(NavState.ModelID);
                    });

                await InvokeAsync(() => Snackbar.Add("Revert analysis complete.", Severity.Success));
            }
            catch (Exception ex)
            {
                Error("MainLayout", "Error processing reverted models", ex);
                await InvokeAsync(() => Snackbar.Add($"Error after revert: {ex.Message}", Severity.Error));
            }
        });
    }

    private async void OnPendingChangesUpdated()
    {
        await InvokeAsync(() =>
        {
            _pendingChangesCount = FileMonitoringService.GetPendingChangesSummary().TotalChanges;
            StateHasChanged();
        });
    }

    /// <summary>
    /// Converts loaded libraries to LibraryInfo objects for GraphBuilder. Lives on the service so
    /// dependency analysis started there builds the same list as the ones started here.
    /// </summary>
    private List<LibraryInfo> GetLibraryInfos() => LibraryDataService.GetLibraryInfos();

    private string GetRefreshTooltip()
    {
        if (_isRefreshing)
            return "Refreshing...";

        if (_pendingChangesCount == 0)
            return "Refresh libraries";

        var summary = FileMonitoringService.GetPendingChangesSummary();
        var parts = new List<string>();

        if (summary.AddedFiles > 0)
            parts.Add($"{summary.AddedFiles} added");
        if (summary.ModifiedFiles > 0)
            parts.Add($"{summary.ModifiedFiles} modified");
        if (summary.DeletedFiles > 0)
            parts.Add($"{summary.DeletedFiles} deleted");
        if (summary.RenamedFiles > 0)
            parts.Add($"{summary.RenamedFiles} renamed");

        return $"Refresh libraries ({string.Join(", ", parts)})";
    }

    private async Task RefreshLibrariesAsync()
    {
        if (_isRefreshing)
            return;

        _isRefreshing = true;
        StateHasChanged();

        try
        {
            var pendingChanges = FileMonitoringService.PendingChanges.ToList();
            if (pendingChanges.Count == 0)
            {
                Snackbar.Add("No pending changes to process", Severity.Info);
                return;
            }

            LogProcessStart("MainLayout", $"Processing {pendingChanges.Count} file changes");
            Snackbar.Add($"Processing {pendingChanges.Count} file changes...", Severity.Normal);

            // Pause file monitoring for affected repositories to prevent file writes
            // (from formatting) generating new pending changes and triggering cascading
            // VCS status queries via OnRepositoryFileActivity.
            var affectedRepoIds = pendingChanges.Select(c => c.RepositoryId).Distinct().ToList();
            foreach (var repoId in affectedRepoIds)
                FileMonitoringService.StopMonitoring(repoId);

            // Collect all changed file paths (deletions, renames, modifications, additions)
            var changedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in pendingChanges.Where(c => c.IsModelicaFile))
            {
                changedFilePaths.Add(change.FilePath);
                // For renames, the old path is also affected (needs removal)
                if (change.ChangeType == FileChangeType.Renamed && !string.IsNullOrEmpty(change.OldFilePath))
                    changedFilePaths.Add(change.OldFilePath);
            }

            Info("MainLayout", $"Processing {changedFilePaths.Count} changed Modelica files from {pendingChanges.Count} pending changes");

            // Determine root path from the first affected repository
            var firstRepo = RepositoryService.GetRepository(pendingChanges[0].RepositoryId);
            var rootPath = firstRepo?.VcsRootPath ?? Path.GetDirectoryName(pendingChanges[0].FilePath) ?? "";

            // Batch update: remove stale models, re-parse changed files, rebuild library indexes
            HashSet<string> affectedModelIds;
            using (LibraryDataService.SuppressTreeDataChanged())
            {
                affectedModelIds = await LibraryDataService.UpdateChangedFilesAsync(changedFilePaths, rootPath);
            }

            Info("MainLayout", $"File processing complete: {affectedModelIds.Count} affected models, {changedFilePaths.Count} changed files");

            // Save changed files with formatting, using per-repository style settings
            if (changedFilePaths.Count > 0)
            {
                Info("MainLayout", "Applying code formatting...");

                // Group changed files by repository so each group uses the correct style settings
                var filesByRepo = pendingChanges
                    .Where(c => c.ChangeType != FileChangeType.Deleted && c.IsModelicaFile && changedFilePaths.Contains(c.FilePath))
                    .GroupBy(c => c.RepositoryId)
                    .ToList();

                foreach (var repoGroup in filesByRepo)
                {
                    var repository = RepositoryService.GetRepository(repoGroup.Key);
                    if (repository is not null && SkipReferenceOnly(repository, "formatting"))
                        continue;
                    var styleSettings = repository?.StyleSettings ?? new StyleCheckingSettings();
                    var repoPaths = repoGroup.Select(c => c.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    await SaveChangedFilesWithFormattingAsync(repoPaths, styleSettings);
                }
                Info("MainLayout", "Code formatting complete");
            }

            // Re-analyse dependencies whenever the graph HAS edges — the reload above dropped them for
            // the affected models, so skipping this leaves the graph claiming to be analysed while those
            // models look unreferenced, and the unused-class rule then flags everything they used.
            // Asking the graph rather than NavState matters: HasDependencyAnalysisRun is only set by the
            // deferred pipeline, so on a library below the defer threshold it is false all session.
            if (affectedModelIds.Count > 0 && LibraryDataService.CombinedGraph.DependenciesAnalyzed)
            {
                Info("MainLayout", "Re-analyzing dependencies for affected models...");
                var libraryInfos = GetLibraryInfos();
                var analysisScope = affectedModelIds.ToHashSet();
                await Task.Run(async () =>
                {
                    await GraphBuilder.AnalyzeDependenciesForModelsAsync(
                        LibraryDataService.CombinedGraph, analysisScope, libraryInfos);
                    LibraryDataService.CombinedGraph.ReconcileDependencyEdges();
                });
                Info("MainLayout", "Dependency analysis complete");
            }

            // Remove old findings for affected models and re-check style.
            // Run off the render thread because EnsureSpellChecker uses
            // .GetAwaiter().GetResult() which deadlocks the Blazor sync context.
            if (affectedModelIds.Count > 0)
            {
                Info("MainLayout", $"Style checking {affectedModelIds.Count} affected model(s)...");
                CodeReviewService.RemoveLogMessagesForModels(affectedModelIds);
                // The clear above takes the parser findings with it — put them back.
                SurfaceParserErrors(affectedModelIds);
                var graph = LibraryDataService.CombinedGraph;
                await Task.Run(async () =>
                    await StyleCheckingService.CheckModelsAsync(affectedModelIds, graph));
                Info("MainLayout", "Style checking complete");
            }

            // Re-analyze external resources only if already run
            if (affectedModelIds.Count > 0 && NavState.HasExternalResourcesAnalyzed)
            {
                await ExternalResourceService.AnalyzeResourcesForModelsAsync(
                    affectedModelIds, LibraryDataService.CombinedGraph);
            }

            // If the currently selected model was affected, invalidate its render cache
            if (!string.IsNullOrEmpty(NavState.ModelID) && affectedModelIds.Contains(NavState.ModelID))
            {
                NavState.ModelContentChanged(affectedModelIds.ToList());
                NavState.ChangeModelID(NavState.ModelID);
            }

            // Clear pending changes (including any generated by formatting file writes)
            // and restart file monitoring for affected repositories.
            FileMonitoringService.ClearPendingChanges();
            foreach (var repoId in affectedRepoIds)
            {
                var repo = RepositoryService.GetRepository(repoId);
                if (repo != null && !string.IsNullOrEmpty(repo.VcsRootPath))
                    FileMonitoringService.StartMonitoring(repoId, repo.VcsRootPath);
            }

            LogProcessEnd("MainLayout", $"Processing {pendingChanges.Count} file changes");
            Snackbar.Add($"Successfully processed {pendingChanges.Count} file changes", Severity.Success);
        }
        catch (Exception ex)
        {
            Error("MainLayout", "Error processing file changes", ex);
            Snackbar.Add($"Error processing file changes: {ex.Message}", Severity.Error);
        }
        finally
        {
            _isRefreshing = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Saves only the changed files with formatting applied.
    /// This is more efficient than saving all libraries when only a few files have changed.
    /// </summary>
    /// <param name="changedFilePaths">File paths that have been modified.</param>
    /// <param name="styleSettings">The style settings of the repository the files belong to.</param>
    private Task SaveChangedFilesWithFormattingAsync(
        IEnumerable<string> changedFilePaths, StyleCheckingSettings styleSettings)
        => FormattingPipeline.FormatChangedFilesAsync(changedFilePaths, styleSettings);

    private async Task OpenAddRepositoryDialog()
    {
        var options = new DialogOptions
        {
            CloseOnEscapeKey = true,
            MaxWidth = MaxWidth.Small,
            FullWidth = true
        };

        var dialog = await DialogService.ShowAsync<AddRepositoryDialog>("Add Repository", options);
        var result = await dialog.Result;

        if (result != null && !result.Canceled)
        {
            // Switch to repository mode to show the newly added repository
            _settings.UI.RepositoryMode = true;
            StateHasChanged();

            // Run the same post-load analysis steps as startup for the new repository's models
            var repositoryId = result.Data as string;
            if (repositoryId != null)
            {
                var newModelIds = LibraryDataService.Libraries
                    .Where(l => l.RepositoryId == repositoryId)
                    .SelectMany(l => l.ModelIds)
                    .ToHashSet();

                if (newModelIds.Count > 0)
                {
                    Snackbar.Add("Analyzing dependencies for new repository...", Severity.Normal);
                    var libraryInfos = GetLibraryInfos();
                    await Task.Run(async () =>
                    {
                        await GraphBuilder.AnalyzeDependenciesForModelsAsync(
                            LibraryDataService.CombinedGraph, newModelIds, libraryInfos);
                        // Reconcile edges in case existing models already referenced
                        // models from the newly added library
                        LibraryDataService.CombinedGraph.ReconcileDependencyEdges();
                    });

                    await ExternalResourceService.AnalyzeResourcesForModelsAsync(
                        newModelIds, LibraryDataService.CombinedGraph);
                    ExternalResourceService.StartMonitoringResources();
                }
            }
        }
    }

    private void ApplyThemeFromSettings(UISettings uiSettings)
    {
        _isDarkMode = uiSettings.Theme == Theme.Dark;
        _myTheme = uiSettings.Theme == Theme.Custom
            ? MlqtTheme.BuildTheme(MlqtTheme.BuildCustomPalette(uiSettings))
            : MlqtTheme.BuildTheme(MlqtTheme.GetDefaultPaletteLight());
    }

    private async void OnThemeChangedHandler(UISettings uiSettings)
    {
        ApplyThemeFromSettings(uiSettings);
        await InvokeAsync(StateHasChanged);
    }

    private void ToggleView() {
        _settings.UI.RepositoryMode = !_settings.UI.RepositoryMode;
        StateHasChanged();
    }
}
