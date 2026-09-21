# MLQT Roadmap — Future Developments

The forward plan: candidate work grouped by theme, and the agreed order of the phases still to come.
The working list of individual items is [backlog.md](backlog.md); this document holds sequencing and
the reasons behind it.

**Guiding scope:** MLQT's mission is to improve and test the *quality* of Modelica models through
**style checking and static analysis**. Features that require model **translation** (flattening to
executable form) or **simulation** are explicitly out of scope — model checking against
Dymola/OpenModelica remains the only touch-point with those tools, and it stays optional.

> Status legend — **Value**: ⭐ (nice) → ⭐⭐⭐ (flagship). **Effort**: S / M / L / XL.
> **Boundary**: ⚠ marks items that approach (but should stay inside) the
> no-translation/no-simulation line and need care. **✅ shipped** marks delivered work.

---

## Where we are (2026-09-17)

**Phases 1–7 of the original sequencing are shipped.** The CI/CD toolchain is finished and the
desktop host migration is done:

| Delivered | What it means now |
|---|---|
| **Findings foundation** | Every rule and analysis emits a `Finding` with a rule id, a configured severity and a reformat-stable fingerprint |
| **Headless CLI** | `mlqt check`, `baseline`, `compare`, `hook` and `review`, over the same check pipeline the app uses |
| **Baseline / ratchet** | A version-controlled debt ledger, new-vs-accepted classification, changed-model escalation |
| **CI ergonomics** | SARIF (schema-validated, ingested by GitHub), TeamCity service messages, markdown, JUnit, real per-rule severities |
| **`__MLQT` suppression** | In-source, rename-safe, with GUI and MCP authoring actions |
| **Wave-1 analyses + dashboard** | Six whole-graph and per-class analyses, the Metrics tab, and the coverage trend |
| **GUI test harness** | The code-behind sweep, `MLQT.Shared.Tests`, `MLQT.TestHost` + Playwright journeys, and the `/selftest` conformance route |
| **Photino desktop host** | MLQT runs on **Windows and Linux** from one `net10.0` project with no .NET workload; MAUI is deleted; one installer per platform carries the GUI, the CLI and the MCP server |

Seventeen end-to-end reviews of the CI/CD work and the two phase-7 branches opened and closed
**B1–B167**. Three of those ids are carried forward in [backlog.md](backlog.md) with work still
attached; everything else is done. The per-phase design notes were retired on 2026-09-17 once every
phase they described had shipped — what outlives them is in the code, in
[CODING_GUIDELINES.md](../CODING_GUIDELINES.md), and in `.claude/skills/`
(`skill-encrypted-libraries.md`, `skill-desktop-host.md`, `skill-gui-testing.md`).

**What is not done from phase 7:** macOS (7b-9), deferred and unsized — nothing in the plan assumed
it and no macOS machine has been mentioned as available. Code signing (no supplier decided, so both
installers are unsigned) and arm64 (nobody has a machine to test on) are deliberate omissions rather
than outstanding work.

---

## Locked sequencing

The order was agreed on 2026-07-17 and amended on 2026-09-17, when phases 1–7 shipped and the first
release testing produced a list of its own. The delivered phases have been removed and the remainder
renumbered. **Phase numbers and the `§` theme numbers below are separate** — a phase is an agreed
slice of work in time, a theme is a place to file a candidate.

### 1. Release feedback — issues found testing the new build

**The next phase, and it comes before the Wave-2 analyses deliberately.** The Photino release was the
first build a user exercised end to end on both platforms, and it produced 36 items: mostly UI, not
exclusively. Several are correctness defects with no UI in them at all — a file that fails to parse
disappearing from the graph without a diagnostic, external resources attaching to the wrong
directory, a newly added repository not loading its library.

The argument for going first is the same one that put the CI toolchain ahead of the migration:
finish what is in front of the user before adding to it. A new analysis wave lands on a Code Review
page whose findings list runs off the bottom of the screen, does not scroll to the line it names, and
cannot be filtered by rule — every new rule makes that worse.

The items are **B168–B203** in [backlog.md](backlog.md), grouped by area: findings and the Code
Review page; library browser and navigation; projects, repositories and startup; analysis
correctness; rules and formatting; external tools; MCP; revision control; the build. **B143 was dealt
with first and separately** — `nightly-webkit.yml` was triggered by hand on 2026-09-17, having never
run at all, and passed with 57 WebKit journeys executed and none skipped.

**The plan is [phase-1-release-feedback.md](phase-1-release-feedback.md)**, which regroups those items
into work packages by shared root cause rather than by area (WP0–WP14; WP0–WP5, WP7, WP8 and WP11 are done, and WP13–WP14 were added on 2026-09-21), and records the eleven root causes established while planning — two of which change what the
fix is.

The phase has grown since: **B204–B212** while fixing and confirming the first set, **B213–B218**
on 2026-09-18 from the one decision that shapes it, **B230–B237** on 2026-09-19 from running it, and
**B238–B250** on 2026-09-19/20 — half from the work itself and half from a user exercising what it
had just shipped, which is the more useful half.

**B233 was outside the phase until 2026-09-20**, when using the feature showed the case it leaves
behind is the common one: a `connect(...)` equation carries its annotation on the same line, so
hiding annotations changes nothing in an equation section. **✅ Shipped the same day in WP11**, and
the measurement that decided it is worth keeping: over 8,367 files, 31.6% of non-blank
equation-section lines carry an annotation and 62% of those were left on screen. What follows is the
reasoning that put it outside the phase, which the numbers overturned.

**B233** — an annotation sharing a line with real
code survives "hide annotations", because `ElisionFinder` removes a construct as a unit or not at
all. That default is right (dropping whole lines would leave the user reading
`Real x "d" annotation (Placement(`), and it already hides the bulk: 41–44% of lines sit wholly
inside an annotation. What remains is the inline `Placement` on declarations, which is exactly the
noise the toggle was asked about. Doing it properly means splicing **markup rather than source** —
the elision applies to highlighted lines, so a cut at a character offset can land inside a tag — and
the likely shape is the classifier exposing where each token's markup begins so a line can be
rebuilt from whole tags. **Measure what fraction of annotations this actually leaves before
committing to that**, which is why it is a candidate here and not a phase-1 item.

Three of the items are larger than the rest and worth naming here rather than only in the table:

- **B213–B215 — the Code Review page shows the file, not a reformat of it.** `ModelicaRenderer` runs
  on the save path only, when the repository has Apply Formatting on; everything the user reads —
  the viewer and both diff views — is the bytes on disk or in the revision, coloured by a token
  classifier driven from the original source. Measured in
  [analysis-viewer-fidelity.md](analysis-viewer-fidelity.md): ~95% of displayed lines are currently
  not where the user's editor puts them, the colouring survives without the reformat at 99.8%+, and
  the fidelity path is ~45% cheaper. It closes B182, B183, B185 and B178 as consequences rather than
  as work, and WP2 is now built on it.
- **B184 — make `--changed-from` check only what changed.** Everything still has to be loaded, but
  re-checking everything then filtering is the largest available win on CI check time.
- **B191 ✅ — distinguish the kind of change a model carries.** Marking a model as modified is small;
  telling a simulation-affecting edit from a graphical or documentation one needs a comparison of the
  parsed classes, and that capability is useful well beyond the marker. **Shipped 2026-09-21 as
  `ModelicaParser/Comparison/`**, which reduces a class to what it means and what it says and
  compares the two against its committed self. The marker is one caller of it; the pull-request
  review, the CLI and the MCP server are the obvious others.

### 2. Wave-2 analyses

The confidence-aware resolver, then broken references, connection integrity, deprecated-API usage,
cyclic-dependency detection and external-resource validation. See §2 below for the three-state
resolution model these are built on — it is the thing that keeps a reference into an invisible
library from flooding a real repository with false errors.

**External-resource validation is the one added after the phase-1 testing**, and it is a gap rather
than an enhancement: MLQT already finds missing resources and shows them on the External Resources
tab, but they are `ResourceWarning`s rather than `Finding`s. They have no rule id, so they never
reach `mlqt check`, cannot be baselined and cannot fail a build — a library can lose a file out of
`Resources/` and every gate stays green.

### 3. Extensibility, then the flagships

Declarative custom rules → compiled plugins; finally the boundary-brushing dimensional-analysis and
structural equation-balance checks. See §4 and §2.

### Deferred, with no place in the sequence

**macOS.** Photino supports it and the intent predates the migration, but nothing depends on it and
there is no machine. Size it when a reason to do it appears.

---

## §1. Cross-platform support — **delivered for Windows and Linux**

The docs promised macOS and Linux support and did not have it: .NET MAUI has no Linux target, so the
UI had to be **re-hosted, not re-targeted**. That is done.

**Decisions (2026-07-17, all honoured):** the UI ships on Linux too, not just a CLI; **mobile is not
a target — desktop only**; and the end-state is **one cross-platform desktop host replacing MAUI**,
not MAUI plus a second host.

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Headless CLI** (`mlqt`) reusing the service layer | ⭐⭐⭐ | M | **✅ shipped** — `check`, `baseline`, `compare`, `hook`, `review`. Shipped inside each platform's installer rather than as a `dotnet tool`, because `dotnet tool install` is an SDK command and would oblige a build agent to install the SDK to run a linter. See [cli.md](../Documentation/cli.md) |
| **MCP server on Linux** as a tested target | ⭐⭐ | S | **✅ shipped** — and it needed two fixes to work there: a configuration source asking to be watched, and one inotify watcher per resource directory taking 85 of the machine's 128 instances |
| **Single cross-platform desktop host (Photino.Blazor)** replacing MAUI | ⭐⭐⭐ | L | **✅ shipped 2026-09-10.** In-process webview, so direct filesystem and git/svn access are kept and `MLQT.Shared` is reused unchanged. The MAUI project is deleted and `PortabilityTests` is the tombstone |
| **WebKitGTK interop spike** (Cytoscape.js, MudBlazor, highlighting) | — | S | **✅ answered on both platforms** — 16 `/selftest` probes, zero differences against the MAUI baseline under WebKitGTK and WebView2 alike. Cytoscape and MudBlazor both work, and the syntax highlighting was never at risk: it is CSS over server-rendered spans, not a JS library |
| **Platform-service ports** (file dialog, power/sleep, settings paths) | ⭐⭐ | M | **✅ shipped** — the picker is Photino's own native dialogs on both platforms, sleep prevention is Win32 on Windows and a `systemd-inhibit` lock on Linux, and settings are a JSON file that is no longer host-specific at all. Existing users' MAUI `Preferences` migrate themselves on first run |
| **GUI test harness** (the code-behind sweep, unit + bUnit tests, the Playwright test host, `/selftest`) | ⭐⭐⭐ | L | **✅ shipped** — and it is what made the host swap a small change. See `skill-gui-testing.md` |
| **macOS** | ⭐ | ? | **Deferred.** Photino supports it and `FilePickerService` already had an NSOpenPanel branch under MAUI, so the intent predates this. No machine, no dependency on it |
| **arm64** | ⭐ | S/M | **Not built.** The host is a plain `net10.0` application with no platform-specific project, so this is a runner and a test pass rather than a port |
| **Code signing** | ⭐⭐ | S | **Not done.** Both installers are unsigned and SmartScreen warns on first run; a certificate and a supplier are still to be decided |

---

## §2. New Modelica-specific static analyses (no simulation)

Deeper checks that stay purely structural, extending the existing style rules, dependency graph, and
resource tracking.

### Guiding principle — confidence-aware resolution (avoid false positives)

MLQT often **cannot see the whole symbol universe**: commercial libraries ship encrypted (opaque to a
source-based tool), and even unencrypted dependencies (MSL, another team's library) may not be
loaded. A naive reference checker would flag these as "broken" and flood a real library with false
errors — fatal to trust. So resolution is **three-state**, not boolean:

1. **Resolved** — definition found in readable source. Fully checkable.
2. **Unresolved but external** — points into a namespace with no source (encrypted, or not loaded, or
   a declared `uses` dependency). → **Assume valid; never gates** (info at most).
3. **Unresolved and should-be-visible** — points into a namespace we have *complete* source for, yet
   the target is missing. → the **only** case that becomes an `error`.

"External" is decided from the loaded-library set + `uses(...)` declarations (an allowlist of
expected-invisible namespaces) + encrypted-file detection + an explicit treat-as-external config.
Severity follows confidence.

**Encrypted libraries move *individual classes* from state 2 to state 1** (shipped — see
[encrypted-libraries.md](../Documentation/encrypted-libraries.md) and `skill-encrypted-libraries.md`).
MLQT reads the vendor's generated help HTML and recovers each documented class's name, description,
base classes and whether it has an icon, so references into commercial libraries resolve and inherited
icons are seen. Resolution against documentation is **asymmetric**: a hit is reliable, but a miss is
not proof of absence — vendors choose how much to document, and that varies between releases of the
same version — so anything the documentation does not name stays in state 2 and never gates.

### Wave 1 — self-contained (need only the user's own source; zero missing-library risk)

Shipped first, so MLQT earned trust in CI before attempting resolution-dependent checks.

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Unused-element detection** (parameters, constants, components, imports, protected vars) | ⭐⭐⭐ | M | **✅ shipped** — `UnusedMembersAnalyzer`, `UnusedImportAnalyzer` |
| **Unused-class detection** | ⭐⭐⭐ | M | **✅ shipped** — `UnusedClassAnalyzer`, with the "possibly unused API" Info case for public top-level classes |
| **Duplicate / shadowing declarations** | ⭐⭐ | S | **✅ shipped** — `DuplicateDeclarations` rule + `ShadowingAnalyzer` |
| **`uses` annotation hygiene** | ⭐⭐⭐ | M | **✅ shipped** — `UsesHygieneAnalyzer`, conservative both ways |
| **`package.order` / file-structure consistency** | ⭐⭐⭐ | M | **✅ shipped** — `PackageOrderAnalyzer`. Aligning its `package.order` half with Dymola's own warning is backlog **B195**; the stray-file half has no upstream equivalent |
| **Missing-units presence check** | ⭐⭐⭐ | M | **✅ shipped (plain `Real` only)** — `MLQT.Units.MissingUnit`. A user type that aliases `Real` without a unit is still missed, though the Unit coverage dimension resolves those |
| **Single-file package warning** | ⭐⭐ | M | **Not built** — backlog **B177**. A package held entirely in one `.mo` file rather than as a directory of standalone classes, as a switchable rule with a finding |

### Wave 2 — resolution-dependent (built on the confidence-aware resolver) — **phase 2**

Resource validation belongs here rather than in Wave 1 for the same reason the rest of this wave
does: a `modelica://OtherLib/Resources/x` into a library that is not loaded, or into an encrypted one
whose files nobody can enumerate, must not be reported as broken. It is the three-state rule applied
to files instead of classes.

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Broken references / unresolved `extends` & types** | ⭐⭐⭐ | L | Extends existing `ValidateModelReferences`. Only errors on state 3 above |
| **Connection integrity** (unconnected/incompatible/duplicate `connect`, direction) | ⭐⭐⭐ | L | Promotes the `ConnectorCompatibility` helper; connector *types* may be external → same three-state gate |
| **Deprecated-API usage** | ⭐⭐ | M | References to `obsolete` classes + MSL-version compatibility. Needs the target library visible |
| **Cyclic-dependency detection** | ⭐⭐ | S | The directed graph already exists; surface package dependency cycles |
| **External-resource validation** | ⭐⭐⭐ | M | **Nothing checks these today.** Missing data files, images and C sources are found — `ExternalResourceService` does the work and the External Resources tab shows it — but they are `ResourceWarning`s and not `Finding`s, so they have no rule id, never reach `mlqt check`, cannot be baselined, and cannot gate CI. A library can lose a `Resources/` file and every gate stays green. Needs a rule id per kind (missing file, missing annotated directory, absolute path), which then brings severity, suppression and the ratchet with it |

### Flagships (phase 3; brush the no-simulation boundary — keep inside it)

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Structural equation-balance check** ⚠ | ⭐⭐⭐ | XL | Count equations vs. unknowns (locally balanced). Needs flattening-lite semantics |
| **Unit / dimensional consistency** ⚠ | ⭐⭐⭐ | XL | Full dimensional analysis on equations (distinct from the Wave-1 presence check) |

---

## §3. Code metrics & quality gates (the SonarQube playbook)

The **metrics dashboard is the surface for reviewing progress** — it is where the ratchet's payoff
becomes visible. **Coverage metrics** (documentation %, unit-attribute %, description %) are the
burndown numbers: legacy libraries start low and the dashboard shows them climbing as debt is worked
off.

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Metrics dashboard** (class counts by kind, coverage %) | ⭐⭐⭐ | M | **✅ shipped** — `MetricsDashboard.razor` + `MetricsCalculator`; coverage dimensions follow each repository's enabled rules. LOC, connection counts and inheritance depth were not built — no one asked for them |
| **Cyclomatic / cognitive complexity** | ⭐⭐ | M | For algorithm sections and functions |
| **Duplicate / clone detection** | ⭐⭐ | L | Near-identical models/equation blocks via subtree hashing |
| **Quality gates** ("fail if doc coverage < 80%", "no new critical findings") | ⭐⭐⭐ | M | **✅ shipped** — "no new findings" is the baseline/ratchet gate; `--min-coverage` and `--coverage-ratchet` gate on the coverage numbers |
| **Trend tracking** (metric snapshots over commits) | ⭐⭐ | L | **✅ shipped** — `.mlqt/metrics-history.json`, written by the dashboard or `mlqt check --metrics`, plotted per scope |

---

## §4. Linter ergonomics other tools have

Rulesets live per-repo in `<repo>/.mlqt/settings.json`, revision-controlled, as a rule-id-keyed
severity map. Built-in and custom rules are uniform (stable id + severity), and CI reads severity
directly: warnings report but do not fail, errors fail.

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Per-rule severity map** (off/warning/error, rule-id-keyed) | ⭐⭐⭐ | M | **✅ shipped** — `RuleSeverities`, with per-repository Off/Info/Warning/Error selectors in the settings UI |
| **In-source suppression via `__MLQT` vendor annotations** | ⭐⭐⭐ | M | **✅ shipped** — with GUI and MCP authoring actions. Not comments: comments are position-bound and get orphaned when the formatter reorders declarations, whereas annotations ride on the element. Class- and component-level, carrying a `reason`. Also the rename-safe replacement for the name-based `FormattingExcludedModels` list — which the exclusion button does not yet write (backlog **B175**) |
| **Baseline / ratchet mode** (only fail on *new* findings) | ⭐⭐⭐ | M | **✅ shipped** — see §5 |
| **Custom-rule authoring — declarative tier** (config-driven shape checks) | ⭐⭐ | L | **Phase 3.** The 80%: annotation-present, identifier-regex, banned-`extends`. No compilation; CI-safe. Registers a rule id + severity |
| **Custom-rule authoring — compiled-plugin tier** (`VisitorWithModelNameTracking`) | ⭐⭐ | XL | **Phase 3.** Full parse-tree power escape hatch. ⚠ Loading compiled code in CI is a supply-chain consideration |
| **Cross-repo shared rule profiles** (ESLint-`extends` style) | ⭐ | M | *Deferred / optional.* Only for orgs running many libraries wanting one house ruleset without drift. Per-repo config + existing naming presets cover the common cases |

---

## §5. CI/CD & automation integration — **complete**

Direction (decided and honoured): **generic CLI + standard report formats, no platform-specific
integrations** — the first customer uses TeamCity and a prospect uses GitHub without Actions.
Machine-readable output *is* the integration.

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Baseline / ratchet mode** (new-vs-existing, warn on touched debt) | ⭐⭐⭐ | M | **✅ shipped** — `mlqt baseline create/update/prune`, `--changed-from`, `--touched-debt warn\|fail\|ignore`. Making a changed-from run *check* only what changed is backlog **B184** |
| **CLI + JUnit/exit-code contract** | ⭐⭐⭐ | M | **✅ shipped** — `--format junit`, `--fail-on off\|warning\|error`, documented exit codes |
| **SARIF + TeamCity + markdown serializers** | ⭐⭐ | S | **✅ shipped**, and validated against the SARIF 2.1.0 schema on every push, with a confirmed GitHub ingest |
| **Pre-commit hook / commit gate** | ⭐⭐ | S | **✅ shipped** — `mlqt hook install` writes a git pre-commit hook running the same check |
| **PR review annotations** | ⭐⭐ | M | **✅ shipped** — `--format review` writes a GitHub pull-request review body, posted with `gh api --input` |

---

## §6. Documentation-quality analysis

Building on the existing spell-checking.

| Item | Value | Effort | Notes |
|------|-------|--------|-------|
| **Documentation coverage reporting** | ⭐⭐ | S | **✅ shipped** — class description, documentation info, documentation revisions, parameter and constant description are coverage dimensions; the Code Review findings name the classes |
| **Spell-check accuracy** (what counts as a word) | ⭐⭐ | M | **✅ shipped** — inherited element names are in scope, the shipped term list carries the engineering vocabulary no English dictionary has (dialect-split), and a word can be waived for one class in source. On MSL this removed 41% of the spelling findings without accepting a single new word |
| **Preferred English variant** (dialect consistency) | ⭐⭐ | M | **Not built.** Consistency is currently a side effect of choosing one dictionary: an `en_US` repository reports "modelling" as *misspelled*, which is true but says the wrong thing, and enabling en_GB alongside would accept both spellings everywhere. A rule with a variant map would name the actual problem ("British spelling — this repository uses American") and let a repository take en_GB's vocabulary without its spellings |
| **HTML validity + broken-link checking** in doc strings | ⭐⭐ | M | Validate `modelica://` cross-references resolve to real classes |
| **Terminology consistency** | ⭐ | M | Flag inconsistent capitalization/naming of domain terms. Overlaps the variant rule above — the same machinery, a different word list |
