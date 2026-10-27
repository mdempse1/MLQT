# MLQT.Services

Loaded when working in `MLQT.Services/` or `MLQT.Services.Tests/`. The repository-wide rules —
where interfaces live, and that every reusable service is registered in `AddMlqtCore()` — are in the
root `CLAUDE.md`; this file carries each service's contract and the shared check pipeline.

One of the six assemblies whose public surface is tracked in `PublicAPI.*.txt`.

## Key files

| File | Purpose |
|------|---------|
| `MLQT.Services/Checking/LibraryCheckSession.cs` | The shared load→check pipeline: parse diagnostics, per-class rules, graph analyses |
| `MLQT.Services/Checking/StyleCheckRunner.cs` / `StyleCheckContext.cs` | Per-class entry point every surface funnels through, and its once-per-run inputs |
| `MLQT.Services/Checking/Baseline.cs` | The accepted-debt ledger, its drift detection, and `FindingClassifier` |
| `MLQT.Services/Checking/ClassLocation.cs` | Where a class starts in its file — turns a class-relative finding line into a file line |
| `MLQT.Services/LibraryDataService.cs` | Library management |
| `MLQT.Services/RepositoryService.cs` | VCS repository operations |
| `MLQT.Services/StyleCheckingService.cs` | Background style checking with workers |
| `MLQT.Services/Helpers/StyleCheckingWorker.cs` | Parallel style checking per repository |
| `MLQT.Services/Helpers/ModelicaPackageSaver.cs` | Code formatting and file saving |

## Core services

**Reusable .NET services** (in `MLQT.Services/`):

| Service | Purpose |
|---------|---------|
| **ILibraryDataService** | Manages loaded Modelica libraries, combined graph, server-side tree data. `EnsureDependenciesAnalyzedAsync()` is the one way to run dependency analysis — idempotent, and concurrent callers share a single run. `GetOwningLibrary(modelId)` is the **only** way to ask which library a class belongs to — never `Libraries.FirstOrDefault(l => l.ModelIds.Contains(id))`, which returns whichever load finished first; `LibraryOwnershipPolicyTests` holds every read of a library's `ModelIds` to a ledger. An encrypted library is **never loaded beside readable source for the same library** (`LibraryPrecedence`, B268): the source wins whole, so the vendor build contributes no classes the checkout has deleted. `AddLibraryFromSourceAsync(IReadOnlyClassSource)` adds a library a host **supplies from memory** — read-only through the same loader as an encrypted one, and ranked between them: readable source > supplied > recovered from documentation, except that a supplied copy naming a different version from the installed encrypted build gives way to it, and a second supplied copy of a library is not loaded. A supplied library may only hold classes of its own name - one declared outside it is refused before it reaches the graph - and read-only loads of one library name run one at a time. A library's `Version` is its top-level package's `version` annotation; a version in its directory name, or a source's claim, only fills in when there is none. Ask `LoadedLibrary.IsReadOnly`, never `SourceType == EncryptedDirectory`, for "may this be touched?" — and in an MCP tool that edits a class, `ReadOnlyClassGuard.Refuse` first |
| **IRepositoryService** | Git/SVN repository management, library discovery, VCS operations. `GetWorkingCopyChanges` runs **one status query per working copy at a time**, shared by every caller that arrives while it runs (B293) — two libraries checked out in one tree share it (B330) — never call the VCS for status yourself. `GetRepositoriesSharingWorkingCopy` is the group a VCS operation acts on: two libraries checked out in one tree are two repositories, and whatever follows an operation (monitor pause, reload, analysis) must reach all of them (B301). `RelocateLibraryAsync` is how a library that was one `.mo` file becomes the directory Format All expanded it into — its `SourcePath`, `RelativePathInRepository` and the repository's `DiscoveredLibraries` together, so the state is what a project reload would give (B417). `RefreshRepositoryAsync` keeps which classes the reload changed - source that differs, and everything below a package whose `__MLQT` waivers or imports now reach it differently - until the VCS pipeline takes them with `TakeClassesChangedByReload`, because neither the paused monitor nor the VCS status can name what an update committed (B499) |
| **IFileMonitoringService** | FileSystemWatcher-based change detection with debouncing. A VCS operation holds the monitor off with a `MonitorPause` (`MLQT.Shared/Helpers`), which starts it again on disposal unless it was handed to the `VcsFilesChanged` pipeline — never with a bare `StopMonitoring`/`StartMonitoring` pair (B296) |
| **ICodeReviewService** | Log messages and findings from parsing/style checking |
| **EncryptedLibraryDetector** | Recognises an encrypted library (`package.moe`) and reads its name/version — versioned directory name first, `libraryinfo.mos` as fallback |
| **IBaselineStatusService** | Classifies findings against each repository's committed baseline (new / touched / accepted), so the Code Review list can be narrowed to what the working copy changed. "Touched" = pending commit, not a commit-to-commit diff. `Refresh()` is a working-copy scan of every repository on the calling thread; from the UI use `RefreshAsync()`, which queues one on the pool and never lets two overlap (B293) |
| **IModelChangeClassifier** | The one place a repository is asked what kind of change each model carries (B191). Finds the committed version of each changed file, hands both to `ClassChangeClassifier`, and keys the answer by `ModelNode.Id`. **Absence from the result means the question was not asked**, not that nothing changed — an unchanged class in a modified file is present and `Unchanged`. A class that reads as added is looked for by name in the committed versions of the other changed and deleted files (and a rename's `OldPath`), so a class moved between files is compared with where it came from (B350). Cached per file against its size, write time and the repository's revision, and cleared on a project change |
| **IStyleCheckingService** | Background style rule checking for models with queue management. Every entry point runs the per-class rules *and* the whole-graph analyses, arranging dependency analysis first when an enabled rule needs the edges, so all paths report the same finding count. Cancellation and finding removal are scoped to the repository being re-checked — a project holds several, each with rules of its own |
| **IFormattingPipeline** (`Helpers/FormattingPipeline.cs`) | Every formatted write MLQT makes: `FormatModifiedFilesAsync` (VCS-modified and untracked files, at startup and after a VCS operation), `FormatChangedFilesAsync` (a named set) and `SaveAllLibrariesWithFormattingAsync` (Format All, which restructures files and leaves a file with syntax errors untouched, B414). It records `WrittenFileTimestamps` so the monitor can tell MLQT's writes from the user's — never write formatted files around it. Reference-only repositories are never written |
| **IImpactAnalysisService** | Dependency impact analysis with BFS traversal |
| **IExternalResourceService** | External resource analysis, validation, and monitoring |
| **ICustomDictionaryService** | Accepted spellings per repository (`<repo>/.mlqt/dictionary.txt`, committed with the code so the app and CLI accept the same words). `DictionaryScope` decides which repository's list applies to a class |
| **IDictionaryManagerService** | Hunspell dictionary management (bundled + imported at `%LocalAppData%/MLQT/Dictionaries/`) |
| **IModelCheckingService** | Interface for external tool checking (Dymola, OpenModelica) |
| **DymolaCheckingService** / **OpenModelicaCheckingService** | Model checking via Dymola's HTTP JSON-RPC and omc's ZeroMQ. **One shape, written once** in `ModelCheckingServiceBase<TSession>` (B398): the run, its progress and cancellation, the package fan-out, and the check sequence (clear the log, check, read the log). Each service supplies only what differs — its session factory, how it opens a library, and how it turns its tool's way of saying "no verdict" into a `CheckAnswer` — so a fix to shared behaviour goes in the base, never in one tool. What they promise is still asserted **once**, in `MLQT.Services.Tests/ModelCheckingServiceContract.cs`, and run against both — the copy in each of them that was not shared is exactly where a promise went unkept on one path (B229/B272). `CheckSingleModelAsync` is the only place a check happens; `CheckModelAsync` opens the library and calls it. Both take their session from a factory that returns **`IDymolaInterface`/`IOpenModelicaInterface`**, not the concrete class: the concrete one's every method is a round trip to a running tool, so with it in that signature none of this could be tested without one installed. Each tool has a **Check time limit** (External Tools tab; 0 is none) and Cancel reaches a check in flight: a timeout is reported as `ModelCheckResult.TimedOut`, never as a failed model, and the log is not read after one — Dymola is still busy (`IDymolaInterface.LastOutcome` says so, since every command answers `false` whatever went wrong), and omc has closed its session (B262, B263). **When MLQT exits, `ExternalToolShutdown` ends omc with its process tree and leaves Dymola running** — the user's decision, both platforms: each factory's `Shutdown()` is "MLQT is exiting", never "stop this session", and Dymola's only detaches (B493) |
| **ExternalToolShutdown** | What exiting does to the simulation tools MLQT started; a host calls `EndWith(AppDomain)` and `Run(...)` after its window closes (see the row above) |
| **JsonSettingsService** | `ISettingsService` as JSON under the log directory; `MigrateFrom(MauiPreferencesFile.Read(...))` brings a pre-7b user's settings across once (`HasMigrated`) |
| **LoggingService** | Static NLog-based logging (`%LocalAppData%/MLQT/`, `~/.local/share/MLQT/` on Linux). **File only** — the console target is off unless `MLQT_LOG_CONSOLE` is set, so the log file is the single place to look |

## The shared check pipeline (`MLQT.Services/Checking/`)

Not services — the primitives that keep the desktop app, `mlqt check` and the MCP server reporting
the **same findings with the same line numbers**. Change the primitive, never one tool's path.

| Type | Role |
|------|------|
| **LibraryCheckSession** | `Check(...)` — the whole answer for a set of models: parse diagnostics, the per-class rules in parallel, then the whole-graph analyses. The CLI and MCP call this |
| **StyleCheckRunner** | The per-class entry point *every* surface funnels through, including the GUI's workers, which is why the external-stub guard and the coverage measurement live here |
| **StyleCheckContext** | The once-per-run inputs (spell checker, known ids, icon and inherited-name callbacks, unit lookup, coverage measurer) built once instead of per class |
| **ParserErrorReporter** | Parse diagnostics as `Finding`s. Always reported, never configurable, never baselined — `RuleIds.IsDiagnostic` is that question |
| **Baseline** / **FindingClassifier** | The accepted-debt ledger, New/AcceptedDebt/TouchedDebt classification, and drift against the rules the baseline was taken with |
| **ClassLocation** | Where a class starts in its file. Findings carry class-relative lines; every report maps them through this |
| **ChangedModelResolver** / **ChangedLineResolver** | Which models, and which lines, a change touched. `VcsLocator` (in `VcsStamp.cs`) owns which system a path belongs to |
| **ModelScope** | Which repository each loaded class belongs to, so which settings it is checked against, and whether any repository needs the dependency edges |
| **VcsChangeScope** | What a VCS operation affected — monitor's pending changes, else VCS status, else the whole repository (re-analysed, not formatted) |
| **ReferenceOnlyScope** | The classes of reference-only libraries: loaded so references resolve, never checked or written |
| **SuppressionScope** / **SpellingAcceptance** | What a "suppress this rule" or "accept this spelling" action covers, and which findings it clears |
| **SpellCheckerFactory** / **DictionaryAvailability** / **DictionaryScope** | The spell checker for a run, which requested dictionaries this machine lacks, and which repository's accepted words apply to a class |
| **ReportLocation** / **FindingExport** | A finding's file (library-relative) and file line for a report; the desktop app's JSON export in the shape `mlqt check --format json` writes |
| **PackageCodeTrimmer** (in `ModelicaGraph/`) | Trims a package's inline standalone children before checking, so every surface checks the same representation. **Only where a child really is inline** — one that already lives in its own file is not in the package's source, so re-rendering the package around it rewrote the text and lost the line mapping for nothing (B230) |
| **CheckTimings** (in `ModelicaGraph/Analysis/`) | Where a run's time went, per phase: parse, each rule by name, each analysis, and the shared work a rule triggers. One instance per run, passed through the contexts. `mlqt check --timings` prints it; the desktop app logs it. **Use `MeasureNested` for anything lazy and cached**, or the first caller to reach it is billed for it and reads as the slow one (B128) |

**Every host runs server GC** (`<ServerGarbageCollection>` in the CLI, MCP server and Photino projects). A check
parses tens of thousands of classes from every core, and under the default workstation GC the threads spent most
of their time suspended waiting for collections: a large user library took 242s under workstation GC and 70.5s under server GC,
with the same findings and about the same peak memory (B281). `HostGarbageCollectionTests` holds the setting —
the parallel graph analyzers are *slower* than serial ones without it.
