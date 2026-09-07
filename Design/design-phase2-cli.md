# Design Note — Phase 2: Headless CLI (`mlqt check`)

> **Status: IMPLEMENTED** (all suites green — MCP 240 after the move, Services 519, CLI 22; the
> tool packs and installs via `dotnet tool`). Phase 2 of the locked roadmap
> ([roadmap.md](roadmap.md)). Builds on the Phase 1 findings foundation
> ([design-phase1-findings-foundation.md](design-phase1-findings-foundation.md)) and is the
> vehicle for the CI quality gate ([design-ci-quality-gate.md](design-ci-quality-gate.md)).
>
> **Deviations from the sketch, decided during implementation:**
> - **No DI host.** `LibraryDataService`, `CustomDictionaryService`, and `DictionaryManagerService`
>   have no constructor dependencies, so the CLI constructs them directly — `MLQT.Cli` needs **zero
>   NuGet packages** (no `Microsoft.Extensions.Hosting`).
> - **Hand-rolled arg parser** instead of `System.CommandLine`, to avoid the preview package's API
>   churn for the small MVP surface. Contained to `CheckOptions.TryParse`.
> - The `check` command also accepts a **single `.mo` file** (via `AddLibraryFromFileAsync`), not
>   just a directory.
> - Console colour auto-disables when stdout is redirected (`Console.IsOutputRedirected`) in
>   addition to `--no-color`/`NO_COLOR`, so piped/CI output is never littered with ANSI codes.
> - The shared pipeline landed as `MLQT.Services/Checking/` (`SpellCheckerFactory`,
>   `StyleCheckContext`, `StyleCheckRunner`, `LibraryCheckSession`); the MCP server now references it.

## Purpose

A headless, cross-platform CLI that loads a Modelica library, runs the Phase 1 findings
pipeline, and emits results in CI-friendly formats with a meaningful exit code. This is:

- **the Linux/macOS-headless entry point** — the first thing MLQT ships that runs where MAUI can't; and
- **the CI vehicle** — the `check` command CI invokes.

**Non-goals for Phase 2** (later phases; seams reserved): baseline/ratchet and
`--changed-from` diffing (Phase 3); SARIF, TeamCity service messages, markdown summary
(Phase 4); `__MLQT` suppression *behaviour* (Phase 5 — the no-op seam already runs). Phase 2
runs the existing rules and emits console/JSON/JUnit.

## What the code already gives us (reuse, don't reinvent)

The MCP server is a headless console app that already does load + check without MAUI; the CLI
mirrors it:

- **Load a directory** → `ILibraryDataService.AddLibraryFromDirectoryAsync(path)` recursively
  collects the package structure (following directories with a `package.mo`) and populates
  `LoadedLibrary.ModelIds`; the graph is `ILibraryDataService.CombinedGraph`.
  Dependency analysis (`AnalyzeDependenciesAsync`) and `SessionState`/`GraphRefresh` are **not**
  prerequisites for a one-shot check.
- **Check pipeline** (mirrors `StyleTools.CheckLibrary`): gather `library.ModelIds` →
  `GetModelById` (skip `IsParseFailurePlaceholder`) → `StyleCheckContext.Build(settings, graph,
  customDict, dictMgr)` once → per model `StyleCheckRunner.Run(node, settings, context)`.
- **Spell/dictionary services are already headless** — `CustomDictionaryService`,
  `DictionaryManagerService`, `SpellCheckerFactory` use `Environment.GetFolderPath`, not MAUI.
  Bundled dictionaries ship in `ModelicaParser`. Reusable as-is.
- **Engine**: the Phase 1 `StyleChecking.RunStyleCheckingFindings(...)` returns structured
  `Finding`s — the CLI uses this directly (not the `LogMessage` projection).

Two facts that shape the design:

1. **`.mlqt/settings.json` in a bare directory is not auto-loaded.** It is read only when a path
   is added as a *repository* via `RepositoryService`. So the CLI loads it directly (a 3-line
   `JsonSerializer.Deserialize<StyleCheckingSettings>`), avoiding the VCS machinery.
2. **`StyleCheckContext` / `StyleCheckRunner` / `SpellCheckerFactory` are `internal` to
   `MLQT.McpServer`.** The CLI can't reference them → see the structural decision below.

## Structural decision: factor the check glue into `MLQT.Services`

**Recommended:** move `SpellCheckerFactory`, `StyleCheckContext`, and `StyleCheckRunner` from
`MLQT.McpServer/Helpers/` into `MLQT.Services` as **public** types (e.g.
`MLQT.Services/Checking/`), and add a small facade:

```csharp
// MLQT.Services/Checking/LibraryCheckSession.cs
public sealed class LibraryCheckSession
{
    public static IReadOnlyList<Finding> Check(
        DirectedGraph graph, IEnumerable<ModelNode> models, StyleCheckingSettings settings,
        ICustomDictionaryService customDict, IDictionaryManagerService dictMgr,
        IFindingSuppressor? suppressor = null);   // parallel per-model RunStyleCheckingFindings
}
```

`MLQT.Services` is UI-free and already referenced by the MCP server, so this is the natural
shared home. `StyleTools` then calls the shared facade (small change; behaviour identical —
guard with the existing MCP tests), and the CLI calls the same code. One implementation, reused
by MCP, CLI, and — later — the Phase 3 baseline runner and any web front end.

*Fallback (lower effort, worse):* copy the three helpers into the CLI. Rejected — it forks the
pipeline the whole roadmap builds on.

## New project: `MLQT.Cli`

- `net10.0` console, mirrors the MCP server's references: `MLQT.Services`, `ModelicaGraph`,
  `ModelicaParser`, `RevisionControl`. Add one `<Project Path="MLQT.Cli/MLQT.Cli.csproj"/>` line
  to `MLQT.slnx`.
- **`PackAsTool`** (the MCP server is *not*): `<PackAsTool>true</PackAsTool>`,
  `<ToolCommandName>mlqt</ToolCommandName>`, a `PackageId` — installable via
  `dotnet tool install -g`. (Self-contained per-OS binaries are a Phase 7 packaging concern.)
- **DI bootstrap**: reuse `Host.CreateApplicationBuilder(args)` and register the singletons the
  MCP server uses (`Program.cs:26-38`) minus the MCP/monitoring/impact/external ones — the
  minimal set is `HeadlessSettingsService`, `LibraryDataService`, `CustomDictionaryService`,
  `DictionaryManagerService` (trim during implementation). This wires `LibraryDataService`'s
  dependencies cleanly rather than hand-constructing them.
- **Argument parsing**: greenfield (the MCP server has none). **Recommended: `System.CommandLine`**
  — the idiomatic .NET CLI library, gives `--help`/validation/subcommands for free and scales to
  the later `baseline`/`--changed-from` surface. *(Note its preview status; a minimal hand-rolled
  parser is the zero-dependency fallback for the small MVP surface.)*

## Command surface (MVP)

```
mlqt check <library-path>
    [--config <path>]              # default: <library-path>/.mlqt/settings.json, else built-in defaults
    [--format console|json|junit]  # default: console
    [--out <file>]                 # default: stdout
    [--fail-on off|warning|error]  # default: error (see exit-code policy)
    [--no-color]                   # also honour the NO_COLOR env var

# reserved, error out with "not yet supported" until the owning phase:
    [--baseline <path>]            # Phase 3
    [--changed-from <ref>]         # Phase 3
```

Pipeline: resolve path → `AddLibraryFromDirectoryAsync` → load settings (see below) → build
`StyleCheckContext` once → `LibraryCheckSession.Check(...)` → format → exit code.

**Settings resolution:** `--config` if given, else `<library-path>/.mlqt/settings.json` if it
exists (direct `JsonSerializer.Deserialize<StyleCheckingSettings>`), else
`new StyleCheckingSettings()` (all rules off → no findings — the CLI prints a hint that no rules
are enabled).

## Output formats — one findings model, several serializers

Behind an `IFindingFormatter` seam (so Phase 4's SARIF/TeamCity/markdown slot in without touching
the pipeline):

| Format | Shape |
|--------|-------|
| **console** (default) | Human summary grouped by file; each line = severity · `ruleId` · `line` · message; footer with per-severity counts. Colourised unless `--no-color`/`NO_COLOR`. |
| **json** | Array of finding DTOs: `ruleId, severity, model, element, line, message, fingerprint, file`. `fingerprint` is already emitted here → baseline-ready for Phase 3. |
| **junit** | `<testsuites><testsuite><testcase classname="<file or model>" name="<ruleId>[:element]"><failure message="…"/></testcase>…</testsuites>`. Each finding = a failing test case → renders natively in any CI test-report UI (TeamCity, Jenkins, GitLab, Azure) with **zero** custom integration. No findings → an empty/passing suite. |

**File paths:** `Finding` carries `ModelId` (FQN) but not a file path. The CLI builds a
`modelId → filePath` map from the graph's `FileNode`→model links for the JSON `file` field and the
JUnit `classname`.

## Exit-code policy

| Code | Meaning |
|------|---------|
| 0 | No findings at/above the `--fail-on` threshold (gate passed) |
| 1 | Findings at/above the threshold |
| 2 | Usage / load error (bad path, unreadable config, parse-of-config failure) |

**Default `--fail-on error`.** Rationale: Phase 1 emits only `Warning`s, so the default gate is
effectively report-only today (exit 0 while surfacing warnings) — the non-breaking adoption story
for a legacy library. Real gating becomes meaningful as per-rule `error` severities (Phase 4) and
the baseline (Phase 3) arrive. `--fail-on warning` gives strict teams the stricter gate now.

## Testing — `MLQT.Cli.Tests`

- A small fixture library in a temp directory (a couple of `.mo` files with known findings),
  plus a `.mlqt/settings.json` fixture.
- Assert: finding count for enabled rules; each formatter's output shape (valid JUnit XML, JSON
  round-trips to the DTO, console contains the summary); exit codes for the three `--fail-on`
  modes; settings resolution (config path vs directory default vs built-in defaults); bad-path → 2.
- Reuse the temp-dir fixture pattern from the existing service tests.

## Work breakdown (each step compiles + tests green)

1. **Factor the check glue** into `MLQT.Services/Checking/` (public `SpellCheckerFactory`,
   `StyleCheckContext`, `StyleCheckRunner` + `LibraryCheckSession`); update `StyleTools` to call
   it; confirm MCP tests still pass.
2. **Create `MLQT.Cli`** (csproj with `PackAsTool`, project refs) + add to `MLQT.slnx`.
3. **Arg parsing** + the `check` command and its options.
4. **Settings resolver** (`--config` / `<path>/.mlqt/settings.json` / defaults).
5. **Load + check** via the DI host and `LibraryCheckSession`.
6. **Formatters** (`IFindingFormatter`: console, json, junit) + the `modelId→filePath` map.
7. **Exit-code policy**.
8. **`MLQT.Cli.Tests`**.
9. **`dotnet tool` pack + local install smoke test**; user docs (`Documentation/cli.md`,
   CLAUDE.md project list).

## Roadmap seams established in Phase 2

| Seam | Serves | Phase |
|------|--------|-------|
| `IFindingFormatter` | SARIF / TeamCity / markdown serializers | 4 |
| `--baseline` / `--changed-from` args (reserved, stubbed) | baseline / ratchet | 3 |
| `LibraryCheckSession` shared facade | Phase 3 baseline runner, future web UI | 3 / 7 |
| `fingerprint` already in JSON output | baseline identity | 3 |
| The `mlqt` tool itself running on Linux/macOS | cross-platform headless | (now) |

## Key decisions & risks

- **Decision:** factor the check glue into `MLQT.Services` (shared) rather than copy into the CLI.
- **Decision:** load `<path>/.mlqt/settings.json` directly rather than via `RepositoryService` —
  avoids pulling the VCS/repository machinery into a one-shot check.
- **Decision:** `System.CommandLine` for parsing (hand-rolled fallback).
- **Decision:** default `--fail-on error` (report-only until severities/baseline land).
- **Risk:** moving the three `internal` helpers touches `MLQT.McpServer` — behaviour-identical,
  guarded by the existing 240 MCP tests.
- **Risk:** `System.CommandLine` preview API churn — contained to the arg-parsing layer.
- **Risk:** cross-platform path/encoding — loading already uses `Encoding.Latin1` and
  `Environment.GetFolderPath`; validate the app-data dir resolves on Linux/macOS in the smoke test.
