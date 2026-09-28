# MLQT Roadmap — Future Developments

The forward plan: candidate work grouped by theme, the agreed order of the phases still to come, the
reasons behind both, and the few issues still open. There is no separate working list any more — see
[Item ids](#item-ids) at the end.

**Guiding scope:** MLQT's mission is to improve and test the *quality* of Modelica models through
**style checking and static analysis**. Features that require model **translation** (flattening to
executable form) or **simulation** are explicitly out of scope — model checking against
Dymola/OpenModelica remains the only touch-point with those tools, and it stays optional.

> Status legend — **Value**: ⭐ (nice) → ⭐⭐⭐ (flagship). **Effort**: S / M / L / XL.
> **Boundary**: ⚠ marks items that approach (but should stay inside) the
> no-translation/no-simulation line and need care. **✅ shipped** marks delivered work.

---

## Where we are (2026-09-28)

**Phase 1, release feedback, has shipped.** It took the first end-to-end use of the Photino release
(B168–B203) and everything that working on it opened (B204–B301), grouped into work packages WP0–WP16
by shared root cause; all sixteen are complete. What it delivered:

| Delivered | What it means now |
|---|---|
| **The viewer shows the file** | Code Review, both diff views and the MCP source tool show the user's own text coloured in place by `ModelicaTokenClassifier`; `ModelicaRenderer` runs on the save path only. A finding's line is the viewer's line, and hiding annotations or nested classes is `SourceElision` |
| **A usable Code Review page** | Resizable panes, a virtualised findings list, AND search with a rule filter, reveal-in-tree, back/forward over the selection, a count of what the filters left, and the first paint of a very large class without waiting for its parse |
| **The kind of change** | `ModelicaParser/Comparison/` tells a simulation-affecting edit from a graphical or documentation one; the library browser marks **M** / **G** and filters by it |
| **Correctness under the graph** | An unparseable file keeps a placeholder and its diagnostic; one watcher per watched path; `modelica://` resolves against the referencing library's copy, then the readable one; an encrypted library is never loaded beside source for the same library; `ModelicaLanguage` keeps the built-ins out of the graph |
| **Rules and formatting** | `ComponentsBeforeClasses`, `DeclarationOrder`, `SingleFilePackage` and a match-Dymola package-order option; excision instead of re-rendering in the trimmer; every write keeps its file's final newline and line endings; formatting exclusion written as `__MLQT(format=false)` |
| **Performance** | A Claytex check went from 6m51s to about 65s with identical findings: cached ancestry and unit lookups shared by the rule and coverage, keyword reads from terminals only, a linear grammar for comment runs, server GC in every host, and a narrowed second `loadSelector` pass |
| **External tools and revision control** | A result dialog, dead-session recovery, a time limit per tool and Cancel reaching a running check, for both Dymola and OpenModelica; tags and detached HEAD, history diffs against the predecessor, and revision content decoded by MLQT's encoding funnel |
| **MCP** | `get_diagram_image` renders a whole diagram so an agent can see what it drew; an encrypted class returns the members recovered from its documentation; `get_class_source` elides rather than re-renders, so its lines match the findings |
| **A suite that tells the truth** | One settings double held to a contract, `build/run-mutation.ps1` and `survivor-map.py`, a journey host that isolates journeys, and CI pinned off the floating Ubuntu label |

**Then the end-of-branch review of 2026-09-25 opened B302–B369**, with eight reviewers each reading
the whole branch against `main` for one area. Working through those found B370–B498. By 2026-09-28
every item was closed except the two intermittent failures under
[Known open issues](#known-open-issues), and the backlog file was retired (see [Item ids](#item-ids)).
What the round changed, by area:

| Area | What it means now |
|---|---|
| **Writes to the user's files** | Format All, Split into files and the incremental formatter all leave a file's header and trailing comments where they were. They keep a file's encoding and line endings. They write nothing of a library they could not place completely, and leave a file with syntax errors as it is, naming it. Format All splits a single-file repository library into a directory and re-registers it as that directory. It refuses a directory whose `package.mo` is not a package, and says which class to change. Comments in modifications, arrays, enumerations and after the `within` clause now parse and are kept on save |
| **Formatter layout** | Matrices keep the rows their author wrote. Long arguments, bindings, if-expressions and conditions wrap at stated points and at stated columns (see `code-formatting.md`), so a line the formatter produces does not pass the limit when a break can prevent it. The shapes that are a matter of taste are [left alone](#decided-against-for-now) |
| **Revision control** | A VCS operation reaches every repository in its working copy and holds off the whole working copy's monitor. No VCS operation starts while the pipeline an earlier one started is still running. A stalled git or svn command is stopped by an idle limit and releases the lock it left. Detached HEAD refuses what it cannot do. A rebase left in progress can be continued or aborted from the dialog |
| **External tools** | One checking base class for both tools. Starting, stopping, cancelling, a tool that dies and a timeout are each reported as what they are, never as a problem with the model. Dymola and omc are auto-detected and can be browsed to on Linux as well. On exit, omc is ended with its process tree and Dymola is left running, by decision |
| **Diagrams (MCP)** | Connection lines end on the connector drawn. Bases' connections and graphics are drawn through their `IconMap`/`DiagramMap`. Text, Bitmap and inherited coordinate systems follow the specification |
| **Code Review and the browser** | The diff diffs once, off the UI thread, against the class of the same full name. Renders land only if they are still the one asked for. A large class waits for its parse instead of flashing. A project switch or a reload during startup shows the work still going and never starts it twice |
| **Tests and CI** | A warning fails a CI build. CI runs the Dymola and OpenModelica suites without the classes that need the tool, and both assemblies are under the coverage ratchet. Every SVN test builds its own repository. Every documentation image is generated or listed as a photograph, and the SVN shots are now generated. Every workflow `dotnet` command is a step of its own |

**Phases 1–7 of the original sequencing shipped before that.** The CI/CD toolchain is finished and
the desktop host migration is done:

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
**B1–B167**, and all of them are closed. The last two carried forward (B152, the screenshots that were
still photographs, and B166, a finding-count variance) closed on 2026-09-27. The same day B198, the
last phase-1 item, was reproduced and fixed. The per-phase design notes were retired on 2026-09-17 once every
phase they described had shipped, and phase 1's plan and its viewer-fidelity analysis followed on
2026-09-25 — git history has both. What outlives them is in the code, in
[CODING_GUIDELINES.md](../CODING_GUIDELINES.md) (its Testing and Working a Defect sections), and in
`.claude/skills/`.

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

### 1. Release feedback — ✅ shipped 2026-09-24

Taken before the Wave-2 analyses deliberately, for the reason that put the CI toolchain ahead of the
migration: finish what is in front of the user before adding to it. A new analysis wave would have
landed on a Code Review page whose findings list ran off the screen, did not scroll to the line it
named and could not be filtered by rule. What it delivered is summarised under *Where we are*. The
plan that grouped the items into WP0–WP16 was retired on 2026-09-25, and the items themselves on
2026-09-28. Git history has both.

What it leaves as candidates rather than work:

- **A formatting preview beside *Format All Files*.** The viewer deliberately has no formatted mode
  (the reasoning is on `CodeReview.Show`); the one question such a mode answered — *what will turning
  formatting on do to my files?* — belongs in repository settings, if anywhere. A `mlqt` run and a
  diff answer it today.
- **More callers of the change classifier.** `ModelicaParser/Comparison/` was built for the browser's
  marker; the pull-request review, the CLI and the MCP server are the obvious next ones.
- **The peek half of B197** — hovering an identifier to see its class. The classifier's `TYPE` and
  `NAME` tags make it possible; nothing resolves a token to a class yet.
- **Going back from tabs other than Code Review.** By the user's decision the back and forward
  arrows sit on the Code Review toolbar beside *Go to a class this one uses*. That leaves
  Dependencies, External Resources, Metrics and Settings with no way back. The history itself is
  shared, on `AppState`. If this becomes a problem, the other direction is to bring the
  used-classes menu up beside the class name, rather than to move the arrows back down (B249).
- **Units and truncation in diagram labels.** `get_diagram_image` substitutes `%parameter` values but
  writes no unit and does not cut text to the icon's width. The unit needs unit resolution and a
  decision about how to show it (B278).
- **Server GC in the desktop app over a long session.** It was measured in the CLI, where a Claytex
  check fell from 242 s to 70.5 s. The desktop host is set the same way, but nobody has measured its
  memory and responsiveness over a long session. Confirm it on the next long Claytex session (B281).

**How a phase is kept whole**, which this one learned twice: every open item names a work package or
is a roadmap candidate. An item filed as "separable at any point" is never scheduled once the package
beside it ships, and one named only in a package's prose is counted by no item list — B257, B267 and
B268 each escaped one of those two ways. Check it when items are opened, not only when a package
closes.

### 2. Wave-2 analyses — next

**The next phase**, once the `backlog` branch has merged.

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

**Wider incremental re-analysis when a class appears in an enclosing package.** When a package gains
or loses a class, the classes below it in other files are re-analysed if their own text names that
class (`EnclosingImportChanges`, B387). A class that only *inherits* a component of the captured type
can keep a stale cached resolution until a full re-analysis. Its base, which names the type, is
re-analysed. No report has shown this. Widen it if one does.

---

## Decided against, for now

Each of these was looked at and deliberately left alone. Reopen one only with what the entry asks for.

- **Formatter layouts that are a matter of taste.** The formatter fixes clear layout defects: a line
  it takes past the limit, or a wrap that makes code read as something else. It records debatable
  shapes rather than chasing them, because engineers would not agree on the better layout and every
  change rewrites hundreds of files in real libraries. By the user's decision (2026-09-28), the shapes
  listed in `Documentation/code-formatting.md` under *Layouts the formatter leaves as they are* stay
  as they are (B494 a/b, B495, B497 1/2). Reopen one with a concrete preferred layout for a named
  class.
- **A directory whose `package.mo` defines a class that is not a package.** MLS 13.4.1 allows it.
  Format All refuses it cleanly: it touches nothing and says which class to declare as a `package`
  (B443, B448). Reopen if a real library turns up with this layout.
- **A Linux Dymola launcher that starts Dymola in the background and exits.** MLQT would take the
  live session as gone and start a second Dymola. Detection picks `bin64/dymola`, which runs as a
  direct child of MLQT, so the case does not arise (B427). Reopen if a user points MLQT at such a
  launcher.

---

## Known open issues

These are intermittent journey failures, each seen once and not reproduced since. **If one recurs,
find the cause from its evidence. Never add a retry.**

| # | Issue | What to do if it recurs |
|---|-------|-------------------------|
| B492 | `CodeSearchJourney.TheMatchCountSaysHowManyLinesMatched` timed out waiting for its findings row. It happened in one local full run, before the `ProgressDialog` fix, and did not reproduce in 24 runs, 3 of them under full CPU load. Instrumenting it showed that only a journey's own `ClearLogMessages` removes the injected finding, and that the `tbody tr` locator always meets a real row | Keep the run's trace (`MLQT_JOURNEY_TRACE`) and host log (`MLQT_LOG_CONSOLE=Info`) |
| B496 | On Windows CI, `CodeReviewToolbarJourney`'s navigation failed with `ERR_CONNECTION_FAILED` while the host was up (run 36398522354). It failed within 10 ms, between two successful requests, and the `GET /` never reached Kestrel | A traced Chromium run writes `chromium-netlog.json`, and the host logs Kestrel connection events (`skill-gui-testing.md`). Find the failed `TCP_CONNECT_ATTEMPT` for the port and read its `os_error` |

---

## Item ids

Individual work items were tracked in `Design/backlog.md` as **B1–B498** until 2026-09-28. Every item
was then closed or moved into this file: candidates under their phase or theme, deliberate
omissions under *Decided against*, and the open two under *Known open issues*. The file was then
deleted. **The ids are cited throughout the code, tests, scripts and workflows**, and they keep
their meaning: `git log --diff-filter=D -- Design/backlog.md` finds the commit that deleted it, and
its parent has the full record, including each item's closing note.

**An id is never reused.** A new item that needs one continues from **B499**. It goes under *Known
open issues* if it is a defect, and among the candidates if it is work. `MLQT.Cli.Tests/MarkdownTableTests.cs`
reads the next id from the sentence above. It checks that every item id in this file's tables is
unique and below that number. When you add an item, move the number up with it. A closed item's row
is then removed and its id is retired.

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
| **`package.order` / file-structure consistency** | ⭐⭐⭐ | M | **✅ shipped** — `PackageOrderAnalyzer`, with an option that aligns its `package.order` half with Dymola's own warning (B195). The stray-file half has no upstream equivalent |
| **Missing-units presence check** | ⭐⭐⭐ | M | **✅ shipped (plain `Real` only)** — `MLQT.Units.MissingUnit`. A user type that aliases `Real` without a unit is still missed, though the Unit coverage dimension resolves those |
| **Single-file package warning** | ⭐⭐ | M | **✅ shipped** — `MLQT.Structure.SingleFilePackage`, the first rule on by default, with a **Split into files** fix on the finding (B177) |

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
| **In-source suppression via `__MLQT` vendor annotations** | ⭐⭐⭐ | M | **✅ shipped** — with GUI and MCP authoring actions. Not comments: comments are position-bound and get orphaned when the formatter reorders declarations, whereas annotations ride on the element. Class- and component-level, carrying a `reason`. Also the rename-safe replacement for the name-based `FormattingExcludedModels` list. The Code Review exclusion toggle writes `__MLQT(format=false)` (B175) |
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
| **Baseline / ratchet mode** (new-vs-existing, warn on touched debt) | ⭐⭐⭐ | M | **✅ shipped** — `mlqt baseline create/update/prune`, `--changed-from`, `--touched-debt warn\|fail\|ignore`. A changed-from run checks only the models that changed (B184) |
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
