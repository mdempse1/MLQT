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
    }
}
```

**State properties:**

| Property | Type | Description |
|----------|------|-------------|
| `ModelID` | `string` | Currently selected model ID |
| `SelectedModelIDs` | `HashSet<string>` | Selected models in multi-select mode |
| `SelectionMode` | `SelectionMode` | Single or multi-selection |

`AppState` deliberately carries no "library loaded/cleared" state or event. The set of loaded
libraries is owned by the services that manage it, and they publish the changes:
`ILibraryDataService.OnLibrariesChanged` / `OnTreeDataChanged` for library content, and
`IRepositoryService.OnProjectChanged` (with `AppState.OnProjectSwitchStarting`) for a project switch.

**State methods** (always use methods, not direct property access):

| Method | Description |
|--------|-------------|
| `ChangeModelID(string)` | Changes the current model and fires `OnChangeModel` |
| `SetSelectedModels(IEnumerable<string>)` | Sets multi-select models, fires `OnSelectedModelsChanged` |
| `ClearSelectedModels()` | Clears multi-select, fires `OnSelectedModelsChanged` |
| `ChangeSelectionMode(SelectionMode)` | Changes selection mode, fires `OnEnableMultiSelect` |
| `SaveSettings()` | Fires `OnSaveSettings` |
| `ClearLogMessages()` | Fires `OnClearLogMessages` |

### Application Settings (AppSettings)

```csharp
public class AppSettings
{
    public UISettings UI { get; set; }                              // Theme, custom colors, mode
    public SyntaxHighlightingSettings SyntaxHighlighting { get; set; } // Color scheme
    public DymolaSettings Dymola { get; set; }                     // Dymola connection config
    public OpenModelicaSettings OpenModelica { get; set; }         // OMC path config
    public ReferenceLibrarySettings ReferenceLibraries { get; set; } // Libraries loaded for reference only
}
```

Style rules are **not** here. They belong to the repository, in its own `.mlqt/settings.json`
(`Repository.StyleSettings`), so they are committed with the library and CI checks it by the same
rules the app does. There is no application-wide rule set to fall back on.

Predefined syntax themes are available:

```csharp
var lightTheme = SyntaxHighlightingSettings.GetLightTheme();
var darkTheme = SyntaxHighlightingSettings.GetDarkTheme();
var dymolaTheme = SyntaxHighlightingSettings.GetDymolaTheme(darkMode: false);
var omTheme = SyntaxHighlightingSettings.GetOpenModelicaTheme(darkMode: false);
```

### Pages

| Page | Description |
|------|-------------|
| `Index.razor` | Main page with library browser and code viewer |
| `CodeReview.razor` | Code review, style findings, and file change diffs |
| `Dependencies.razor` | Impact analysis with network graph visualization |
| `ExternalResources.razor` | External resource tree with filtering and warnings |
| `MetricsDashboard.razor` | Coverage by dimension, the debt burndown trend, and the snapshot history |
| `Settings.razor` | Application settings (UI, external tools, reference libraries, repositories) |

### Components

| Component | Description |
|-----------|-------------|
| `LibraryBrowser.razor` | Tree view for browsing loaded Modelica libraries |
| `CodeViewer.razor` | Syntax-highlighted Modelica code display |
| `DiffViewer.razor` | Side-by-side or unified diff view for file changes |
| `BranchSelector.razor` | Branch selection and management widget |
| `ChangeReview.razor` | Review uncommitted file changes |
| `ColorPicker.razor` | Color selection for syntax highlighting |
| `CurrentModelDisplay.razor` | The selected class, shown in the toolbar |
| `CytoscapeGraph.razor` | Interactive network graph (Cytoscape.js) for dependencies and impact |
| `SettingsUI.razor` | UI preference controls (theme, custom colors, and syntax-highlighting color scheme) |
| `NamingStyleSelect.razor` | Naming-style dropdown used by the style-checking settings |
| `RuleSeverityPicker.razor` | Off / Info / Warning / Error selector for one rule |
| `SettingsExternalTools.razor` | Dymola/OpenModelica configuration |
| `SettingsReferenceLibraries.razor` | Libraries loaded so references resolve, never reported on |
| `SettingsRepositories.razor` | Repositories and their per-repository rules: style, formatting, naming, spell-check languages |
| `SettingsRepositoryDictionary.razor` | A repository's accepted spellings (`.mlqt/dictionary.txt`) |

There is no application-wide style-rule page: the rules belong to each repository and are edited in
`SettingsRepositories.razor`, which is what keeps them committed with the library and identical in
CI. `SettingsStyleChecking.razor` was the global page and has been removed.

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
