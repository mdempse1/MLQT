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
> no-translation/no-simulation line and need care.

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
| **Headless CLI** (`mlqt-cli`) reusing the service layer | ⭐⭐⭐ | M | Ships first — Linux/macOS *headless* immediately; also the CI vehicle (§5). |
| **MCP server on Linux/macOS** as a tested target | ⭐⭐ | S | Already headless stdio; likely close to working. |
| **Single cross-platform desktop host (Photino.Blazor)** replacing MAUI | ⭐⭐⭐ | L | In-process webview → keeps direct filesystem + git/svn access, near drop-in reuse of `MLQT.Shared`. Reimplement the 3 platform services once. Retires MAUI. |
| — *fallback host:* Blazor Server (+ desktop wrapper) | — | L | If in-process webview proves limiting; also opens a future hosted/browser option. |
| **WebKitGTK interop spike** (Cytoscape.js, MudBlazor, highlighting) | — | S | De-risk the Linux webview engine early — it is **not** WebView2. Gate before committing the host. |
| **Platform-service ports** (file dialog, power/sleep, settings paths) | ⭐⭐ | M | Per-OS implementations behind the existing interfaces. Settings is mostly path differences. |

**Migration approach:** build the Photino host and port the platform services, then
**validate on Windows first** (confirm feature parity against the known-good MAUI build
before adding OS variables), then Linux, then macOS. When it lands, the `MLQT` MAUI project
is superseded by a new desktop host project — update CLAUDE.md and docs accordingly.

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

### Wave 1 — self-contained (need only the user's own source; zero missing-library risk)

Ship these first — they cannot produce library-visibility false positives, so MLQT earns
trust in CI before attempting resolution-dependent checks. *(All approved.)*

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Unused-element detection** (parameters, constants, components, imports, protected vars) | ⭐⭐⭐ | M | Modelica equivalent of dead-code/unused-import lints. |
| **Unused-class detection** | ⭐⭐⭐ | M | Internal/nested/protected class never referenced → high-confidence. Unreferenced *public top-level* class → only "possibly unused API" (info): a downstream consumer we can't see may use it. |
| **Duplicate / shadowing declarations** | ⭐⭐ | S | Same name twice in a class, duplicate import aliases, silently shadowed inherited members. Local, bug-class. |
| **`uses` annotation hygiene** | ⭐⭐⭐ | M | Libraries used but not declared in `uses(...)`, and declared-but-unused deps. Library-maintainer gold; doubles as the "external namespace" allowlist above. |
| **`package.order` / file-structure consistency** | ⭐⭐⭐ | M | Entries not matching classes/files, missing entries, standalone-vs-nested placement mistakes. Common real defect; pieces already exist. |
| **Missing-units presence check** | ⭐⭐⭐ | M | `Real` vars/params with no `unit` where a physical quantity is implied; prefer `Modelica.Units.SI.*`. **Attribute-presence only, NOT dimensional analysis.** Cheap, huge burndown content. |

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
| **Metrics dashboard** (LOC, component/connection counts, inheritance depth, coverage %) | ⭐⭐⭐ | M | Confirmed. One aggregation layer over existing parse trees + graph; the burndown/review surface. |
| **Cyclomatic / cognitive complexity** | ⭐⭐ | M | For algorithm sections and functions. |
| **Duplicate / clone detection** | ⭐⭐ | L | Near-identical models/equation blocks via subtree hashing. |
| **Quality gates** ("fail if doc coverage < 80%", "no new critical issues") | ⭐⭐⭐ | M | Governance layer that makes MLQT a CI gatekeeper. Depends on CLI (§1). |
| **Trend tracking** (metric snapshots over commits) | ⭐⭐ | L | See quality improving/regressing over time. |

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
| **Per-rule severity map** (off/warning/error, rule-id-keyed) | ⭐⭐⭐ | M | Foundation for the CI gate *and* extensibility. Migrates booleans: `true`→default severity, `false`→off. |
| **In-source suppression via `__MLQT` vendor annotations** | ⭐⭐⭐ | M | Not comments — comments are position-bound and get orphaned when the formatter reorders declarations. Annotations ride on the element (like icons/docs, which already round-trip). Class- and component-level; carries a `reason`. Also the in-source, rename-safe replacement for the name-based `FormattingExcludedModels` list. See §5 design note. |
| **Baseline / ratchet mode** (only fail on *new* violations) | ⭐⭐⭐ | M | Makes adopting a linter on a large legacy library viable. See §5 design note. |
| **Custom-rule authoring — declarative tier** (config-driven shape checks) | ⭐⭐ | L | The 80%: annotation-present, identifier-regex, banned-`extends`. No compilation; CI-safe. Registers a rule id + severity. |
| **Custom-rule authoring — compiled-plugin tier** (`VisitorWithModelNameTracking`) | ⭐⭐ | XL | Full parse-tree power escape hatch. ⚠ Loading compiled code in CI is a supply-chain consideration. |
| **Cross-repo shared rule profiles** (ESLint-`extends` style) | ⭐ | M | *Deferred / optional.* Only for orgs running many libraries wanting one house ruleset without drift. Per-repo config + existing naming presets cover the common cases. |

---

## 5. CI/CD & automation integration ← current focus

MLQT is GUI-first today; the ecosystem norm is "runs in the pipeline." This has become
the leading initiative — see the deep-dive **[design-ci-quality-gate.md](design-ci-quality-gate.md)**
for the baseline/ratchet design, finding-identity model, CLI surface, and phased plan.

Direction (decided): **generic CLI + standard report formats first**, no platform-specific
integrations (first customer uses TeamCity; a prospect uses GitHub without Actions).
Machine-readable output *is* the integration — JUnit XML renders findings in any CI's
test UI, TeamCity service messages auto-graph baseline debt trend.

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Baseline / ratchet mode** (new-vs-existing, warn on touched debt) | ⭐⭐⭐ | M | The adoption unlock for large legacy libraries. See design note. |
| **CLI + JUnit/exit-code contract** | ⭐⭐⭐ | M | Universal CI integration without per-platform work. Vehicle = CLI (§1). |
| **SARIF + TeamCity + markdown serializers** | ⭐⭐ | S | Thin serializers over the shared findings model. |
| **Pre-commit hook / commit gate** | ⭐⭐ | S | Extends existing "commit requires issue number." |
| **PR review annotations** | ⭐⭐ | M | Post findings as inline PR comments (markdown summary path). |

---

## 6. Documentation-quality analysis

Building on existing spell-checking.

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Documentation coverage reporting** | ⭐⭐ | S | Which public classes/parameters lack info/revisions/descriptions. |
| **HTML validity + broken-link checking** in doc strings | ⭐⭐ | M | Validate `modelica://` cross-references resolve to real classes. |
| **Terminology consistency** | ⭐ | M | Flag inconsistent capitalization/naming of domain terms. |

---

## Locked sequencing (agreed 2026-07-17)

The CI quality gate (§5) is the leading initiative and commercial wedge; its own phased
plan lives in [design-ci-quality-gate.md](design-ci-quality-gate.md). Full unified order
across all workstreams:

1. **Findings foundation** — rule-ID registry, boolean→severity map, semantic
   fingerprints. Prerequisite for everything (CI, baseline, suppression, analyses); also
   improves the existing GUI Code Review. **Implementation plan:**
   [design-phase1-findings-foundation.md](design-phase1-findings-foundation.md).
2. **Headless CLI MVP** — `mlqt check` reusing the service layer; console + exit code +
   JUnit + JSON; `dotnet tool`. Linux/macOS **headless** starts here, running existing rules.
   **Implementation plan:** [design-phase2-cli.md](design-phase2-cli.md).
3. **Baseline / ratchet** — `baseline create/update/prune`, new-vs-accepted classification,
   changed-model warn-by-default. The adoption unlock for legacy libraries.
   **Implementation plan:** [design-phase3-baseline.md](design-phase3-baseline.md).
4. **CI ergonomics** — SARIF, TeamCity service messages + debt-trend statistics, markdown
   summary. Lights up the first customer's TeamCity.
5. **Suppression (`__MLQT` annotations)** — with GUI + MCP authoring actions. Lands before
   the analysis wave generates intentional-exception cases (declaration-order case).
6. **Wave-1 analyses + dashboard** — unused elements/classes, duplicate/shadowing, `uses`
   hygiene, `package.order` consistency, missing-units; plus the metrics dashboard/burndown.
   The debt-ledger content that makes the ratchet compelling.
7. **Desktop host migration (Photino, retire MAUI)** — delivers the **Linux UI**. Pull the
   WebKitGTK interop spike *early* to de-risk; then host + platform-service ports, validated
   Windows-parity → Linux → macOS.
8. **Wave-2 analyses** — confidence-aware resolver, then broken references, connection
   integrity, deprecated-API, cyclic dependencies.
9. **Extensibility, then flagships** — declarative custom rules → compiled plugins; finally
   the boundary-brushing dimensional-analysis and structural equation-balance checks.

**Placement notes:** desktop UI sits at #7, after the CI value that ships fastest to real
customers — hedged by pulling its WebKitGTK spike early; swap earlier if the Linux UI
becomes the harder customer commitment. Suppression (#5) precedes the analyses (#6) so
intentional-exception handling exists before new rules generate exceptions.
