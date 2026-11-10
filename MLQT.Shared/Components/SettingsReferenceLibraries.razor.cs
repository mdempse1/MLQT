namespace MLQT.Shared.Components;

public partial class SettingsReferenceLibraries : IDisposable
{
    [Inject] private ISettingsService SettingsService { get; set; } = null!;
    [Inject] private IFilePickerService FilePickerService { get; set; } = null!;
    [Inject] private AppState NavState { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IReferenceLibraryLoader ReferenceLibraryLoader { get; set; } = null!;
    [Inject] private ILibraryDataService LibraryDataService { get; set; } = null!;
    [Inject] private IStyleCheckingService StyleCheckingService { get; set; } = null!;
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;

    /// <summary>
    /// What is using the graph now, or null when nothing is. Adding or removing a folder loads or
    /// unloads its libraries at once, which must not happen under a load, a check, an analysis or a VCS
    /// operation reading the same classes - so the edit is refused, and the user told why, until it
    /// finishes.
    /// </summary>
    internal string? BusyWith() =>
        NavState.StartupStep is not null ? "the project is loading"
        : NavState.IsVcsWorkInProgress ? "a version control operation is running"
        : StyleCheckingService.IsRunning ? "a style check is running"
        : LibraryDataService.IsDependencyAnalysisRunning ? "dependency analysis is running"
        : null;

    private bool RefusedWhileBusy()
    {
        if (BusyWith() is not { } reason)
            return false;

        Snackbar.Add($"The reference libraries cannot be changed while {reason}. Try again when it has finished.",
            Severity.Warning);
        return true;
    }

    private ReferenceLibrarySettings _settings = new();

    protected override void OnInitialized()
    {
        NavState.OnSaveSettings += SaveOnRequest;
        base.OnInitialized();
    }

    public void Dispose() => NavState.OnSaveSettings -= SaveOnRequest;

    protected override async Task OnInitializedAsync()
    {
        _settings = await SettingsService.GetAsync("ReferenceLibraries", new ReferenceLibrarySettings());
    }

    // The folder list saves as it is edited, but the switch does not, and the page's Save button
    // must mean what it says on every tab.
    private void SaveOnRequest() => _ = SaveAsync();

    /// <summary>
    /// What a configured folder actually contributes, so a mistyped or moved path is visible here
    /// rather than only as unresolved references much later.
    /// </summary>
    private string DescribeFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return "folder not found";

        try
        {
            var libraries = LibraryDiscovery.DiscoverLibraryPaths(path);
            if (libraries.Count == 0)
                return "no libraries found";

            var encrypted = libraries.Count(EncryptedLibraryDetector.IsEncryptedLibraryRoot);
            return encrypted == 0
                ? $"{libraries.Count} librar{(libraries.Count == 1 ? "y" : "ies")}"
                : $"{libraries.Count} ({encrypted} encrypted)";
        }
        catch (Exception)
        {
            return "could not be read";
        }
    }

    private async Task AddPath()
    {
        if (RefusedWhileBusy())
            return;

        var folder = await FilePickerService.PickFolderAsync("Select a folder containing Modelica libraries");
        if (string.IsNullOrEmpty(folder) || RefusedWhileBusy())
            return;

        if (_settings.Paths.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            Snackbar.Add("That folder is already configured.", Severity.Info);
            return;
        }

        _settings.Paths.Add(folder);
        await SaveAsync();

        // Loaded now rather than at the next project load: until then the folder was listed here and
        // resolved nothing. The folders already loaded are skipped, so only the new one's libraries
        // arrive.
        var summary = await ReferenceLibraryLoader.LoadAsync(_settings.Paths, _settings.UseEncryptedLibraryDocumentation);
        if (summary.Loaded > 0)
            Snackbar.Add(
                $"Loaded {summary.Loaded} reference librar{(summary.Loaded == 1 ? "y" : "ies")}. " +
                "Run the checks again to update the findings.", Severity.Success);
    }

    private async Task RemovePath(string path)
    {
        if (RefusedWhileBusy())
            return;

        _settings.Paths.Remove(path);
        await SaveAsync();

        // Unloaded now: left in the graph until the next project load, the folder's classes went on
        // resolving references and counting as known words, so checking every class again reported
        // what it had with the folder - and a restart reported something else. The folders that remain
        // are then loaded again, which brings in any library this folder had been standing in for
        // (one skipped elsewhere as a second copy of it), and nothing they had loaded already.
        //
        // Repositories first: a copy in one outranks a reference folder's, so the encrypted build or
        // other copy a repository holds of a library this folder was providing is loaded before the
        // remaining folders are asked, and they then skip it.
        var unloaded = ReferenceLibraryLoader.UnloadFolder(path);
        await RepositoryService.LoadLibrariesNoLongerProvidedAsync();
        await ReferenceLibraryLoader.LoadAsync(_settings.Paths, _settings.UseEncryptedLibraryDocumentation);

        if (unloaded > 0)
            Snackbar.Add(
                $"Unloaded {unloaded} reference librar{(unloaded == 1 ? "y" : "ies")}. " +
                "Run the checks again to update the findings.", Severity.Info);
    }

    /// <summary>Persists the settings. Called by the Settings page's Save button.</summary>
    public async Task SaveSettingsAsync() => await SaveAsync();

    /// <summary>Clears the configured reference libraries and persists that.</summary>
    public async Task ResetToDefaultsAsync()
    {
        _settings = new ReferenceLibrarySettings();
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        try
        {
            await SettingsService.SetAsync("ReferenceLibraries", _settings);
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Could not save reference library settings: {ex.Message}", Severity.Error);
        }
    }
}
