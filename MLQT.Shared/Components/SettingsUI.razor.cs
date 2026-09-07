using System.Threading.Tasks;

namespace MLQT.Shared.Components;

public partial class SettingsUI : IDisposable
{
    [Inject] private ISettingsService SettingsService { get; set; } = null!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
    [Inject] private AppState NavState { get; set; } = null!;

    private AppSettings _settings = new();
    private bool _customUIStyles { get; set; } = false;
    private bool _customSyntaxStyles { get; set; } = false;

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
            _settings.UI = await SettingsService.GetAsync("UI", new UISettings());
            _settings.SyntaxHighlighting = await SettingsService.GetAsync("SyntaxHighlighting", new SyntaxHighlightingSettings());
            _customUIStyles = _settings.UI.Theme == Theme.Custom;
            _customSyntaxStyles = _settings.SyntaxHighlighting.ThemeName == "Custom";
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
            await SettingsService.SetAsync("UI", _settings.UI);
            await SettingsService.SetAsync("SyntaxHighlighting", _settings.SyntaxHighlighting);
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Restores the UI and syntax highlighting settings to their defaults,
    /// persists them, applies the theme immediately, and refreshes the UI.
    /// </summary>
    public async Task ResetToDefaultsAsync()
    {
        try
        {
            _settings.UI = new UISettings();
            _settings.SyntaxHighlighting = new SyntaxHighlightingSettings();
            _customUIStyles = _settings.UI.Theme == Theme.Custom;
            _customSyntaxStyles = _settings.SyntaxHighlighting.ThemeName == "Custom";

            await SettingsService.SetAsync("UI", _settings.UI);
            await SettingsService.SetAsync("SyntaxHighlighting", _settings.SyntaxHighlighting);

            NavState.ThemeChanged(_settings.UI);
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error resetting settings: {ex.Message}");
        }
    }

    private async Task ApplyPresetUITheme(string themeName)
    {
        switch (themeName)
        {
            case "Light":
                _customUIStyles = false;
                _settings.UI.Theme = Theme.Light;
                // Reset custom colors to defaults to match the light palette
                _settings.UI.CustomBlack = "#272c34";
                _settings.UI.CustomWhite = "#ffffff";
                _settings.UI.CustomPrimary = "#6a70b1";
                _settings.UI.CustomPrimaryContrastText = "#ffffff";
                _settings.UI.CustomSecondary = "#666666";
                _settings.UI.CustomSecondaryContrastText = "#ffffff";
                _settings.UI.CustomTertiary = "#a18ac1";
                _settings.UI.CustomTertiaryContrastText = "#ffffff";
                _settings.UI.CustomInfo = "#cccccc";
                _settings.UI.CustomInfoContrastText = "#ffffff";
                break;
            case "Dark":
                _customUIStyles = false;
                _settings.UI.Theme = Theme.Dark;
                break;
            case "Custom":
                _customUIStyles = true;
                _settings.UI.Theme = Theme.Custom;
                break;
            default:
                _customUIStyles = false;
                _settings.UI.Theme = Theme.Light;
                break;
        }

        NavState.ThemeChanged(_settings.UI);
        await ApplyPresetSyntaxTheme(_settings.SyntaxHighlighting.ThemeName);
        await SettingsService.SetAsync("UI", _settings.UI);
    }

    // Individual color change handlers — method references are stable across Blazor renders,
    // preventing unnecessary ColorPicker re-renders that could disrupt user input.
    private void OnBlackChanged(string v) { _settings.UI.CustomBlack = v; ApplyCustomTheme(); }
    private void OnWhiteChanged(string v) { _settings.UI.CustomWhite = v; ApplyCustomTheme(); }
    private void OnPrimaryChanged(string v) { _settings.UI.CustomPrimary = v; ApplyCustomTheme(); }
    private void OnPrimaryContrastTextChanged(string v) { _settings.UI.CustomPrimaryContrastText = v; ApplyCustomTheme(); }
    private void OnSecondaryChanged(string v) { _settings.UI.CustomSecondary = v; ApplyCustomTheme(); }
    private void OnSecondaryContrastTextChanged(string v) { _settings.UI.CustomSecondaryContrastText = v; ApplyCustomTheme(); }
    private void OnTertiaryChanged(string v) { _settings.UI.CustomTertiary = v; ApplyCustomTheme(); }
    private void OnTertiaryContrastTextChanged(string v) { _settings.UI.CustomTertiaryContrastText = v; ApplyCustomTheme(); }
    private void OnInfoChanged(string v) { _settings.UI.CustomInfo = v; ApplyCustomTheme(); }
    private void OnInfoContrastTextChanged(string v) { _settings.UI.CustomInfoContrastText = v; ApplyCustomTheme(); }

    private void ApplyCustomTheme()
    {
        NavState.ThemeChanged(_settings.UI);
    }

    private async Task ApplyPresetSyntaxTheme(string themeName)
    {
        var darkMode = _settings.UI.Theme == Theme.Dark;
        _settings.SyntaxHighlighting = themeName switch
        {
            "Dymola" => SyntaxHighlightingSettings.GetDymolaTheme(darkMode),
            "OpenModelica" => SyntaxHighlightingSettings.GetOpenModelicaTheme(darkMode),
            "VSCode" => (darkMode ? SyntaxHighlightingSettings.GetDarkTheme() : SyntaxHighlightingSettings.GetLightTheme()),
            _ => _settings.SyntaxHighlighting
        };
        _settings.SyntaxHighlighting.ThemeName = themeName;
        _customSyntaxStyles = false;
        StateHasChanged();
    }

    private string GetPreviewStyle()
    {
        return $"background-color: {_settings.SyntaxHighlighting.BackgroundColor}; " +
               $"color: {_settings.SyntaxHighlighting.TextColor}; " +
               $"border: 1px solid {_settings.SyntaxHighlighting.BorderColor}; " +
               $"border-radius: 4px; " +
               $"overflow: auto; " +
               $"font-family: var(--mud-typography-default-family);";
    }

    private async Task SetCustomSyntaxTheme() {
        _customSyntaxStyles = true;
        _settings.SyntaxHighlighting.ThemeName = "Custom";
    }

    // Individual color change handlers — method references are stable across Blazor renders,
    // preventing unnecessary ColorPicker re-renders that could disrupt user input.
    private void OnBackgroundChanged(string v) { _settings.SyntaxHighlighting.BackgroundColor = v; }
    private void OnTextChanged(string v) { _settings.SyntaxHighlighting.TextColor = v; }
    private void OnBorderChanged(string v) { _settings.SyntaxHighlighting.BorderColor = v; }
    private void OnLineNumberChanged(string v) { _settings.SyntaxHighlighting.LineNumberColor = v; }
    private void OnKeywordChanged(string v) { _settings.SyntaxHighlighting.KeywordColor = v; }
    private void OnTypesChanged(string v) { _settings.SyntaxHighlighting.TypeColor = v; }
    private void OnIdentifiersChanged(string v) { _settings.SyntaxHighlighting.IdentColor = v; }
    private void OnNamesTextChanged(string v) { _settings.SyntaxHighlighting.NameColor = v; }
    private void OnFunctionsChanged(string v) { _settings.SyntaxHighlighting.FunctionColor = v; }
    private void OnOperatorsChanged(string v) { _settings.SyntaxHighlighting.OperatorColor = v; }
    private void OnNumbersChanged(string v) { _settings.SyntaxHighlighting.NumberColor = v; }
    private void OnStringsChanged(string v) { _settings.SyntaxHighlighting.StringColor = v; }
    private void OnCommentsChanged(string v) { _settings.SyntaxHighlighting.CommentColor = v; }
}
