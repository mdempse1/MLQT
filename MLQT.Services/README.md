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
| `CodeReviewService` | `ICodeReviewService` | Log messages and findings from parsing/style checking |
| `StyleCheckingService` | `IStyleCheckingService` | Background style rule checking for models |
| `ImpactAnalysisService` | `IImpactAnalysisService` | Dependency impact analysis with network graph visualization |
| `ExternalResourceService` | `IExternalResourceService` | External resource analysis, validation, and monitoring |
| `CustomDictionaryService` | `ICustomDictionaryService` | Each repository's accepted spellings, at `<repo>/.mlqt/dictionary.txt` |
| `BaselineStatusService` | `IBaselineStatusService` | Classifies findings against each repository's committed baseline (new / touched / accepted) |
| `DictionaryManagerService` | `IDictionaryManagerService` | Hunspell dictionary management (bundled + imported) |
| `DymolaCheckingService` | `IModelCheckingService` | Model checking via Dymola |
| `OpenModelicaCheckingService` | `IModelCheckingService` | Model checking via OpenModelica |

### The shared check pipeline (`Checking/`)

Every surface that reports findings — the app's background workers, `mlqt check`, and the MCP
server — goes through the same primitives, so the same library and settings give all three the same
count. A change belongs in the primitive, not in one tool's path.

| Type | Purpose |
|------|---------|
| `StyleCheckContext` | The inputs a check needs, built **once** per run: known class ids and names, the spell checker, the inherited-icon and inherited-element lookups, the naming config, optional coverage collection |
| `StyleCheckRunner` | Checks one class against that context. The single per-model entry point, which is why the guard against reporting on a class reconstructed from an encrypted library lives here |
| `LibraryCheckSession` | A whole-library run: load, trim, check, and the whole-graph analyses, in the order they depend on each other |
| `Baseline` / `FindingClassifier` | The accepted-debt file and the new / touched / accepted classification the ratchet gates on |
| `ChangedModelResolver` | Which classes a VCS ref touched, for `--changed-from` |
| `ChangedLineResolver` | Which *lines* a change touched, for the pull-request review comments. Git only — a line-level diff is `ILineLevelDiff`, which SVN does not implement |
| `ClassLocation` | Where a class starts in its file. Findings carry class-relative lines; every report that names a file maps through here, so they all mean the same line |
| `DictionaryScope` | Which repository's accepted spellings apply to a class |
| `SpellCheckerFactory` / `DictionaryAvailability` | Builds a spell checker for the chosen languages, and says so when the machine has no dictionary for one |
| `ReferenceOnlyScope` | The classes loaded only so references resolve, which are never reported on |
| `ParserErrorReporter` | Parse failures as findings. The parser reads whole files, so its lines are the file's; these are converted to class-relative ones like every other finding, and mapped back through `ClassLocation` by whatever reports them |
| `UsesVersionChecker` / `VcsStamp` | Dependency version mismatches, and the revision a report was produced at |

### Static Helpers

| Helper | Purpose |
|--------|---------|
| `LibraryDiscovery` | Finds the library roots under a path — a directory holding a `package.mo` *or* a `package.moe` |
| `EncryptedLibraryDetector` | Recognises an encrypted library and reads its name, version and help directory. Version comes from the versioned directory name first (`Battery 2.9.0`) and `libraryinfo.mos` only as a fallback: the directory name is what a tool resolves against, so it states which copy is actually installed |

Encrypted libraries are loaded through `ILibraryDataService.AddEncryptedLibraryFromDirectoryAsync`,
which reconstructs their classes from the vendor's documentation via `ModelicaParser.ExternalDocs`
and `ModelicaGraph.ExternalStubBuilder`. The result is read-only: `ModelicaPackageSaver` refuses to
write such a class, and `LibraryCheckSession` filters them out of the reported set centrally. A
library shipping no documentation loads **zero** classes rather than appearing empty — an empty
library would make every reference into it a fabricated broken-reference finding.

### Platform Services

These interfaces are implemented by the MAUI host project:

| Interface | Purpose |
|-----------|---------|
| `ISettingsService` | Persistent key-value settings storage |
| `IFilePickerService` | File and folder selection dialogs |
| `IPowerManagementService` | Prevents system sleep during long operations |

## Usage

### Library Management (ILibraryDataService)

```csharp
// Load a library from a single file
LoadedLibrary lib = await libraryDataService.AddLibraryFromFileAsync("package.mo");

// Load from a directory
LoadedLibrary lib = await libraryDataService.AddLibraryFromDirectoryAsync(@"C:\MyLibrary");

// Access the combined graph (all libraries merged)
DirectedGraph graph = libraryDataService.CombinedGraph;

// Run full dependency analysis once. Idempotent, and concurrent callers share a single run.
// Everything that needs UsedModelIds/UsedByModelIds must go through here rather than calling
// GraphBuilder.AnalyzeDependenciesAsync directly, so two runs can never overlap.
await libraryDataService.EnsureDependenciesAnalyzedAsync();

// graph.DependenciesAnalyzed is the single source of truth for "are the edges populated?".
// Adding a library clears it; DirectedGraph.Clear() clears it.
bool ready = libraryDataService.CombinedGraph.DependenciesAnalyzed;

// Get model by ID
ModelNode? model = libraryDataService.GetModelById("MyLibrary.MyModel");

// Get tree data for the UI
var topItems = await libraryDataService.GetTopLevelModelsAsync();
var children = await libraryDataService.GetChildModelsAsync(parentNode);

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
var findings = await styleCheckingService.CheckModelAsync(modelDefinition, settings);

// Start background checking for a single repository (async)
await styleCheckingService.StartBackgroundCheckingAsync(repository);

// Start background checking for a single repository (synchronous, fire-and-forget)
styleCheckingService.StartBackgroundChecking(repository);

// Start background checking for all repositories at once
// OnProgressChanged fires true only after ALL repos finish (not per-repo)
styleCheckingService.StartBackgroundCheckingForRepositories(repositories);

// Re-check specific models after file changes (clears previous findings first)
await styleCheckingService.CheckModelsAsync(changedModelIds, graph);

// Every entry point above runs BOTH the per-class rules and the whole-graph analyses
// (package.order, uses hygiene, unused class/member, shadowing), and arranges dependency
// analysis first when an enabled rule needs the edges. That is what keeps a single-repository
// check (Apply in repository settings) reporting the same count as a whole-project check and
// as `mlqt check`. OnProgressChanged(true) waits for the graph analyses too, so the total
// shown on completion is final.

// Graph analyses only — for the deferred pipeline, which runs the per-class rules itself
await styleCheckingService.RunGraphAnalysesForRepositoriesAsync(repositories);

// Subscribe to progress (bool = allComplete)
styleCheckingService.OnProgressChanged += (allComplete) =>
{
    if (allComplete) Console.WriteLine("All style checks finished");
};

// Subscribe to finding results
styleCheckingService.OnFindingsFound += (findings) =>
{
    foreach (var v in findings)
        Console.WriteLine($"{v.ModelName}: {v.Summary}");
};
```

### Accepted Spellings (ICustomDictionaryService)

Every call names the repository whose list it means. The words live in
`<repo>/.mlqt/dictionary.txt` and are committed with the code, which is what makes the desktop app
and `mlqt check` in CI accept the same words — a machine-wide list could only be seen by one of them.

```csharp
// The words accepted for a repository (read on first use, then cached)
IReadOnlyCollection<string> words = customDictionaryService.WordsFor(repositoryRoot);

// Where they are stored
string path = customDictionaryService.PathFor(repositoryRoot);

// Add and remove
await customDictionaryService.AddWordAsync(repositoryRoot, "Pacejka");
await customDictionaryService.RemoveWordAsync(repositoryRoot, "typo");

// Re-read from disk, merge another list in, or write one out
IReadOnlyCollection<string> reloaded = await customDictionaryService.LoadAsync(repositoryRoot);
int added = await customDictionaryService.MergeFromAsync(repositoryRoot, @"C:\words.txt");
await customDictionaryService.ExportAsync(repositoryRoot, @"C:\export.txt");

// Fires with the repository root whose list changed — drop that repository's spell checker
customDictionaryService.OnDictionaryChanged += root => { /* rebuild for `root` */ };
```

Which repository's list applies to a given class is decided by `Checking/DictionaryScope`, so the
app, the CLI and the MCP server all answer that question the same way. Words that every Modelica
library needs are not here at all — they ship with the parser, in `modelica_terms.txt`.

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
- `StyleCheckingWorker` passes `isExcludedFromFormatting` from the repository's style settings when calling `RunStyleChecking`, so excluded models are not flagged for formatting findings.
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
- NLog 6.1.0

This project is UI-agnostic (no Blazor/MudBlazor dependency). The library tree data is returned as
`ModelNode` lists (`GetTopLevelModelsAsync` / `GetChildModelsAsync` / `ModelHasChildren`); the UI layer
(`MLQT.Shared`) wraps them into MudBlazor `TreeItemData`.

**Project References:**
- DymolaInterface
- ModelicaGraph
- ModelicaParser
- OpenModelicaInterface
- RevisionControl
