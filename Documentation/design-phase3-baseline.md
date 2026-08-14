# Design Note — Phase 3: Baseline / Ratchet + Changed-Model Escalation

> **Status: IMPLEMENTED** (3a + 3b; all suites green — RevisionControl 620, Services 527, MCP 240,
> CLI 31). Phase 3 of the locked roadmap ([roadmap.md](roadmap.md)). Implements the baseline/ratchet
> mechanics specified in [design-ci-quality-gate.md](design-ci-quality-gate.md), on top of the
> Phase 1 fingerprint ([design-phase1-findings-foundation.md](design-phase1-findings-foundation.md))
> and the Phase 2 CLI ([design-phase2-cli.md](design-phase2-cli.md)).
>
> **Deviations from the sketch, decided during implementation:**
> - `check --baseline <path>` takes a **required value** (the bare-flag default was dropped as a
>   parsing footgun); the `baseline` subcommand defaults to `<lib>/.mlqt/baseline.json`.
> - **Both Git and SVN** `GetChangedFilePathsSince` shipped (not Git-only) — SVN via
>   `svn diff --summarize --xml -r <rev>`. Git has unit tests; SVN is implemented but lightly tested.
> - `ChangedModelResolver` works off the **model→file map** (decoupled from `DirectedGraph`) and
>   selects the VCS the way `RepositoryService` does (first system whose root contains the path).
> - JUnit emits only `New` (and `TouchedDebt`) as failing test cases; accepted debt is omitted, so a
>   green build means "no new debt."

## Purpose

Make the CLI adoptable on a large legacy library: **tolerate the existing debt, fail only on new
issues**, and optionally nudge cleanup of pre-existing issues in models a change touched. This is
the adoption unlock — the reason the whole CI initiative exists.

The mechanics are already specified (two orthogonal axes, `.mlqt/baseline.json` as a
version-controlled debt ledger, semantic fingerprints). Phase 3 builds them. The Phase 2 CLI
already emits each finding's `Fingerprint` in its JSON output, so the identity layer is done.

## Internal staging

Two sub-steps, because the primary value needs **no VCS at all**:

- **3a — Baseline / ratchet** (no VCS): the baseline file, `baseline create/update/prune`, and
  `check --baseline` classifying findings as *new* vs *accepted debt*, gating on new. Pure
  fingerprint-set membership. Ships the adoption unlock on its own.
- **3b — Changed-model escalation** (VCS diff): `check --changed-from <ref>` splits accepted debt
  into *untouched* (pass) vs *touched* (warn by default). Requires one new VCS-layer method.

## Classification model (recap)

Two independent booleans per finding → three statuses:

| | model unchanged | model changed (`--changed-from`) |
|---|---|---|
| **not in baseline** | `New` | `New` |
| **in baseline** | `AcceptedDebt` | `TouchedDebt` |

```csharp
// MLQT.Services/Checking/FindingStatus.cs
public enum FindingStatus { New, AcceptedDebt, TouchedDebt }
```

With **no** `--baseline`, every finding is `New` — so the gate logic below reduces exactly to the
Phase 2 behaviour (fail on any finding at/above the threshold). One code path, no special-casing.

## The baseline — a version-controlled debt ledger

File: **`<library-path>/.mlqt/baseline.json`** (beside `settings.json`). Instance-level: one entry
per known finding, keyed by fingerprint, with human-readable metadata for review and prune output.

```jsonc
{
  "version": 1,
  "findings": [
    { "fingerprint": "3c634e43…", "ruleId": "MLQT.Doc.ParameterDescription",
      "model": "MyLib.Foo", "element": "x", "message": "Public parameter x must have a description" }
    // …sorted by (model, ruleId, element, fingerprint) for clean, reviewable diffs…
  ]
}
```

- **Only `fingerprint` is load-bearing** for matching; the rest makes the ledger greppable
  (`count by rule` = the debt report) and gives `prune` readable output.
- **No timestamp** — regeneration stays byte-identical when findings are unchanged, so the file
  only churns when debt actually changes. Git history supplies the "when".

```csharp
// MLQT.Services/Checking/Baseline.cs  (shared: CLI now, web/other later)
public sealed class Baseline
{
    public IReadOnlyList<BaselineEntry> Entries { get; }
    public bool Contains(Finding f);                        // fingerprint-set membership
    public static Baseline Load(string path);
    public static Baseline FromFindings(IEnumerable<Finding> findings);
    public void Save(string path);                          // sorted, stable
    public IReadOnlyList<BaselineEntry> StaleEntries(IEnumerable<Finding> current); // fixed → prunable
}
```

## Gate logic (unified)

```
fail if any New finding        with severity >= failOn threshold
   OR (touchedDebtPolicy == fail AND any TouchedDebt >= threshold)
AcceptedDebt never fails.
```

- `--fail-on` threshold as Phase 2 (default `error`). The ratchet's real use is
  `--fail-on warning --baseline …`: fail on **new** warnings, tolerate existing.
- `--touched-debt warn|fail|ignore` (default **warn**) — the boy-scout escalation. `warn` reports
  touched debt without failing.
- **Stale entries** (baseline fingerprints not in the current findings = fixed) never fail; they're
  reported and `prune` removes them.

Exit codes unchanged: `0` pass, `1` gate failed, `2` usage/load error.

## CLI surface

Additions to Phase 2:

```
mlqt check <path> [--baseline <p>] [--changed-from <ref>] [--touched-debt warn|fail|ignore] [existing opts]
      # --baseline default: <path>/.mlqt/baseline.json when the flag is given bare

mlqt baseline create <path> [--baseline <p>] [--config <p>] [--force]   # snapshot current findings
mlqt baseline update <path> [--baseline <p>] [--config <p>]             # regenerate
mlqt baseline prune  <path> [--baseline <p>] [--config <p>]             # drop fixed entries, report count
```

`create`/`update`/`prune` all run the same `LibraryCheckSession` load+check, then read/write the
baseline. This needs a `baseline` command group in `CliEntry` (Phase 2 dispatches only `check`).

## Changed-model detection (3b)

`--changed-from <ref>` → which models did this change touch?

1. **Resolve the VCS system** for the library path by mirroring `RepositoryService` (no factory
   exists): `FindRepositoryRoot(path)`, then pick Git or SVN via `IsValidRepository`.
2. **Get changed file paths since the ref.** The interface has `GetChangedFiles(repo, revision)`
   (single-revision-vs-parent) and `GetWorkingCopyChanges` — neither is "diff since an arbitrary
   ref." **Add one method:**
   ```csharp
   // IRevisionControlSystem
   /// Absolute paths of files changed between <sinceRevision> and the current working state
   /// (committed + uncommitted). Git: tree(sinceRevision) vs working directory (LibGit2Sharp
   /// Diff.Compare<TreeChanges>). SVN: `svn diff --summarize -r <rev>`.
   IReadOnlyList<string> GetChangedFilePathsSince(string repositoryPath, string sinceRevision);
   ```
   **Git first** (covers both target CI customers; the GitHub prospect and TeamCity-on-Git). SVN
   as a fast-follow — matches the existing pattern where some methods are one-VCS-only.
3. **Map changed `.mo` files → model IDs** using the graph's `FileNode.FilePath` → `GetModelsInFile`
   (the reverse of the `modelId→file` map the CLI already builds). Normalise to absolute,
   case-insensitively on Windows. Result: the `changedModelIds` set.

"Changed" = a model's own file changed — no transitive dependents (the baseline already catches
genuinely-new inherited findings via the New axis, per the design note).

## Report & formatter changes

`CheckReport` carries classified findings (`Finding` + `FindingStatus`); with no baseline all are
`New` (Phase 2 output is unchanged). Formatters become status-aware:

- **console** — new findings shown prominently; accepted/touched summarised with counts; footer
  states the gate result.
- **json** — each finding gains a `status`; add a `summary` block (`new`/`accepted`/`touched`
  counts). Fingerprint already present.
- **junit** — `New` (and `TouchedDebt` when `--touched-debt fail`) become failing test cases;
  `AcceptedDebt` is omitted (or `<skipped>`), so a green build means "no new debt."

## Tests

- **Baseline**: load/save round-trip; `FromFindings`; `Contains`; `StaleEntries`; sorted-stable
  output (regenerating identical findings yields an identical file).
- **Classification**: New / AcceptedDebt / TouchedDebt from a baseline + `changedModelIds` set;
  no-baseline ⇒ all New.
- **CLI**: `baseline create` writes the file; `check --baseline` — a pre-existing finding passes,
  a newly introduced finding fails at `--fail-on warning`; `prune` drops a fixed entry; exit codes.
- **Changed-model (Git)**: reuse `RevisionControl.Tests`' LibGit2Sharp temp-repo pattern — commit a
  library, add a baseline, modify one model, assert its pre-existing debt becomes `TouchedDebt`.
- **VCS method**: `GetChangedFilePathsSince` unit tests (Git) alongside the existing Git suite.

## Work breakdown (each step compiles + tests green)

**3a**
1. `Baseline` + `BaselineEntry` + `FindingStatus` + a `FindingClassifier` in `MLQT.Services/Checking/`.
2. CLI `baseline` command group (`create`/`update`/`prune`) in `CliEntry`.
3. `check --baseline` classification + unified gate; wire `--touched-debt` (parsed now, effective in 3b).
4. Status-aware `CheckReport` + formatters.
5. Tests (baseline, classification, CLI).

**3b**
6. Add `GetChangedFilePathsSince` to `IRevisionControlSystem` + Git implementation (+ tests).
7. `ChangedModelResolver` in `MLQT.Services/Checking/` (VCS-select → changed paths → model ids).
8. Wire `--changed-from` into the classifier; touched-debt escalation live.
9. SVN implementation of `GetChangedFilePathsSince` (fast-follow) + tests.

## Roadmap seams established here

| Seam | Serves | Phase |
|------|--------|-------|
| `FindingStatus` on the report | SARIF `baselineState`, TeamCity/markdown "new vs existing" | 4 |
| `Baseline` shared class | web UI debt ledger, trend metrics | later |
| `GetChangedFilePathsSince` (VCS) | PR-annotation "changed files" scoping | 4 |
| Debt counts in JSON `summary` | dashboard burndown | 6 |

## Key decisions & risks

- **Decision:** stage 3a (no VCS) before 3b (VCS diff) — the adoption unlock ships without touching
  the VCS layer.
- **Decision:** checked-in `.mlqt/baseline.json` as the mechanism (reviewable debt ledger);
  base-branch-comparison stays a possible later optional mode, not built now.
- **Decision:** `GetChangedFilePathsSince` is Git-first; SVN follows.
- **Risk:** path normalisation between VCS output and graph `FileNode.FilePath` (absolute vs
  repo-relative, case, separators) — centralise in `ChangedModelResolver` with tests.
- **Risk:** baseline-file diff churn if ordering isn't stable — enforce a total sort on save, tested.
- **Risk:** a reformat that changes a finding's element identity would drop its baseline entry;
  mitigated by the fingerprint deliberately excluding line/position (Phase 1) — but worth a test
  that reformatting a baselined model keeps its findings `AcceptedDebt`.
