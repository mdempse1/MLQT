# CLAUDE.md

This file provides guidance to Claude Code when working with this repository. It holds what every
session needs; **detail lives where it is used** — a `CLAUDE.md` in each major project (loaded when
you work in that folder) and the skills in `.claude/skills/` (loaded by their description). Keep this
file under about 200 lines: add detail to those, not here.

## Project Overview

MLQT is a cross-platform Blazor application built with .NET 10. UI components in the Shared project are hosted in a desktop webview: **`MLQT.Photino`, on Windows and Linux** — WebView2 and WebKitGTK respectively. It was a .NET MAUI application until phase 7b-8 (2026-09-10), which deleted that host after verifying Photino against it on all 16 `/selftest` probes on both platforms. Nothing in the repository depends on MAUI now, `PortabilityTests` is what keeps it that way, and the `desktop-host` skill carries what the host work taught.

The MLQT UI is intended to be a users primary way to manage Modelica libraries in revision control systems and supports SVN and Git. The intention is for users to work with MLQT to review and commit changes, pull updates, create new branches and push changes to the revision control system. It also provides static analysis of Modelica code to understand the impact of changes, apply formatting rules and check code against style guidelines.

Use the CODING_GUIDELINES.md whenever generating or refactoring code.

## Solution Structure

| Project (+ `.Tests`) | What it is | Detail |
|---|---|---|
| **MLQT.Shared** | Shared Blazor components, pages, layouts, `AppState`; logic in `.razor.cs` code-behind | `MLQT.Shared/CLAUDE.md`, `gui-testing` skill |
| **MLQT.UI.Theme** | MLQT's look as a Razor class library other tools reference: the MudBlazor theme, the base and density stylesheets, Roboto, `ProgressDialog`. MLQT.Shared and MLQT.McpTester build on it, and it alone pins MudBlazor | `MLQT.UI.Theme/CLAUDE.md`, `README.md` (for a tool using it) |
| **MLQT.Photino** | **The desktop host**, Photino.Blazor on Windows and Linux. `Program.cs` is the whole of it: `AddMlqtCore()`, the three platform services, the window, settings migration from MAUI | `desktop-host` skill |
| **MLQT.Services** | Business logic services and the shared check pipeline (`Checking/`) | `MLQT.Services/CLAUDE.md` |
| **MLQT.McpServer** | Headless MCP server over stdio; every tool `mlqt_`-prefixed; `AddMlqtMcpServer` lets another server be built on it | `MLQT.McpServer/CLAUDE.md`, `README.md` |
| **MLQT.McpTester** | Photino app for testing any stdio MCP server by hand, images included | `MLQT.McpTester/README.md` |
| **MLQT.Cli** | Headless `mlqt` CLI (`check`, `baseline`, `compare`, `hook`), shipped in the installers | `MLQT.Cli/CLAUDE.md`, `Documentation/cli.md` |
| **ModelicaParser** | ANTLR-based Modelica parser, renderer, highlighter, style rules | `ModelicaParser/CLAUDE.md` |
| **ModelicaGraph** | Directed graph for file/model relationships, resolution, whole-graph analyses | `ModelicaGraph/CLAUDE.md` |
| **RevisionControl** | Git/SVN integration | `revision-control` skill |
| **DymolaInterface** / **OpenModelicaInterface** | Dymola HTTP JSON-RPC / OpenModelica ZeroMQ interfaces | `simulation-tools` skill |
| **MLQT.TestHost** / **MLQT.Journeys** | Blazor Server host of the shared UI, and the Playwright journeys driven against it (also the documentation screenshots) | `gui-testing` skill |
| **TestSupport/** | Source files linked into the test projects (`SvnTestRepository`, `TestPaths`, `InMemorySettingsService`, the settings and JSON contracts) | `test-tooling` skill |

**Seven of these are built on outside this repository.** Other projects build `ModelicaParser`,
`ModelicaGraph`, `RevisionControl`, `DymolaInterface`, `OpenModelicaInterface`, `MLQT.Services` and
`MLQT.UI.Theme` from source, so those seven track their public surface in `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt`
(`Microsoft.CodeAnalysis.PublicApiAnalyzers`, switched on by `<TrackPublicApi>`). A new public member
fails the build until it is listed, and a removed one until its line goes, so every change a consumer
would feel is a reviewed diff. See `CODING_GUIDELINES.md` §Public API of the Assemblies Others Build On.

## Build and Run Commands

Test projects run on **xUnit v3 / Microsoft.Testing.Platform**, opted into repository-wide by
`global.json` — an all-or-nothing switch, so every test project must stay on it. VSTest-only flags
(`--nologo`, `-v`, `--logger trx`) are errors there and present as "0 tests ran" rather than as an
error; use `--report-trx --report-trx-filename x` and `--coverlet` instead. `--filter` keeps its
VSTest syntax. Test projects are `OutputType=Exe` and can be run directly as executables.

**A warning fails a CI build** (`Directory.Build.props`, B439): wherever `CI=true` - every GitHub
Actions runner - `TreatWarningsAsErrors` is on, except NuGet's vulnerability audit. A local build still
only warns, so check the output: `CI=true dotnet build MLQT.slnx -c Release` is what CI will say.

```bash
dotnet build MLQT.slnx                                   # build everything
dotnet test MLQT.Services.Tests                          # one suite
pwsh ./build/run-all-tests.ps1                           # every suite, more than CI runs
dotnet run --project MLQT.Photino/MLQT.Photino.csproj    # the desktop app (Windows and Linux)
build/publish-tools.sh --version 1.2.3 --output publish/win-x64 --allow-missing-svn   # installer tree + smoke tests
```

Building, the installers and CI are written up for contributors in `BUILDING.md`; cutting a release
in `RELEASING.md`.

### Editing files and running commands without a prompt

`.claude/settings.json` pre-approves the routine work here: `dotnet build/test/run/publish/restore`,
git's read commands plus `add`, `commit`, `mv` and `restore --staged`, the repository's own `build/`
scripts, `python build/mcp-eval.py analyse|compare|check-tasks`, and the read-only shell tools (`cat`,
`grep`, `sed -n`, `head`, `tail`, `cut`, `sort`, `uniq`, `diff`, `od`, `find`, `wc`). **`git push`,
`gh pr create|merge`, `gh api`, `gh release` and `python build/mcp-eval.py run` always ask** - they
publish, or call the model and cost money.

A command can still ask when every word of it is approved, because what decides is its shape. In one
long session, 378 shell commands out of 719 asked, and **351 of the 378 were compound commands**.
None of the Edit or Write calls asked. So:

- **Change a file with the Edit tool**, not a shell command. Edit keeps the file's CRLF endings; a
  script, `sed -i` or a heredoc is how the tree has lost them before, and the guard tests whose
  regexes carry a literal `\r\n` then fail
- **When a script really must rewrite files**, write it into the scratchpad with Write and run
  `python -I <script>`. It reads bytes and writes bytes, keeping `\r\n`. Never paste Python inline as
  `python - <<'EOF'` or a multi-line `python -c "..."`; both usually ask
- **Don't `cd` into the repository**: the shell already starts there. Of 108 commands that began
  `cd /c/Projects/MLQT`, 84 asked
- **One command per call, or a plain pipeline.** Each of these makes a command ask: `$(...)`, a `for`
  loop, `VAR=...;` followed by `$VAR`, a heredoc, a `> file` outside the repository, and an
  environment prefix other than `CI=true dotnet build`. Run two plain calls instead of composing them

## Architecture Patterns

Services that could be used outside Blazor are in `MLQT.Services/` with interfaces in `MLQT.Services/Interfaces/`.

**Pattern for platform-specific services:**
1. Define interface in `MLQT.Services/Interfaces/`
2. Implement in `MLQT.Photino/Services/`, per platform where the platforms differ
3. Register in `MLQT.Photino/Program.cs`

**Pattern for reusable .NET services:**
1. Define interface in `MLQT.Services/Interfaces/` — **always there**, even when the implementation
   lives in a subfolder such as `Checking/`. One folder answers "what services are there?"
2. Implement in `MLQT.Services/`
3. Register as singleton in `MLQT.Shared/MlqtServiceCollectionExtensions.cs` (`AddMlqtCore`) — **not**
   in a host. Every host calls it, so a service registered in one host and not another is a class of
   bug that stops existing

**Adding a feature:** define the interface in the appropriate `Interfaces/` folder, implement it in
`MLQT.Services/` or `MLQT.Shared/Services/`, register it as a singleton in `AddMlqtCore()`, use events
for cross-component communication, and keep business logic in services, not Razor components.

**The core services** (`ILibraryDataService`, `IRepositoryService`, `IFileMonitoringService`,
`IStyleCheckingService`, `IModelChangeClassifier`, the model-checking services and the rest) each have
rules a caller must keep — the one way to ask which library owns a class, the one status query per
working copy, how to pause the monitor. **Read `MLQT.Services/CLAUDE.md` before calling one.**

**The shared check pipeline** (`MLQT.Services/Checking/`) keeps the desktop app, `mlqt check` and the
MCP server reporting the **same findings with the same line numbers**. Change the primitive, never
one tool's path. Every host runs server GC (B281).

**Platform-specific services** — the whole of what a host contributes, registered in
`MLQT.Photino/Program.cs`:

| Service | Implementation | Purpose |
|---------|----------------|---------|
| **IFilePickerService** | `MLQT.Photino/Services/PhotinoFilePickerService.cs` | Native file/folder picker dialogs, through the window |
| **IPowerManagementService** | `MLQT.Photino/Services/PowerManagementService.cs` | Prevents system sleep during long operations — `SetThreadExecutionState` on Windows, an inhibit lock held through `systemd-inhibit` on Linux |
| **ISettingsService** | `MLQT.Services/JsonSettingsService.cs` | Settings persistence, as JSON under `%LocalAppData%/MLQT` (`~/.local/share/MLQT`). Not host-specific at all any more, which is why it is in `MLQT.Services`; `MauiPreferencesFile` is read once on first run to bring a pre-7b user's settings across |

**Application state and UI** (`MLQT.Shared/CLAUDE.md` has the whole of both): cross-component events
are on `AppState`, and you call its methods (`ChangeModelID()`, `VcsFilesChanged()`, ...) rather than
assigning its properties, because the method is what raises the event. UI is MudBlazor, Small and
Dense, minimal spacing; component logic goes in a `.razor.cs` code-behind with `[Inject]` properties;
Razor event handlers use `await InvokeAsync(StateHasChanged)`.

**Logging** is file only (`%LocalAppData%/MLQT/`, `~/.local/share/MLQT/` on Linux) unless
`MLQT_LOG_CONSOLE` is set, so the log file is the single place to look.

## Key Files

| File | Purpose |
|------|---------|
| `MLQT.slnx` | Solution file |
| `MLQT.Shared/MlqtServiceCollectionExtensions.cs` | `AddMlqtCore()` — the service registrations every host needs |
| `MLQT.Photino/Program.cs` | The desktop host: the window, the three platform services, settings migration |
| `MLQT.Shared/Layout/MainLayout.razor` | Main layout, analysis pipeline orchestration |
| `MLQT.Shared/Models/AppState.cs` | Application state and cross-component events |
| `ModelicaParser/StyleRules/RuleIds.cs` / `RuleCatalog.cs` | The rule registry — a new rule needs no registering anywhere else (see `ModelicaParser/CLAUDE.md`) |
| `MLQT.Services/Checking/LibraryCheckSession.cs` | The shared load→check pipeline |
| `RevisionControl/Interfaces/IRevisionControlSystem.cs` | Unified Git/SVN interface |
| `RevisionControl/Interfaces/ILineLevelDiff.cs` | Line-level diff, implemented by Git only — why a pull-request review needs Git |

Each project's `CLAUDE.md` lists its own key files.

## Modelica Package Structure

- **package.order files** define child element ordering
- **Standalone classes** can be stored as separate files (no `replaceable`, `redeclare`, `inner`, `outer` prefix)
- **Non-standalone classes** must be nested in parent package.mo
- `ModelicaRenderer` supports selective class exclusion when generating package.mo

## Where the Detail Lives

**Project `CLAUDE.md` files** — `ModelicaParser/`, `ModelicaGraph/`, `MLQT.Services/`, `MLQT.Shared/`,
`MLQT.UI.Theme/`, `MLQT.McpServer/`, `MLQT.Cli/`. Each is read when you work in its folder.

**Skills** (`.claude/skills/<name>/SKILL.md`, chosen by their description): `revision-control`,
`simulation-tools`, `external-resources`, `icon-extraction`, `nuget-packages`, `cytoscape`,
`spell-checking`, `naming-conventions`, `encrypted-libraries`, `desktop-host`, `gui-testing`,
`mcp-library-loading`, `test-tooling` and `user-documentation`. A skill's `description` is what
decides when it is loaded, so it names the files and tasks it covers.

**User documentation** is in `Documentation/` (the `user-documentation` skill indexes it, and holds how
its screenshots are generated rather than taken). **Contributor documentation** is `BUILDING.md` and
`RELEASING.md`.

## Planning and Design Notes

In `Design/`, deliberately outside `Documentation/`: these are not user documentation, they are the
forward plan and what a release needs beyond CI. **Read the roadmap before starting anything
substantial.** These four are the whole of `Design/`; two pieces of work are planned in notes of
their own: phase 2, in progress, and the unit-consistency flagship, not started.

| Document | Covers |
|----------|--------|
| `Design/roadmap.md` | Candidate work by theme, the locked phase sequencing, where the project is, which design notes were retired into what, what was *decided against*, the *known open issues*, and the item-id rule |
| `Design/phase-2-analyses.md` | **The design for phase 2, the Wave-2 analyses, in progress**: what every analysis shares (one `IGraphAnalyzer` each, three-state reporting, built off by default), then each analysis in turn, designed as it is started. Retire it when the phase ships |
| `Design/unit-consistency.md` | **The design for the phase-3 unit / dimensional consistency check, not started**: what it builds on (measured), the unit algebra, three-state typing of an equation, the finding, the increments, and the questions the user decides first. Retire it when the check ships |
| `Design/release-checklist.md` | **What CI cannot do for you before a release**: the three suites no runner runs, the fidelity and resolution corpora (opt-in, so an ordinary run says nothing about them), the nightly WebKit rehearsal, the `.deb` job that only a tag exercises, and the protected-build comparison of the MCP stack. Adding to it is a decision that something *cannot* be a gate on every push |

**A `Bnnn` in a comment, a test or a script is a backlog id.** They were issued, B1–B498, in
`Design/backlog.md`, the working list until 2026-09-28. That file was retired once every item was
closed or carried into the roadmap, and what it knew that outlives the fix went into the docs,
skills and guidelines. The ids keep their meaning: `git log --diff-filter=D -- Design/backlog.md`
finds the commit that deleted it, and **its parent holds each item's full record**, including how it
was closed. **An id is never reused.** A new item continues from the number the roadmap's *Item ids*
section states, and `MLQT.Cli.Tests/MarkdownTableTests.cs` holds that every id in the roadmap is
unique and below it.

**Write a design note for a phase that has not shipped**, not for one that has: a note describing
what was planned rather than what exists is worse than no note, and every review of this repository
found notes that had drifted into exactly that. Retire it when the phase lands and move what is
durable into the code, the guidelines or a skill.

## Documentation Maintenance

Update this file only for what every session needs — a new project, a changed architectural pattern.
Put the rest where it is used:
- A project's services, types and traps → that project's `CLAUDE.md`
- A subsystem that spans projects, or a procedure (testing, packaging, docs) → a skill; adding or
  removing a NuGet package → the `nuget-packages` skill
- A user-visible change → its page in `Documentation/`, and the project's README

Update `Design/roadmap.md` when:
- A phase ships, or a decision changes the agreed sequencing
- A defect is found that is not fixed at once (add it under *Known open issues* with the next id),
  or one there is fixed (remove its row, and put anything durable it taught into the docs, a skill
  or the guidelines)
- Something is deliberately not done (*Decided against, for now*, with what would reopen it)

`MLQT.Cli.Tests/MarkdownTableTests.cs` checks every table in `Design/`, every `CLAUDE.md` and every
skill: a blank line between rows, or an unescaped `|` in a cell (even inside backticks), fails it.

## Test Cases

Comprehensive tests are required for all classes with the goal being >80% coverage for each class.  The ModelicaParser assembly requires >95% coverage for all classes as this is critical to the project.

```powershell
./build/run-all-tests.ps1                        # all 10 suites, ~4 minutes - run before pushing
./build/run-all-tests.ps1 -CoreOnly -SkipBuild   # what CI runs, on a machine without Dymola or omc
./build/check-coverage.ps1                       # the per-class coverage ratchet CI enforces
```

**Load the `test-tooling` skill** before running anything beyond one suite: which suites CI runs and
why, the live-tool and svn filters, the installer smoke tests, the coverage ledger, the opt-in
real-library corpora (run by hand after changing `ModelicaTokenClassifier` or a resolver),
`dotnet-trace`, and mutation testing. The rules every test must keep:

- **Classify a test by what it needs, never by what it is called.** A class needing a live tool
  carries `[Trait("Requires", "Dymola")]` or `[Trait("Requires", "OpenModelica")]`; every SVN test
  builds its own repository with `TestSupport/SvnTestRepository.cs`, never a fixed path on the machine
- **A raw string literal's line endings are the checkout's** — normalise a fixture where it is
  declared and search for `\n`, never `\r\n`; and **never write a path as `@"C:\repo\..."`** — build it
  with `Path.Combine` from `Path.GetTempPath()`. Both have shipped green on Windows and failed on Linux
- **A failure is a failure, whichever suite it is in.** `-CoreOnly` is a decision; reading past a red
  line is not
- **Test fixtures:** `TestFixtures/SarifSmoke/` (SARIF validation, see `MLQT.Cli/CLAUDE.md`),
  `TestFixtures/McpEval/` (the MCP evaluation harness, which costs money to run — see
  `MLQT.McpServer/CLAUDE.md`) and `TestFixtures/StartupScripts/` (Dymola startup scripts, the
  `mcp-library-loading` skill)
