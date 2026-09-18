# Phase 1 — Release feedback

The plan for **B168–B203**, the 36 items the first end-to-end pass over the Photino release opened on
2026-09-17, plus the three ids carried forward from phases 7a/7b. The items themselves are in
[backlog.md](backlog.md); the argument for this phase going before the Wave-2 analyses is in
[roadmap.md](roadmap.md). This note holds the **grouping, the sequencing and the root causes found
while planning** — it is not a second copy of the backlog and does not restate what each item wants.

**Retire this note when the phase lands.** What outlives it goes into the code, into
[CODING_GUIDELINES.md](../CODING_GUIDELINES.md), or into `.claude/skills/`.

---

## What planning established before any code was written

Eleven items were diagnosed while writing this note, several of them differently from the guess
recorded in the backlog. They are written down because each one changes the size of the item, and two
of them change what the fix is.

| # | Root cause, confirmed | Consequence for the plan |
|---|------|------|
| B201 | `GraphBuilder.LoadModelicaFile` creates a placeholder only when `hasFatal && models.Count == 0` (`ModelicaGraph/GraphBuilder.cs:63`). A rejected `import` after `within` records a **`RecoveredSyntax`** error and extracts zero models, so `hasFatal` is false, no placeholder is made, and `fileParserErrors` is dropped with nothing to attach it to. `ParserErrorSeverity.RecoveredSyntax`'s own doc comment — "the parser recovered and the rest of the file was still processed" — is the assumption that broke | The fix is the condition, not the placeholder machinery: **errors present and zero models is fatal whatever severity was recorded.** S, and the most severe item in the phase |
| B192 | `savedSettings` is read *before* the new project is created, then `MLQT.Shared/Layout/MainLayout.razor.cs:182` looks the selection up in that stale snapshot and falls back with `?? savedSettings.Projects.FirstOrDefault()`. A brand-new project is not in the snapshot, so `checkProject` becomes **the first existing project**, which has repositories, so the empty-project short-circuit is skipped and startup loads that project's repositories | Neither of the two faults the backlog hypothesised. It is a stale snapshot plus a silent fallback to "some project". Re-read settings after `CreateProject`, and make the fallback explicit rather than positional |
| B200 | The "Reference only" chip sits inside `@if (VcsType != Local && !string.IsNullOrEmpty(CurrentRevision))` (`LibraryBrowser.razor:88-100`) — a block about displaying the current branch. Only a non-Local repository that has a revision ever reaches it | Exactly B80's shape. Hoist the chip out of the branch-display conditional. Trivial once seen |
| B187 | `FilterFunc` combines terms with `strings.Any(...)` (`CodeReview.razor.cs:1268`) — the OR. Separately, the all-models scope is **smuggled through the search string** (`_searchString + " " + NavState.ModelID`) and stripped back out inside the filter | The scope must come out of the search string before AND semantics can be correct, or scoping to a model starts excluding every finding. `LogMessage.RuleId` already exists, so the rule filter is UI-only |
| B178 | `DiffViewer` has **its own** `ApplyModelicaSyntaxHighlighting` (regex over text) and its own hard-coded palette in `DiffViewer.razor.css`, while the single-file viewer is coloured by `ModelicaRenderer(renderForCodeEditor: true)` over the parse tree, with CSS generated at runtime from `SyntaxHighlightingSettings` | The two highlighters **cannot** be merged — a diff hunk is not parseable. Share the *colour source* (emit CSS custom properties from the settings and have both stylesheets reference them), not the highlighter. This is the "sibling components duplicate rules" shape |
| B182 / B185 | The viewer renders through `ModelicaRenderer` because the highlighting is parse-tree driven (`CodeReview.razor.cs:716`). The reformat is not incidental to colouring — it *is* the colouring | "Stop reformatting" means losing the highlighting the page exists for. See the decision below |
| B186 | `Height="221px"` is hard-coded on the findings pane with the comment "221 px for table, 185px for other stuff", and `MudTablePager` is `HideRowsPerPage="true"` | The pane height and the row count are two independent constants that have to become one measured value |
| B183 | `Items="@CodeReviewService.LogMessages"` is bound with no ordering at all | The unordered half is a one-line change; the scroll half is the work |
| B171 | `DymolaInterfaceFactory.GetOrCreateAsync` returns the cached `_instance` whenever it is non-null, with **no liveness check** — closing the Dymola window leaves a non-null handle to a dead process | Probe liveness and recreate. `IsOfflineMode()` already exists as half of the probe |
| B168 | `FileMonitoringService.StartMonitoring` keys watchers by `repositoryId` (`MLQT.Services/FileMonitoringService.cs:62`), so one directory reached as a project repository *and* through the reference-library paths gets two watchers under two ids | The backlog's suspicion is right. Key the watcher set by resolved path, with the ids that share it as subscribers — the same shape B129 applied to loading |
| B203 | Confirmed by a `--no-incremental` Release build of `ModelicaParser.Tests`: exactly two warnings, `xUnit1051` at `SpellCheckerConcurrencyTests.cs:59` and `xUnit1031` at `:75` | Make the test `async Task`, `await reading`, and pass `TestContext.Current.CancellationToken` to the `Task.Run`. Then re-measure from a build that actually compiled |

Two items need **no work** and are recorded here so nobody re-opens them:

- **B152** — the twelve photographed screenshots. Not a defect; the list exists so it is not
  re-derived. Revisit only if a fixture ever gains an SVN server.
- **B166** — the `check_library` finding-count variance did not reproduce across six runs. Left open
  at low confidence. **The trigger is specific:** if it moves again, capture the **per-rule breakdown
  of two adjacent runs** before anything else. A total says nothing about which rule moved.

---

## The one decision that shapes the phase

**B182, B183, B185 and B197 all sit on the same question: what representation does the viewer show?**
It renders reformatted source, while findings carry lines computed against the stored source. Three
ways out were framed here:

1. **Stop reformatting.** Loses the highlighting. Rejected.
2. **Map lines through the renderer.** Have `ModelicaRenderer` emit a rendered-line → source-line map
   alongside the text. Costs a change in the renderer, which every surface shares.
3. **Re-check against the rendered text.** Correct lines by construction, but every finding in the
   viewer would then come from a second check the CLI never ran — two tools, two answers. Rejected:
   it breaks the invariant the whole `MLQT.Services/Checking/` pipeline exists to hold.

**That framing was wrong, and [analysis-viewer-fidelity.md](analysis-viewer-fidelity.md) is where the
decision now lives.** Option (1) was rejected on the premise that the highlighting comes from the
reformat. It does not: it comes from the parse tree, and the reformat is only how the current code
happens to walk it. A token classifier driven by lexer offsets over the *original* text — a path not
considered here at all — was prototyped and measured to agree with the colours the page shows today
on **99.98% / 99.77% of 114,642 identifier tokens**, round-trip byte-exact over 8,367 files and
1.09M lines, at **~45% less cost end to end**. That dissolves the question rather than answering it:
`Finding.LineNumber` is already class-relative to the text such a view shows, so B182 needs no map,
B183's scroll is a line number, and B185's fallback is already the fast path.

**Where option (2) still earns its place:** if the formatted view is kept as a mode — and it should
be, it is the honest preview of what the formatter will write — then findings shown against *that*
view still need the map. It becomes an enhancement to a secondary mode rather than the foundation of
the page.

Read that note before starting WP2. It also names the three things still undecided (whether the
formatted view is kept and what the default is, what hide-annotations means in a fidelity view, and
whether `DiffViewer`'s side-by-side path adopts the classifier) and sizes the work.

**The lesson for the rest of this phase**, since it was nearly expensive: the rejected option was
rejected from a reading of the code, not a measurement, and the reading was of *how the code works
now* rather than of what the requirement needs. B185 already said to measure the two costs before
choosing. That instruction applied to the decision above it as well.

---

## Work packages

Grouped by shared root cause and shared machinery rather than by the backlog's area headings, because
that is where the savings are. Cross-package dependencies are named where they exist.

### WP0 — Unblock, and clear the confirmed one-liners — **✅ complete**

**B143 ✅, B203 ✅, B180 ✅, B194 ✅ (fixed twice — see below), B200 ✅** · 5 items

What each fix turned out to be, and the two things worth carrying into the rest of the phase:

| # | Fix | Tests |
|---|------|------|
| B203 | `async Task` + `await reading`, and `TestContext.Current.CancellationToken` on the `Task.Run`. **Re-measured properly**: a full `dotnet build MLQT.slnx -c Release --no-incremental` now reports 0 warnings, which is the claim the item was really about | The build is the test |
| B200 | The chip moved to `TitleContent` beside the repository name. The `if/else` it shared with the branch buttons became a negated `if`, so the buttons stay withheld from a reference repository | `LibraryBrowserReferenceOnlyTests` — 7 tests, and the 2 that matter fail against the old markup |
| B180 | `McpServerLocator` probes beside the tester, then the sibling project's output for the *same* configuration and framework, then gives up and returns `""`. Platform-aware executable name, which the old literal was not. "Use MLQT server" is disabled when nothing was found, so it cannot blank the box | `McpServerLocatorTests` — 8 tests, linked by source since the tester has no suite of its own |
| B194 | **The wrong dialog first.** "The startup dialog" is the *progress* dialog with the deferred-analysis steps, not the project picker shown before it. Each deferred step could only be started from the small play button in its avatar slot; the row now runs it too. The project-picker change was reverted | `StartupDialogDeferredStepTests` — 10 tests, the 3 row assertions fail against the old markup |

**Two lessons for the packages that follow.**

- **MudTooltip text is not in the rendered markup.** The first version of the B200 test asserted on
  `"Switch branch"` and passed against a build with the buttons present *and* absent — it was
  vacuous, and only the positive control caught it. Probe a MudBlazor button by its **icon path
  constant**. WP2 is full of tooltip-labelled controls, so this will come up again.
- **Closing a row means advancing the id watermark.** The guard checks that ids *above* the watermark
  run unbroken, so removing B180, B194, B200 and B203 while it sat at B167 would have read as four
  lost rows. The backlog now says B1–B203 issued, new items at B204, and says why.
- **A flapping coverage ratchet is a defect report, not noise.** `DymolaCheckingService` and
  `OpenModelicaCheckingService` each moved by one line between identical runs, and the first instinct
  — baseline the low value and move on — would have buried the cause. The line was
  `StartCheckingAsync`'s `if (_isRunning) return;`, and the reason it was covered about one run in
  four is that the test named `..._WhenAlreadyRunning_DoesNotStartAgain` only attempted its second
  call `if (service.IsRunning)`, which was usually false by the time it looked: the factory throws at
  once, so the background task had already cleared the flag in its `finally`. Its closing assertion
  was `callCount >= 1`, which cannot fail. So the test never checked what it was named for, and the
  coverage number was the only thing saying so. Both now hold the first check open at a gate and
  assert the refused call never reached the factory; four runs agree to the line. **The same
  `if (condition) { assert }` shape as B201's placeholder test** — that is three in this phase, and it
  is worth looking for deliberately.

- **B194 was implemented against the wrong dialog, and nothing in the process would have caught it.**
  The item said "the startup dialog" and "clicking a row in the startup list"; MLQT shows two dialogs
  during startup and the words fit the project picker, which is the one that appears first. Tests,
  mutation checks and review all confirmed a correct fix to something nobody had asked about — none
  of them can tell you the target was wrong. **When an item names a screen rather than a symbol, say
  which screen you took it to mean before building it**, because that is the one assumption the
  verification cannot reach. The real fix is above; the picker was returned to how it was.

**B143** was run by hand on 2026-09-17 (`workflow_dispatch`, run 35257465944, `main`, 2m59s), and it
passed: **57 journeys under WebKit, 0 failed, 0 skipped.** That number is the point of recording it.
A workflow written blind can go green by skipping its own work, and B142 is why nobody should take
"success" at face value here; 57 executed journeys is the evidence that the nightly job does what it
claims.

This package existed to get the rhythm going against work that cannot go wrong, and to take five
items off a list of thirty-six before starting anything that needs thought.

### WP1 — Correctness: the things that lose data or lie about state — **B169 outstanding**

**B201 ✅, B192 ✅, B168 ✅, B172 ✅, B198 narrowed, B204 ✅ (new)** · B169 not started

| # | What it turned out to be |
|---|------|
| B201 | Worse than reported. The placeholder fired on `hasFatal && models.Count == 0`, and **outright garbage produced no placeholder either** — the path was dead for every failure that did not throw. Now "errors recorded and no classes extracted", whatever severity. The boundary is drawn against a class the parser *salvages*: that keeps its own node and its diagnostic, and a placeholder there would be a regression |
| B192 | The three silent links in the chain: `CreateProject` does not await its save, `LoadRepositorySettingsAsync` replaces the in-memory project list from disk, and it then resolved the active project with `?? Projects.First()`. An explicitly requested id is no longer substituted; the fallback is kept for a saved active id naming a deleted project, which is what it was written for |
| B168 | Watchers were keyed by repository, and repositories share a path routinely — every one watches its `VcsRootPath`, so two libraries in one working copy are two watchers over one tree. One watcher per path now, reference-counted, fanning out to each subscriber |
| B172 | The `Include` regex discarded the delimiter, so `<stdio.h>` resolved to `<library>/Resources/Include/stdio.h` and every external function using the C standard library reported a missing file. **C's own rule carries the fix** — `<name>` is the compiler's search path, `"name"` is the project's — so no platform include paths and no list are needed for the main case; a short standard-name list is the second line for `#include "math.h"` |
| B204 | `ExtractLibraryName` looked for the outermost class with `ParentModelName == null`, and it is the **empty string**, never null. It returned null on every call, ever, and every caller fell back to the folder name. Invisible while the two agree, which they usually do |

**B198 is narrowed, not fixed, and should not be closed.** The layout hypothesis the backlog named is
disproven: a repository with `package.mo` at the top level is discovered, loaded, recorded, announced
and visible to the query MainLayout gates its analysis on — `AddRepositoryLoadsItsLibraryTests`
asserts each of those. B204 was found in that investigation and may be the whole of what the user
saw. Reproducing it needs the user's own repository; the next question is whether the library was
absent from the tree or merely misnamed.

**B169 is the one item of WP1 not attempted.** It needs a reproduction with two libraries whose
registered resource roots share a prefix, and the backlog is explicit that nothing should change
before that exists. It cannot be constructed from the fixtures here — it wants encrypted libraries
with registered `Resources/` roots.

**Three lessons, all about tests rather than code.**

- **B201 hid behind a test written to cover it.**
  `LoadModelicaFile_UnparseableContent_CreatesPlaceholderWithFullSource` wrapped its assertions in
  `if (placeholder != null)` and recorded that producing nothing "is still acceptable — no crash
  reached the caller". That is the defect, excused in the test for the machinery that prevents it.
  A conditional assertion is not an assertion.
- **Two of my own tests were vacuous, and only the controls caught them.** The first B168 suite
  passed against the unfixed code — everything it asserted was already true, because each repository
  had its own watcher. A real-filesystem test written to replace it *also* passed either way, and was
  flaky besides: which repository wins depends on how the OS interleaves two watchers' events. What
  finally distinguished the fix was counting the watchers, which is why `WatchedPathCount` exists.
- **A test expectation can be the thing that is wrong.** The first B201 suite asserted a placeholder
  for a truncated class; the parser salvages that class, which is correct, and the test was changed
  rather than the code. Checking which of the two is wrong is the step, not a formality.

**B201's blast radius reached `mlqt compare`.** An unreadable file used to produce no classes, so
everything it held read as missing and the gate failed on the count. With a placeholder standing in,
a single-class file stops looking missing — so `compare` would have exited 0 on a library with a
merge conflict marker in it. It now fails on unparseable files explicitly, and the warning no longer
claims their classes are "counted as absent", because they are not. `cli.md` changed with it.

### WP2 — The Code Review page

**B182, B185, B183, B176, B186, B187, B178, B189, B197**, and **B173** alongside B186 · 10 items ·
the bulk of the phase

The keystone package, and what the roadmap's ordering argument is really about: every new analysis
wave lands here. Strict internal order, because most of it sits on the line map.

1. **Measure** the two costs B185 names. Nothing else starts until that number exists.
2. **B182** — the renderer line map, and the finding-line mapping through it.
3. **B183** — stable alphabetical ordering (one line) and scroll-to-line, using the map. The existing
   `spellCheck.scrollWordIntoView` / `setScroll` interop is the machinery to extend, not to duplicate.
4. **B185** — the size threshold, with the no-reformat path as the identity case of the map.
5. **B186 with B173** — resizable panes. `MudExSplitPanel` is **already a dependency and already in
   use** in `MainLayout.razor:233`, so this is one control applied twice, and the fixed `221px` and
   hidden pager collapse into one measured value.
6. **B176** (search over the rendered source) and **B187** (AND plus a rule filter) — independent of
   the map, so they can run in parallel with 2–5. B187 must untangle the scope-through-search-string
   smuggling first.
7. **B178** — the shared colour source. Independent of everything else here.
8. **B189** then **B197** — reveal-in-tree, then the navigation stack over it. Both hang off the
   finding-click path that B183 rewrites, so they come last and are cheap once it exists.

### WP3 — Rules and formatting

**B181, B177, B195, B175** · 4 items · M

Batched because a new rule id walks the same six places every time: `RuleIds`, `RuleCatalog`,
`RuleSettingsLayout`, the visitor or analyzer, `settings-reference.md`, and the catalogue guard test.
Walk that path once with three rules in hand rather than three times.

- **B181** — `OneOfEachSection` is the working template for a formatting concern that also reports,
  and `FormattingOptions.ComponentsBeforeClasses` already exists. Giving it an id also closes B103's
  gap: it is the one row in `settings-reference.md` with no rule id to bind its label to.
- **B177** and **B195** both belong to `PackageOrderAnalyzer`'s family. B195 needs the explicit
  "match Dymola" setting, because Dymola never descends into non-package folders and so gives no
  signal at all for the stray-file half.
- **B175** — the exclusion button writes `__MLQT(format=false)` instead of a name into
  `FormattingExcludedModels`. Both mechanisms are already honoured everywhere (B39, B65) and the
  annotation writer already exists for suppression, so this is a change of which writer the button
  calls.

**The standing trap in this package** is a catalogued promise with no test behind it: a rule id
implies every other surface honours it, and only a test over `RuleCatalog` holds anyone to that. Each
new id needs its catalogue row, its layout row and its guard assertion in the same commit.

### WP4 — Performance, measured before it is touched

**B190, B174, B184, B199** · 4 items · M–L

**No change in this package without a measurement first.** The log at `%LocalAppData%/MLQT/*.log`
holds weeks of timestamped phase durations and `mlqt check --timings` prints the per-phase breakdown;
B128 established both. B174 says explicitly not to theorise first.

- **B190** — confirm the freeze still happens before investigating it. It predates several fixes.
- **B184** is the largest available win on CI check time and the most delicate change in the phase.
  `ChangedModelResolver.Resolve` currently runs **after** `load.Findings` is fully computed
  (`MLQT.Cli/CheckRunner.cs:129`), so resolution has to move ahead of the check. Two traps found
  while planning, both invisible until someone reads a report:
  - `baseline.StaleEntries(load.Findings)` over a partial finding set reports **every unchanged
    model's baseline findings as fixed**. It has to be restricted to changed models on both sides.
  - `FindingClassifier.Classify` over a partial set changes the accepted-debt counts, and with them
    the summary, `--min-coverage` and `--metrics` output. **Decide explicitly what a `--changed-from`
    run reports versus what it gates on**, and write that into `cli.md`, before touching the
    sequencing.
- **B199** — a node-count threshold above which the plot is skipped in favour of the list, with an
  override for a user who wants it anyway.

### WP5 — External tools

**B170 then B171** · 2 items · strict order

B170 first: MLQT needs a result dialog of its own, reporting what the tool said, for both Dymola and
OpenModelica. B171's fix — notice a dead session and start a new one — needs somewhere to say so when
it cannot, and that is B170's dialog. The liveness probe goes in `DymolaInterfaceFactory`.

These two suites run in **no CI job** — `build/run-all-tests.ps1` is the only thing that runs them —
so verify by hand on a machine with the tool, and remember that `-CoreOnly` is a decision while
reading past a red line is not.

### WP6 — Revision control

**B193, B202** · 2 items · S–M

B193's real work is not enumerating tags, it is the surrounding UI being able to describe a detached
HEAD rather than showing an empty branch name — and `LibraryBrowser.razor:97` already has a
"Detached HEAD" branch, so that half may be closer than it looks. B202 changes the history diff from
revision-against-working-copy to revision-against-predecessor, which is what the dialog is for; B155
documented the current behaviour, so the documentation changes with it.

### WP7 — The two large ones

**B188, B191** · 2 items · S and L

Deliberately last, and **B191 should be confirmed as in scope before it is started.** Marking a model
as modified is small; classifying the *kind* of change needs a comparison of the parsed old and new
class, and that capability is worth well beyond the marker — which is also why it is the one item
here big enough to be its own phase. B188 (a persisted repository order) is unrelated and small; it
is here only because nothing else needs it.

### Not in any package

**B179** and **B196**, the two MCP items, have no dependency on anything above and no dependency on
each other. They are separable at any point: B179 returns what is already sitting on the synthesized
stub (description, base classes, parameters, connectors, inputs and outputs), marked as recovered
from documentation rather than read from source and still declaring the class not editable (B85).
B196 is a question about a dependency — which SVG-to-PNG conversion to take on — before it is a
question about code.

---

## Sequencing summary

```
WP0 ──▶ WP1 ──▶ WP2   measure ▸ B182 map ▸ B183 ▸ B185 ▸ B186/B173 ▸ B189 ▸ B197
          │              with B176, B187, B178 in parallel
          ├──▶ WP3   rules, independent
          ├──▶ WP5   external tools, independent
          ├──▶ WP6   revision control, independent
          └──▶ WP4   perf; B184 after WP1 so the graph is trusted first
                       └──▶ WP7   B191 only if confirmed in scope

WP-less: B179, B196 (MCP) — separable at any point
```

WP3, WP5 and WP6 depend on nothing in WP2 and can be taken whenever a change of subject is wanted.
WP4's B184 is placed after WP1 on purpose: re-scoping what gets checked is not worth doing while a
malformed file can still drop classes out of the graph underneath it (B201).

## Ground rules for the phase

- **One package per branch, one PR per package**, with the backlog table updated in the same PR.
  CLAUDE.md is explicit that a finished item still listed reads as outstanding work.
- **Every fix gets a test that fails first.** For the eleven items diagnosed above the failing test is
  already specified by the diagnosis — B201's is the ModelicaTools file shape, B192's is creating a
  project while another one with repositories exists, B200's is a Local repository with the flag set.
- **Verify by mutation** for the UI work, per `skill-gui-testing.md`: a guard test that also passes
  against the unfixed code is not a test.
- **No performance change without a before-and-after number**, from the log or from `--timings`.
- Run `build/run-all-tests.ps1` before pushing, and `build/check-coverage.ps1` when a class is added
  or code moves between classes. The coverage gate is a ratchet and `MLQT.Shared` is in it at 80%.
