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
- **MLQT.McpServer** / **MLQT.McpServer.Tests** - Headless Model Context Protocol (MCP) server exposing MLQT's Modelica capabilities as tools over stdio; reuses the service layer without MAUI. See `MLQT.McpServer/README.md`
- **MLQT.McpTester** - MAUI Blazor (Windows) desktop app for manually testing any stdio MCP server: connect, list tools, auto-generate parameter fields from each tool's JSON Schema, call, and view results. Uses MudBlazor + the ModelContextProtocol client SDK. See `MLQT.McpTester/README.md`
- **MLQT.Cli** / **MLQT.Cli.Tests** - Headless cross-platform `mlqt` CLI (packaged as a `dotnet tool`). `mlqt check` style-checks a Modelica library and emits console/JSON/JUnit/SARIF/TeamCity/markdown output with CI exit codes, reusing the shared check pipeline in `MLQT.Services/Checking/`; `mlqt baseline` manages the accepted-debt file; `mlqt compare` lists the classes one copy of a library has that another does not, matching on full Modelica name so a restructure on disk is not a difference; `mlqt hook` installs the check as a git pre-commit hook. See `Documentation/cli.md`
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
1. Define interface in `MLQT.Services/Interfaces/` — **always there**, even when the implementation
   lives in a subfolder such as `Checking/`. One folder answers "what services are there?"
2. Implement in `MLQT.Services/`
3. Register as singleton in `MauiProgram.cs`

### Core Services

**Reusable .NET services** (in `MLQT.Services/`):

| Service | Purpose |
|---------|---------|
| **ILibraryDataService** | Manages loaded Modelica libraries, combined graph, server-side tree data. `EnsureDependenciesAnalyzedAsync()` is the one way to run dependency analysis — idempotent, and concurrent callers share a single run |
| **IRepositoryService** | Git/SVN repository management, library discovery, VCS operations |
| **IFileMonitoringService** | FileSystemWatcher-based change detection with debouncing |
| **ICodeReviewService** | Log messages and findings from parsing/style checking |
| **EncryptedLibraryDetector** | Recognises an encrypted library (`package.moe`) and reads its name/version — versioned directory name first, `libraryinfo.mos` as fallback |
| **IBaselineStatusService** | Classifies findings against each repository's committed baseline (new / touched / accepted), so the Code Review list can be narrowed to what the working copy changed. "Touched" = pending commit, not a commit-to-commit diff |
| **IStyleCheckingService** | Background style rule checking for models with queue management. Every entry point runs the per-class rules *and* the whole-graph analyses, arranging dependency analysis first when an enabled rule needs the edges, so all paths report the same finding count. Cancellation and finding removal are scoped to the repository being re-checked — a project holds several, each with rules of its own |
| **IImpactAnalysisService** | Dependency impact analysis with BFS traversal |
| **IExternalResourceService** | External resource analysis, validation, and monitoring |
| **ICustomDictionaryService** | Accepted spellings per repository (`<repo>/.mlqt/dictionary.txt`, committed with the code so the app and CLI accept the same words). `DictionaryScope` decides which repository's list applies to a class |
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
- **Deferred Analysis**: `IsDeferredMode`, `HasDependencyAnalysisRun`, `HasStyleCheckingRun`, `HasExternalResourcesAnalyzed`
- **Events**:
  - Model/UI: `OnChangeModel`, `OnSelectedModelsChanged`, `OnEnableMultiSelect`, `OnModelContentChanged`, `OnThemeChanged`
  - Settings: `OnSaveSettings`, `OnClearLogMessages`, `OnRepositorySettingsApplied`
  - VCS: `OnVcsFilesChanged`, `OnVcsModelsChanged`
  - Projects: `OnProjectSwitchStarting`, `OnProjectChanged`
  - Deferred analysis: `OnRunDeferredDependencies`, `OnRunDeferredStyleChecking`, `OnRunDeferredExternalResources`, `OnRunAllDeferredAnalysis`, `OnDeferredAnalysisCompleted`
  - Formatting: `OnFormatChangedFilesForCommit`
- Always use methods (`ChangeModelID()`, `SetSelectedModels()`, `ChangeSelectionMode()`, `RepositorySettingsApplied()`, `VcsFilesChanged()`, etc.) not direct property access
- AppState carries no "library loaded/cleared" state — the set of loaded libraries is published by
  `ILibraryDataService.OnLibrariesChanged`/`OnTreeDataChanged` and `IRepositoryService.OnProjectChanged`

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
| `MLQT.Shared/Pages/CodeReview.razor` | Code viewer, diff, findings, external tool checks |
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
- **IconExtractor** (`Visitors/`) / **IconSvgRenderer** (`Icons/`) - Modelica icon annotation to SVG
- **ExternalResourceExtractor** (`Visitors/`) - Extract resource references from parse trees
- **ExternalDocs** (`ExternalDocs/`) - `DymolaHelpParser`/`DymolaHelpReader` recover classes (name, description, extends, has-icon) from a vendor's generated help HTML, for encrypted libraries with no readable source. Scanning is **tag-oriented, never line-oriented** — Dymola 2024x Refresh 1 emits a junk token where newlines belong
- **StyleRules** (`StyleRules/`) - Style rule visitors (extends `VisitorWithModelNameTracking` base class). Visitors only check the outermost class — nested class definitions are skipped because each has its own `ModelNode` and is checked independently
- **SpellChecking** (`SpellChecking/`) - Hunspell-based spell checker, text extraction, and embedded dictionaries
- **WithinClause** (`Helpers/`) - **The only place that adds or removes a leading `within ...;` clause.** A within clause belongs to a *file*, not a class: a `ModelNode`'s stored `ModelicaCode` never carries one, while text written to a `.mo` file always must (or the file re-parses with no package context and its classes come back with detached IDs). Use `Ensure` when rendering to disk and `Strip` before storing rendered text back on a node. Never hand-roll the check — the versions drifted, some guarding against a clause that was already there and some not, and a formatter that assumed a model's code had none wrote a second clause into every file it touched. The grammar accepts at most one clause, so a duplicate is a syntax error, not a silent corruption
- **ModelicaFileEncoding** (`Helpers/`) - **All `.mo`/`package.order` reads and writes must go through this.** Modelica files declare no encoding and the population is mixed: older libraries use single-byte Windows-1252, most files are BOM-less UTF-8. Encoding is detected per file (BOM → strict UTF-8 → Latin-1 fallback, which cannot fail) and **written back in the encoding it was read in**. A read here paired with a plain `File.WriteAllText` re-encodes the decoded characters and corrupts the file, progressively, on every save

**Grammar modification**: Edit `modelica.g4`, then `dotnet build` to regenerate parser code.

## ModelicaGraph Project

Directed graph for tracking file/model relationships, dependencies, external resources, and style checking.

**Node Types:**
- `FileNode` - Represents a Modelica file
- `ModelNode` - Represents a Modelica model with definition, dependencies, and `HasExperimentAnnotation` flag
- `ResourceFileNode` - Represents an external resource file
- `ResourceDirectoryNode` - Represents an external resource directory

**Key Classes:**
- `DirectedGraph` - Main graph structure with node/edge management. `DependenciesAnalyzed` is the single source of truth for whether `UsedModelIds`/`UsedByModelIds` are populated — never infer it by checking whether some model happens to have edges
- `GraphBuilder` (static) - Loads files (`LoadModelicaFile`, `LoadModelicaFiles`, `LoadModelicaDirectory`), analyzes dependencies (`AnalyzeDependenciesAsync`, `AnalyzeDependenciesForModelsAsync`). Model queries are instance methods on `DirectedGraph` (e.g. `GetModelsInFile`, `GetUsedModels`, `GetModelUsedBy`)
- `ExternalStubBuilder` - Turns `DocumentedClass` records into graph nodes by synthesizing a minimal Modelica declaration, so every parse-tree-based consumer resolves them unchanged. Nodes are flagged `ModelNode.IsExternalStub`: never reported on, never written
- `StyleChecking` / `StyleCheckingSettings` - Run configurable style checks on model definitions
- `StyleCheckingSettings` includes `FormattingExcludedModels` (models that skip the formatter and formatting-rule findings) and `SvnBranchDirectories` (configurable per-repository SVN branch directory names, default: trunk/branches/tags)

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

**Reference Types:** (`ResourceReferenceType` enum)
- `LoadResource` - `Modelica.Utilities.Files.loadResource()` calls
- `LoadResourceParameter` - a parameter whose default value is a `loadResource()` call
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
| `code-review.md` | Code viewer, diff, findings, external tool checks, formatting exclusion toggle |
| `code-formatting.md` | Formatting rules, triggers, incremental vs full, exclusion |
| `settings-reference.md` | All settings: style rules, formatting, spell check, SVN branch dirs, JSON schema |
| `dependency-analysis.md` | Impact analysis, Cytoscape graph, layout options |
| `external-resources.md` | Resource tracking, tree view, file type filters |
| `metrics-dashboard.md` | Metrics tab: coverage dimensions, scope and sub-library comparison, the trend and its snapshots |
| `encrypted-libraries.md` | Commercial `package.moe` libraries: what is recovered from vendor help HTML, accuracy, reference-library setup |
| `external-tools.md` | Dymola and OpenModelica configuration |
| `naming-conventions.md` | Naming styles, presets, exception names |
| `spell-checking.md` | Dictionaries, custom words, Code Review workflow |
| `git-operations.md` | Pull, commit, branch, merge, rebase, push, PR, history |
| `svn-operations.md` | Update, commit, branch, merge, tree conflicts, configurable branch dirs |
| `file-monitoring.md` | Change detection, debouncing, refresh button |
| `modelica-concepts.md` | Modelica language primer for non-Modelica users |
| `ui-customization.md` | Themes, syntax highlighting presets, custom colors |
| `mcp-server.md` | MCP server for AI agents: registering, workflow, tool groups, McpTester, logging |
| `cli.md` | Headless `mlqt` CLI: install, `check` options, formats (console/JSON/JUnit/SARIF/TeamCity/markdown/review), baseline/ratchet, `compare` for missing classes, `hook` for the git pre-commit gate, `review` for pull-request comments, exit codes |
| `ci-quality-gate.md` | Hands-on work-through: set up `mlqt` in CI, enable rules + severities, baseline existing debt, gate on new findings, wire into TeamCity/GitHub, comment on a pull request, install the pre-commit hook |
| `troubleshooting.md` | Common findings, FAQ |

## Planning and Design Notes

Not user documentation — the record of what was decided and what shipped. **Read the roadmap before
starting anything substantial**: it holds the agreed sequencing, the decisions behind it, and the
backlog (items `B1`-`Bnn`), which is where work in progress is tracked.

| Document | Covers |
|----------|--------|
| `Documentation/roadmap.md` | Candidate work by theme, the locked phase sequencing, and the backlog — including which items are shipped and which are open |
| `Documentation/design-ci-quality-gate.md` | The deep-dive behind §5: baseline/ratchet design, finding identity, CLI surface, phased plan |
| `Documentation/design-phase1-findings-foundation.md` | Phase 1 — `Finding`, rule ids, severity map, fingerprints |
| `Documentation/design-phase2-cli.md` | Phase 2 — the headless `mlqt` CLI and the shared check pipeline |
| `Documentation/design-phase3-baseline.md` | Phase 3 — baseline/ratchet and changed-model escalation |
| `Documentation/design-phase4-ci-ergonomics.md` | Phase 4 — SARIF, TeamCity, markdown, real per-rule severities |
| `Documentation/design-phase5-suppression.md` | Phase 5 — `__MLQT` suppression, checker/formatter/authoring |
| `Documentation/design-phase6-analyses-dashboard.md` | Phase 6 — Wave-1 analyses, graph-analyzer seam, metrics dashboard |
| `Documentation/design-phase7-gui-tests.md` | Phase 7a — the GUI test harness that must precede the desktop-host migration |
| `Documentation/design-encrypted-libraries.md` | Recovering classes from a vendor's generated help HTML |

Each phase note records what actually landed, including where the implementation deviated from the
sketch — so when the note and the code disagree, that is a defect in one of them, not a detail.

## Documentation Maintenance

Update this file when:
- Adding new projects or major features
- Changing architectural patterns
- Modifying service interfaces
- Adding/removing NuGet packages

Update `Documentation/roadmap.md` when:
- A backlog item is finished, or a new one is found — the backlog is the working list, and an item
  that is done but still open reads as outstanding work to whoever picks it up next
- A phase ships, or a decision changes the agreed sequencing

Update the phase's design note when its implementation deviates from what the note describes. The
notes are read as the record of what was built; a note describing something that was planned and not
built is worse than no note.

Update relevant skill files for specialized subsystem changes.

Update project readme files when changes are made.

## Test Fixtures

`TestFixtures/SarifSmoke/` is a deliberately imperfect Modelica library committed at a **nested**
path (`Libraries/Smoke`). `build/validate-sarif.ps1` checks a report generated from it against the
SARIF 2.1.0 schema with `Sarif.Multitool`, and the nesting is what proves `--sarif-base` writes paths
a consumer can resolve. Run on every push by `build-and-test.yml`; run it locally the same way.

## Test Cases

Comprehensive tests are required for all classes with the goal being >80% coverage for each class.  The ModelicaParser assembly requires >95% coverage for all classes as this is critical to the project.

**CI enforces this** — `build/check-coverage.ps1` runs all six suites, merges their reports, and fails
the build per class. Run it locally the same way:

```powershell
dotnet build MLQT.slnx -c Release
./build/check-coverage.ps1                 # gate
./build/check-coverage.ps1 -SkipTests      # re-judge coverage already collected
./build/check-coverage.ps1 -UpdateBaseline # re-record accepted debt; review the diff
```

It is a **ratchet, not a flat threshold**, for the same reason MLQT offers its users one: some debt
predates the bar, and some of it cannot be paid on a runner at all — the SVN tests need a working copy
and a server no runner has. `build/coverage-baseline.json` records the classes currently below their
bar, and the build fails when one goes further backwards, when a class that met the bar stops meeting
it, or when a new class arrives below it. That file is a debt ledger: adding to it needs the same
justification as any other accepted debt.

Classes under 25 coverable lines are measured but not gated (a four-line record whose only uncovered
lines are the compiler's `Equals`/`GetHashCode` reads as 50%, and chasing that produces tests that
assert nothing), as is source-generated code. `MLQT.Shared` has no tests at all until phase 7a builds
the harness — see `Documentation/design-phase7-gui-tests.md`.
