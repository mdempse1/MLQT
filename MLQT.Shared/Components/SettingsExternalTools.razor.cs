using DymolaInterface;
using OpenModelicaInterface;
using System.Threading.Tasks;

namespace MLQT.Shared.Components;

public partial class SettingsExternalTools : IDisposable
{
    [Inject] private ISettingsService SettingsService { get; set; } = null!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
    [Inject] private AppState NavState { get; set; } = null!;
    [Inject] private IFilePickerService FilePickerService { get; set; } = null!;

    private AppSettings _settings = new();
    private bool _showDymolaWarning = false;
    private bool _showOpenModelicaWarning = false;

    protected override void OnInitialized()
    {
        NavState.OnSaveSettings += SaveSettings;
        base.OnInitialized();
    }

    /// <summary>
    /// Unsubscribes from the singleton services this component listens to.
    ///
    /// <para>Blazor calls this because the component declares <c>@implements IDisposable</c> at the
    /// top of the file. It did not: the method was here, named <c>OnDispose</c>, <c>protected</c>, and
    /// called by nothing — so every handler stayed on the singleton after the component was gone.
    /// The settings tabs are re-created on every switch (MudTabs renders only the active panel), so
    /// the subscriptions accumulated for the life of the process: <b>Save Settings</b> ran once per
    /// instance ever created, and each dead one raised <c>StateHasChanged</c> on itself.</para>
    /// </summary>
    public void Dispose()
    {
        NavState.OnSaveSettings -= SaveSettings;
    }  

    protected override async Task OnInitializedAsync()
    {
        await LoadSettings();
    }

    private async Task LoadSettings()
    {
        try
        {
            // Load each settings category
            _settings.Dymola = await SettingsService.GetAsync("Dymola", new DymolaSettings());
            _settings.OpenModelica = await SettingsService.GetAsync("OpenModelica", new OpenModelicaSettings());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading settings: {ex.Message}");
        }
    }

    private async void SaveSettings()
    {
        try
        {
            await SettingsService.SetAsync("Dymola", _settings.Dymola);
            await SettingsService.SetAsync("OpenModelica", _settings.OpenModelica);

            StateHasChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Restores the external tool settings to their defaults, persists them, and refreshes the UI.
    /// </summary>
    public async Task ResetToDefaultsAsync()
    {
        try
        {
            _settings.Dymola = new DymolaSettings();
            _settings.OpenModelica = new OpenModelicaSettings();
            _showDymolaWarning = false;
            _showOpenModelicaWarning = false;

            await SettingsService.SetAsync("Dymola", _settings.Dymola);
            await SettingsService.SetAsync("OpenModelica", _settings.OpenModelica);

            StateHasChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error resetting settings: {ex.Message}");
        }
    }

    private async Task BrowseForDymolaFolder()
    {
        try
        {
            var folder = await FilePickerService.PickFolderAsync("Select Dymola installation directory");
            if (!string.IsNullOrEmpty(folder))
            {
                _settings.Dymola.DymolaPath = Path.Combine(folder, "bin64", "dymola.exe");
                _showDymolaWarning = !File.Exists(_settings.Dymola.DymolaPath);
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            // A picker that throws used to leave the button doing nothing and no trace anywhere —
            // not even in the log — which is indistinguishable from the user having cancelled.
            LoggingService.Warn("SettingsExternalTools", $"Could not browse for the Dymola folder: {ex.Message}");
        }
    }

    private async Task BrowseForOpenModelicaFolder()
    {
        try
        {
            var folder = await FilePickerService.PickFolderAsync("Select OpenModelica installation directory");
            if (!string.IsNullOrEmpty(folder))
            {
                _settings.OpenModelica.OmcPath = Path.Combine(folder, "bin", "omc.exe");
                _showOpenModelicaWarning = !File.Exists(_settings.OpenModelica.OmcPath);
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            LoggingService.Warn("SettingsExternalTools",
                $"Could not browse for the OpenModelica folder: {ex.Message}");
        }
    }
}
