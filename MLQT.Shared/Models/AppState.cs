using MudBlazor;

namespace MLQT.Shared.Models;

/// <summary>
/// Centralized application state service.
/// Manages UI state, selection state, and cross-component communication.
/// </summary>
public class AppState
{
    // ========== Model Selection State ==========

    /// <summary>
    /// The currently selected model ID.
    /// </summary>
    public string ModelID { get; private set; } = string.Empty;

    /// <summary>
    /// The current tree view selection mode.
    /// </summary>
    public SelectionMode SelectionMode { get; private set; } = SelectionMode.SingleSelection;

    /// <summary>
    /// Currently selected model IDs (multi-selection mode).
    /// </summary>
    public HashSet<string> SelectedModelIDs { get; private set; } = new();

    /// <summary>
    /// Event fired when the current model changes.
    /// </summary>
    public event Action? OnChangeModel;

    /// <summary>
    /// Event fired when the selected models change (multi-select).
    /// </summary>
    public event Action? OnSelectedModelsChanged;

    /// <summary>
    /// Event fired when the selection mode changes.
    /// </summary>
    public event Action? OnEnableMultiSelect;

    /// <summary>
    /// Changes the current model ID and notifies listeners.
    /// </summary>
    public void ChangeModelID(string modelID)
    {
        ModelID = modelID;
        OnChangeModel?.Invoke();
    }

    /// <summary>
    /// Sets the selected models (for multi-select mode) and notifies listeners.
    /// </summary>
    public void SetSelectedModels(IEnumerable<string> modelIds)
    {
        SelectedModelIDs = new HashSet<string>(modelIds);
        OnSelectedModelsChanged?.Invoke();
    }

    /// <summary>
    /// Clears the selected models and notifies listeners.
    /// </summary>
    public void ClearSelectedModels()
    {
        SelectedModelIDs.Clear();
        OnSelectedModelsChanged?.Invoke();
    }

    /// <summary>
    /// Changes the selection mode and notifies listeners.
    /// </summary>
    public void ChangeSelectionMode(SelectionMode selectionMode)
    {
        SelectionMode = selectionMode;
        OnEnableMultiSelect?.Invoke();
    }

    // ========== Library State ==========

    /// <summary>
    /// Indicates whether a library is currently loaded.
    /// </summary>
    public bool IsLibraryLoaded { get; private set; }

    /// <summary>
    /// Event fired when a library is loaded.
    /// </summary>
    public event Action? OnLibraryLoaded;

    /// <summary>
    /// Event fired when the library is cleared.
    /// </summary>
    public event Action? OnLibraryCleared;

    /// <summary>
    /// Event fired when the in-memory content of specific models has changed (e.g. after
    /// a file reload from disk or VCS revert). Subscribers should invalidate any cached
    /// rendered output for the supplied model IDs.
    /// </summary>
    public event Action<IReadOnlyCollection<string>>? OnModelContentChanged;

    /// <summary>
    /// Notifies that the in-memory content of the given models has changed.
    /// </summary>
    public void ModelContentChanged(IReadOnlyCollection<string> modelIds)
    {
        OnModelContentChanged?.Invoke(modelIds);
    }

    /// <summary>
    /// Notifies that a library has been loaded.
    /// </summary>
    public void LibraryLoaded()
    {
        IsLibraryLoaded = true;
        OnLibraryLoaded?.Invoke();
    }

    /// <summary>
    /// Notifies that the library has been cleared.
    /// </summary>
    public void LibraryCleared()
    {
        IsLibraryLoaded = false;
        ModelID = string.Empty;
        SelectedModelIDs.Clear();
        OnLibraryCleared?.Invoke();
    }

    // ========== Settings State ==========

    /// <summary>
    /// Event fired when settings are saved.
    /// </summary>
    public event Action? OnSaveSettings;

    /// <summary>
    /// Notifies that settings have been saved.
    /// </summary>
    public void SaveSettings()
    {
        OnSaveSettings?.Invoke();
    }

    /// <summary>
    /// Event fired when the UI theme has changed and should be applied immediately.
    /// </summary>
    public event Action<UISettings>? OnThemeChanged;

    /// <summary>
    /// Notifies that the UI theme has changed and applies it immediately.
    /// </summary>
    public void ThemeChanged(UISettings uiSettings)
    {
        OnThemeChanged?.Invoke(uiSettings);
    }

    // ========== Repository Settings ==========

    /// <summary>
    /// Event fired when repository settings are applied by the user.
    /// Parameters: repositoryId, formattingChanged, styleSettingsChanged
    /// </summary>
    public event Action<string, bool, bool>? OnRepositorySettingsApplied;

    /// <summary>
    /// Notifies that repository settings have been applied and triggers appropriate re-analysis.
    /// </summary>
    public void RepositorySettingsApplied(string repositoryId, bool formattingChanged, bool styleSettingsChanged)
    {
        OnRepositorySettingsApplied?.Invoke(repositoryId, formattingChanged, styleSettingsChanged);
    }

    // ========== VCS Operations ==========

    /// <summary>
    /// Event fired when a VCS operation (pull, switch branch, revert, checkout) has changed files
    /// on disk and the analysis pipeline (formatting, dependencies, style checking, external resources)
    /// should be re-run for the affected models.
    /// Parameter: repositoryId
    /// </summary>
    public event Action<string>? OnVcsFilesChanged;

    /// <summary>
    /// Notifies that a VCS operation has changed files and triggers re-analysis.
    /// </summary>
    public void VcsFilesChanged(string repositoryId)
    {
        OnVcsFilesChanged?.Invoke(repositoryId);
    }

    /// <summary>
    /// Event fired when a VCS revert operation has restored files to their committed state.
    /// Triggers dependency/style/resource analysis for the affected models WITHOUT formatting,
    /// since the committed content must be preserved exactly as-is.
    /// Parameters: repositoryId, affected model IDs.
    /// </summary>
    public event Action<string, IReadOnlyList<string>>? OnVcsModelsChanged;

    /// <summary>
    /// Notifies that reverted models need re-analysis (no formatting).
    /// </summary>
    public void VcsModelsChanged(string repositoryId, IReadOnlyList<string> modelIds)
    {
        OnVcsModelsChanged?.Invoke(repositoryId, modelIds);
    }

    // ========== Project Profiles ==========

    /// <summary>
    /// Event fired when a project switch is about to begin (before repos are loaded).
    /// Listeners should show a progress dialog immediately.
    /// </summary>
    public event Action? OnProjectSwitchStarting;

    /// <summary>
    /// Notifies that a project switch is about to begin.
    /// Called before SwitchProjectAsync to show the progress dialog immediately.
    /// </summary>
    public void ProjectSwitchStarting()
    {
        OnProjectSwitchStarting?.Invoke();
    }

    /// <summary>
    /// Event fired when the active project profile changes.
    /// Parameter: new project ID.
    /// </summary>
    public event Action<string>? OnProjectChanged;

    /// <summary>
    /// Notifies that the active project has changed.
    /// </summary>
    public void ProjectChanged(string projectId)
    {
        OnProjectChanged?.Invoke(projectId);
    }

    // ========== Deferred Analysis State ==========

    /// <summary>
    /// Whether the current project is in deferred analysis mode (large repository).
    /// When true, dependency analysis, style checking, and external resource
    /// analysis are skipped on startup and must be run manually.
    /// </summary>
    public bool IsDeferredMode { get; private set; }

    /// <summary>Whether dependency analysis has been run in this session.</summary>
    public bool HasDependencyAnalysisRun { get; private set; }

    /// <summary>Whether style checking has been run in this session.</summary>
    public bool HasStyleCheckingRun { get; private set; }

    /// <summary>Whether external resource analysis has been run in this session.</summary>
    public bool HasExternalResourcesAnalyzed { get; private set; }

    /// <summary>Async event: run deferred dependency analysis.</summary>
    public event Func<Task>? OnRunDeferredDependencies;

    /// <summary>Async event: run deferred style checking.</summary>
    public event Func<Task>? OnRunDeferredStyleChecking;

    /// <summary>Async event: run deferred external resource analysis.</summary>
    public event Func<Task>? OnRunDeferredExternalResources;

    /// <summary>Async event: run all deferred analysis steps.</summary>
    public event Func<Task>? OnRunAllDeferredAnalysis;

    /// <summary>
    /// Async event: format changed files before commit in deferred mode.
    /// Parameter: repositoryId.
    /// </summary>
    public event Func<string, Task>? OnFormatChangedFilesForCommit;

    /// <summary>Event fired when a deferred analysis step completes.</summary>
    public event Action? OnDeferredAnalysisCompleted;

    /// <summary>Enables deferred analysis mode and notifies listeners.</summary>
    public void EnableDeferredMode()
    {
        IsDeferredMode = true;
        HasDependencyAnalysisRun = false;
        HasStyleCheckingRun = false;
        HasExternalResourcesAnalyzed = false;
        OnDeferredAnalysisCompleted?.Invoke();
    }

    /// <summary>Disables deferred analysis mode (all steps already run).</summary>
    public void DisableDeferredMode()
    {
        IsDeferredMode = false;
    }

    /// <summary>Marks dependency analysis as completed.</summary>
    public void DependencyAnalysisCompleted()
    {
        HasDependencyAnalysisRun = true;
        OnDeferredAnalysisCompleted?.Invoke();
    }

    /// <summary>Marks style checking as completed.</summary>
    public void StyleCheckingCompleted()
    {
        HasStyleCheckingRun = true;
        OnDeferredAnalysisCompleted?.Invoke();
    }

    /// <summary>Marks external resource analysis as completed.</summary>
    public void ExternalResourcesAnalysisCompleted()
    {
        HasExternalResourcesAnalyzed = true;
        OnDeferredAnalysisCompleted?.Invoke();
    }

    /// <summary>Triggers deferred dependency analysis.</summary>
    public async Task RunDeferredDependenciesAsync()
    {
        if (OnRunDeferredDependencies != null)
            await OnRunDeferredDependencies.Invoke();
    }

    /// <summary>Triggers deferred style checking.</summary>
    public async Task RunDeferredStyleCheckingAsync()
    {
        if (OnRunDeferredStyleChecking != null)
            await OnRunDeferredStyleChecking.Invoke();
    }

    /// <summary>Triggers deferred external resource analysis.</summary>
    public async Task RunDeferredExternalResourcesAsync()
    {
        if (OnRunDeferredExternalResources != null)
            await OnRunDeferredExternalResources.Invoke();
    }

    /// <summary>Triggers all deferred analysis steps.</summary>
    public async Task RunAllDeferredAnalysisAsync()
    {
        if (OnRunAllDeferredAnalysis != null)
            await OnRunAllDeferredAnalysis.Invoke();
    }

    /// <summary>Formats changed files in a repository before commit (deferred mode).</summary>
    public async Task FormatChangedFilesForCommitAsync(string repositoryId)
    {
        if (OnFormatChangedFilesForCommit != null)
            await OnFormatChangedFilesForCommit.Invoke(repositoryId);
    }

    /// <summary>Resets all deferred state (e.g., when switching projects).</summary>
    public void ResetDeferredState()
    {
        IsDeferredMode = false;
        HasDependencyAnalysisRun = false;
        HasStyleCheckingRun = false;
        HasExternalResourcesAnalyzed = false;
    }

    // ========== Log Messages ==========

    /// <summary>
    /// Event fired when log messages should be cleared.
    /// </summary>
    public event Action? OnClearLogMessages;

    /// <summary>
    /// Notifies that log messages should be cleared.
    /// </summary>
    public void ClearLogMessages()
    {
        OnClearLogMessages?.Invoke();
    }
}
