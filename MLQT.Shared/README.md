# MLQT.Shared

Shared Blazor components, pages, layouts, and models for the MLQT application. This project contains all UI code that is hosted within the MAUI application via BlazorWebView.

## Overview

MLQT.Shared is a Razor class library providing the complete UI layer:

- **Pages** - Full application pages (library browser, code review, settings, etc.)
- **Components** - Reusable Blazor components (code viewer, diff viewer, branch selector, etc.)
- **Models** - Application state and settings
- **Layout** - Main application layout

The UI is built using MudBlazor for Material Design components.

## Key Concepts

### Application State (AppState)

`AppState` is a centralized state container registered as a singleton. Components subscribe to its events for cross-component communication.

```csharp
@inject AppState AppState

@code {
    protected override void OnInitialized()
    {
        AppState.OnChangeModel += HandleModelChanged;
        AppState.OnLibraryLoaded += HandleLibraryLoaded;
    }

    private async void HandleModelChanged()
    {
        await InvokeAsync(() =>
        {
            // Access AppState.ModelID for the newly selected model
            StateHasChanged();
        });
    }

    public void Dispose()
    {
        AppState.OnChangeModel -= HandleModelChanged;
        AppState.OnLibraryLoaded -= HandleLibraryLoaded;
    }
}
```

**State properties:**

| Property | Type | Description |
|----------|------|-------------|
| `ModelID` | `string` | Currently selected model ID |
| `SelectedModelIDs` | `HashSet<string>` | Selected models in multi-select mode |
| `SelectionMode` | `SelectionMode` | Single or multi-selection |
| `IsLibraryLoaded` | `bool` | Whether a library is currently loaded |

**State methods** (always use methods, not direct property access):

| Method | Description |
|--------|-------------|
| `ChangeModelID(string)` | Changes the current model and fires `OnChangeModel` |
| `SetSelectedModels(IEnumerable<string>)` | Sets multi-select models, fires `OnSelectedModelsChanged` |
| `ClearSelectedModels()` | Clears multi-select, fires `OnSelectedModelsChanged` |
| `ChangeSelectionMode(SelectionMode)` | Changes selection mode, fires `OnEnableMultiSelect` |
| `LibraryLoaded()` | Notifies library loaded, fires `OnLibraryLoaded` |
| `LibraryCleared()` | Clears state and fires `OnLibraryCleared` |
| `SaveSettings()` | Fires `OnSaveSettings` |
| `ClearLogMessages()` | Fires `OnClearLogMessages` |

### Application Settings (AppSettings)

```csharp
public class AppSettings
{
    public UISettings UI { get; set; }                              // Dark mode, theme
    public EditorSettings Editor { get; set; }                      // Font, tab size, line numbers
    public SyntaxHighlightingSettings SyntaxHighlighting { get; set; } // Color scheme
    public DymolaSettings Dymola { get; set; }                     // Dymola connection config
    public OpenModelicaSettings OpenModelica { get; set; }         // OMC path config
    public StyleCheckingSettings StyleChecking { get; set; }       // Active style rules
}
```

Predefined syntax themes are available:

```csharp
var lightTheme = SyntaxHighlightingSettings.GetLightTheme();
var darkTheme = SyntaxHighlightingSettings.GetDarkTheme();
var dymolaTheme = SyntaxHighlightingSettings.GetDymolaTheme();
var omTheme = SyntaxHighlightingSettings.GetOpenModelicaTheme();
```

### Pages

| Page | Description |
|------|-------------|
| `Index.razor` | Main page with library browser and code viewer |
| `CodeReview.razor` | Code review, style violations, and file change diffs |
| `Dependencies.razor` | Impact analysis with network graph visualization |
| `ExternalResources.razor` | External resource tree with filtering and warnings |
| `Settings.razor` | Application settings (UI, editor, tools, style rules) |

### Components

| Component | Description |
|-----------|-------------|
| `LibraryBrowser.razor` | Tree view for browsing loaded Modelica libraries |
| `CodeViewer.razor` | Syntax-highlighted Modelica code display |
| `DiffViewer.razor` | Side-by-side or unified diff view for file changes |
| `BranchSelector.razor` | Branch selection and management widget |
| `ChangeReview.razor` | Review uncommitted file changes |
| `ColorPicker.razor` | Color selection for syntax highlighting |
| `SettingsUI.razor` | UI preference controls (dark mode, theme) |
| `SettingsSyntaxHighlighting.razor` | Syntax color configuration |
| `SettingsStyleChecking.razor` | Style rule toggles, spell check settings, language dictionaries, custom dictionary management |
| `SettingsExternalTools.razor` | Dymola/OpenModelica configuration |
| `SettingsRepositories.razor` | Repository management |

### UI Guidelines

- Use MudBlazor components with **Small** size options where available
- Use **Dense** styling options
- Use minimal padding and margin spacing
- RowStack components should have `spacing=0`
- Use `Typo.body1` for all text except code
- Use `Typo.body2` for code

### Thread Safety in Components

When handling events from external services (not Blazor UI events), wrap state changes:

```csharp
private async void OnExternalEvent()
{
    await InvokeAsync(() =>
    {
        _data = "Updated";
        StateHasChanged();
    });
}
```

## License

MIT License — see [LICENSE](../LICENSE) for details.

## Dependencies

**NuGet Packages:**
- MudBlazor 9.0.0
- CodeBeam.MudBlazor.Extensions 9.0.0
- MudBlazor.Extensions 8.15.1
- Microsoft.AspNetCore.Components.Web 10.0.3
- NLog 6.1.0

**Project References:**
- DymolaInterface
- MLQT.Services
- ModelicaParser
- ModelicaGraph
- OpenModelicaInterface
