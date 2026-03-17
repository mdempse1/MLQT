# CLAUDE.md

This file provides guidance to Claude Code when working with this repository.

## Project Overview

MLQT is a cross-platform Blazor application built with .NET 10 targeting native platforms via .NET MAUI (Android, iOS, macOS Catalyst, Windows). UI components in the Shared project are hosted within MAUI using BlazorWebView.  Only the Windows build is currently included in the project.

The MLQT UI is intended to be a users primary way to manage Modelica libraries in revision control systems and supports SVN and Git. The intention is for users to work with MLQT to review and commit changes, pull updates, create new branches and push changes to the revision control system. It also provides static analysis of Modelica code to understand the impact of changes, apply formatting rules and check code against style guidelines.

Use the CODING_GUIDELINES.md whenever generating or refactoring code.

## Solution Structure

- **MLQT.Shared** - Shared Blazor components, pages, layouts, services
- **MLQT** - .NET MAUI application
- **MLQT.Services** / **MLQT.Services.Tests** - Business logic services
- **ModelicaParser** / **ModelicaParser.Tests** - ANTLR-based Modelica parser
- **ModelicaGraph** / **ModelicaGraph.Tests** - Directed graph for file/model relationships
- **RevisionControl** / **RevisionControl.Tests** - Git/SVN integration
- **DymolaInterface** / **DymolaInterface.Tests** - Dymola HTTP JSON-RPC interface
- **OpenModelicaInterface** / **OpenModelicaInterface.Tests** - OpenModelica ZeroMQ interface

## Build and Run Commands

```bash
# Build entire solution
dotnet build MLQT.slnx

# Run tests for a project
dotnet test MLQT.Services.Tests
dotnet test ModelicaParser.Tests
dotnet test ModelicaGraph.Tests

# Run MAUI application (Windows)
dotnet build MLQT/MLQT.csproj && dotnet run --project MLQT/MLQT.csproj
```

## Architecture Patterns

### Platform Abstraction

Services that could be used outside Blazor are in `MLQT.Services/` with interfaces in `MLQT.Services/Interfaces/`.

**Pattern for platform-specific services:**
1. Define interface in `MLQT.Services/Interfaces/`
2. Implement in `MLQT/Services/` using MAUI APIs
3. Register in `MLQT/MauiProgram.cs`

**Pattern for reusable .NET services:**
1. Define interface in `MLQT.Services/Interfaces/`
2. Implement in `MLQT.Services/`
3. Register as singleton in `MauiProgram.cs`

### Core Services

**Reusable .NET services** (in `MLQT.Services/`):

| Service | Purpose |
|---------|---------|
| **ILibraryDataService** | Manages loaded Modelica libraries, combined graph, server-side tree data |
| **IRepositoryService** | Git/SVN repository management, library discovery, VCS operations |
| **IFileMonitoringService** | FileSystemWatcher-based change detection with debouncing |
| **ICodeReviewService** | Log messages and issues from parsing/style checking |
| **IStyleCheckingService** | Background style rule checking for models with queue management |
| **IImpactAnalysisService** | Dependency impact analysis with BFS traversal |
| **IExternalResourceService** | External resource analysis, validation, and monitoring |
| **ICustomDictionaryService** | User custom word list persistence (`%LocalAppData%/MLQT/custom_dictionary.txt`) |
| **IDictionaryManagerService** | Hunspell dictionary management (bundled + imported at `%LocalAppData%/MLQT/Dictionaries/`) |
| **IModelCheckingService** | Interface for external tool checking (Dymola, OpenModelica) |
| **DymolaCheckingService** | Model checking via Dymola HTTP JSON-RPC |
| **OpenModelicaCheckingService** | Model checking via OpenModelica ZeroMQ |
| **LoggingService** | Static NLog-based logging (`%LocalAppData%/MLQT/`) |

**Platform-specific services** (in `MLQT/Services/`, use MAUI APIs):

| Service | Purpose |
|---------|---------|
| **IFilePickerService** | Native file/folder picker dialogs |
| **IPowerManagementService** | Prevents system sleep during long operations |
| **ISettingsService** | Application settings persistence (JSON, per-project) |

### Application State (AppState)

Centralized state container in `MLQT.Shared/Models/AppState.cs`:
- **Model Selection**: `ModelID`, `SelectedModelIDs`, `SelectionMode`
- **Library State**: `IsLibraryLoaded`
- **Deferred Analysis**: `IsDeferredMode`, `HasDependencyAnalysisRun`, `HasStyleCheckingRun`, `HasExternalResourcesAnalyzed`
- **Events**:
  - Model/UI: `OnChangeModel`, `OnSelectedModelsChanged`, `OnEnableMultiSelect`, `OnModelContentChanged`, `OnThemeChanged`
  - Library: `OnLibraryLoaded`, `OnLibraryCleared`
  - Settings: `OnSaveSettings`, `OnClearLogMessages`, `OnRepositorySettingsApplied`
  - VCS: `OnVcsFilesChanged`, `OnVcsModelsChanged`
  - Projects: `OnProjectSwitchStarting`, `OnProjectChanged`
  - Deferred analysis: `OnRunDeferredDependencies`, `OnRunDeferredStyleChecking`, `OnRunDeferredExternalResources`, `OnRunAllDeferredAnalysis`, `OnDeferredAnalysisCompleted`
  - Formatting: `OnFormatChangedFilesForCommit`
- Always use methods (`ChangeModelID()`, `SetSelectedModels()`, `ChangeSelectionMode()`, `LibraryLoaded()`, `LibraryCleared()`, `RepositorySettingsApplied()`, `VcsFilesChanged()`, etc.) not direct property access

### User Interface

UI components should be used from MudBlazor.  Any custom components should be created as Components in MLQT.Shared.

Use the following styling guidelines
* Use Small size options when available
* Use Dense styling options when available
* Use minimal padding and margin spacing
* RowStack components should have spacing=0
* Use Typo.body1 for all text except code
* Use Typo.body2 for code

**Thread Safety**: In Razor event handlers, use `await InvokeAsync(StateHasChanged)`.

**Graph Visualization**: Interactive network graphs use the `CytoscapeGraph` component (`Components/CytoscapeGraph.razor`) backed by Cytoscape.js. It accepts generic `DiagramNode`/`DiagramEdge` parameters. See `skill-cytoscape.md` for full details.

## Key Files

| File | Purpose |
|------|---------|
| `MLQT.slnx` | Solution file |
| `MLQT/MauiProgram.cs` | DI setup, service registration |
| `MLQT.Shared/Layout/MainLayout.razor` | Main layout, analysis pipeline orchestration |
| `MLQT.Shared/Models/AppState.cs` | Application state and cross-component events |
| `MLQT.Shared/Components/LibraryBrowser.razor` | Model tree navigation, VCS operation UI |
| `MLQT.Shared/Components/SettingsRepositories.razor` | Repository settings with formatting/style rules |
| `MLQT.Shared/Pages/CodeReview.razor` | Code viewer, diff, issues, external tool checks |
| `MLQT.Shared/Pages/Dependencies.razor` | Impact analysis with Cytoscape network graph |
| `ModelicaParser/modelica.g4` | ANTLR grammar |
| `ModelicaParser/Helpers/ModelicaParserHelper.cs` | Parser utilities |
| `ModelicaParser/StyleRules/VisitorWithModelNameTracking.cs` | Base class for all style rule visitors |
| `ModelicaGraph/DirectedGraph.cs` | Main graph structure |
| `ModelicaGraph/GraphBuilder.cs` | Loads libraries, analyzes dependencies |
| `ModelicaGraph/StyleChecking.cs` | Orchestrates all style rule checks |
| `ModelicaGraph/StyleCheckingSettings.cs` | Persisted style/formatting settings |
| `MLQT.Services/LibraryDataService.cs` | Library management |
| `MLQT.Services/RepositoryService.cs` | VCS repository operations |
| `MLQT.Services/StyleCheckingService.cs` | Background style checking with workers |
| `MLQT.Services/Helpers/StyleCheckingWorker.cs` | Parallel style checking per repository |
| `MLQT.Services/Helpers/ModelicaPackageSaver.cs` | Code formatting and file saving |
| `RevisionControl/Interfaces/IRevisionControlSystem.cs` | Unified Git/SVN interface |

## ModelicaParser Project

ANTLR 4 based parser for Modelica source code. Includes code formatting, icon extraction, style rule checking, and external resource extraction.

```csharp
using ModelicaParser;

// Parse Modelica code
var parseTree = ModelicaParserHelper.Parse(modelicaSourceCode);

// Extract model definitions
var models = ModelicaParserHelper.ExtractModels(modelicaCode);
```

**Key subsystems:**
- **ModelicaParserHelper** - Parsing and model extraction
- **ModelicaRenderer** (`Visitors/`) - Code formatting with configurable rules
- **IconExtractor / IconSvgRenderer** (`Icons/`) - Modelica icon annotation to SVG
- **ExternalResourceExtractor** (`Visitors/`) - Extract resource references from parse trees
- **StyleRules** (`StyleRules/`) - Style rule visitors (extends `VisitorWithModelNameTracking` base class). Visitors only check the outermost class — nested class definitions are skipped because each has its own `ModelNode` and is checked independently
- **SpellChecking** (`SpellChecking/`) - Hunspell-based spell checker, text extraction, and embedded dictionaries

**Grammar modification**: Edit `modelica.g4`, then `dotnet build` to regenerate parser code.

## ModelicaGraph Project

Directed graph for tracking file/model relationships, dependencies, external resources, and style checking.

**Node Types:**
- `FileNode` - Represents a Modelica file
- `ModelNode` - Represents a Modelica model with definition and dependencies
- `ResourceFileNode` - Represents an external resource file
- `ResourceDirectoryNode` - Represents an external resource directory

**Key Classes:**
- `DirectedGraph` - Main graph structure with node/edge management
- `GraphBuilder` (static) - Loads files (`LoadModelicaFile`, `LoadModelicaFiles`, `LoadModelicaDirectory`), analyzes dependencies (`AnalyzeDependenciesAsync`), queries models (`GetModelsFromFile`, `GetModelByName`)
- `StyleChecking` / `StyleCheckingSettings` - Run configurable style checks on model definitions
- `StyleCheckingSettings` includes `FormattingExcludedModels` (models that skip the formatter and formatting-rule violations) and `SvnBranchDirectories` (configurable per-repository SVN branch directory names, default: trunk/branches/tags)

```csharp
var graph = new DirectedGraph();
var fileNode = new FileNode("file1", "Models.mo");
var modelNode = new ModelNode("model1", "MyModel", modelicaCode);
graph.AddNode(fileNode);
graph.AddNode(modelNode);
graph.AddFileContainsModel("file1", "model1");
```

## Modelica Package Structure

- **package.order files** define child element ordering
- **Standalone classes** can be stored as separate files (no `replaceable`, `redeclare`, `inner`, `outer` prefix)
- **Non-standalone classes** must be nested in parent package.mo
- `ModelicaRenderer` supports selective class exclusion when generating package.mo

## External Resources System

External resources (data files, C libraries, images) are tracked as graph nodes:

- **ResourceFileNode** - Files referenced via `loadResource()`, `Bitmap`, external annotations
- **ResourceDirectoryNode** - Directories from `IncludeDirectory`, `LibraryDirectory`, `SourceDirectory` annotations
- **ResourceEdge** - Links models to resources with metadata (RawPath, ReferenceType, ParameterName)

**Reference Types:**
- `LoadResource` - `Modelica.Utilities.Files.loadResource()` calls
- `UriReference` - `modelica://` URIs in documentation/Bitmap
- `LoadSelector` - Parameters with `loadSelector` annotation
- `ExternalInclude/Library/IncludeDirectory/LibraryDirectory/SourceDirectory` - External function annotations

**External Resources Page** (`Pages/ExternalResources.razor`):
- Tree view of all referenced resources organized by directory
- Different icons for annotated directories (Include=Code, Library=LibraryBooks, Source=DataObject)
- Click files or annotated directories to see referencing models
- Filter by file type (Data, C/C++, Libs, Images, Documentation)

## Adding New Features

1. Define interface in appropriate `Interfaces/` folder
2. Implement in `MLQT.Services/` or `MLQT.Shared/Services/`
3. Register as singleton in `MauiProgram.cs`
4. Use events for cross-component communication
5. Keep business logic in services, not Razor components

## Skills (Load on Demand)

Detailed documentation for specialized subsystems is available in `.claude/skills/`:

| Skill | When to Load |
|-------|--------------|
| `skill-revision-control.md` | Git/SVN integration, workspace reuse, revision comparison |
| `skill-simulation-tools.md` | Dymola and OpenModelica interfaces |
| `skill-external-resources.md` | External resource extraction, resolution, graph nodes |
| `skill-icon-extraction.md` | Modelica icon parsing and SVG rendering |
| `skill-nuget-packages.md` | NuGet package details and licenses |
| `skill-cytoscape.md` | CytoscapeGraph component, cytoscapeGraph.js, layout options, script loading |
| `skill-spell-checking.md` | Spell checking system: SpellChecker, dictionaries, custom words, style rule visitors, UI integration |
| `skill-naming-conventions.md` | Naming convention checking: NamingValidator, NamingStyle, presets, FollowNamingConvention visitor, exception names |

## User Documentation

User-facing documentation is in `Documentation/`:

| Document | Covers |
|----------|--------|
| `getting-started.md` | Prerequisites, project/repo setup, first steps |
| `library-browser.md` | Tree navigation, VCS status indicators, view modes |
| `code-review.md` | Code viewer, diff, issues, external tool checks, formatting exclusion toggle |
| `code-formatting.md` | Formatting rules, triggers, incremental vs full, exclusion |
| `settings-reference.md` | All settings: style rules, formatting, spell check, SVN branch dirs, JSON schema |
| `dependency-analysis.md` | Impact analysis, Cytoscape graph, layout options |
| `external-resources.md` | Resource tracking, tree view, file type filters |
| `external-tools.md` | Dymola and OpenModelica configuration |
| `naming-conventions.md` | Naming styles, presets, exception names |
| `spell-checking.md` | Dictionaries, custom words, Code Review workflow |
| `git-operations.md` | Pull, commit, branch, merge, rebase, push, PR, history |
| `svn-operations.md` | Update, commit, branch, merge, tree conflicts, configurable branch dirs |
| `file-monitoring.md` | Change detection, debouncing, refresh button |
| `modelica-concepts.md` | Modelica language primer for non-Modelica users |
| `ui-customization.md` | Themes, syntax highlighting presets, custom colors |
| `troubleshooting.md` | Common issues, FAQ |

## Documentation Maintenance

Update this file when:
- Adding new projects or major features
- Changing architectural patterns
- Modifying service interfaces
- Adding/removing NuGet packages

Update relevant skill files for specialized subsystem changes.

Update project readme files when changes are made.

## Test Cases

Comprehensive tests are required for all classes with the goal being >80% coverage for each class.  The ModelicaParser assembly requires >95% coverage for all classes as this is critical to the project.
