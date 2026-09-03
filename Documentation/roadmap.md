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

**Current focus: finish the CI/CD toolchain before starting cross-platform.** What is left inside
phases 1–6 is a short list of gaps rather than new workstreams — collected in
[Backlog — finishing phases 1–6](#backlog--finishing-phases-16-current-focus) below. Cross-platform
(§1) is deliberately last of the two: it is the big task, and it should start against a toolchain
that is already complete rather than one still being finished.

Then **phase 7, the desktop host migration**, opening with the WebKitGTK spike — no longer pulled
early, because the CI work ahead of it does not depend on the answer.

**Backlog: B1–B4 shipped; B8 and B9 next.** B8–B11 were added on 2026-09-03 after asking how the
SARIF work would actually be tested — it has never been checked against the 2.1.0 schema or against
the one consumer it was written for. Before B4 started, the work since the list was written had been
bug-driven or user-driven rather than planned:

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
| **SARIF + TeamCity + markdown serializers** | ⭐⭐ | S | **✅ shipped.** SARIF's two original gaps (line mapping, base path) are closed; conformance and the rule metadata GitHub renders are not — see B8–B11 in the backlog. |
| **Pre-commit hook / commit gate** | ⭐⭐ | S | Not built. Today it is a documented recipe (`--changed-from BASE`), not a feature. In the backlog. |
| **PR review annotations** | ⭐⭐ | M | Not built; the markdown summary path it would use exists. In the backlog. |

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

Everything below sits *inside* phases 1–6: gaps left behind when a phase shipped, or an item a
phase half-delivered. None of it is a new workstream, and none of it is cross-platform (§1) — the
point of the list is a CI/CD toolchain with nothing outstanding before the big migration starts.

| # | Item | From | Value | Effort | What is missing |
|---|------|------|-------|--------|-----------------|
| B1 | **SARIF line numbers are model-relative** | Phase 4 known limitation | ⭐⭐⭐ | M | **✅ shipped (2026-09-03)** — findings still carry class-relative lines (what a rule can know, and what the code viewer wants), and every report maps them through `ClassLocation` to the line in the file. It also settled a split nobody had noticed: the whole-graph analyses and the parser were emitting file lines while the rules emitted class lines, so the app scrolled to the wrong place for the first two. A package whose stored source was trimmed is reported at its declaration rather than at a confidently wrong line. |
| B2 | **`--sarif-base <path>`** | Phase 4 deferral | ⭐⭐ | S | **✅ shipped (2026-09-03)** — `--sarif-base <path>` writes SARIF file paths relative to that directory instead of the library. Resolved against the library like the other path options, and refused up front when it does not contain the library, since the report would then have to point outside it — which GitHub rejects, the same silent non-attachment reached from the other side. |
| B3 | **Two outputs from one run** | Phase 4 non-goal | ⭐⭐ | S | **✅ shipped (2026-09-03)** — `--report <format>:<path>`, repeatable, writes extra reports alongside the primary output from the same findings. `--format`/`--out` are unchanged, so existing invocations are untouched. |
| B4 | **Metric-threshold gates** | §3 quality gates | ⭐⭐⭐ | M | **✅ shipped (2026-09-03)** — `--min-coverage <percent>` and `--min-coverage <dimension>=<percent>` gate on a figure; `--coverage-ratchet` gates on the last recorded snapshot, so a legacy library can adopt it without meeting any particular number. Independent of `--fail-on`, and a dimension the repository does not track is warned about rather than silently checked. |
| B5 | **Missing-units rule vs the Unit dimension** | Phase 6 increment | ⭐⭐ | M | **✅ shipped (2026-09-03)** — the rule takes the same `UnitResolver` lookup the dimension uses, so a type that fixes no unit anywhere in its chain is reported like a bare `Real`. The dimension's compliance is now *defined* as "the rule does not flag it", so the two cannot drift again. Without a graph (a snippet check) the rule still judges plain `Real` only — all it can honestly say. On MSL it adds one finding: the library is disciplined about SI types, which is exactly why the gap went unnoticed. |
| B6 | **Pre-commit hook / commit gate** | §5 | ⭐⭐ | S | A documented `--changed-from BASE` recipe today. As a feature it extends the existing "commit requires issue number" gate. |
| B7 | **PR review annotations** | §5 | ⭐⭐ | M | Post findings as inline PR comments. The markdown summary path it would build on already exists. |
| B8 | **SARIF conformance is asserted, never validated** | Phase 4 risk, never discharged | ⭐⭐⭐ | S | **✅ shipped (2026-09-03)** — `build/validate-sarif.ps1` generates a report from the committed fixture at `TestFixtures/SarifSmoke/Libraries/Smoke`, checks the paths came out relative to the repository root (so `--sarif-base` is exercised by the same step), and runs `sarif validate`, failing on warnings as well as errors. Wired into `build-and-test.yml`, which also builds and tests `MLQT.Cli` — it did neither before. Validation immediately found two real defects, both fixed here: the driver carried no `informationUri` and no version (SARIF2005). |
| B9 | **SARIF rule metadata is too thin to be useful in GitHub** | Phase 4 gap | ⭐⭐⭐ | S | **✅ shipped (2026-09-03)** — every rule now carries `shortDescription` (the title), `fullDescription` (what the rule wants), `help.text` and `help.markdown` (the alert body: title, description, rule id, category, and where to configure it), a `helpUri` to the settings reference, and its category as a tag. The smoke script checks each of these on the generated report, so an alert cannot go back to naming an id and saying nothing. (The driver's missing `version` and `informationUri` were part of this item and were fixed under B8, since the validator flags them.) |
| B10 | **Accepted debt arrives in GitHub as an open alert** | Phase 4 decision, contradicted | ⭐⭐ | S | **✅ decided and shipped (2026-09-03)** — confirmed against GitHub's documented subset: it supports neither `baselineState` nor `suppressions`, and its own docs say a suppressed result still becomes an alert. So accepted debt is omitted from SARIF by default, with `--sarif-include-accepted` for a consumer that honours `baselineState`, and the run reports how many it left out. The findings are still tagged, and every other format is unchanged — this is a decision about one consumer's display, not about what MLQT found. The same commit warns at GitHub's 25,000-result rejection and 5,000-result display caps, which a library the size of MSL crosses silently. |
| B12 | **A `replaceable` nested class is checked twice** | found while doing B5 | ⭐⭐ | M | **✅ shipped (2026-09-03)** — `StyleCheckRunner` reports only findings about the class it was given, dropping the parent's copy when the nested class is in the graph and so gets a check of its own. The walk itself is unchanged, so a check with no graph behind it (a snippet, a test) still sees such a class — which is what the guard was protecting and what three tests had encoded. The duplicate copy also carried a line counted from the parent's source while naming the nested class, so a report mapped it to a line belonging to something else; that goes with it. The same walk was corrupting the Unit coverage numbers (a parent whose own quantities were all united could read 0%), fixed alongside. On MSL: 5,328 findings became 5,176, which is exactly what the dashboard counts — the two agree to the finding now. |
| B11 | **Nothing has ever been ingested by GitHub** | Phase 4 claim, unproven | ⭐⭐ | S | **✅ confirmed (2026-09-03)** — a 34-finding report from `ModelicaEditorTests` uploaded to `/code-scanning/sarifs` came back `processing_status: complete` with `errors: null`, and the alerts carry the rule metadata B9 added: `full_description`, a rendered `help` body, `help_uri` and the category tag. Two things it taught us, both now recorded in the CI guide: the upload needs a **public** repository (a private one answers 403 "Code scanning is not enabled" whatever the token's scopes, because it wants a Code Security licence) and the named `commit_sha` must be one GitHub has — an unpushed SHA is accepted and then displays nothing, which reads exactly like success. |

**Sequencing within the backlog:** B1–B3 are the ones a real pipeline hits (they are why a working
GitHub or TeamCity setup still needed hand-holding), so they came first; B4 next, since it is what
turns the metrics work into a gate; B6 and B7 last, being conveniences rather than gaps.

**B1–B5 and B8–B12 are done — only the two conveniences, B6 and B7, are left.** B8–B11 (added 2026-09-03) came out of asking how the SARIF work would be tested,
and they change the claim that phase 4 is closed: the output has never been validated, and the one
consumer it was written for renders almost none of what it carries. **B6 and B7 are what remain**, both conveniences rather than gaps: a pre-commit hook, and posting
findings as inline PR comments. B11 is run once B8–B10 land, as
confirmation rather than development. **B12** came out of B5 the same way B8–B11 came out of the
SARIF work: it is a correctness bug that inflates every rule's count, so it belongs before the two
conveniences (B6, B7).

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
