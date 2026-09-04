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

## Where we are (2026-09-04)

**Phases 1–6 of the locked sequencing are shipped**: the findings foundation, the headless CLI,
baseline/ratchet, the CI report formats, `__MLQT` suppression, and the Wave-1 analyses with the
metrics dashboard and coverage trend. Each has an implementation note recording what landed.

**The CI/CD toolchain is finished.** The gaps left inside phases 1–6 were collected in
[Backlog — finishing phases 1–6](#backlog--finishing-phases-16-current-focus) below. B1–B12 closed
the feature list; reviewing the branch end to end before merging it opened **B13–B25**, and those are
closed — five correctness defects, two pieces of duplication, and documentation that had drifted from
the code. A second read on 2026-09-04 opened **B26–B35**, and those are closed too: it confirmed every phase
1–6 claim against the code and a green build, and found one exception path outside the documented
exit codes, two rule-classification mistakes, and a set of duplication, test and documentation gaps.
A third read the same day opened **B37–B49**, and those are closed too: it re-confirmed phases 1–6
against a green build, a passing coverage gate and a schema-validated SARIF report, and found one
destructive defect (**B37** — the MCP `set_style_settings` tool switched off every rule the caller
did not name, and committed that), plus the same two patterns the second read named, one surface
further on — a catalogued promise no test holds anyone to, and one rule written in several places.
**B36 is closed too** — spelling and naming were given the four-button picker, which removes the
two-level control the demotion came from rather than papering over it. A fourth read, also on
2026-09-04, opened **B50–B64**, and those are closed too: it re-confirmed phases 1–6 against a
0-warning Release build, a passing coverage gate and a schema-validated SARIF report, and found one
wrong number a user would see (**B50** — a library excluded from checking was still counted in every
coverage percentage and in the coverage gate, B39's defect on the one exclusion mechanism B39 did not
look at), plus the same two patterns for the third read running and one the reading could not have
found: **B64**, a coverage gate that failed and then passed on identical code. **B20 is
deliberately left**: it belongs inside phase 7a, because the logic sitting in the two largest
pages is what makes a GUI test harness expensive to build. Cross-platform (§1) was kept last of the
two on purpose — it is the big task, and the point was to start it against a toolchain that is
complete rather than one still being finished.

Then **phase 7, the desktop host migration**, opening with the WebKitGTK spike — no longer pulled
early, because the CI work ahead of it does not depend on the answer.

**Backlog: everything is shipped except B20, which belongs to phase 7a.**
B13–B25 were opened by the end-of-branch review on 2026-09-03 (see
[Branch review](#branch-review-2026-09-03)), B26–B35 by the second read on 2026-09-04 (see
[Second review](#second-review-2026-09-04)), B37–B49 by the third (see
[Third review](#third-review-2026-09-04)) and B50–B64 by the fourth (see
[Fourth review](#fourth-review-2026-09-04)). B8–B11 had been added earlier the same day after
asking how the SARIF work would actually be tested — it had never been checked against the 2.1.0
schema or against the one consumer it was written for. Before B4 started, the work since the list was
written had been bug-driven or user-driven rather than planned:

| Landed | What |
|--------|------|
| `mlqt compare` | A third CLI command: the classes one copy of a library has that another does not, matched on full Modelica name so a restructure on disk is not a difference. Not a roadmap item — it answers "did that refactor lose anything". |
| Spell-check accuracy | Names inherited through `extends` are in scope; the shipped term list carries the engineering vocabulary the dictionaries lack, split so a repository is not given the other dialect's spelling; **Ignore** now records `__MLQT(spelling="…")` in the source, so a waiver holds for the app, the CLI and MCP alike. |
| Formatter correctness | The incremental formatter was corrupting the files it reformatted (duplicate `within` clauses); a new standalone file now takes its `within` from where it is written. |
| Deterministic settings file | `RuleSeverities` is written in alphabetical order — saving unchanged settings used to rewrite every rule to a new line. |
| Layout rules say how much they mean it | A layout rule's severity is now worked out rather than chosen: off when its switch is off, **Warning** while it is advice, **Error** once the formatter is rewriting every class on save to satisfy it. Came out of B28 — the settings dialog offers these as switches, so the level had nowhere to be set and every one of them reported at Warning whatever the repository intended. |
| Ordering rules require one of each section | The renderer reorders a class only in its one-of-each-section mode, so the other ordering rules on their own reported an arrangement **Format All Files** could never produce. The dependency is enforced rather than documented: the rules resolve to Off without it, the settings dialog greys the switches out (keeping what they were set to, so enabling the prerequisite restores them), and `mlqt check` warns a hand-edited settings file that they are inert. A repository carrying that combination stops reporting ordering findings, which is the point — and is why it warns rather than going quiet. |
| The formatter honours "initial sections last" | `ModelicaRenderer` wrote `initial equation`/`initial algorithm` blocks first whatever the setting said, so a repository choosing **last** had the finding reintroduced on every save and the rule could never be satisfied. It writes them where you asked; the rule joins the set the formatter maintains, and its coverage dimension drops off with the others. |

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
half-delivered, or — for B13 onwards — a defect one of the four end-of-branch reviews found in what
was delivered. None of it is a new workstream, and none of it is cross-platform (§1). The point of
the list is a CI/CD toolchain with nothing outstanding before the big migration starts; B1–B12 got it
feature-complete, B13–B25 are what the first careful read of the result turned up, B26–B35 what the
second one did, B37–B49 the third, and B50–B64 the fourth.

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
| B26 | **`MLQT.Check.Failed` is style debt everywhere the parse diagnostics are not** | second review 2026-09-04 | ⭐⭐ | S | **✅ shipped (2026-09-04)** — `RuleIds.IsDiagnostic` is the question the baseline and the metrics trend were actually asking — *is this a diagnostic rather than a rule* — and all three ids answer yes. `IsParseDiagnostic` stays, narrowed to its one real use: deciding whether a finding is projected as a parser message or a style one. `MLQT.Check.Failed` also leaves the `Parse` category for one of its own, since nothing failed to parse — reading the file worked and MLQT threw afterwards, and filing it under Parse is what made the predicate look like it covered the id. `suppress_rule` now refuses a diagnostic rather than editing the source and reporting success for an annotation nothing reads; its example rule id was `MLQT.Documentation.ParameterDescription`, which is not a rule, so an agent copying it wrote a waiver that matched nothing. |
| B27 | **An unexpected exception breaks the exit-code contract in the CLI and loses every graph finding in the GUI** | second review 2026-09-04 | ⭐⭐⭐ | S | **✅ shipped (2026-09-04)** — `GraphAnalysisRunner` reports an analyzer that throws as `MLQT.Check.Failed` and runs the rest, which fixes the CLI, MCP and the app at the one place all three go through; reading a class's suppression annotations is per-class for the same reason, so a class that will not re-parse costs its own waivers and its findings arrive unsuppressed — the safe direction. Around the CLI's dispatch, an unexpected exception is now exit `2` and one `error:` line saying it is our defect, with the detail to report. The GUI catches per repository instead of around the loop, so one repository's failure no longer loses the project's graph findings in silence. |
| B28 | **`MLQT.Style.ExtendsAtTop` accepts a severity nobody honours** | second review 2026-09-04 | ⭐⭐ | S | **✅ shipped (2026-09-04)** — decided as an explicit sub-rule rather than a new toggle: `RuleDefinition.GovernedBy` records that `ExtendsAtTop` is decided by `ImportStatementsFirst`, `SeverityFor` resolves a governed rule through its governor rather than through an entry it will never have, and the checker's Off-to-default fallback becomes the safety net it was always described as. `StyleCheckingSettings.IgnoredRuleKeys` answers the other half and `mlqt check` warns about every key a settings file cannot set — a governed rule, a diagnostic, or an id matching no rule at all. That last case is the reason to have it: until now the only thing between a typo in `ruleSeverities` and a rule silently never running was noticing the finding count. |
| B29 | **Nothing checks that a rule in the catalog is reachable in the settings UI** | second review 2026-09-04 | ⭐⭐ | S | **✅ shipped (2026-09-04)** — `RuleSettingsLayout` is the single answer to “where is this rule set?”. The dialog renders its picker rows from it instead of from copies, and the rules it places by hand — the formatting switches, which interlock and drive the formatter; naming and spelling, which reveal sub-panels — are declared with the binding they use. The tests close both directions: every configurable rule has a home, nothing is placed twice or placed unsettable, and a bespoke row is checked against the markup, because that is the one claim the layout cannot make true by itself. |
| B30 | **Small duplications in the places most likely to drift** | second review 2026-09-04 | ⭐⭐ | M | **✅ shipped (2026-09-04)** — `CheckFailure` writes the “this result is incomplete” report in both shapes, taking the two things that legitimately vary (what was attempted, and whether anything beyond this class's findings was lost — dependency analysis also loses the edges). `RepositoryService` calls `VcsLocator`, passing its own long-lived systems, so a directory cannot be Git to `mlqt check` and Local to the app. `ModelicaName` takes a fully-qualified name apart in one place instead of six, two of which were private helpers in a single file doing the identical thing under different names. |
| B31 | **CI measures coverage for the assemblies this branch did not add** | second review 2026-09-04 | ⭐⭐ | S | **✅ shipped (2026-09-04)** — `build/check-coverage.ps1` runs all six suites, merges their reports and gates per class; CI runs it. It is a **ratchet, not a flat threshold**, for the reason MLQT sells one: some debt predates the bar and some cannot be paid on a runner at all — the SVN tests need a working copy and a server no runner has, which puts `SvnRevisionControlSystem` near zero there as a fact about the runner rather than the code. `build/coverage-baseline.json` records the fourteen classes currently below their bar, and the build fails when one goes further backwards, when a class that met the bar stops meeting it, or when a new class arrives below it. Classes under 25 coverable lines are measured but not gated — a four-line record whose only uncovered lines are the compiler's `Equals`/`GetHashCode` reads as 50%, and chasing that produces tests that assert nothing. Merged line coverage is 86.7% and ModelicaParser 97.6% against its 95% bar. The ledger's fifteen entries are the remaining coverage debt: five that no runner can pay (the three SVN classes, the MCP server's stdio entry point, and the two external-tool checking services that talk to a live Dymola or OpenModelica), and ten that are ordinary missing tests — two of which, `ModelicaFileEncoding` and `MlqtSuppressionWriter`, are within a point of clearing. |
| B32 | **Gaps in the tests behind the fixes that were made** | second review 2026-09-04 | ⭐⭐ | M | **✅ shipped (2026-09-04)** — the TeamCity escaper is tested, including the one input that can tell the two possible orderings of the replacements apart (a bar immediately followed by a quote). `ChangedModelResolver` takes its VCS as an optional argument, empty in production, so B14's null-diff branch is finally reachable from a test — what the tests protect is the distinction itself: null means the diff could not be taken, empty means nothing changed. And `SvnRevisionControlSystem.GetChangedFilePathsSince` has tests where it had none, two of which need no working copy, plus the two properties the caller depends on and neither implementation stated: absolute paths, and no deleted files. |
| B33 | **The pre-commit hook checks the working tree, not what is being committed** | second review 2026-09-04 | ⭐ | S | **✅ shipped (2026-09-04)** — documented rather than changed, in `cli.md` and in the generated hook's own header. A partial commit is judged on the unstaged remainder too, and a fix made but not staged will not block the commit that still contains the problem; both are now stated, with why it is a trade (checking the index means materialising it, which taxes every commit for a case that is rare in Modelica work) rather than an oversight. |
| B34 | **The Coverage dashboard has no user documentation** | second review 2026-09-04 | ⭐⭐ | S | **✅ shipped (2026-09-04)** — `metrics-dashboard.md`: what each of the fourteen dimensions counts, the three reasons one is not listed, why the finding count moves when the percentages barely do, and why coverage and the finding list disagree about a waived finding. Writing it turned up that the tab is called Metrics in the app and “the Coverage dashboard” in the documentation, the CLI help and a dozen comments; it is Metrics everywhere now. |
| B35 | **CLAUDE.md does not mention the roadmap or any design note** | second review 2026-09-04 | ⭐⭐ | S | **✅ shipped (2026-09-04)** — CLAUDE.md has a Planning and Design Notes section naming `roadmap.md` and all eight design notes, and says to read the roadmap before starting anything substantial. Its maintenance rules now ask for the backlog and the phase notes to be kept up: an item finished but still open reads as outstanding work, and a note describing something planned and not built is worse than no note. `IBaselineStatusService` moves to `MLQT.Services/Interfaces/`, and the instruction says “always there, even when the implementation lives in a subfolder” — which is the case that drifted. |
| B36 | **Switching a rule off in the GUI forgets the severity it was set to** | found while doing B28 | ⭐ | S | `SetRuleEnabled(false)` removes the rule's entry outright, so an explicit severity is not remembered: switching it back on re-seeds the catalog default and a repository's `"Error"` has become `"Warning"`, with the dialog looking exactly as it did before and nothing written down about it. **Narrowed since it was opened.** It reached the rules the dialog shows as a switch rather than as a severity picker, which was the four formatting rules plus spelling and naming; the formatting four now derive their level from whether the formatter maintains them, so they store nothing to lose and the exposure is spelling and naming alone — three rules nobody is likely to have raised to Error for a gate. Current behaviour is pinned by `GovernedRuleTests.SwitchingOffAndOnAgainCurrentlyLosesAnExplicitSeverity`, which names this item and should assert `Error` once it is fixed. Two ways out, and it is a UI decision rather than a mechanical one: give those rules the same four-button picker as every other rule, or keep the switch and remember the severity across an off/on within the dialog. **✅ shipped (2026-09-04)** — the first of the two, as decided: spelling and naming now use the same four-button picker as every other rule, and the choice between them turned out to be less even than it looked. Remembering a severity across an off/on is a workaround for a control that can only express two of the four levels; a picker writes the level the user chose, so there is nothing to remember on their behalf and no demotion to be silent about. Revealing a sub-panel — the dictionary languages, the naming presets — was never a reason to hide the level, and each still appears whenever its rule is not Off. `RuleSettingsLayout` carries the three as `SeverityPicker` rows, so the dialog renders them from the same list as the rest and a regression to a switch fails `SpellingAndNamingAreOfferedAsPickers`. The bool facade still cannot carry a severity and still has a test saying so — that is now a fact about the facade, which the settings file's backward compatibility and the derived-level formatting switches both still need. |
| B37 | **`set_style_settings` switches off every rule the caller did not name** | third review 2026-09-04 | ⭐⭐⭐ | S | `StyleSettingsInput`'s rule properties are non-nullable `bool`s and `ApplyTo` assigns all twenty-nine of them, so an agent that sends `{ "classHasDescription": true }` to enable one rule writes `false` over every other rule, and `SaveRepositorySettingsAsync` persists that to the committed `.mlqt/settings.json`. An omitted property and a deliberate "off" are indistinguishable. The same DTO got this right for `SpellCheckLanguages` — nullable, and "when null or empty on save, the existing languages are kept" — so the shape of the fix is already in the file: make the rule properties `bool?` and leave a null alone. Until then the tool's own description ("Only the rule toggles and spell languages are changed") reads as a reassurance while describing the loss, and the visible symptom is a large unexplained diff on a reviewed file, or a CI gate quietly narrowed to one rule. **✅ shipped (2026-09-04)** — the toggles are `bool?`, and `ApplyTo` writes only the ones that were supplied: a rule the caller did not name keeps whatever the repository had. `SpellCheckLanguages` had always worked that way and the rules had not, which is the whole defect. The tool's description says it is a merge and that a key may be omitted. Two things fell out of doing it. `From` now reports whether a rule is *switched on* rather than whether it would currently run, because the ordering rules read as off while `OneOfEachSection` is off — so a read-modify-write round trip through `get_style_settings` used to discard them, silently, having changed nothing. And the check tools are unaffected: they build from a blank settings object, where "not mentioned" and "off" are the same thing, which is pinned by a test. |
| B38 | **Nothing holds the MCP settings DTO to the rule catalog** | third review 2026-09-04 | ⭐⭐ | S | Adding a rule to `RuleCatalog` needs three hand edits in `QualityDtos.cs` — the property, the `ApplyTo` line, the `From` line — and no test fails if any of them is missed: an agent then cannot enable the rule, and `get_style_settings` cannot see it. This is exactly the gap B29 closed for the settings dialog and B16 for the documentation, one surface short. The count happens to be right today (29 configurable rules, 29 bools plus the `ComponentsBeforeClasses` formatter flag), which is the point — it is right by hand. The guard is the same one twice over: assert every `RuleCatalog.Configurable` id round-trips through `From`/`ApplyTo`. Better still, drive the DTO from the catalog and delete the three lists. **✅ shipped (2026-09-04)** — `ApplyTo` and `From` walk one table binding each rule id to its property instead of listing the twenty-nine names three times, and `StyleSettingsInput.SettableRuleIds` exposes it. `StyleSettingsCoverageTests` holds that list to `RuleCatalog.Configurable` in both directions — a rule with no toggle fails, and a toggle for a governed rule or a diagnostic fails too — and checks every toggle reads back what it wrote, which is what catches one wired to a different rule for reading than for writing. Same guard as `RuleSettingsLayoutTests` and `RuleDocumentationTests`, now on the third surface. |
| B39 | **The two formatting exclusions disagree about coverage** | third review 2026-09-04 | ⭐⭐ | S | `FormattingExcludedModels` takes a class off the Layout coverage dimensions (`CoverageDimensions.TrackedFor`, `MetricsCalculator`); `__MLQT(format=false)` / `preserveOrder=true` does not, because it is applied inside `RunStyleCheckingFindings` by the suppression extractor and nothing outside the checker knows about it. So the mechanism phase 5b calls "the in-source, rename-safe successor", and that the docs steer new usage to, leaves the dashboard reporting a Layout gap no finding will ever name — the precise thing `CoverageDimensions` says it exists to prevent. Note that the two defensible directions conflict: `MetricsCalculator` states that coverage is "never scraped from findings" and shows the true state "whatever has been waived", which argues the *name list* is the one behaving wrongly. Either answer is fine; having both is not. **✅ shipped (2026-09-04)** — settled in the direction the file already argued for: coverage must not show a gap no finding will name, and the checker skips the layout rules for both exclusions, so coverage skips them for both. `__MLQT(format=false)` / `preserveOrder=true` is a fact about the source, so it is read where the tree is already in hand — `CoverageMeasurer` records it as `CoverageFacts.FormattingPreserved` — and `CoverageDimensions.ForClass` consults it alongside the name list. A test asserts the two mechanisms produce the same rows, which is the property that was missing rather than either answer. |
| B40 | **The per-model layout exclusion is written three times, and the shared version has no caller** | third review 2026-09-04 | ⭐⭐ | S | `CoverageDimensions.TrackedFor(settings, modelId)` exists to answer "which dimensions apply to this class", and its `modelId` overload is reached only from `CoverageDimensionsTests`. Production asks the question three times instead, each rebuilding the same `FormattingExcludedModels.Count > 0 && IsModelExcludedFromFormatting(id) ? tracked & ~Layout : tracked`: `MetricsCalculator.Compute`, `StyleCheckingService`'s coverage sweep, and — by omission — `StyleCheckContext`, which builds its measurer from the un-narrowed mask. Three copies of one rule with the canonical one unused is how B39 comes to be fixed in only one of them. **✅ shipped (2026-09-04)** — `CoverageDimensions.ForClass` is the one narrowing, and the three sites call it: `MetricsCalculator` (twice — once to decide what to measure, once with the facts to decide what to report), `StyleCheckingService`'s sweep, and `TrackedFor(settings, modelId)`, which is now a front door onto it rather than a fourth copy. `StyleCheckContext` deliberately does not narrow and says why: it decides what to *measure*, and measuring a dimension the report will drop costs one walk while not measuring it costs a re-parse. |
| B41 | **`mlqt hook install` ignores `core.hooksPath`** | third review 2026-09-04 | ⭐⭐ | S | `HookCommand` walks up to `.git` and writes `hooks/pre-commit` under it. A repository that sets `core.hooksPath` — which husky, pre-commit and lefthook all do — makes git read hooks from somewhere else entirely, so the file is written, `install` reports success, `status` reports "mlqt pre-commit hook installed", and no commit is ever checked. `cli.md` and `ci-quality-gate.md` already tell the user to wire the check into their existing configuration in that case; the command itself neither detects the config nor mentions it. Reading `git config core.hooksPath` and refusing with that advice is the whole fix. A silent no-op is the one outcome a commit gate cannot have. **✅ shipped (2026-09-04)** — `install` asks `git config --get core.hooksPath` and refuses when it is set, naming the directory, saying husky/pre-commit/lefthook are the usual cause, and printing the `mlqt check` line to add to whatever they run. `status` and `uninstall` carry on but say the same thing — status has to be able to report what is there, and uninstall has to be able to remove a hook installed before the redirect was set. Git being unavailable is not a refusal: the overwhelmingly common case is no redirect at all, and declining to install because we could not ask would be worse than installing where git looks by default. |
| B42 | **A diagnostic's SARIF alert explains how to configure a rule that cannot be configured** | third review 2026-09-04 | ⭐⭐ | S | `SarifFindingFormatter.Help` is written for every rule id in the report, so an alert for `MLQT.Parse.SyntaxError`, `MLQT.Parse.Failure` or `MLQT.Check.Failed` says "Configure this rule's severity, or switch it off, in the repository's `.mlqt/settings.json`" and links to `settings-reference.md` — which mentions none of the three, because `RuleIds.IsDiagnostic` is precisely the set that cannot be configured. This is B16's defect for the three ids B16 did not cover, and `RuleDocumentationTests` does not catch it because it holds the catalog's *configurable* rules to the page. The diagnostics need their own help text — what the finding means, and that it is not switchable — and a page to point at. **✅ shipped (2026-09-04)** — the alert body branches on `RuleIds.IsDiagnostic`. A diagnostic is told it is a diagnostic — always reported, never configurable, never baselined, and a statement that the results are incomplete — and its `helpUri` points at `cli.md#diagnostics`, which is where the three are documented. Ordinary rules are unchanged. `RuleDocumentationTests` already held the settings page to the catalog; it now also holds the heading the diagnostics link to, so renaming it fails the build rather than the link. |
| B43 | **The review body calls findings over the comment cap "not on a changed line"** | third review 2026-09-04 | ⭐ | S | `ReviewFindingFormatter` folds `unplaceable` and `overflow` into one `<details>` block headed "N finding(s) not on a changed line", explaining that a comment cannot be attached to them. The overflow findings *are* on changed lines — they were held back only because `MaxInlineComments` is 50 — so the reader is told something false about them, and sent looking for a cause that is not the cause. `ReviewFindingFormatterTests.BeyondTheCap_TheRestAreListedRatherThanDropped` asserts the wrong wording, so the test moves with the fix. Two sections, or one heading that names both causes. **✅ shipped (2026-09-04)** — the block is headed "N finding(s) not commented inline" and lists the two causes separately: "Not on a changed line", which is about the diff, and "Over the comment limit", which is about the fifty-comment cap and says so. The test that asserted the wrong wording moved with it, and two more pin each cause on its own and both together. |
| B44 | **The parse-tree release convention is unwritten, and four sites do not follow it** | third review 2026-09-04 | ⭐⭐ | M | `StyleCheckRunner`, `MetricsCalculator`, `ShadowingAnalyzer`, `UnusedImportAnalyzer` and `UnusedMembersAnalyzer` all clear `Definition.ParsedCode` after use, "to release the parse tree to bound memory". `GraphAnalysisRunner.BuildSuppressions`, `ClassElementResolver`, `TypeResolver` and `UnitResolver` do not — and each of them parses classes *other* than the one under check: base classes up an `extends` chain, the type a component is declared as. On a large library those trees then accumulate for the rest of the run. `CoverageMeasurer` already has the right primitive (its `borrowed` flag: release only what you parsed yourself); nothing else uses it and nothing states the rule. For the suppression reader this is a straightforward leak; for the three resolvers it may be a deliberate cache, in which case that is the thing to write down. It matters on exactly the run the reference-library note is about, where nothing may scale with graph size. **✅ shipped (2026-09-04)** — `ModelDefinition.Borrow` is the convention with a name: it parses if needed, runs the work, and releases the tree again only if it was what parsed it — so a walk hands back the base classes it reached for while leaving the caller's own tree alone. The four sites that kept trees now use it: the graph runner's suppression reader (a plain leak, once per class carrying a graph finding) and the three resolvers, which turned out not to need the trees at all, since what they cache is the answer. `ParseTreeBorrowingTests` pins the primitive — including release on an exception — and each of the four walks. |
| B45 | **`StyleCheckRunner.Run` duplicates `RunFindings` rather than projecting over it** | third review 2026-09-04 | ⭐ | S | The two methods differ only in a final `.Select(f => f.ToLogMessage())` — the same stub guard, the same eleven arguments to `RunStyleCheckingFindings`, the same `OnlyAbout`, the same `Coverage.Measure`, the same tree release, written out twice. They have already diverged once: `Run` has no `honorSuppressions` parameter, so the GUI cannot ask for the audit pass the CLI can. `Run` should be one line over `RunFindings`. **✅ shipped (2026-09-04)** — `Run` is three lines over `RunFindings`, and takes the `honorSuppressions` argument it had been missing. |
| B46 | **Two more small duplications in the finding pipeline** | third review 2026-09-04 | ⭐ | S | The severity-stamping loop — walk the findings, `SeverityFor(id)`, fall back to `RuleCatalog.DefaultSeverityFor` when it resolves Off — is written out in both `StyleChecking.RunStyleCheckingFindings` and `GraphAnalysisRunner.Run`, and the two have already diverged (only the second skips diagnostics, correctly). And `c.Status != FindingStatus.AcceptedDebt` — "is this finding actionable" — is repeated in five of the six formatters, which is one predicate away from a `CheckReport.Actionable`. Neither is broken; both are the shape B30 was opened for. **✅ shipped (2026-09-04)** — `StyleCheckingSettings.StampSeverities` is the one severity stamp, used by both the per-class checker and the graph-analysis runner, with the diagnostic exemption the two had disagreed about. `CheckReport.Actionable` is the one "not accepted debt" predicate, used by all six formatters. |
| B47 | **`ChangedLineResolver` has no tests of its own** | third review 2026-09-04 | ⭐⭐ | S | `GitRevisionControlSystem.GetChangedLinesSince` is covered by `GitChangedLinesTests`, and `mlqt check --format review` end to end by `ReviewCommandTests` — but the resolver between them, which picks the VCS, refuses SVN with an explanation, turns a null diff into the "shallow checkout" message and maps absolute paths back to repository-relative ones, is exercised only incidentally. `RepositoryRelativePath` returning null for a file outside the repository is the branch a review comment depends on, and no test names it. `ChangedModelResolver`, which answers the coarser question, has a test file; this one does not. **✅ shipped (2026-09-04)** — `ChangedLineResolverTests`, eight of them, over the branches a real repository will not perform to order: a diff that cannot be taken, an SVN working copy, somewhere that is not a working copy at all, and a file outside the repository having no path a forge could resolve. Reaching them needed a seam, and the seam is worth more than the tests: `ILineLevelDiff`, implemented by Git and not by SVN, so "a review needs Git" is a fact about the type rather than a check against a concrete class that somebody remembered to write. |
| B48 | **The hook script quotes its arguments without escaping them** | third review 2026-09-04 | ⭐ | S | `HookCommand.Quote` wraps a value in double quotes and stops there, so a library path or a `--changed-from` ref containing `"`, `$`, a backtick or a backslash produces a hook that is wrong rather than one that fails: `sh` expands `$` inside double quotes, and a git ref name may contain one. The values come from whoever runs `mlqt hook install`, so this is robustness rather than a security boundary — but a hook that silently checks the wrong thing is the same failure mode as B41. **✅ shipped (2026-09-04)** — `Quote` escapes backslash, `"`, `$` and backtick, which are what `sh` still reads inside double quotes. A ref named `origin/feature$x` used to be written into the hook unescaped and quietly diffed against `origin/feature`. |
| B49 | **`MLQT.McpServer/README.md` has a broken table and a stale project layout** | third review 2026-09-04 | ⭐ | S | A paragraph about parse-error reporting sits between the "Code quality" and "Spelling" rows of the tools table, which ends the table there — the last six groups render as loose text. And the `Helpers/` bullet still lists `StyleCheckRunner`, which phase 2 moved to `MLQT.Services/Checking/` and which is now the shared primitive the whole pipeline funnels through; the nine helpers actually in that folder are unlisted. The tool *names* in the same file are exact — all 65 match the code, in both directions — so this is presentation and one moved type, not drift in the list itself. **✅ shipped (2026-09-04)** — the paragraph moved below the table, so the last six tool groups render as rows again, and the project layout lists what `Helpers/` actually holds and says the check pipeline is not among it: `StyleCheckRunner`, `StyleCheckContext` and `LibraryCheckSession` are in `MLQT.Services/Checking/`, shared with the CLI and the app. |
| B50 | **An excluded library still counts against coverage** | fourth review 2026-09-04 | ⭐⭐⭐ | S | **✅ shipped (2026-09-04)** — `CoverageDimensions.ForClass` returns `None` for a class in an excluded library, so it is on no dimension rather than on all of them, and `StyleCheckRunner` stops measuring one: the narrowing was already the single answer for the other two mechanisms, and this is the third asking the same method. The class stays in the **Size** census — excluding a library suppresses the quality judgement, not the library, and the settings reference already said so. Four tests, including that an excluded library missing every description leaves the figure at 100% rather than 50%, and that its classes are still counted. |
| B51 | **Baseline drift is blind to a rule going inert** | fourth review 2026-09-04 | ⭐⭐ | S | **✅ shipped (2026-09-04)** — `Baseline.InForce` is the one answer to "which rules will actually run, at what level", resolved through `SeverityFor` and used both to write the ledger and to compare against it, so the two cannot come to mean different things. Switching off `OneOfEachSection` now reports the four ordering rules as disabled-since, which is what stranding their entries actually is; switching the formatter on reports the severity change it makes. A baseline written before this recorded configured levels, so the first check after upgrading may report a change that is not one — the warning already says to regenerate. |
| B52 | **`--no-suppress` reports layout findings coverage has already dropped** | fourth review 2026-09-04 | ⭐ | S | **✅ shipped (2026-09-04)** — `CoverageMeasurer` takes `honorSuppressions` from the run it belongs to, so an audit records `FormattingPreserved: false` and the layout rows stay while `--no-suppress` is putting the layout findings back. It makes the fact one about a run rather than only about the source, which is written down where it is recorded: a facts cache belongs to a run with one suppression mode, and the CLI is a process per run while the app and MCP always honour them. |
| B53 | **An invocation error is found after the whole check has run** | fourth review 2026-09-04 | ⭐⭐ | S | **✅ shipped (2026-09-04)** — both now answer before anything is read. The baseline is loaded ahead of `LoadAndCheckAsync`, and `UsesVersionChecker` runs as soon as the libraries are in the graph, before trimming, dependency analysis or the check — the versions come off the loaded nodes, so nothing below was ever needed to know. Tested by the absence of the first thing the load prints (`settings from …`) and of `running dependency analysis`. |
| B54 | **The `Borrow` convention has more sites outside it than in it** | fourth review 2026-09-04 | ⭐⭐ | M | **✅ shipped (2026-09-04)** — the five hand-written sites are gone: `ShadowingAnalyzer`, `UnusedMembersAnalyzer` (twice) and `UnusedImportAnalyzer` released unconditionally and now borrow, and `CoverageMeasurer` had a second copy of the primitive. Two more leaks turned up while doing it, both of the category B44 named — `StyleChecking.ImportsOf` and the inherited-icon walk, which climbs an extends chain and kept every class it reached. `Borrow`'s summary no longer claims the resolvers keep trees on purpose (they cache the answer, and B44 is what made that true), and it now says where the convention does **not** apply: `GraphBuilder`'s bulk load owns what it parses and releases unconditionally on purpose. Six tests, including that a graph analysis leaves a tree its caller was holding. |
| B55 | **The suppression set is extracted up to three times per class per run** | fourth review 2026-09-04 | ⭐ | S | **✅ shipped (2026-09-04)** — `ClassSuppressions.For(definition, modelId)` is the one read, kept on the class and cleared when its source changes. `SuppressionSet.Empty` is a shared instance, so keeping the answer for a library of tens of thousands of classes costs a reference each rather than a set each — which is what made caching it the right shape rather than a memory regression. The graph analyses stop re-parsing a class to ask what the checker asked of the same class minutes earlier. |
| B56 | **The CLI's metrics file is not the one the app reads** | fourth review 2026-09-04 | ⭐⭐ | S | **✅ shipped (2026-09-04)** — `ResolvedMetricsPath` is `MetricsHistoryStore.RepoPath` over `SettingsResolver.RepositoryRootFor(library)`, the same two pieces the desktop app uses, so whatever `.mlqt` the rules came out of is the one the run's numbers go back into. A library in a subdirectory writes the repository's history rather than a private second file; `--coverage-ratchet` reads it back, which is the half that was a silent gate rather than a stray file. A loose library with no `.mlqt` and no working copy still records beside itself. |
| B57 | **Two adjacent resolvers pick their default VCS list two different ways** | fourth review 2026-09-04 | ⭐ | S | **✅ shipped (2026-09-04)** — `ChangedLineResolver` asks `VcsLocator` for the default systems, as `ChangedModelResolver` already did. Its test seam is unchanged: passing systems explicitly still overrides. |
| B58 | **Metrics for the whole checked set are computed twice** | fourth review 2026-09-04 | ⭐ | S | **✅ shipped (2026-09-04)** — `CheckRunner` computes the whole checked set's figures at most once and hands them to both the trend point and the coverage gate. `MetricsRecorder` takes them for the `""` scope and computes only the per-library ones. |
| B59 | **Nothing holds a coverage dimension to a rule that exists** | fourth review 2026-09-04 | ⭐⭐ | S | **✅ shipped (2026-09-04)** — two tests. One holds `CoverageDimensions.Ordered` to the enum in both directions and asserts every dimension is tracked when every rule is on, which catches a `RuleFor` arm falling through to `""`. The other pins `CoverageDimension.Layout` to the rules `StyleChecking` puts behind `isExcludedFromFormatting`, by running the checker both ways over a class that violates them — the coupling B39 turned on, in two files with nothing else tying them together. |
| B60 | **Six ordinary classes sit in the coverage ledger with no reason** | fourth review 2026-09-04 | ⭐ | M | **✅ shipped (2026-09-04)** — the ledger carries a `reason` per entry and the gate fails on one that does not, so accepting coverage debt is a decision rather than a keystroke. `-UpdateBaseline` carries existing reasons forward and writes a `TODO` placeholder for anything new, which the gate then refuses. All fifteen are written, and the six that prompted this say plainly that the tests are reachable and not yet written — a different fact from "needs a working SVN server", and the one the ledger could not previously express. |
| B61 | **`__MLQT(format=false)` is missing from the formatting documentation** | fourth review 2026-09-04 | ⭐⭐ | S | **✅ shipped (2026-09-04)** — `code-formatting.md` has a section on the annotation as the preferred, rename-safe form, listing all three things it does (out of every formatting pass, the rules suppressed, the layout coverage rows dropped) and saying when to prefer it over the toggle; `code-review.md`'s toolbar row points at it; `ci-quality-gate.md`'s one line says all three rather than one; and `metrics-dashboard.md` carries the exception to its own rule, plus a scope bullet for `ExcludedLibraries` (B50's user-visible half). One stale claim went with it: that page still said the formatter writes initial sections first and so defeats *Initial sections last*, which the renderer stopped doing. |
| B62 | **CLAUDE.md describes the codebase as it was before phases 1–6** | fourth review 2026-09-04 | ⭐⭐ | S | **✅ shipped (2026-09-04)** — CLAUDE.md's key-file table names the phase 1–6 substrate (`Finding`, `RuleIds`/`RuleCatalog`, `RuleSettingsLayout`, `ClassSuppressions`, the three `Analysis/` entry points, `LibraryCheckSession`, `Baseline`, `ClassLocation`, `ILineLevelDiff`); the ModelicaGraph section has an `Analysis/` block and calls out `ModelDefinition.Borrow`, `ClassSuppressions` and `CoverageDimensions.ForClass` as conventions in the style it already used for `WithinClause`; and there is a table for `MLQT.Services/Checking/`, the shared pipeline, saying what each primitive is for. |
| B63 | **An orphaned doc comment in the SARIF formatter** | fourth review 2026-09-04 | ⭐ | S | **✅ shipped (2026-09-04)** — the blank line is gone, so the summary documents the method again. |
| B64 | **The coverage gate is a coin flip on one class** | fourth review 2026-09-04 | ⭐⭐ | S | **✅ shipped (2026-09-04)** — found by running the gate twice on the same code: `MLQT.Services.FileMonitoringService` measured 76.7% and then 81.4%, straddling its 80% bar, so the build failed and then passed with nothing changed. The cause was that its rename and delete handlers had no test of their own and were reached only when Windows happened to report a write as a rename — which it does sometimes, a text write being a temp file and a rename underneath. A handler covered by accident is a handler nobody is checking, and a gate that fails at random teaches people to re-run until green, which is how a gate stops meaning anything. Nine tests now wait for the specific change and assert it arrived, so they either cover those lines or fail: 89.5% on repeated runs. |

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
361 files — and **B25 came out of fixing B13**. All of them are done except **B20**, which is
sequenced into phase 7a rather than before it, because it is what makes 7a expensive.

They were taken in cost order: **B13–B15 first** (a wrong line handed to an agent, a broken diff
that reads as a clean one, and a diff that blames the wrong author), then **B16–B17**, then the
duplication and documentation items.

Most were things the branch *exposed* rather than things it broke. B13 is B1 unapplied to MCP; B15
is the correction B7 needed for review comments, not yet applied to the ratchet; B14, B17 and B25
are all older than this branch. Two — B18 and B24 — are B7's own, found the day after it landed.

Two of them only existed because something else was written down: B16 was a claim that had been true
when B9 made it, and B22 a design note describing an `AppState` surface that was never added. Both
now have a test or a correction rather than a good intention, and B16's is the kind that matters — it
fails when the catalog knows a rule the documentation does not.

What the review covered, so the gaps in it are on the record too: every CLI flag against `cli.md` and
`--help`; every rule id and every setting against `settings-reference.md`; the phase 1–6 design notes
against the code that claims to implement them; who uses the shared check primitives; the CI
workflow's triggers and steps; and `MLQT.Cli` line coverage (94.5%, lowest class `HookCommand` at
82.2%, all above the 80% bar). What it did **not** do is read all 44,000 changed lines — the reading
was targeted at the CI/CD surface this branch is about, the seams between GUI, CLI and MCP, and the
places the design notes said a risk lived.

### Second review (2026-09-04)

**B26–B35 came out of a second read of the branch**, asked for after B13–B25 closed: phases 1–6
against the roadmap and the design notes, then the code for duplication, missing tests, logic
implemented twice, and the documentation against what shipped. **All ten are now closed**, and fixing B28 opened one more: **B36**, where the GUI's on/off switches forget an explicit severity.

**Phases 1–6 verify as delivered.** Every claim in the six implementation notes has code behind it,
the whole solution builds, and all 4,714 tests pass (ModelicaParser 1835, Services 747, ModelicaGraph
706, RevisionControl 645, McpServer 276, CLI 253, Dymola 207, OpenModelica 45 — the last two run here
because this machine has the tools; a runner still cannot). Every `mlqt` flag in the source is in
`cli.md` and in `--help`, and every configurable rule id is on the settings page, held there by
B16's test. Nothing on the list below unships a phase.

What it found instead is one correctness defect worth taking seriously (**B27** — an exception ends a
CLI run outside the documented exit codes, and ends a GUI graph pass with no findings and no notice),
two classification mistakes that are quiet by design (**B26**, `MLQT.Check.Failed` baselineable and
counted as debt; **B28**, a rule id that accepts a severity nothing reads), and a set of gaps that are
each individually small: a guard the settings UI never got that the documentation did (**B29**),
duplication in the three or four places most likely to drift (**B30**), coverage measured in CI for
the assemblies this branch did not write (**B31**), tests missing from under three fixes that were
made (**B32**), and three documentation gaps — the hook's real subject (**B33**), the Coverage
dashboard's missing page (**B34**), and CLAUDE.md never naming the roadmap it is sequenced by
(**B35**).

The pattern across B26–B29 is worth naming, because it is the same one: a *catalogued* thing —
a rule id, a category — carries an implicit promise that some other surface honours it, and no test
holds anyone to that promise. B16 built exactly that test for the documentation, and the fixes point
it at the rest: `RuleCatalog.IsConfigurable` now splits every id into configurable, governed or
diagnostic, `RuleSettingsLayoutTests` fails when a configurable rule has nowhere in the settings
dialog to be set, and `mlqt check` warns about any key a settings file cannot actually set. The three
questions — can this be configured, can it be baselined, can it be found in the UI — are now answered
in one place each rather than implied in several.

What this review covered, so its gaps are on the record too: the six design notes claim by claim
against the code; the rule catalog against `RuleIds`, `settings-reference.md`, the settings UI and
the severity map; every CLI flag against `cli.md` and the usage text; every MCP tool name against
both READMEs; the check pipeline's four entry points (CLI, MCP, GUI worker, GUI graph pass) against
each other; a full build and test run; and the `Documentation/` set against the features that
shipped. What it did **not** do, again, is read all 44,000 changed lines, and it did not review
`CodeReview.razor` or `MetricsDashboard.razor` in detail — that is B20's subject and belongs to
phase 7a.


### Third review (2026-09-04)

**B37–B49 came out of a third read of the branch**, asked for on the same terms as the second:
phases 1–6 against the roadmap and the design notes, then the code for problems, duplication,
missing tests and logic implemented twice, then the documentation against what shipped. **All
thirteen are closed.**

**Phases 1–6 verify as delivered, again, and this time against a green run of everything.** The
solution builds in Release; all **4,809 tests pass** (ModelicaParser 1860, Services 760,
ModelicaGraph 743, RevisionControl 651, McpServer 276, CLI 267, Dymola 207, OpenModelica 45);
`build/check-coverage.ps1` passes, with no CLI or ModelicaGraph class in the debt ledger at all; and
`build/validate-sarif.ps1` validates a generated report against the SARIF 2.1.0 schema with
`Sarif.Multitool` — so B8's conformance claim is not just recorded but re-run. Every `mlqt` flag in
the source is in `cli.md`; every configurable rule id is in `settings-reference.md`; every one of the
65 MCP tool names matches between the code and both READMEs, in both directions; and the fourteen
coverage dimensions are all named in `metrics-dashboard.md`. Nothing below unships a phase.

**One item is worth taking seriously on its own: B37.** `set_style_settings` replaces every rule
toggle from the object it is handed and persists the result to the committed `.mlqt/settings.json`,
so an agent enabling one rule switches off the other twenty-eight. It is the only finding here that
destroys something, and it is reachable by an ordinary use of the tool.

The rest fall into three groups, and the first two are the same two the second review named — which
is the useful thing this read found:

- **A catalogued thing whose promise no test holds anyone to.** B38 is B29 and B16 one surface
  short: the MCP settings DTO lists all twenty-nine configurable rules by hand, in three places, and
  nothing fails when a new rule misses them. B42 is B16 for the three ids B16 excluded — a
  diagnostic's SARIF alert explains how to configure a rule that by definition cannot be configured,
  and links to a page that does not mention it. Both fixes are the shape the second review already
  established: turn the promise into an assertion over `RuleCatalog`.

- **One rule written more than once.** B40 is the sharpest: the per-model layout exclusion exists as
  a shared method whose only callers are its tests, while production rebuilds it in three places —
  which is exactly how **B39** happened, the two formatting-exclusion mechanisms now disagreeing
  about coverage, with the in-source one phase 5b calls the successor being the one that behaves
  worse. B44, B45 and B46 are the same pattern lower down: a parse-tree release convention four sites
  do not follow, a method copied instead of projected over (and already diverged), and the severity
  stamp and the actionable-finding predicate each written out more times than there are of them.

- **A gate or a report that is quietly wrong rather than loudly.** B41 — `mlqt hook install` ignores
  `core.hooksPath`, so on a repository using husky or lefthook it writes a file git never reads and
  then reports the hook installed. B43 — the review body tells the reader that findings held back by
  the comment cap were "not on a changed line", which is false, and a test asserts the wording. B48 —
  the hook script quotes without escaping. B47 is the test gap underneath the same area:
  `ChangedLineResolver` sits between two well-tested things and has no tests of its own.

B49 is presentation: a paragraph split the MCP README's tools table, and its project layout still
named a type phase 2 moved.

**What the fixes converged on.** Four of the thirteen ended in the same place — a rule that had been
written out several times became a named thing with a test behind it — and that is the shape to reach
for next time rather than the individual defects. `CoverageDimensions.ForClass` is now the one answer
to "which dimensions apply to this class" (B39/B40); `StyleCheckingSettings.StampSeverities` the one
severity stamp and `CheckReport.Actionable` the one actionable-finding predicate (B46);
`ModelDefinition.Borrow` the one way to read a class you do not own (B44). Two more turned an
implicit promise into a type or an assertion: `StyleSettingsInput.SettableRuleIds` is held to
`RuleCatalog.Configurable` the way the settings dialog and the documentation already were (B38), and
`ILineLevelDiff` — implemented by Git, not by SVN — makes "a pull-request review needs Git" a fact
about the type rather than a check against a concrete class (B47). That last one was reached for to
make B47's tests possible and is worth more than the tests.

Two fixes turned up a defect next to the one being fixed. B37's `From` was reporting whether a rule
would currently *run* rather than whether it was switched on, so a read-modify-write round trip
through `get_style_settings` silently discarded the ordering rules whenever `OneOfEachSection` was
off — the same distinction `IsRuleSwitchedOn` was added to the settings dialog for, not carried over
to MCP. And B44's three resolvers turned out not to want their trees at all: what they cache is the
answer, so handing the tree back costs nothing, and the "deliberate cache" the item allowed for did
not exist.

What this review covered, so its gaps are on the record too: the six design notes claim by claim
against the code; the rule catalog against `RuleIds`, the severity map, the settings UI declaration
and `settings-reference.md`; every CLI flag against `cli.md` (including which flags in the docs
belong to other tools); every MCP tool name against both READMEs, both ways; the four check entry
points and the three coverage-masking sites against each other; the CI workflow's steps against what
they claim to gate; and a full build, test, coverage and SARIF-validation run. What it did **not** do,
for the third time, is read all 47,000 changed lines, and it did not review `CodeReview.razor` or
`MetricsDashboard.razor` — that is **B20**, and it belongs to phase 7a.

### Fourth review (2026-09-04)

**B50–B64 came out of a fourth read of the branch**, on the same terms as the second and third:
phases 1–6 against the roadmap and the design notes, then the code for problems, duplication,
missing tests and logic implemented twice, then the documentation against what shipped. **All
fifteen are closed.** B64 was the one the reading did not find — it took running the gate twice.

**Phases 1–6 verify as delivered.** As read, the solution built in Release with 0 warnings, all six
gated suites passed (4,603 tests), the coverage gate passed at 86.7% and `build/validate-sarif.ps1`
re-validated a generated report against the SARIF 2.1.0 schema. **After the fixes**: 0 warnings,
**4,642 tests** (ModelicaParser 1860, ModelicaGraph 781, Services 781, RevisionControl 651, McpServer
286, CLI 283 — plus Dymola 207 and OpenModelica 45), the coverage gate at **86.8%** with 148 classes
gated and every accepted entry now carrying a reason, and the SARIF still valid: 3 rules, 5 results,
paths relative to the repository root. Every `mlqt` flag in the source is in `cli.md` and in `--help`;
`cli.md` names no flag the tool does not have. Nothing below unships a phase, and nothing below is a
defect in what a phase claimed — they are all in the seams between what two phases claimed
separately.

**The one worth taking seriously on its own is B50.** MLQT now has three ways to take a class out of
scope — the `ExcludedLibraries` name list, the `FormattingExcludedModels` name list, and
`__MLQT(format=false)` — and B39 spent this branch making the second and third agree about coverage.
The first was never asked. A repository that excludes its examples or test library still has every
class in it counted in the coverage percentages, in the recorded trend and in the `--min-coverage`
gate, while no rule will ever report a finding about it. That is the same defect B39 closed, on the
exclusion mechanism B39 did not look at, and it is the one item here a user would notice as a wrong
number rather than as a missing warning.

The rest fall into the three groups the last two reviews named, which is now the finding rather than
any individual item:

- **One rule written more than once, or in one place and not the neighbouring one.** B54 is the
  sharpest and the most recent: `ModelDefinition.Borrow` was introduced *this branch* as the one way
  to read a class you do not own, and it now has four callers and five hand-written sites — four of
  which release unconditionally, the half its own summary says is wrong, and one of which is a second
  copy of the primitive. Its closing paragraph still describes the resolvers as keeping their trees,
  which the same commit stopped being true. B56 and B57 are the same shape a level down: the CLI
  builds the metrics path by hand instead of through `MetricsHistoryStore.RepoPath`, and so writes the
  trend somewhere the app does not read it; and of two adjacent resolvers, one asks `VcsLocator` for
  the default systems and the other lists them itself. B55 and B58 are the cheap end — the same
  suppression walk done up to three times per class, the same metrics computed twice.

- **A catalogued thing whose promise no test holds anyone to.** B59 is B29/B38/B42 on the fifth
  surface: `CoverageDimensions` maps every dimension to a rule id through a `switch` with a
  `_ => string.Empty` arm and lists them in an `Ordered` array, and a dimension missing from either
  is silently never reported. The same file also carries the `Layout`-to-`isExcludedFromFormatting`
  coupling B39 turned on, with nothing asserting the two sets are the same. B60 is the test-shaped
  version: the coverage ledger this branch added accepts six ordinary in-process classes with no
  reason recorded, against its own rule that an entry needs the same justification as any other
  accepted debt.

- **A gate or a report that is quietly wrong rather than loudly.** B64 was found by running the
  coverage gate twice rather than by reading anything: the same code failed and then passed, because
  one class's measured coverage moves several points between runs and sits on its bar. B51 — baseline
  drift compares the raw severity map, so switching off `OneOfEachSection` strands four rules' entries for ever, and
  flipping `ApplyFormattingRules` moves every layout rule between Warning and Error, with no drift
  reported for either. B52 — `--no-suppress` puts a class's layout findings back while coverage has
  already dropped its rows, B39 in the other direction. B53 — two invocation errors are found after
  the whole check has run, against the principle the same file states for two others.

**Documentation.** B61 and B62 are the two gaps. `__MLQT(format=false)` is called the successor to
`FormattingExcludedModels` in the design notes and the documentation is told to steer users to it,
and it appears in no user page that discusses formatting — while `metrics-dashboard.md` states the
general rule B39 made an exception to, without the exception. And CLAUDE.md's key-file and key-class
tables still describe the codebase as it was before phase 1: they name none of
`ModelicaGraph/Analysis/` and almost none of `MLQT.Services/Checking/`, which between them are the
substrate of phases 2, 3 and 6. B62 is also why B54 keeps happening — the file that calls out
`WithinClause` and `ModelicaFileEncoding` as conventions has nowhere that `ModelDefinition.Borrow` is
named, so a convention introduced this branch is already only discoverable by reading the type.

**What the fixes converged on.** The same shape as last time, one level down: a rule or a read that
had been written out several times became a named thing with a test behind it.
`CoverageDimensions.ForClass` gained the third and last exclusion mechanism (B50) and now answers for
all of them, which is the whole reason B39 made it a method; `ClassSuppressions.For` is the one read
of a class's `__MLQT` directives, where three passes had each walked the tree (B55); `Baseline.InForce`
is the one answer to "which rules will actually run, at what level", used to write the ledger and to
compare against it (B51); and `ModelDefinition.Borrow`, introduced by the *previous* review, finally
has no hand-written rivals (B54) — which is the more interesting fact, because a convention with four
callers and five exceptions is not yet a convention. Two of B54's five turned out to be leaks nobody
had listed: `StyleChecking.ImportsOf`, and the inherited-icon walk that climbs an extends chain.

The pattern worth naming for next time is **B50 itself**. MLQT has three ways to take a class out of
scope, they arrived separately, and each was taught to the checker before anything asked what it meant
for the report. B39 fixed two of them and did not ask about the third. So the rule is not "check the
settings in one more place" but: when adding any way to exclude a class, find every consumer of the
existing exclusions — and add the test that says the mechanisms agree, which is the property that was
missing rather than either answer.

What this review covered, so its gaps are on the record too: the six design notes claim by claim
against the code; the uncommitted working tree, which is where the third review's fixes still sit and
which no review has seen before; every `mlqt` flag against `cli.md` and `--help` in both directions;
the rule catalog against `RuleIds`, the severity map, `RuleSettingsLayout`, the MCP toggle table and
`settings-reference.md`; the three exclusion mechanisms against the four places that decide coverage;
every `EnsureParsed`/`ParsedCode = null` pair in the solution against the `Borrow` convention; and a
full build, test, coverage and SARIF-validation run, repeated after the fixes. What it did **not** do, for the fourth time, is
read all 47,000 changed lines, and it did not review `CodeReview.razor` or `MetricsDashboard.razor` —
that is **B20**, and it belongs to phase 7a.

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
