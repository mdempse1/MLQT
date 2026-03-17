# MLQT.Services

Business logic services for the MLQT application. Provides library management, repository integration, file monitoring, code review, style checking, impact analysis, external resource tracking, and simulation tool interfaces.

## Overview

MLQT.Services contains the core application logic as injectable services, each with an interface in `Interfaces/`. Services are registered as singletons in `MauiProgram.cs` and communicate via events for cross-component updates.

## Key Concepts

### Service Architecture

All services follow the pattern:
1. Interface defined in `Interfaces/` (e.g., `ILibraryDataService`)
2. Implementation in the project root (e.g., `LibraryDataService`)
3. Registered as singleton in `MauiProgram.cs`
4. Events for notifying UI components of state changes

### Core Services

| Service | Interface | Purpose |
|---------|-----------|---------|
| `LibraryDataService` | `ILibraryDataService` | Manages loaded Modelica libraries, combined graph, tree data |
| `RepositoryService` | `IRepositoryService` | Git/SVN repository management, library discovery, VCS operations |
| `FileMonitoringService` | `IFileMonitoringService` | FileSystemWatcher-based change detection with debouncing |
| `CodeReviewService` | `ICodeReviewService` | Log messages and issues from parsing/style checking |
| `StyleCheckingService` | `IStyleCheckingService` | Background style rule checking for models |
| `ImpactAnalysisService` | `IImpactAnalysisService` | Dependency impact analysis with network graph visualization |
| `ExternalResourceService` | `IExternalResourceService` | External resource analysis, validation, and monitoring |
| `CustomDictionaryService` | `ICustomDictionaryService` | User custom word list for spell checking |
| `DictionaryManagerService` | `IDictionaryManagerService` | Hunspell dictionary management (bundled + imported) |
| `DymolaCheckingService` | `IModelCheckingService` | Model checking via Dymola |
| `OpenModelicaCheckingService` | `IModelCheckingService` | Model checking via OpenModelica |

### Platform Services

These interfaces are implemented by the MAUI host project:

| Interface | Purpose |
|-----------|---------|
| `ISettingsService` | Persistent key-value settings storage |
| `IFilePickerService` | File and folder selection dialogs |

## Usage

### Library Management (ILibraryDataService)

```csharp
// Load a library from a single file
LoadedLibrary lib = await libraryDataService.AddLibraryFromFileAsync("package.mo");

// Load from a directory
LoadedLibrary lib = await libraryDataService.AddLibraryFromDirectoryAsync(@"C:\MyLibrary");

// Access the combined graph (all libraries merged)
DirectedGraph graph = libraryDataService.CombinedGraph;

// Get model by ID
ModelNode? model = libraryDataService.GetModelById("MyLibrary.MyModel");

// Get tree data for the UI
var topItems = await libraryDataService.GetTopLevelTreeItemsAsync();
var children = await libraryDataService.GetChildTreeItemsAsync(parentNode);

// Reload a changed file
List<string> updatedModelIds = await libraryDataService.ReloadFileAsync("Models.mo");

// Subscribe to changes
libraryDataService.OnLibrariesChanged += () => { /* refresh UI */ };
libraryDataService.OnTreeDataChanged += () => { /* rebuild tree */ };

// Remove/clear
libraryDataService.RemoveLibrary(libraryId);
libraryDataService.ClearAllLibraries();
```

### Repository Management (IRepositoryService)

```csharp
// Add a Git/SVN repository
var result = await repositoryService.AddRepositoryAsync(@"C:\Projects\MyRepo");
if (result.Success)
{
    // Libraries discovered in the repository
    foreach (var lib in result.DiscoveredLibraries)
        Console.WriteLine($"Found: {lib.LibraryName} at {lib.RelativePath}");
}

// Load discovered libraries
await repositoryService.LoadLibrariesAsync(repositoryId);

// VCS operations
var log = repositoryService.GetLogEntries(repositoryId);
var changes = repositoryService.GetWorkingCopyChanges(repositoryId);
var branches = repositoryService.GetBranches(repositoryId, includeRemote: true);

var progress = new Progress<string>(msg => Console.WriteLine(msg));
await repositoryService.CommitAsync(repositoryId, "Fix equations", filesToCommit, progress);
await repositoryService.SwitchBranchAsync(repositoryId, "feature/new-model");
await repositoryService.CreateBranchAsync(repositoryId, "release/v2.0");
await repositoryService.UpdateRepositoryAsync(repositoryId);

// File content at revision
string? oldContent = repositoryService.GetFileContentAtRevision(
    repositoryId, "Models/MyModel.mo", "v1.0.0");
```

**SVN Branch Directory Support**: For SVN repositories, `RepositoryService` automatically passes `repository.StyleSettings?.SvnBranchDirectories` to SVN-specific overloads of `GetCurrentBranch`, `GetBranches`, `GetLogEntries`, and `CreateBranch`. This allows SVN repositories with non-standard branch directory layouts (e.g., directories other than `trunk/branches/tags`) to be handled correctly without manual configuration per call.

### File Monitoring (IFileMonitoringService)

```csharp
// Start monitoring a repository's local path
fileMonitoringService.StartMonitoring(repositoryId, @"C:\Projects\MyRepo");

// Subscribe to file changes
fileMonitoringService.OnFileChanged += (changeInfo) =>
{
    Console.WriteLine($"{changeInfo.ChangeType}: {changeInfo.FilePath}");
};

// Get pending changes
var pending = fileMonitoringService.GetPendingChangesForRepository(repositoryId);
var summary = fileMonitoringService.GetPendingChangesSummary();

// Clear pending changes
fileMonitoringService.ClearPendingChanges(repositoryId);
```

### Code Review (ICodeReviewService)

```csharp
// Add log messages from parsing/style checking
codeReviewService.AddLogMessage(new LogMessage("MyModel", "Warning", 42, "Unused variable"));

// Subscribe to changes
codeReviewService.OnLogMessagesChanged += () => { /* refresh UI */ };

// Access messages
List<LogMessage> messages = codeReviewService.LogMessages;
```

### Style Checking (IStyleCheckingService)

```csharp
// Check a single model
var violations = await styleCheckingService.CheckModelAsync(modelDefinition, settings);

// Start background checking for a single repository (async)
await styleCheckingService.StartBackgroundCheckingAsync(repository);

// Start background checking for a single repository (synchronous, fire-and-forget)
styleCheckingService.StartBackgroundChecking(repository);

// Start background checking for all repositories at once
// OnProgressChanged fires true only after ALL repos finish (not per-repo)
styleCheckingService.StartBackgroundCheckingForRepositories(repositories);

// Re-check specific models after file changes (clears previous violations first)
await styleCheckingService.CheckModelsAsync(changedModelIds, graph);

// Subscribe to progress (bool = allComplete)
styleCheckingService.OnProgressChanged += (allComplete) =>
{
    if (allComplete) Console.WriteLine("All style checks finished");
};

// Subscribe to violation results
styleCheckingService.OnViolationsFound += (violations) =>
{
    foreach (var v in violations)
        Console.WriteLine($"{v.ModelName}: {v.Summary}");
};
```

### Custom Dictionary (ICustomDictionaryService)

```csharp
// Load custom dictionary from disk
await customDictionaryService.LoadAsync();

// Add a word
await customDictionaryService.AddWordAsync("Dymola");

// Remove a word
await customDictionaryService.RemoveWordAsync("typo");

// Access current words
IReadOnlyCollection<string> words = customDictionaryService.CustomWords;

// Import/export/merge word lists
await customDictionaryService.ImportAsync(@"C:\words.txt");    // replaces
await customDictionaryService.MergeAsync(@"C:\more-words.txt"); // unions
await customDictionaryService.ExportAsync(@"C:\export.txt");

// Subscribe to changes
customDictionaryService.OnDictionaryChanged += () => { /* reload spell checker */ };
```

### Dictionary Management (IDictionaryManagerService)

```csharp
// List all available dictionaries (bundled + imported)
IReadOnlyList<DictionaryInfo> dicts = dictionaryManagerService.GetAvailableDictionaries();
foreach (var d in dicts)
    Console.WriteLine($"{d.LanguageCode}: {d.DisplayName} (bundled: {d.IsBundled})");

// Import a Hunspell dictionary pair
string? langCode = await dictionaryManagerService.ImportDictionaryAsync(
    @"C:\dicts\de_DE.aff", @"C:\dicts\de_DE.dic");

// Remove an imported dictionary
await dictionaryManagerService.RemoveImportedDictionaryAsync("de_DE");

// Get file paths for an imported dictionary
DictionarySource? source = dictionaryManagerService.GetImportedDictionaryPaths("de_DE");

// Subscribe to changes
dictionaryManagerService.OnDictionariesChanged += () => { /* refresh UI */ };
```

### Impact Analysis (IImpactAnalysisService)

```csharp
// Analyze impact of changes to selected models
var result = impactAnalysisService.AnalyzeImpact(
    graph, selectedModelIds, svgWidth: 700, svgHeight: 450);

Console.WriteLine($"Impacted models: {result.ImpactedModelsCount}");

// Access network graph data for visualization
foreach (var node in result.Nodes)
    Console.WriteLine($"{node.FullName} ({(node.IsImpacted ? "impacted" : "selected")})");

// Find impacted models without visualization
HashSet<string> impacted = impactAnalysisService.FindImpactedModels(graph, selectedModelIds);
```

### External Resource Service (IExternalResourceService)

```csharp
// Analyze all resources in the graph
await externalResourceService.AnalyzeResourcesAsync(graph);

// Get resources for a specific model
var resources = externalResourceService.GetResourcesForModel(modelId);

// Get models referencing a specific file
var modelIds = externalResourceService.GetModelsReferencingResource(@"C:\lib\data.mat");

// Get warnings (missing files, absolute paths)
var warnings = externalResourceService.GetWarnings();
```

### Model Checking (IModelCheckingService)

Both `DymolaCheckingService` and `OpenModelicaCheckingService` implement `IModelCheckingService`:

```csharp
// Check a single model
var result = await checkingService.CheckModelAsync(modelNode, graph);
if (!result.Success)
    Console.WriteLine($"Check failed: {result.ErrorMessage}");

// Start checking with progress tracking
checkingService.OnProgressChanged += (progress) =>
    Console.WriteLine($"Checked {progress.ModelsChecked}/{progress.TotalModels}");

checkingService.OnModelChecked += (result) =>
    Console.WriteLine($"{result.ModelId}: {(result.Success ? "OK" : result.ErrorMessage)}");

await checkingService.StartCheckingAsync(modelNode, graph, cancellationToken);
```

### Formatting and Saving Libraries (ModelicaPackageSaver)

`ModelicaPackageSaver` (in `Helpers/`) is the key helper class for formatting and saving Modelica libraries to disk. It renders models through `ModelicaRenderer` with configurable formatting rules and writes the resulting directory structure (package.mo, package.order, standalone model files).

```csharp
// Save a library to a directory with formatting applied
var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
    graph, modelIds, rootDirectory,
    showAnnotations: true,
    oneOfEachSection: true,
    importsFirst: true,
    componentsBeforeClasses: false);

// Save with formatting exclusions — excluded models keep their original code
var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
    graph, modelIds, rootDirectory,
    showAnnotations: true,
    oneOfEachSection: true,
    importsFirst: true,
    componentsBeforeClasses: false,
    excludedModelIds: excludedIds);

// SaveResult contains written file paths and model-to-file mappings
foreach (var file in result.WrittenFiles)
    Console.WriteLine($"Wrote: {file}");
```

**Formatting Exclusion**: Models can be excluded from formatting at multiple levels:
- `ModelicaPackageSaver.SaveLibraryToDirectoryWithResult` accepts an `excludedModelIds` parameter — excluded models use their original `ModelicaCode` instead of being rendered through the formatter.
- `StyleCheckingWorker` passes `isExcludedFromFormatting` from the repository's style settings when calling `RunStyleChecking`, so excluded models are not flagged for formatting violations.
- `SaveChangedFilesWithFormattingAsync` in `MainLayout` skips excluded models during incremental formatting after VCS operations.

## Data Types

### Library & Repository

| Type | Description |
|------|-------------|
| `LoadedLibrary` | Library metadata (Id, Name, SourcePath, SourceType, ModelIds, TopLevelModelIds) |
| `Repository` | Repository metadata (Id, Name, LocalPath, VcsType, CurrentBranch, LibraryIds) |
| `AddRepositoryResult` | Result of adding a repository (Success, Repository, DiscoveredLibraries) |
| `DiscoveredLibraryInfo` | Library found during discovery (LibraryName, RelativePath, FullPath) |
| `RepositoryVcsType` | Enum: Local, Git, SVN |
| `LibrarySourceType` | Enum: File, Directory, Zip, Git, SVN |

### File Monitoring

| Type | Description |
|------|-------------|
| `FileChangeInfo` | File change event (FilePath, ChangeType, RepositoryId, DetectedAt) |
| `FileChangeType` | Enum: Added, Modified, Deleted, Renamed |
| `PendingChangesSummary` | Summary of pending changes (Added, Modified, Deleted counts) |

### Analysis & Visualization

| Type | Description |
|------|-------------|
| `ImpactAnalysisResult` | Network graph data (Nodes, Edges, ImpactDetails) |
| `ImpactDetail` | Impact detail (ModelId, ClassType, ImpactedBy) |
| `NetworkNode` | Graph node for visualization (Id, FullName, X, Y, IsImpacted) |
| `NetworkEdge` | Graph edge for visualization (FromId, ToId, X1, Y1, X2, Y2) |

### Model Checking

| Type | Description |
|------|-------------|
| `ModelCheckResult` | Check result (Success, ModelId, ErrorMessage) |
| `ModelCheckProgress` | Progress tracking (TotalModels, ModelsChecked, CurrentModel) |

### UI Support

| Type | Description |
|------|-------------|
| `ModelTreeNode` | Tree view node (Id, Name, ClassType, IconSvg, FileStatus) |
| `ExternalResourceReference` | Resource reference (ModelId, RawPath, ResolvedPath, ReferenceType) |
| `ResourceWarning` | Resource warning (missing files, absolute paths) |

## License

MIT License — see [LICENSE](../LICENSE) for details.

## Dependencies

**NuGet Packages:**
- MudBlazor 9.0.0
- NLog 6.1.0

**Project References:**
- DymolaInterface
- ModelicaGraph
- ModelicaParser
- OpenModelicaInterface
- RevisionControl
