# MLQT Roadmap — Future Developments

This is a living planning document for potential future work on MLQT. It captures
candidate features and enhancements, grouped by theme, with rough value/effort notes.

**Guiding scope:** MLQT's mission is to improve and test the *quality* of Modelica
models through **style checking and static analysis**. Features that require model
**translation** (flattening to executable form) or **simulation** are explicitly out
of scope — model checking against Dymola/OpenModelica remains the only touch-point
with those tools, and it stays optional.

> Status legend — **Value**: ⭐ (nice) → ⭐⭐⭐ (flagship). **Effort**: S / M / L / XL.
> **Boundary**: ⚠ marks items that approach (but should stay inside) the
> no-translation/no-simulation line and need care. **✅ shipped** marks delivered work.

## Where we are (2026-09-03)

**Phases 1–6 of the locked sequencing are shipped**: the findings foundation, the headless CLI,
baseline/ratchet, the CI report formats, `__MLQT` suppression, and the Wave-1 analyses with the
metrics dashboard and coverage trend. Each has an implementation note recording what landed.

**The CI/CD toolchain is feature-complete.** The gaps left inside phases 1–6 were collected in
[Backlog — finishing phases 1–6](#backlog--finishing-phases-16-current-focus) below, and B1–B12 are
all closed. Reviewing the branch end to end before merging it then opened **B13–B24**: four
correctness defects, three pieces of duplication and structure, and the rest documentation that had
drifted from the code. None of them blocks the merge — they are things the work exposed rather than
things it broke — but B20 is sequenced into phase 7a rather than after it, because it is what makes
7a expensive. Cross-platform (§1) was deliberately kept last of the two: it is the big task, and the
point was to start it against a toolchain that is complete rather than one still being finished.

Then **phase 7, the desktop host migration**, opening with the WebKitGTK spike — no longer pulled
early, because the CI work ahead of it does not depend on the answer.

**Backlog: B1–B12 are shipped; B13–B24 were opened by the end-of-branch review on 2026-09-03**
(see [Branch review](#branch-review-2026-09-03)). B8–B11 had been added earlier the same day after
asking how the SARIF work would actually be tested — it had never been checked against the 2.1.0
schema or against the one consumer it was written for. Before B4 started, the work since the list was
written had been bug-driven or user-driven rather than planned:

| Landed | What |
|--------|------|
| `mlqt compare` | A third CLI command: the classes one copy of a library has that another does not, matched on full Modelica name so a restructure on disk is not a difference. Not a roadmap item — it answers "did that refactor lose anything". |
| Spell-check accuracy | Names inherited through `extends` are in scope; the shipped term list carries the engineering vocabulary the dictionaries lack, split so a repository is not given the other dialect's spelling; **Ignore** now records `__MLQT(spelling="…")` in the source, so a waiver holds for the app, the CLI and MCP alike. |
| Formatter correctness | The incremental formatter was corrupting the files it reformatted (duplicate `within` clauses); a new standalone file now takes its `within` from where it is written. |
| Deterministic settings file | `RuleSeverities` is written in alphabetical order — saving unchanged settings used to rewrite every rule to a new line. |

The point of recording them here: the tool has moved since the backlog was drawn up, and the
backlog has not.

---

## 1. Cross-platform support (Linux / macOS) — **including the UI**

The docs promise macOS and Linux support ([getting-started.md](getting-started.md)),
currently unfulfilled. Key architectural fact: **.NET MAUI has no official Linux
target**, so the current BlazorWebView shell cannot simply be recompiled for Linux — the
UI must be **re-hosted, not re-targeted**.

**Decisions (2026-07-17):** the UI ships on Linux too (not just a CLI); **mobile is not a
target — desktop only**; and the end-state is **one cross-platform desktop host replacing
MAUI** (single UI codebase for Windows/Linux/macOS), not MAUI-plus-a-second-host.

What's already portable: the entire `MLQT.Shared` Blazor UI (MudBlazor, Cytoscape) and all
business logic (`MLQT.Services`, `ModelicaParser`, `ModelicaGraph`, `RevisionControl`,
`MLQT.McpServer`) — none depend on MAUI. The only non-portable surface is **three MAUI
platform services**: `IFilePickerService`, `IPowerManagementService`, `ISettingsService`.

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Headless CLI** (`mlqt`) reusing the service layer | ⭐⭐⭐ | M | **✅ shipped** — `mlqt check`, `mlqt baseline` and `mlqt compare`, packaged as a `dotnet tool`. See [cli.md](cli.md). |
| **MCP server on Linux/macOS** as a tested target | ⭐⭐ | S | Built and shipping on Windows; headless stdio, so likely close to working. Not yet *tested* on Linux/macOS — that claim belongs with the phase-7 work. |
| **Single cross-platform desktop host (Photino.Blazor)** replacing MAUI | ⭐⭐⭐ | L | In-process webview → keeps direct filesystem + git/svn access, near drop-in reuse of `MLQT.Shared`. Reimplement the 3 platform services once. Retires MAUI. |
| — *fallback host:* Blazor Server (+ desktop wrapper) | — | L | If in-process webview proves limiting; also opens a future hosted/browser option. |
| **WebKitGTK interop spike** (Cytoscape.js, MudBlazor, highlighting) | — | S | **Opens phase 7.** De-risk the Linux webview engine — it is **not** WebView2 — and gate before committing to the host. Originally to be pulled early; kept in phase 7 (decided 2026-09-02) since nothing in the CI work depends on its answer. |
| **Platform-service ports** (file dialog, power/sleep, settings paths) | ⭐⭐ | M | Per-OS implementations behind the existing interfaces. Settings is mostly path differences. |
| **GUI test harness** (phase 7a — bUnit, Playwright test host, `/selftest` route) | ⭐⭐⭐ | L | `MLQT.Shared` currently has **no tests**, so a host swap has no mechanical parity check. Must run **before** the port: the MAUI conformance baseline cannot be captured once MAUI is retired. See [design-phase7-gui-tests.md](design-phase7-gui-tests.md). |

**Migration approach:** build the **GUI test harness (phase 7a)** first — "confirm feature parity
against the known-good MAUI build" below is an empty promise while `MLQT.Shared` has no tests, and the
conformance baseline it captures is only obtainable while MAUI still runs. Then run the WebKitGTK
spike, then build the Photino host and port the platform services, then **validate on Windows first**
(confirm feature parity against the known-good MAUI build before adding OS variables), then Linux,
then macOS. When it lands, the `MLQT` MAUI project is superseded by a new desktop host project —
update CLAUDE.md and docs accordingly.

---

## 2. New Modelica-specific static analyses (no simulation)

Deeper checks that stay purely structural, extending the existing style rules,
dependency graph, and resource tracking.

### Guiding principle — confidence-aware resolution (avoid false positives)

MLQT often **cannot see the whole symbol universe**: commercial libraries ship encrypted
(opaque to a source-based tool), and even unencrypted dependencies (MSL, another team's
library) may not be loaded. A naive reference checker would flag these as "broken" and
flood a real library with false errors — fatal to trust. So resolution is **three-state**,
not boolean:

1. **Resolved** — definition found in readable source. Fully checkable.
2. **Unresolved but external** — points into a namespace with no source (encrypted, or not
   loaded, or a declared `uses` dependency). → **Assume valid; never gates** (info at most).
3. **Unresolved and should-be-visible** — points into a namespace we have *complete* source
   for, yet the target is missing. → the **only** case that becomes an `error`.

"External" is decided from the loaded-library set + `uses(...)` declarations (an allowlist
of expected-invisible namespaces) + encrypted-file detection + an explicit
treat-as-external config in `.mlqt/settings.json`. Severity follows confidence.

**Encrypted libraries now move *individual classes* from state 2 to state 1** (shipped —
see [encrypted-libraries.md](encrypted-libraries.md) and
[design-encrypted-libraries.md](design-encrypted-libraries.md)). MLQT reads the vendor's
generated help HTML and recovers each documented class's name, description, base classes and
whether it has an icon, so references into commercial libraries resolve and inherited icons are
seen. Resolution against documentation is **asymmetric**: a hit is reliable, but a miss is not
proof of absence — vendors choose how much to document, and that varies between releases of the
same version — so anything the documentation does not name stays in state 2 and never gates.

### Wave 1 — self-contained (need only the user's own source; zero missing-library risk)

Ship these first — they cannot produce library-visibility false positives, so MLQT earns
trust in CI before attempting resolution-dependent checks. *(All approved.)*

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Unused-element detection** (parameters, constants, components, imports, protected vars) | ⭐⭐⭐ | M | **✅ shipped** — `UnusedMembersAnalyzer`, `UnusedImportAnalyzer`. |
| **Unused-class detection** | ⭐⭐⭐ | M | **✅ shipped** — `UnusedClassAnalyzer`, with the "possibly unused API" Info case for public top-level classes. |
| **Duplicate / shadowing declarations** | ⭐⭐ | S | **✅ shipped** — `DuplicateDeclarations` rule + `ShadowingAnalyzer`. |
| **`uses` annotation hygiene** | ⭐⭐⭐ | M | **✅ shipped** — `UsesHygieneAnalyzer`, conservative both ways. |
| **`package.order` / file-structure consistency** | ⭐⭐⭐ | M | **✅ shipped** — `PackageOrderAnalyzer`. |
| **Missing-units presence check** | ⭐⭐⭐ | M | **✅ shipped (plain `Real` only)** — `MLQT.Units.MissingUnit`. A user type that aliases `Real` without a unit is still missed, though the Unit coverage dimension resolves those; see the backlog. |

### Wave 2 — resolution-dependent (built on the confidence-aware resolver)

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Broken references / unresolved `extends` & types** | ⭐⭐⭐ | L | Extends existing `ValidateModelReferences`. Only errors on state 3 above. |
| **Connection integrity** (unconnected/incompatible/duplicate `connect`, direction) | ⭐⭐⭐ | L | Promotes the `ConnectorCompatibility` helper; connector *types* may be external → same three-state gate. |
| **Deprecated-API usage** | ⭐⭐ | M | References to `obsolete` classes + MSL-version compatibility. Needs the target library visible. |
| **Cyclic-dependency detection** | ⭐⭐ | S | Directed graph already exists; surface package dependency cycles. |

### Flagships (later; brush the no-simulation boundary — keep inside it)

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Structural equation-balance check** ⚠ | ⭐⭐⭐ | XL | Count equations vs. unknowns (locally balanced). Needs flattening-lite semantics. |
| **Unit / dimensional consistency** ⚠ | ⭐⭐⭐ | XL | Full dimensional analysis on equations (distinct from the Wave-1 presence check). |

---

## 3. Code metrics & quality gates (the SonarQube playbook)

The largest category of things mature static-analysis tools have that MLQT lacks.
The **metrics dashboard is confirmed** as the surface for reviewing progress — it is where
the ratchet's payoff becomes visible (debt-burndown over time). **Coverage metrics**
(documentation %, unit-attribute %, description %) are the burndown numbers: legacy
libraries start low and the dashboard shows them climbing as debt is worked off.

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Metrics dashboard** (class counts by kind, coverage %) | ⭐⭐⭐ | M | **✅ shipped** — `MetricsDashboard.razor` + `MetricsCalculator`; coverage dimensions follow each repository's enabled rules. LOC, connection counts and inheritance depth were not built — no one asked for them. |
| **Cyclomatic / cognitive complexity** | ⭐⭐ | M | For algorithm sections and functions. |
| **Duplicate / clone detection** | ⭐⭐ | L | Near-identical models/equation blocks via subtree hashing. |
| **Quality gates** ("fail if doc coverage < 80%", "no new critical findings") | ⭐⭐⭐ | M | **✅ shipped** — "no new findings" is the baseline/ratchet gate; `--min-coverage` and `--coverage-ratchet` gate on the coverage numbers. |
| **Trend tracking** (metric snapshots over commits) | ⭐⭐ | L | **✅ shipped** — `.mlqt/metrics-history.json`, written by the dashboard or `mlqt check --metrics`, plotted per scope. |

---

## 4. Linter ergonomics other tools have

Things ESLint / clang-tidy / Roslyn / Checkstyle users expect. Rulesets already live
per-repo in `<repo>/.mlqt/settings.json` (revision-controlled) — today a flat set of
on/off booleans in `StyleCheckingSettings`.

**Key architectural change (Phase 0):** replace the named on/off booleans with a
**rule-id-keyed severity map** — `{ "MLQT.Style.ImportStatementsFirst": "error", ... }`.
One change delivers both per-rule severity *and* an extension slot custom rules drop into
with no schema change. Built-in and custom rules become uniform (stable id + severity).
CI reads severity directly: warnings report but don't fail, errors fail (`--fail-on error`).

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Per-rule severity map** (off/warning/error, rule-id-keyed) | ⭐⭐⭐ | M | **✅ shipped** — `RuleSeverities` in `.mlqt/settings.json`, with the per-repository Off/Info/Warning/Error selectors in the settings UI. |
| **In-source suppression via `__MLQT` vendor annotations** | ⭐⭐⭐ | M | **✅ shipped** — with GUI and MCP authoring actions. Not comments — comments are position-bound and get orphaned when the formatter reorders declarations. Annotations ride on the element (like icons/docs, which already round-trip). Class- and component-level; carries a `reason`. Also the in-source, rename-safe replacement for the name-based `FormattingExcludedModels` list. See §5 design note. |
| **Baseline / ratchet mode** (only fail on *new* findings) | ⭐⭐⭐ | M | **✅ shipped** — see §5. |
| **Custom-rule authoring — declarative tier** (config-driven shape checks) | ⭐⭐ | L | The 80%: annotation-present, identifier-regex, banned-`extends`. No compilation; CI-safe. Registers a rule id + severity. |
| **Custom-rule authoring — compiled-plugin tier** (`VisitorWithModelNameTracking`) | ⭐⭐ | XL | Full parse-tree power escape hatch. ⚠ Loading compiled code in CI is a supply-chain consideration. |
| **Cross-repo shared rule profiles** (ESLint-`extends` style) | ⭐ | M | *Deferred / optional.* Only for orgs running many libraries wanting one house ruleset without drift. Per-repo config + existing naming presets cover the common cases. |

---

## 5. CI/CD & automation integration

MLQT is GUI-first today; the ecosystem norm is "runs in the pipeline." This has become
the leading initiative — see the deep-dive **[design-ci-quality-gate.md](design-ci-quality-gate.md)**
for the baseline/ratchet design, finding-identity model, CLI surface, and phased plan.

Direction (decided): **generic CLI + standard report formats first**, no platform-specific
integrations (first customer uses TeamCity; a prospect uses GitHub without Actions).
Machine-readable output *is* the integration — JUnit XML renders findings in any CI's
test UI, TeamCity service messages auto-graph baseline debt trend.

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Baseline / ratchet mode** (new-vs-existing, warn on touched debt) | ⭐⭐⭐ | M | **✅ shipped** — `mlqt baseline create/update/prune`, `--changed-from`, `--touched-debt warn\|fail\|ignore`. |
| **CLI + JUnit/exit-code contract** | ⭐⭐⭐ | M | **✅ shipped** — `--format junit`, `--fail-on off\|warning\|error`, documented exit codes. |
| **SARIF + TeamCity + markdown serializers** | ⭐⭐ | S | **✅ shipped**, and since validated: the two original gaps (line mapping, base path) plus schema conformance, the rule metadata GitHub renders, and a confirmed ingest — B8–B11. |
| **Pre-commit hook / commit gate** | ⭐⭐ | S | **✅ shipped** — `mlqt hook install` writes a git pre-commit hook that runs the same check. See B6. |
| **PR review annotations** | ⭐⭐ | M | **✅ shipped** — `--format review` writes a GitHub pull-request review body (summary + inline comments on changed lines), posted with `gh api --input`. See B7. |

---

## 6. Documentation-quality analysis

Building on existing spell-checking.

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Documentation coverage reporting** | ⭐⭐ | S | **✅ shipped** — class description, documentation info, documentation revisions, parameter and constant description are coverage dimensions; the Code Review findings name the classes. |
| **Spell-check accuracy** (what counts as a word) | ⭐⭐ | M | **✅ shipped** (2026-09-03) — inherited element names are in scope, the shipped term list carries the engineering vocabulary no English dictionary has (dialect-split), and a word can be waived for one class in source. On MSL this removed 41% of the spelling findings without accepting a single new word. |
| **Preferred English variant** (dialect consistency) | ⭐⭐ | M | Not built. Consistency is currently a side effect of choosing one dictionary: an `en_US` repository reports "modelling" as *misspelled*, which is true but says the wrong thing, and enabling en_GB alongside would accept both spellings everywhere. A rule with a variant map would name the actual problem ("British spelling — this repository uses American") and let a repository take en_GB's vocabulary without its spellings. |
| **HTML validity + broken-link checking** in doc strings | ⭐⭐ | M | Validate `modelica://` cross-references resolve to real classes. |
| **Terminology consistency** | ⭐ | M | Flag inconsistent capitalization/naming of domain terms. Overlaps the variant rule above — the same machinery, a different word list. |

---

## Backlog — finishing phases 1–6 (current focus)

Everything below sits *inside* phases 1–6: gaps left behind when a phase shipped, an item a phase
half-delivered, or — for B13–B24 — a defect the end-of-branch review found in what was delivered.
None of it is a new workstream, and none of it is cross-platform (§1). The point of the list is a
CI/CD toolchain with nothing outstanding before the big migration starts; B1–B12 got it
feature-complete, and B13–B24 are what a careful read of the result turned up.

| # | Item | From | Value | Effort | What is missing |
|---|------|------|-------|--------|-----------------|
| B1 | **SARIF line numbers are model-relative** | Phase 4 known limitation | ⭐⭐⭐ | M | **✅ shipped (2026-09-03)** — findings still carry class-relative lines (what a rule can know, and what the code viewer wants), and every report maps them through `ClassLocation` to the line in the file. It also settled a split nobody had noticed: the whole-graph analyses and the parser were emitting file lines while the rules emitted class lines, so the app scrolled to the wrong place for the first two. A package whose stored source was trimmed is reported at its declaration rather than at a confidently wrong line. |
| B2 | **`--sarif-base <path>`** | Phase 4 deferral | ⭐⭐ | S | **✅ shipped (2026-09-03)** — `--sarif-base <path>` writes SARIF file paths relative to that directory instead of the library. Resolved against the library like the other path options, and refused up front when it does not contain the library, since the report would then have to point outside it — which GitHub rejects, the same silent non-attachment reached from the other side. |
| B3 | **Two outputs from one run** | Phase 4 non-goal | ⭐⭐ | S | **✅ shipped (2026-09-03)** — `--report <format>:<path>`, repeatable, writes extra reports alongside the primary output from the same findings. `--format`/`--out` are unchanged, so existing invocations are untouched. |
| B4 | **Metric-threshold gates** | §3 quality gates | ⭐⭐⭐ | M | **✅ shipped (2026-09-03)** — `--min-coverage <percent>` and `--min-coverage <dimension>=<percent>` gate on a figure; `--coverage-ratchet` gates on the last recorded snapshot, so a legacy library can adopt it without meeting any particular number. Independent of `--fail-on`, and a dimension the repository does not track is warned about rather than silently checked. |
| B5 | **Missing-units rule vs the Unit dimension** | Phase 6 increment | ⭐⭐ | M | **✅ shipped (2026-09-03)** — the rule takes the same `UnitResolver` lookup the dimension uses, so a type that fixes no unit anywhere in its chain is reported like a bare `Real`. The dimension's compliance is now *defined* as "the rule does not flag it", so the two cannot drift again. Without a graph (a snippet check) the rule still judges plain `Real` only — all it can honestly say. On MSL it adds one finding: the library is disciplined about SI types, which is exactly why the gap went unnoticed. |
| B6 | **Pre-commit hook / commit gate** | §5 | ⭐⭐ | S | **✅ shipped (2026-09-03)** — `mlqt hook install\|uninstall\|status` writes a git `pre-commit` hook that runs the same check with the same settings and baseline, so a finding is caught while the fix is still a keystroke away. It exits immediately when the staged change has no `.mo` file in it (a hook that taxes every commit gets uninstalled), and blocks on exit `2` as well as `1`, since a check that could not run has approved nothing. Two decisions worth recording: `--no-verify` is left working on purpose — an unskippable hook is one people delete, and the durable waiver is `__MLQT(suppress=)`, which is reviewed with the code and holds in CI; and a `pre-commit` mlqt did not write is refused rather than overwritten, for both install and uninstall. Git only, and it says so — SVN runs its hooks on the server. The repository is found by walking up from the library, following the `.git` *file* a worktree or submodule has. |
| B7 | **PR review annotations** | §5 | ⭐⭐ | M | **✅ shipped (2026-09-03)** — `--format review` writes the body of a GitHub pull-request review: a summary plus one inline comment per changed line that has a finding on it, posted with `gh api ... --input`. MLQT holds no token and speaks no HTTP, which is the same "machine-readable output *is* the integration" line the rest of §5 takes — and it is the path for a repository that cannot use code scanning at all, which B11 showed is any private one without a Code Security licence. The engineering is in what may be said and where: GitHub accepts a comment only on a line in the pull request's diff and rejects the *whole* review over one that is not, so a finding elsewhere goes in the summary instead, accepted debt is never commented on, several findings on a line become one comment, and there is a cap. The diff is measured from the **merge base**, not the ref — diffing the ref directly reports the base branch's own later commits as this branch's work, and a comment on one of those is the rejection above. Needs `fetch-depth: 0`; a diff that cannot be worked out stops the run rather than posting an empty review. Always `COMMENT`, never `REQUEST_CHANGES`: the exit code is the gate, and a tool that blocks a human's merge button loses its token. |
| B8 | **SARIF conformance is asserted, never validated** | Phase 4 risk, never discharged | ⭐⭐⭐ | S | **✅ shipped (2026-09-03)** — `build/validate-sarif.ps1` generates a report from the committed fixture at `TestFixtures/SarifSmoke/Libraries/Smoke`, checks the paths came out relative to the repository root (so `--sarif-base` is exercised by the same step), and runs `sarif validate`, failing on warnings as well as errors. Wired into `build-and-test.yml`, which also builds and tests `MLQT.Cli` — it did neither before. Validation immediately found two real defects, both fixed here: the driver carried no `informationUri` and no version (SARIF2005). |
| B9 | **SARIF rule metadata is too thin to be useful in GitHub** | Phase 4 gap | ⭐⭐⭐ | S | **✅ shipped (2026-09-03)** — every rule now carries `shortDescription` (the title), `fullDescription` (what the rule wants), `help.text` and `help.markdown` (the alert body: title, description, rule id, category, and where to configure it), a `helpUri` to the settings reference, and its category as a tag. The smoke script checks each of these on the generated report, so an alert cannot go back to naming an id and saying nothing. (The driver's missing `version` and `informationUri` were part of this item and were fixed under B8, since the validator flags them.) |
| B10 | **Accepted debt arrives in GitHub as an open alert** | Phase 4 decision, contradicted | ⭐⭐ | S | **✅ decided and shipped (2026-09-03)** — confirmed against GitHub's documented subset: it supports neither `baselineState` nor `suppressions`, and its own docs say a suppressed result still becomes an alert. So accepted debt is omitted from SARIF by default, with `--sarif-include-accepted` for a consumer that honours `baselineState`, and the run reports how many it left out. The findings are still tagged, and every other format is unchanged — this is a decision about one consumer's display, not about what MLQT found. The same commit warns at GitHub's 25,000-result rejection and 5,000-result display caps, which a library the size of MSL crosses silently. |
| B11 | **Nothing has ever been ingested by GitHub** | Phase 4 claim, unproven | ⭐⭐ | S | **✅ confirmed (2026-09-03)** — a 34-finding report from `ModelicaEditorTests` uploaded to `/code-scanning/sarifs` came back `processing_status: complete` with `errors: null`, and the alerts carry the rule metadata B9 added: `full_description`, a rendered `help` body, `help_uri` and the category tag. Two things it taught us, both now recorded in the CI guide: the upload needs a **public** repository (a private one answers 403 "Code scanning is not enabled" whatever the token's scopes, because it wants a Code Security licence) and the named `commit_sha` must be one GitHub has — an unpushed SHA is accepted and then displays nothing, which reads exactly like success. |
| B12 | **A `replaceable` nested class is checked twice** | found while doing B5 | ⭐⭐ | M | **✅ shipped (2026-09-03)** — `StyleCheckRunner` reports only findings about the class it was given, dropping the parent's copy when the nested class is in the graph and so gets a check of its own. The walk itself is unchanged, so a check with no graph behind it (a snippet, a test) still sees such a class — which is what the guard was protecting and what three tests had encoded. The duplicate copy also carried a line counted from the parent's source while naming the nested class, so a report mapped it to a line belonging to something else; that goes with it. The same walk was corrupting the Unit coverage numbers (a parent whose own quantities were all united could read 0%), fixed alongside. On MSL: 5,328 findings became 5,176, which is exactly what the dashboard counts — the two agree to the finding now. |
| B13 | **MCP hands an agent a file path and a line that do not match** | branch review 2026-09-03 | ⭐⭐⭐ | S | **✅ shipped (2026-09-03)** — `list_findings` returns `line` (the line in `filePath`) and `modelLine` (the line within the class), named as the CLI's JSON names them so the two surfaces cannot be read as meaning different things. Parse errors now come through `ParserErrorReporter.ToFindings` like everywhere else, so the array no longer mixes two conventions under one field — and because a check writes those same errors to the review list, reporting both meant every parse error came back twice once `check_library` had been called; the graph is now the single source and `includeParseErrors: false` actually excludes them. The offending token moved from `details` into the message, which is where the GUI and CLI have always shown it. Finding this is what turned up B25. |
| B14 | **A failed VCS diff reads as "nothing changed"** | branch review 2026-09-03 | ⭐⭐⭐ | S | **✅ shipped (2026-09-03)** — `GetChangedFilePathsSince` returns null when it could not take the diff, in both the Git and SVN implementations, and `ChangedModelResolver` stops the run instead of reporting a clean one. Empty and null are now different answers and the interface says why they have to stay so. The message names the usual cause, as B7's does: the ref has to exist locally and share history with the working copy, which a shallow CI checkout gives it neither of. |
| B15 | **`--changed-from` compares against the ref, not the merge base** | branch review 2026-09-03 | ⭐⭐ | S | **✅ shipped (2026-09-03)** — the Git file-level diff is taken from the merge base of the named ref and HEAD, through the same `ResolveDiffBase` the review diff uses, so the ratchet and the review comments agree on what "this change" means. A base branch that has moved on no longer has its own later commits escalated to whoever opened the branch. SVN keeps comparing against the revision, because there is no merge base to ask for — said plainly in the interface and in the CLI guide rather than left to be discovered. |
| B16 | **A SARIF alert's "learn more" link lands on a page that does not mention the rule** | branch review 2026-09-03 | ⭐⭐ | S | **✅ shipped (2026-09-03)** — `settings-reference.md` carries a **Rule id** column, so the page the `helpUri` points at now names all 28 configurable rules rather than 11, and says what the column is for: the id is what appears in `RuleSeverities`, in a `__MLQT(suppress=)` annotation, in CLI output and in the alert that linked here. The three parse diagnostics stay off it because they are not settings, and the page says where they are instead. A test walks up from the test binary to the `Documentation` folder and fails if the catalog knows a rule the page does not — the claim B9 made was true when it was written and had quietly stopped being true. |
| B17 | **A finding's file path is relative or absolute depending on how the library was named** | branch review 2026-09-03 | ⭐⭐ | S | **✅ shipped (2026-09-03)** — `ClassLocation` normalises to an absolute path in its constructor, which is the one place every location goes through, so no consumer has to know how the command was typed. What a *report* shows is a separate question with a different answer per format, and that now lives in one place too: `CheckReport.RelativeFileFor` gives the console, JSON and JUnit a path relative to the library (SARIF keeps `--sarif-base`, a review comment the repository root). The JSON `File` field is library-relative in every invocation rather than echoing the argument — noted in the CLI reference, since it is the one visible change. |
| B18 | **The markdown summary is written twice** | branch review 2026-09-03 | ⭐ | S | **✅ shipped (2026-09-03)** — a `Markdown` helper holds the findings table, the fixed-entries list and the cell escaper; the summary formatter and the review formatter call it. |
| B19 | **Every CLI test file builds its own fixture** | branch review 2026-09-03 | ⭐⭐ | M | **✅ shipped (2026-09-03)** — one `TempWorkspace` holds the temp directory, the file writing, the git-repository variant B6 and B7 need, and the delete that clears read-only attributes first (without which a git repository is undeletable, which only some of the twelve copies knew). `Cli.Run` replaces eleven identical private runners. Each suite keeps what is its own — a baseline path, a way to rewrite the library between runs — as a dozen lines on top instead of fifty-five of plumbing. 240 lines lighter, same 253 tests. |
| B20 | **The two largest pages hold the logic phase 7a has to test** | branch review 2026-09-03 | ⭐⭐⭐ | L | `CodeReview.razor` is 2,252 lines and `MetricsDashboard.razor` 990, both with analysis, VCS and persistence logic inline — against this repository's own instruction to keep business logic in services, not Razor components. It has not hurt yet because nothing tests them. Phase 7a is bUnit component tests plus a `/selftest` conformance route, and both are far harder against a component of that size: the logic can only be reached by rendering the whole page and driving the UI. This is the cheapest it will ever be to extract — before the tests are written against the current shape, and before the Photino port moves the same code again. Sequence it into 7a, not after. |
| B21 | **CI has never seen this work** | branch review 2026-09-03 | ⭐⭐ | S | **✅ shipped (2026-09-03)** — the workflow builds on a push to any branch, so a long-lived working branch is built as it is written rather than at the moment it is merged. `DymolaInterface.Tests` and `OpenModelicaInterface.Tests` are now **built** by CI though still not run: they drive a live Dymola or OpenModelica install and assert on its answers rather than self-skipping, so a runner cannot execute them — but building them catches the thing CI can catch, an interface change that stops them compiling. Same treatment, and the same comment, as the SVN integration tests. |
| B22 | **The phase 6 note describes wiring that was never built** | branch review 2026-09-03 | ⭐ | S | **✅ shipped (2026-09-03)** — the note says the deferred-metrics wiring was planned and deliberately not built, why computing on open turned out better, and what to pick back up if a second consumer ever needs the numbers before the page is opened. |
| B23 | **Two flags are implemented and absent from the reference** | branch review 2026-09-03 | ⭐ | S | **✅ shipped (2026-09-03)** — `--no-suppress` and `--version` are in the option table, and `--version` is in the tool's own usage text, which had never mentioned it either. |
| B24 | **The review summary counts comments and calls them findings** | branch review 2026-09-03 | ⭐ | S | **✅ shipped (2026-09-03)** — it counts findings. Two on one line are still one comment, which is the point of the merging, but the sentence now agrees with the table beneath it. |
| B25 | **A parse error ends up owned by two classes** | found while doing B13 | ⭐⭐ | M | **✅ shipped (2026-09-03)** — the load owns a broken file's diagnosis, and says so: it attributes each error to the innermost class whose text it is in, then clears `MayRecordParserErrors` on every class in that file. First parse of an enclosing class no longer records its own copy — which it used to, since every ancestor contains the broken text and fails for the same reason, giving one problem as many owners as it had ancestors. A class from a file that parsed cleanly is unaffected: if its own stored source will not parse, that is news, and it is how a class held only in memory reports at all. |

**Sequencing within the backlog:** B1–B3 are the ones a real pipeline hits (they are why a working
GitHub or TeamCity setup still needed hand-holding), so they came first; B4 next, since it is what
turns the metrics work into a gate; B6 and B7 last, being conveniences rather than gaps.

**B1–B12 are all done (2026-09-03).** B8–B11 came out of asking how the SARIF work would be tested,
and they were what actually closed phase 4: until then the output had never been validated, and the
one consumer it was written for rendered almost none of what it carried. B11 was run once B8–B10
landed, as confirmation rather than development — and what it found (code scanning needs a public
repository or a paid licence) is why B7 matters more than "convenience" suggested: for a private
repository the pull-request review is the only route findings have into a review. **B12** came out of
B5 the same way: a correctness bug that inflated every rule's count, so it went before the two
conveniences (B6, B7), which closed the original list.

### Branch review (2026-09-03)

**B13–B24 came out of reviewing `ci-cd-integration` end to end before merging it** — 205 commits,
361 files. They are ordered by what they cost rather than by what they are: **B13–B15 first** (a
wrong line handed to an agent, a broken diff that reads as a clean one, and a diff that blames the
wrong author), then **B16–B17**, then the duplication and documentation items, with **B20 sequenced
into phase 7a** rather than after it because it is what makes 7a expensive.

Four of them are things the branch *exposed* rather than things it broke. B13 is B1 unapplied to
MCP; B15 is the correction B7 needed for review comments, not yet applied to the ratchet; B14 and
B17 are both older than this branch. Two — B18 and B24 — are B7's own, found the same day it landed.

What the review covered, so the gaps in it are on the record too: every CLI flag against `cli.md` and
`--help`; every rule id and every setting against `settings-reference.md`; the phase 1–6 design notes
against the code that claims to implement them; who uses the shared check primitives; the CI
workflow's triggers and steps; and `MLQT.Cli` line coverage (94.5%, lowest class `HookCommand` at
82.2%, all above the 80% bar). What it did **not** do is read all 44,000 changed lines — the reading
was targeted at the CI/CD surface this branch is about, the seams between GUI, CLI and MCP, and the
places the design notes said a risk lived.

---

## Locked sequencing (agreed 2026-07-17)

The CI quality gate (§5) is the leading initiative and commercial wedge; its own phased
plan lives in [design-ci-quality-gate.md](design-ci-quality-gate.md). Full unified order
across all workstreams — ✅ marks a phase whose implementation note records it as shipped:

1. ✅ **Findings foundation** — rule-ID registry, boolean→severity map, semantic
   fingerprints. Prerequisite for everything (CI, baseline, suppression, analyses); also
   improves the existing GUI Code Review. **Implementation plan:**
   [design-phase1-findings-foundation.md](design-phase1-findings-foundation.md).
2. ✅ **Headless CLI MVP** — `mlqt check` reusing the service layer; console + exit code +
   JUnit + JSON; `dotnet tool`. Linux/macOS **headless** starts here, running existing rules.
   **Implementation plan:** [design-phase2-cli.md](design-phase2-cli.md).
3. ✅ **Baseline / ratchet** — `baseline create/update/prune`, new-vs-accepted classification,
   changed-model warn-by-default. The adoption unlock for legacy libraries.
   **Implementation plan:** [design-phase3-baseline.md](design-phase3-baseline.md).
4. ✅ **CI ergonomics** — SARIF, TeamCity service messages + debt-trend statistics, markdown
   summary. Lights up the first customer's TeamCity.
   **Implementation plan:** [design-phase4-ci-ergonomics.md](design-phase4-ci-ergonomics.md).
5. ✅ **Suppression (`__MLQT` annotations)** — with GUI + MCP authoring actions. Lands before
   the analysis wave generates intentional-exception cases (declaration-order case).
   **Implementation plan:** [design-phase5-suppression.md](design-phase5-suppression.md).
6. ✅ **Wave-1 analyses + dashboard** — unused elements/classes, duplicate/shadowing, `uses`
   hygiene, `package.order` consistency, missing-units; plus the metrics dashboard/burndown.
   The debt-ledger content that makes the ratchet compelling.
   **Implementation plan:** [design-phase6-analyses-dashboard.md](design-phase6-analyses-dashboard.md).
7. **Desktop host migration (Photino, retire MAUI)** — delivers the **Linux UI**. Opens with the
   WebKitGTK interop spike to de-risk the engine, then host + platform-service ports, validated
   Windows-parity → Linux → macOS. **Starts once the backlog above is clear.**
   Preceded by **phase 7a, the GUI test harness** — bUnit component tests, a Blazor Server test host
   driven by Playwright, and a `/selftest` conformance route whose MAUI baseline must be captured
   *before* the port begins. **Implementation plan:** [design-phase7-gui-tests.md](design-phase7-gui-tests.md).
8. **Wave-2 analyses** — confidence-aware resolver, then broken references, connection
   integrity, deprecated-API, cyclic dependencies.
9. **Extensibility, then flagships** — declarative custom rules → compiled plugins; finally
   the boundary-brushing dimensional-analysis and structural equation-balance checks.

**Placement notes:** desktop UI sits at #7, after the CI value that ships fastest to real
customers; swap earlier if the Linux UI becomes the harder customer commitment. Suppression (#5)
precedes the analyses (#6) so intentional-exception handling exists before new rules generate
exceptions.

**Amendment (2026-09-02):** the WebKitGTK spike is no longer pulled early. It was hedging a
sequencing risk that has gone — phases 1–6 are shipped, nothing in the backlog above depends on the
spike's answer, and interleaving it would interrupt the CI work for a question that only matters
once phase 7 starts. It opens phase 7 instead.
