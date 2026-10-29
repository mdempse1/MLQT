# MLQT.Shared

Loaded when working in `MLQT.Shared/` or `MLQT.Shared.Tests/` — the Blazor components, pages,
layouts and the application state every host renders. The repository-wide rules are in the root
`CLAUDE.md`. Testing this project is the `gui-testing` skill; the graph component is the `cytoscape`
skill.

## Key files

| File | Purpose |
|------|---------|
| `MLQT.Shared/MlqtServiceCollectionExtensions.cs` | `AddMlqtCore()` — the service registrations every host needs |
| `MLQT.Shared/Layout/MainLayout.razor` | Main layout, analysis pipeline orchestration |
| `MLQT.Shared/Models/AppState.cs` | Application state and cross-component events |
| `MLQT.Shared/Components/LibraryBrowser.razor` | Model tree navigation, VCS operation UI |
| `MLQT.Shared/Components/SettingsRepositories.razor` | Repository settings with formatting/style rules |
| `MLQT.Shared/Pages/CodeReview.razor` | Code viewer, diff, findings, external tool checks |
| `MLQT.Shared/Pages/Dependencies.razor` | Impact analysis with Cytoscape network graph |
| `MLQT.Shared/Helpers/MonitorPause.cs` / `VcsPipelineQueue.cs` | Holding the file monitor off through a VCS operation; queuing the analysis pipelines VCS work starts |
| `MLQT.Shared/HostAssetManifest.cs` | The scripts and stylesheets every host page must load, in order (`desktop-host` skill) |
| `MLQT.Shared/Pages/SelfTest.razor` / `SelfTestHost.razor` | The `/selftest` probes a host is compared on (`desktop-host` skill) |

## Application State (AppState)

Centralized state container in `MLQT.Shared/Models/AppState.cs`:
- **Model Selection**: `ModelID`, `SelectedModelIDs`, `SelectionMode`; a visit history behind `GoBack()`/`GoForward()` (`CanGoBack`, `CanGoForward`, `Back`), cleared by `ResetNavigation()`
- **Startup**: `StartupStep`, set through `StartupProgress()`, raising `OnStartupProgressChanged`
- **Deferred Analysis**: `IsDeferredMode`, `HasDependencyAnalysisRun`, `HasStyleCheckingRun`, `HasExternalResourcesAnalyzed`
- **Events**:
  - Model/UI: `OnChangeModel`, `OnSelectedModelsChanged`, `OnEnableMultiSelect`, `OnModelContentChanged`, `OnThemeChanged`, `OnStartupProgressChanged`
  - Settings: `OnSaveSettings`, `OnClearLogMessages`, `OnRepositorySettingsApplied`
  - VCS: `OnVcsFilesChanged`, `OnVcsModelsChanged`, `OnVcsWorkChanged` — `IsVcsWorkInProgress` is true while a VCS operation or the analysis pipeline one started is running, and no VCS operation may start then (B326); take `BeginVcsWork()`, and queue pipelines through `VcsPipelineQueue` (`MLQT.Shared/Helpers`), which counts them from the moment they are queued. Format All Files is VCS work too, and goes through its `TryEnqueue`, which refuses while any VCS work runs (B385)
  - Projects: `OnProjectSwitchStarting`, `OnProjectChanged`, `OnProjectSwitchAbandoned` (a switch that ended without `OnProjectChanged`; B435). A switch from the settings panel goes through `SettingsRepositories.SwitchToProjectAsync`, which raises the first and, when the project did not change, the last
  - Deferred analysis: `OnRunDeferredDependencies`, `OnRunDeferredStyleChecking`, `OnRunDeferredExternalResources`, `OnRunAllDeferredAnalysis`, `OnDeferredAnalysisCompleted`
  - Formatting: `OnFormatChangedFilesForCommit`
- Use the methods (`ChangeModelID()`, `SetSelectedModels()`, `ChangeSelectionMode()`, `RepositorySettingsApplied()`, `VcsFilesChanged()`, etc.) rather than assigning the property, **wherever the property has one** — the method is what raises the event, and an assignment leaves every subscriber unaware. A few members are plain session memory with no event and no method (`MetricsScope`, which the Metrics tab writes so its scope survives the tab being recreated); those are assigned directly and say so at the declaration
- AppState carries no "library loaded/cleared" state — the set of loaded libraries is published by
  `ILibraryDataService.OnLibrariesChanged`/`OnTreeDataChanged` and `IRepositoryService.OnProjectChanged`

## User Interface

UI components should be used from MudBlazor.  Any custom components should be created as Components in MLQT.Shared — or in `MLQT.UI.Theme` when nothing about them is MLQT's, as `ProgressDialog` is (`MLQT.UI.Theme/CLAUDE.md` says what belongs there). A style rule follows the same line: one any tool would want goes in the theme's stylesheets, one about an MLQT page stays in `wwwroot/app.css` or the component's `.razor.css`.

Use the following styling guidelines
* Use Small size options when available
* Use Dense styling options when available
* Use minimal padding and margin spacing
* RowStack components should have spacing=0

**Typography.** `Typo.body1` is the default for ordinary text and `Typo.body2` for code, file paths
and de-emphasised values — but the UI uses a hierarchy above that, and a component built to
"body1 for everything" looks wrong beside the ones already there. What the existing components do:

| Typo | Used for |
|------|----------|
| `h6` | A dialog's title |
| `subtitle1` | The name of the thing a panel is about (a repository, an external tool) |
| `subtitle2` | A section heading inside a panel |
| `body1` | Ordinary text — the default, and by far the most common |
| `body2` | Modelica code, file paths, and values shown beside a label |
| `caption` | Explanatory text under a control, and secondary detail in a tree |

**Component logic goes in a code-behind file** (`Foo.razor` + `Foo.razor.cs` with `public partial class Foo`), not in an `@code { }` block — an `@code` block is for a component with no logic worth testing. Services in such a component are injected with `[Inject]` properties, not `@inject` directives, because a directive-injected service cannot be set by a test. The full rule, including which components are exempt, is in `CODING_GUIDELINES.md` §Blazor Patterns. The sweep is done — every logic-bearing component is converted (the exempt ones are listed in `build/code-behind-exemptions.json`) and `MLQT.Shared.Tests/CodeBehindPolicyTests.cs` holds the line.

**Thread Safety**: In Razor event handlers, use `await InvokeAsync(StateHasChanged)`.

**A dialog the application opens and closes** — a progress dialog, shown while work runs — is a `ProgressDialog` (`MLQT.UI.Theme/Components/ProgressDialog.razor`, since it is generic), never `<MudDialog @bind-Visible="_running">`. MudBlazor opens an inline dialog asynchronously, and a flag cleared before the dialog has finished opening is written back to `true` when it does: work faster than a browser round trip left "Formatting all files" on screen for good (the B414 journey on CI). A dialog the user opens and closes with its own buttons cannot be withdrawn before it has opened, and stays a plain bound `MudDialog`.

**Graph Visualization**: Interactive network graphs use the `CytoscapeGraph` component (`Components/CytoscapeGraph.razor`) backed by Cytoscape.js. It accepts generic `DiagramNode`/`DiagramEdge` parameters. See the `cytoscape` skill for full details.
