namespace MLQT.Shared.Components;

public partial class SettingsReferenceLibraries : IDisposable
{
    [Inject] private ISettingsService SettingsService { get; set; } = null!;
    [Inject] private IFilePickerService FilePickerService { get; set; } = null!;
    [Inject] private AppState NavState { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

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
        var folder = await FilePickerService.PickFolderAsync("Select a folder containing Modelica libraries");
        if (string.IsNullOrEmpty(folder))
            return;

        if (_settings.Paths.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            Snackbar.Add("That folder is already configured.", Severity.Info);
            return;
        }

        _settings.Paths.Add(folder);
        await SaveAsync();
    }

    private async Task RemovePath(string path)
    {
        _settings.Paths.Remove(path);
        await SaveAsync();
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
