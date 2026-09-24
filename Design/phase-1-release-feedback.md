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

**Decided 2026-09-18, and WP2 now opens with it.** The question was put once more, and harder: *run
`ModelicaRenderer` on the **save path only**, when the repository has Apply Formatting on, and show
the bytes that are on disk — or in the revision — everywhere else.* That is Part II of the analysis
note (§10–§15), and it is what WP2 below is now built around. What it settles:

- **The formatted view is not kept as a mode of the code viewer.** With formatting on, the file on
  disk already *is* the renderer's output — MLQT's own MSL renders 400/400 byte-identical — so the
  mode only says anything new when the user is *about* to turn formatting on. That preview belongs
  beside *Format All Files*, and it is not in this phase. **This is the one decision here that is
  expensive to reverse**, because it deletes the rendered path rather than leaving it behind a toggle.
- **Hide-annotations becomes range elision, and it is not deferrable** — hiding class definitions in a
  package is the same mechanism and is on for every package. One type, four callers.
- **`DiffViewer` adopts the classifier.** It already speaks the `<KEYWORD>…` markup, so this deletes
  more than it adds, and it is all of B178.
- **One thing is still open and is decided by a measurement, not an argument:** whether
  `PackageCodeTrimmer` is converted from re-rendering to verbatim excision. It feeds the check
  pipeline all three surfaces share, so the parity number decides it (B216).

**Where option (2) still earns its place:** nowhere in this phase. If a formatted preview is ever
built beside *Format All Files*, findings are not shown against it and it needs no map.

Read Part II before starting WP2 — §14 is the staging the package below follows, §13 the risks, and
§11a/§11b the two places Part II supersedes Part I's sizing.

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
- **B192 was reported fixed when only its visible symptom's neighbours had been.** The first pass
  found two genuine defects on the path, fixed them, tested them by mutation, and never asked the
  plain question: *where do the repositories actually come from?* They came from a call whose comment
  said "load settings first (so existing projects are in memory)" — a sentence that describes reading
  a list and a method that opens working copies. **A call whose comment describes less than it does is
  worth reading the body of**, and an item that reproduces after a fix means the mechanism was never
  established, not that another edge case remains. The user's second report named four symptoms at
  once (old repositories, no progress dialog, a busy UI, a stale title); one cause explains all four,
  and that is what a mechanism looks like when you have it.

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

### WP1 — Correctness: the things that lose data or lie about state — **complete**

**B201 ✅, B192 ✅, B168 ✅, B169 ✅, B172 ✅, B204 ✅** · B198 narrowed and left open

| # | What it turned out to be |
|---|------|
| B201 | Worse than reported. The placeholder fired on `hasFatal && models.Count == 0`, and **outright garbage produced no placeholder either** — the path was dead for every failure that did not throw. Now "errors recorded and no classes extracted", whatever severity. The boundary is drawn against a class the parser *salvages*: that keeps its own node and its diagnostic, and a placeholder there would be a regression |
| B192 | **Fixed twice.** The first pass removed two silent fallbacks that substitute a *different* project when the named one is not found — real, and kept. But the repositories were not arriving through project selection at all: the startup path called `LoadRepositorySettingsAsync()` with no argument, purely to get the saved project list into memory, and that overload **opens every repository of the currently active project and loads their libraries**. Nothing unloads them — it never clears the loaded repositories or the graph, and only `SwitchProjectAsync` does. `CreateAndSelectProjectAsync` is the missing primitive: append a project to the saved settings and make it active, loading nothing |
| B168 | Watchers were keyed by repository, and repositories share a path routinely — every one watches its `VcsRootPath`, so two libraries in one working copy are two watchers over one tree. One watcher per path now, reference-counted, fanning out to each subscriber |
| B172 | The `Include` regex discarded the delimiter, so `<stdio.h>` resolved to `<library>/Resources/Include/stdio.h` and every external function using the C standard library reported a missing file. **C's own rule carries the fix** — `<name>` is the compiler's search path, `"name"` is the project's — so no platform include paths and no list are needed for the main case; a short standard-name list is the second line for `#include "math.h"` |
| B204 | `ExtractLibraryName` looked for the outermost class with `ParentModelName == null`, and it is the **empty string**, never null. It returned null on every call, ever, and every caller fell back to the folder name. Invisible while the two agree, which they usually do |

**B198 has not reproduced, and is left open at low confidence** — the same treatment as B166. Both
candidates are now ruled out rather than merely untested. The *layout* is disproven by
`AddRepositoryLoadsItsLibraryTests`. **B204** would have looked exactly like the report — a library
labelled with its folder's name rather than its own — but the library in that repository shares its
folder's name, so B204 was never visible there.

Worth carrying forward: the report predates the B192 fix, which removed a path that loaded a
*different* project's repositories at startup and left them loaded. That is enough to muddle what
belonged to which project, and it is the most likely remaining explanation without being a
demonstrable one.

**B206 makes a recurrence report itself**, which is the useful outcome of failing to reproduce
something. Adding a repository had two silent failures — nothing found, and things found but none
loadable — and both closed the dialog reporting success over an empty tree. Each now says so, and
says what was searched for. A second sighting can therefore distinguish "no library discovered" from
"discovered but not loaded" from "loaded but not shown", which the original report could not: it is
the same observation for all three.

**B169 turned out to be simpler than its own description, once the repositories were available.** The
backlog guessed at "a resolution that picks the wrong registered root when more than one is a prefix
of the path". Prefixes have nothing to do with it: **library names are not unique across loaded
libraries**, and `ResolveModelicaUri` took the first match by name. A commercial library is routinely
loaded twice — the encrypted build a tool ships and the source the team has checked out — and the
encrypted copy drops its version suffix, so `Claytex 2026.1` registers as `Claytex`. Nine names
collide in the reported setup.

Two rules settle it, in that order. The **referencing file**, when it lies inside one of the
candidates: a model resolves `modelica://Claytex/Resources/x` against the copy of Claytex it is part
of. Then, for everything else, the **readable copy** — because an encrypted library can never be the
right answer while a readable one exists. Nothing can read its code, so nothing knows what it
references; every reference naming it was written somewhere else.

**The second rule was missing from the first attempt and had to be reported again.** The file rule
alone fixes only same-library references, and most references are not of that kind: a model in
Engines naming `modelica://Claytex/...` sits inside neither Claytex root, so the file cannot separate
them and it fell back to first-match — the encrypted build. Measured on the real libraries with the
encrypted copies registered first, loading Engines and Claytex source: 599 resources, and with the
readable preference switched off the cross-library ones move to the encrypted root.

**The removed fallback was removed for a reason that was wrong.** The first attempt dropped
"prefer readable source" on the grounds that a class recovered from documentation belongs to the
encrypted copy, so its resources do too. That is true and irrelevant: stubs are excluded from
dependency analysis entirely, so they never produce a resource reference at all. The premise was
about a case that cannot arise, and it cost the user a second report.

Two things worth keeping. The guess in the backlog entry was confident and wrong — it pointed at
prefix matching, which has nothing to do with it — so **an item's stated cause is a lead, not a
finding**. And the first fix was verified on real data that did not contain the failing case: 38
resources, all same-library. **Measuring the wrong sample proves nothing**, and the fix looked
complete because the measurement agreed with it.

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

### WP2 — The Code Review page — **✅ complete**

**B213 ✅, B214 ✅, B215 ✅, B217 ✅** (from the decision above) and **B230 ✅, B231 ✅** (from
running its first step) and **B182 ✅, B185 ✅, B183 ✅, B178 ✅, B176 ✅, B186 ✅, B187 ✅, B189 ✅,
B197 ✅**, with **B173 ✅** alongside B186 · 15 items · the bulk of the phase — **complete
2026-09-19**

**Four items planned here were moved out rather than closed**, and the reason is worth keeping: the
sequencing diagram carried B216 and B218 as *"separable at any point"*, which was true while this
package was open and became false the moment it closed — "separable" silently turns into "owned by a
finished package". B232 and B237 were worse off again: they were written into the prose of a step
rather than into any package's item list, so nothing at the top of a WP ever counted them. All four
now have a package:

| Moved | To | Because |
|-------|----|---------|
| **B216** trimmer excision | WP3 | it changes what the renderer writes and its gate is the parity number, which WP3 re-runs anyway |
| **B232** subscripts coloured as calls | WP3 | same — a renderer output change, decided by whether the visible difference is wanted |
| **B218** `get_class_source` rewrite | WP10 | the same shape as the two MCP items that were already package-less |
| **B237** nothing proves the search is wired | WP9 | a shared-journey-host defect, which is that package's subject |

**B233** (annotations sharing a line with code survive the hide) is deliberately **not in phase 1**:
its own row says to measure what fraction of annotations it actually leaves before committing to
splicing markup rather than source. It is a roadmap candidate, not a package item.

The keystone package, and what the roadmap's ordering argument is really about: every new analysis
wave lands here. Strict internal order, because most of it sits on **the viewer showing the file** —
which is what the decision above changed, and it is a different foundation from the line map this
package was previously built on.

**Steps 1–5 are `analysis-viewer-fidelity.md` §14's S0–S5 with backlog ids attached.** The stage
letters are kept because the gates are written there.

1. **S0/S1 — measure, and nothing starts until both numbers exist. ✅ done 2026-09-19, both gates
   pass.** Results and what they change are `analysis-viewer-fidelity.md` **Part III (§16–§18)**;
   the headlines:
   - **S0 ✅** — round trip exact over **8,367 files / 1,103,108 lines**; agreement **99.9989% /
     99.9984%** with the renderer's own defects mirrored, **99.9519% / 99.9639%** without, zero
     misalignment. The whole residue is one renderer defect in two facets (**B232**), and the
     classifier is right in both directions.
   - **S1 ✅** — **10.6% / 16.4%** of classes lose their line mapping to the trimmer, and **55% /
     95% of those rewrites remove nothing at all** (**B230**, the cheapest item in the package).
     The excision trimmer loses no finding, adds 2 correct ones in MSL and 0 in Buildings, and its
     line map is exact over 30,599 lines. **B216 is in scope**, with a baseline-drift note.
   - **Read §17.4 before B215.** The re-slice rule this plan specified — "re-read the file and slice
     `[StartIndex..StopIndex]`" — matches **0 of 13,997 classes**. The offsets are into the
     line-ending-normalised text and the slice is `[Start..Stop+1]`; as specified it would have shown
     the wrong class entirely on any CRLF library, which is all of them (**B231**).
   - This also answered the two costs **B185** named, because it separates the parse from the render.
2. **B213 — the classifier** (`ModelicaTokenClassifier`, in `ModelicaParser` beside
   `ModelicaRenderer`). The largest single piece. Two property tests carry it: round trip and
   agreement. >95% per class, and a `run-mutation.ps1 -Mutate` pass over the new file.
3. **B214 — `SourceElision`.** Independent of B213 and can be built alongside it; step 4 needs both.
4. **B215 ✅ — the viewer shows the file**, with **B182 ✅** and **B183 ✅**. **B185 is what is left
   of this step**: the lexer-only tier is built and public, but nothing chooses it yet, so the size
   threshold still has to be measured and set. B231 ✅ came with it — the re-slice rule §17.4 said
   to read first, now `ClassSource`. The source rule, the classifier in place of the renderer,
   `ShowRawSource` deleted. **B182, B183 and B185 close here**: B182 by identity rather than by a map,
   B183's scroll because the finding's line is the viewer's line — the existing
   `spellCheck.scrollWordIntoView` / `setScroll` interop is the machinery to extend, not to duplicate
   — and B183's other half (stable alphabetical ordering) is still one line and independent of all of
   this. B185's threshold becomes the lex-only tier. The page gets its first component tests.
   **B185 ✅, and its own row was wrong twice over.** There is no size to threshold on, and turning
   the highlighting off would have saved nothing: the cost is the **parse**, made quadratic by a run
   of comments inside an `equation` section (**B235**, where the real fix is — it costs the CLI and
   the MCP server the same minutes it costs the page). What ships is that the viewer stops
   *waiting*: above 64 KB the lexer paints at once and the tree's colouring lands when it lands.
   **The measurement caught itself getting this wrong.** Its first version varied the annotations and
   a block of comments together and blamed the annotations; the error only surfaced because an
   unrelated edit dropped the comments and the 72-second case became 367 ms. Vary one thing — and
   re-run a number before building on it.
5. **B178 ✅ with B217 ✅** — `DiffViewer` adopted the classifier on both sides and
   `HighlightRawModelica` is gone; B217 was confirmed (a trimmed package showed every inline child
   as a deletion) and both sides of the class diff now come from the file. **Both were mis-diagnosed
   in planning and both were smaller than the diagnosis**: B178's root-cause row said the two
   highlighters "cannot be merged — a diff hunk is not parseable" and that the fix was to share a
   colour source between two stylesheets. The sides are whole documents rather than hunks, so one
   classifier serves both; and the preset already reached the diff, since the runtime stylesheet is
   global and `!important`. The real fault was that the regex could only produce four of the nine
   categories, so identifiers and types were plain text in one pane and coloured in the other.
6. **B230 ✅ taken; B216 → WP3 and B218 → WP10.** B230 is a guard clause in front of an existing
   render and removed 55% / 95% of the trimmed-package population on its own, which is why it was
   worth taking before anything else here whatever happened to B216. B216 and B218 are the same
   mechanism reaching the check pipeline and an agent, and neither is needed for the viewer — so with
   the viewer done they are ordinary items in other packages rather than a loose end of this one.
   **B231 ✅** (the offset documentation) landed with B215, since both read those fields.
7. **B186 ✅ with B173 ✅** — resizable panes. `MudExSplitPanel` is **already a dependency and already
   in use** in `MainLayout.razor:233`, so this is one control applied twice, and the fixed `221px`
   and hidden pager collapse into one measured value. **The pager did not collapse into a value — it
   went.** A pager shows a fixed number of rows however much room the table has, so resizing alone
   would only have changed which rows were cut off; the table is virtualised and scrolled instead,
   which also carries a real library's tens of thousands of findings.

   **This is the first item in the phase that could not be checked by reading**, and it is worth
   recording what that cost. The layout looked right in a screenshot while the splitter did nothing,
   and the journey that caught it was itself wrong twice first: MainLayout's splitter is on every
   page and nests around the page's own, so a `.First` locator dragged the outer one — which works
   perfectly — and reported the inner one broken. A probe that printed the DOM settled it in one run
   after two rounds of guessing. Drive the browser, and when a UI test fails, ask what it is looking
   at before changing what it is looking at.
8. **B176 ✅** (search over the source — now the user's own source) and **B187 ✅** (AND plus a rule
   filter) — independent of the classifier, so they can run in parallel with 2–5. B187 must untangle
   the scope-through-search-string smuggling first. **That ordering was the whole of B187**: swapping
   OR for AND before pulling the scope out would have excluded every finding, because the class name
   was itself one of the terms. The filter is now a pure function with tests over it.

   **B176 left a gap that is worth naming rather than papering over (B237).** Its logic is
   unit-tested on both sides, but the wiring between them has no automated guard: searching needs a
   class selected, and neither way of getting one survives the shared journey host — through the tree
   needs a repository, which starts the analysis pipeline and timed out another journey; through a
   clicked finding works in isolation in 11 s and then never renders in a full run. The journey was
   written, failed that way, and was removed rather than left red or made opt-in. **B237 is in WP9**,
   because the useful half of it is not the journey but the reason a class will not open once the
   other journeys have run — `ResizablePanesJourney` meets the same wall the moment it needs one.
9. **B189 ✅** then **B197 ✅** — reveal-in-tree, then the navigation stack over it. Both hang off the
   finding-click path step 4 rewrites, so they come last and are cheap once it exists. B197's peek now
   lands in the user's own text, which is the point of it. **They were cheap, and B197 shipped two of
   its three parts**: a menu of the classes this one uses, and back/forward over everything that
   moves the selection. The peek itself — hovering an identifier and seeing its class without
   leaving — is not done; it needs a token resolved to a class, which the classifier's `TYPE` and
   `NAME` tags make possible but do not do. The history lives on `AppState` rather than on the page,
   because every tab moves the selection and one page's history would only know its own moves.

**The standing trap in this package** is scope: the change touches the viewer, both diff views, the
MCP source tool and possibly the check pipeline. Steps 1–5 are each shippable alone and step 6 is
explicitly optional — keep them that way.

### WP3 — Rules and formatting, and what the renderer writes

**B236 ✅, B181 ✅, B177 ✅, B195 ✅, B175 ✅** · 5 items · M · plus **B216 ✅** and **B232 ✅** from WP2 · **complete 2026-09-20**

Batched because a new rule id walks the same six places every time: `RuleIds`, `RuleCatalog`,
`RuleSettingsLayout`, the visitor or analyzer, `settings-reference.md`, and the catalogue guard test.
Walk that path once with three rules in hand rather than three times.

**Two of the seven are not rules at all** — B236, B216 and B232 are all changes to what the renderer
*produces*, which is why they are here rather than anywhere else, and they share a gate: a change to
renderer output is judged by a whole-library comparison, not by reading. B236 and B216 both want the
parity number (MSL = 34329 findings, no finding moving except as the change accounts for it) that
this package re-runs for the rule work regardless.

**✅ The package is done, and the gate was worth having.** Each of the three renderer-output items
was judged by running it over a whole library rather than by reading it, and each time that found
something reading would not have. B236's cause was not the one its row named. B195's premise — that
MLQT descends into folders Dymola ignores — turned out to be false. And B216's parity run is the
clearest case: it confirmed S1's prediction exactly (0 findings lost on either library, the 2
`OneOfEachSection` findings gained on MSL and no others) *and* showed the real payoff, which S1 had
not put a number on — **466 findings across the two libraries that used to be pinned to the class
declaration now point at the line they are about**.

**The parity number in the line above is stale and deliberately left.** It was written when the
enabled rule set was whatever produced 34329; the runs here used an explicit nine-rule config and
got 9008 on MSL. What matters is not the absolute figure but that it is compared against itself
across a change, which is how each of these was judged.

- **B236 first, and it is not like the other four.** They add or refine rules; this is a regression in
  what the formatter *writes* — every file now ends without its final newline, so reformatting a
  library that an earlier build formatted reports every file in it as modified. That is the worst
  shape a formatting defect can take: a diff of thousands of files with nothing in them, which hides
  any real change inside it and makes the reformat look untrustworthy. It also predates 2026.4.0, so
  it is in a shipped release and will keep costing users until it is out. **Bisect it rather than
  reason about it** — the entry names the two lines that write the text and the one that trims it,
  but the fault is a one-character difference on a path several rewrites have crossed, and reading
  will lose to `git log` here. Small, and worth taking before this package's rule work whatever the
  order of the rest. Additional comment, the 2026.3.1 release does the same but the MSL repository
  claims it was reformmated by MLQT on 02/09/2026 which would have been during the 2026.4.0 development.
  So I'm wondering if the difference is down to now I'm clicking "Format All Files" on the repository 
  settings page and maybe the formatting was applied via a different route when I did it previously

  **✅ Fixed 2026-09-19, and that last guess was the answer.** It is not a regression and there was
  nothing to bisect: `git log -S` puts both behaviours in the initial public commit. The incremental
  formatter has always appended a trailing newline and the full library save has always joined the
  rendered lines without one, so the route decided the ending — and since the incremental path runs
  at startup and after every VCS operation, it is the one that had formatted the library. The rule
  is now `ModelicaFileEncoding.EnsureFinalNewline`, applied by every write in the funnel CLAUDE.md
  already designates, and public so a caller comparing disk against what it would write compares
  like with like. That second half was not optional: MCP's `format_class` decides whether to write
  by comparing the two, and putting the rule only in the writer would have made it report every
  already-formatted file as changed and rewrite it on every call — the same defect one layer along.

  **What the measurement added, which reading would not have.** Over MSL: 5,753 files, none left
  without a final newline, and of the 2,480 that both paths rewrite the two now agree byte for byte
  on 2,471. The 9 that differ are all `package.mo`, and they are not a formatting disagreement — the
  full save extracts inline standalone children into their own files and the incremental path does
  not, which is the documented one-file-per-class restructure. Worth knowing before B216, which
  changes how packages are rendered: that asymmetry is the population it operates on.

- **B181** — `OneOfEachSection` is the working template for a formatting concern that also reports,
  and `FormattingOptions.ComponentsBeforeClasses` already exists. Giving it an id also closes B103's
  gap: it is the one row in `settings-reference.md` with no rule id to bind its label to.

  **✅ Done 2026-09-19.** The template held and the six places were the six places. Two things the
  row did not say. The prerequisite is `ImportStatementsFirst`, not `OneOfEachSection` like its
  neighbours — the renderer reads the option only inside the branch imports-first selects — and
  **the section is the unit, not the class**: components are grouped before classes within each
  `public`/`protected` section and nothing moves across the boundary, so a whole-class comparison
  would report exactly the arrangement formatting produces.

  **It also turned up a defect of its own (B238), which had to be fixed first.** A rule switched on
  while its prerequisite is off did not survive a save: the bool facade serialized the *effective*
  answer, so it wrote `false` for a rule the user had enabled, and the facade's setter removes the
  map entry when given false. A new rule sitting behind two prerequisites would have been the most
  exposed thing in the settings file, so B181 could not ship on top of it. The three existing
  ordering rules had the same hole and nothing had ever asked.
- **B177** and **B195** both belong to `PackageOrderAnalyzer`'s family. B195 needs the explicit
  "match Dymola" setting, because Dymola never descends into non-package folders and so gives no
  signal at all for the stray-file half.

  **✅ Done 2026-09-19, and that second sentence was wrong.** B177 is `MLQT.Structure.SingleFilePackage`,
  an analyzer beside the package.order one, judging **could this be split** rather than how big the
  file is. B195 is `PackageOrderMatchesDymola`, a modifier on one rule rather than a rule of its own.

  **There is no stray-file half.** MLQT does not descend into a directory without a `package.mo`
  either — `LibraryDataService` skips them for the same reason Dymola does — so the asymmetry this
  package was told to preserve does not exist. Found by running the CLI over a fixture with a file
  in a plain folder and watching the model count not move. What the setting actually drops is stale
  entries, which Dymola says nothing about, and a class in a file whose name does not match it,
  which MLQT loads and Dymola cannot. **That is the third item in this phase whose row described a
  mechanism that was not there**, after B185 and B236, and in all three cases the thing that found
  it was running the code rather than reading it.
- **B175** — the exclusion button writes `__MLQT(format=false)` instead of a name into
  `FormattingExcludedModels`. Both mechanisms are already honoured everywhere (B39, B65) and the
  annotation writer already exists for suppression, so this is a change of which writer the button
  calls.

  **✅ Done 2026-09-19, and it was not only a change of which writer the button calls.** The writer
  could add a directive; nothing had ever needed to take one out, and a toggle has two directions.
  Removal is the half with the risk, because it rewrites a class's source rather than adding to it:
  it widens as each container empties and re-parses the result before returning it. The button was
  also reading its own state from the name list alone, so a class carrying the annotation showed as
  not excluded and the button offered to exclude it again — the exclusions-must-agree shape, in the
  one place that had been left.
- **B216** — the trimmer re-renders a package where it could excise. **S1 already decided it is in
  scope and measured the gate**: no finding lost on either library, MSL gains 2 correct
  `OneOfEachSection` findings (excision leaves an empty section that re-rendering dropped — baseline
  drift to declare, not a regression), and the line map is exact over 30,599 lines. It is the
  riskiest change in this package because it feeds the pipeline all three surfaces share, so it wants
  the parity run and nothing else taken at the same time. **Finish the file's seven mutation
  survivors with it** — excision keeps two of the decisions they sit on and deletes the rest with the
  render, so each ends either killed by a test or gone with the code.
- **B232** — `ModelicaRenderer` colours array subscripts as function calls, in two facets of one
  mutable-field bug. Mostly a decision rather than a fix: the classifier already declines to
  reproduce either facet, so closing this is agreeing that the visible change is wanted and saying so
  in the catalogue. It is the **entire** residue of B213's 99.998% agreement measurement, which is
  the argument that nothing else in the renderer's colouring is in doubt.

**The standing trap in this package** is a catalogued promise with no test behind it: a rule id
implies every other surface honours it, and only a test over `RuleCatalog` holds anyone to that. Each
new id needs its catalogue row, its layout row and its guard assertion in the same commit.

### WP4 — Performance, measured before it is touched

**B190 ✅, B174 ✅, B184 ✅, B199 ✅, B235 ✅** · 5 items · M–L · **complete 2026-09-20**

**No change in this package without a measurement first.** The log at `%LocalAppData%/MLQT/*.log`
holds weeks of timestamped phase durations and `mlqt check --timings` prints the per-phase breakdown;
B128 established both. B174 says explicitly not to theorise first.

**The measurement was taken on 2026-09-20 and it moves this package's ordering.** Per class of real
library, from the log: MSL 27.7ms, Claytex **314.4ms** — and MSL checked *inside* the Claytex
session cost 24.7ms, unchanged, so it is not the size of the graph. Reproduced with the CLI on
libraries in reach: with the same five rules on, **`rule:MissingUnits` is 82% of the whole check on
Buildings and 94% on MSL**, at 63.6 and 65.5 ms/class — within 3% of each other, so it is not a
property of one library either. **Parse is 6–17%.**

Two consequences:

- **B235 is not the answer to B174**, which this package had explicitly left open as a question. A
  quadratic parse on comment runs is real and worth fixing, but parse is a sixth of the time at
  most. B235 stays; it stops being the first thing to reach for.
- **The first thing to reach for is `MissingUnits` and the unit resolution under it**, which the
  Claytex log shows dragging the coverage measurement with it — 51% of that run, against 22% on the
  same day for MSL with the rule off. One root cause, two symptoms.

One change made on the strength of that (below), and **two hypotheses tested and rejected before
it** — recorded because this is B253's lesson arriving again in the same package it was learned in:
reading code that looks expensive is not measuring it.

- **B235 ✅ arrived from WP2 with its measurement already done**, which is the one item here that
  starts past this package's gate rather than at it: a run of comments inside an `equation` section
  makes the parse quadratic (69 s for 4,000 of them, against 367 ms for a *larger* class without
  them), on legal input with zero parse errors. **Take it with B174, not apart from it.** B174 is
  "loading and style checking are getting slow" and says to start from the log; B235 is a named,
  reproducible cause of exactly that, and every surface parses — so it is also part of B184's
  subject, CI check time. Whether it explains a useful share of B174's ~300 s is itself a
  measurement, and the honest order is to look for this shape in the real libraries before assuming
  it is the answer.

  **✅ Done 2026-09-20. Measuring one more shape than the row asked for is what found it.** Adding
  *the same comments followed by one real equation* to the comparison gave 22ms where the trailing
  run gives 16,813ms — 764× — which says the cost is not comments and not equation sections but a
  **trailing run in any `(c_comment | X)*` loop**, where the parser scans the whole run at every
  comment to decide whether the loop has ended. `element_list` had it too, so the fix covers all
  three loops rather than the one that was reported. One character in each: `c_comment+`. Measured
  16,813ms → 79ms and 17,933ms → 81ms, both linear.

  **The blast radius the row warned about was real and the gates answered it.** 8,367 files rendered
  before and after with **0 differing**, so the write path is byte-identical; the classifier's whole
  round trip still exact; MSL and Buildings finding counts unchanged. **Reverting the grammar alone
  no longer builds**, because the call sites take an array now — the coupling is its own guard, and
  worth more than a comment saying "do not revert this".
- **Its blast radius is the largest in the phase**, which nothing else here shares: the fix is in the
  grammar, so it changes `ModelicaParser` — the assembly at a >95% coverage bar that the GUI, the
  CLI, the MCP server and every rule sit on. The gate is finding-count parity (MSL = 34329) on top of
  the usual suites, the same one B216 carries.
- **The change, and what is left.** `StyleChecking.CreateUnitLookup` memoised the *answer* and not
  the *question*: `unitCache` is keyed by the id of the class a type resolved **to**, so it holds
  nothing for a type that does not resolve — and resolving is the expensive half, walking the
  class's imports, then every enclosing scope, then the whole extends chain. Memoising `(class,
  type)` took `MissingUnits` from 425.1s to 295.4s on MSL and 477.8s to 310.6s on Buildings, with
  all 5,250 and 3,285 findings identical.

  **Then the profiler, which found the rest of it — and found that one of the rejections was wrong.**
  `dotnet-trace` over the same run: of real CPU, ~43% is ANTLR ATN prediction, so the check is
  *parsing*, and 76% of all non-idle samples have `MissingUnits` on the stack. Printing every frame
  between the two gives the answer in one line — `MissingUnits.VisitComponent_declaration →
  ResolveWithInheritance → CollectAncestors → Walk → EnsureParsed`, **82% of the rule's time**.
  Collecting a class's extends chain reads each ancestor's interface, which parses it when nobody
  holds its tree, and nothing remembered the chain.

  `TypeResolver.AncestorCache` now holds it for the run. **Measured end to end**: `MissingUnits`
  425.1s → 128.7s on MSL and 477.8s → 132.1s on Buildings; whole check 450.9s → 159.5s and 579.4s →
  238.1s; all 5,250 and 3,285 findings identical.

  **The hour this cost is the part to remember.** That same cache was tried early, measured as "no
  change at all", and written off — because the binary under test had not been rebuilt. Measured
  properly it was a 56% cut. **Check the change is in the assembly before believing a negative
  result**, and prefer a stack to a guess: the profiling recipe is now in CLAUDE.md.

  The other rejection stands: a short name resolved through an extends chain is not inherently
  dearer than a qualified one — a synthetic library measured it the other way round.

  **Parse is now the largest remaining phase on Buildings, at 43%**, which is where B235 stops being
  premature and becomes the next thing to take.
- **B190 ✅** — confirm the freeze still happens before investigating it. It predates several fixes.

  **✅ And it does not**, which is the whole value of asking first. The user ran it and reported
  something narrower and far more diagnosable: during style checking the window stops following the
  mouse and jumps to where it was dropped. On Windows that is a message pump that is not running,
  and the desktop host runs its pump on the thread Blazor renders on.

  **What was on that thread**: a check delivers findings a class at a time — 1,397 batches for MSL
  on five rules, median size two — and every batch raised `OnLogMessagesChanged`, each raise
  costing a copy of the whole findings list, a sort of it, a scan for misspellings and a full
  re-render. O(batches × findings) on the one thread that has to stay free. Coalesced now: first
  change immediate, the rest collapsed into one trailing announcement, clearing still immediate.

  **A third round, and the lesson is about the measurement rather than the code.** The user then
  reported dependency analysis taking much longer. It looked like a clear regression — 244s against
  a stable series of 74–95s — and it was not one: those runs were the *separate* deferred path and
  this was the **combined** one, in which style checking is interleaved with dependency analysis
  rather than following it. Like-for-like, combined runs at the same scale took 289.0s and 274.3s in
  August, so today's was the fastest of the three; end to end the combined path finished at 321.1s
  against the separate path's 353.4s on the same library the same afternoon. **The faster path
  reports itself as the slower one** because the step is labelled "Analysing dependencies" while it
  does both — filed as B257.

  **Two comparisons, one of them wrong, and the wrong one looked more convincing.** A series of
  fifteen consistent numbers is exactly what makes an outlier persuasive; the outlier was a
  different operation. Check what the run actually did before trusting what it took.

  **One of the costs was introduced the same morning, by B247.** Adding the filtered count to the
  findings heading put another full pass — with a string split per element — on every render. It is
  now taken only when a filter is set. Worth stating plainly: a per-render cost is invisible in the
  work package that adds it and expensive in the one that measures it, and this package is where
  that shows up.
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
- **B199 ✅** — a node-count threshold above which the plot is skipped in favour of the list, with
  an override for a user who wants it anyway.

  **✅ Done, and the threshold is measured.** A layout settles in 377ms at 250 nodes and 2,149ms at
  2,000 — so cost bites only at the top end, and it is **legibility** that fails first: in a 400px
  panel, five hundred nodes are a couple of pixels each. 500 it is. **The test is weaker than the
  rest of this package**: a rendered assertion was attempted and abandoned because the page runs its
  analysis twice, asynchronously, during initialisation, so the one line of markup that consults the
  limit is uncovered. Recorded in the row rather than glossed.

### WP5 — External tools

**B170 ✅ then B171 ✅, then B259 ✅ and B260 ✅** · 4 items · **complete 2026-09-21**

**Two of the four came from using what the first two shipped**, on the day after they shipped, and
they are the reason this package is worth reading. Neither suite here runs in CI, so every one of
these was found by a person pressing the button: a clean Dymola check reporting that it had checked
nothing; OpenModelica refusing to open a class at all, because it was handed the class's own file
rather than the library's `package.mo`; several seconds of nothing at all after the button was
pressed; and a headless `omc` left running after MLQT closed. Three of those existed before B170 and
one was introduced by it. **A package with no automated coverage is not finished when its tests pass**
— it is finished when somebody has used it.

B170 first: MLQT needs a result dialog of its own, reporting what the tool said, for both Dymola and
OpenModelica. B171's fix — notice a dead session and start a new one — needs somewhere to say so when
it cannot, and that is B170's dialog. The liveness probe goes in `DymolaInterfaceFactory`.

These two suites run in **no CI job** — `build/run-all-tests.ps1` is the only thing that runs them —
so verify by hand on a machine with the tool, and remember that `-CoreOnly` is a decision while
reading past a red line is not.

### WP6 — Revision control

**B193 ✅, B202 ✅, B240 ✅, B264 ✅, B265 ✅, B266 ✅** · 6 items · S–M · **complete 2026-09-21**

**Two of the six were found by using what the other two shipped**, on the same day, which is the
pattern WP5 established and this package repeated. B265 is the one worth remembering: viewing any
SVN history diff failed, and had failed before B202 touched it, because the dialog was converting
a log path into a working-copy path by guessing - and testing the guess against a different root
than the lookup used. B266 came out of writing B265's guards and noticing they would never run:
the suite filter was the substring `Svn`, which hid 281 of 673 tests from every automated run and
a failing test among them.

**B240's decision, for the record, went the other way from both options this note offered.**
Neither a new project reference nor a decoder passed in from the caller: `RevisionControl`
returns the bytes it was given and MLQT decodes them. A version control system stores bytes and
what they mean is the caller's question, so the assembly that knows nothing about Modelica does
not have to learn anything to stop getting this wrong. Doing it turned up the same defect in
Git, which the row had not mentioned because B239's scan was looking for raw file APIs and Git
reaches its copy through `Blob.GetContentText()` — and then the same assumption three paths
wider again, which is **B264** and was deliberately left rather than widened into blind.

B193's real work is not enumerating tags, it is the surrounding UI being able to describe a detached
HEAD rather than showing an empty branch name — and `LibraryBrowser.razor:97` already has a
"Detached HEAD" branch, so that half may be closer than it looks. B202 changes the history diff from
revision-against-working-copy to revision-against-predecessor, which is what the dialog is for; B155
documented the current behaviour, so the documentation changes with it.

**B240 is a decision before it is a change.** `SvnRevisionControlSystem` reads a conflicted file's
`.mine`/`.rN` sidecars with `File.ReadAllText`, so a Windows-1252 library's conflict diff shows
replacement characters where its accented characters are. Read-only and display-only, so nothing is
corrupted — but the fix is not simply to call `ModelicaFileEncoding`, because **`RevisionControl` has
no project references at all**. That is deliberate: it is the one assembly that knows nothing about
Modelica. Reaching the funnel means either giving that up or passing a decoder in from the caller,
and which of those is right is the question to answer first. Found by B239's scan, which is the only
reason anyone knows.

### WP7 — The two large ones — **✅ complete 2026-09-21**

**B188 ✅, B191 ✅** · 2 items · S and L

Deliberately last, and **B191 was confirmed as in scope before it was started.** Marking a model
as modified is small; classifying the *kind* of change needs a comparison of the parsed old and new
class, and that capability is worth well beyond the marker — which is also why it is the one item
here big enough to be its own phase. B188 (a persisted repository order) is unrelated and small; it
is here only because nothing else needs it.

**B188 needed no sort key, and looking for where to put one was the whole of the work.** The list
order already *is* the order everything shows: the library browser renders
`IRepositoryService.Repositories` straight through, `SaveRepositorySettingsAsync` writes the active
project's entries from that same list, and `LoadRepositorySettingsAsync` walks them back
sequentially. So the feature is `MoveRepository(id, delta)` and two arrows. Two decisions are worth
keeping. A move is **saved where it is made**, because the Manage Repositories tab has no Save
button — the Settings page hides its action buttons for that panel, which is a thing to check before
adding a control to it. And a move that would leave the list is **refused rather than clamped**, so
that a refused move can save nothing: a settings write rewrites every repository's
`.mlqt/settings.json`, which is not something to do because an arrow was clicked at the end of a
list.

**B191's deliverable is the comparison, not the marker.** `ModelicaParser/Comparison/` reduces every
class in a file to two strings — a canonical token stream with comments, description strings and
display-only annotations removed, and the class's own source verbatim — and compares them against
the committed version class by class, keyed by full Modelica name so a caller holding a `ModelNode`
can look one up by `Id`. Equal meaning and equal text is `Unchanged`, equal meaning and different
text is `Cosmetic`, different meaning is `AffectsSimulation`, and no committed version is `Added`.

Four things in it were not obvious beforehand:

- **Both strings exclude the class's nested classes**, which appear as a placeholder carrying only
  their name. Without that, editing one class in a package marks every package above it, which is
  the behaviour B191 was raised against in the first place. Renaming, adding or removing a nested
  class still changes the parent, because the placeholder carries the name.
- **A class body's `annotation (...) ;` is a statement of its own**, so its semicolon has to go with
  it. Everywhere else an annotation hangs off something that ends in a semicolon anyway — an
  element, an equation, a statement — and both versions emit that semicolon whether or not the
  annotation survives. Without the special case a class that gained nothing but an icon differed
  from its committed self by a stray `;` and read as a change to what is simulated. The external
  clause's semicolon is **not** that semicolon: it terminates a declaration and is emitted
  regardless.
- **An annotation's elements are a set**, so the kept ones are sorted. A tool that writes
  `annotation(Inline=true, Evaluate=true)` where the file had them the other way round has changed
  nothing, and that is exactly the rewrite a save from another tool produces.
- **The visitor assumes a well-formed tree and says so.** `ClassSignatures.Of` refuses a file the
  parser reported any error on and catches anything the visitor throws, so the failure mode is
  `Unknown` — an answer, in the one direction that cannot mislead. Guarding inside the visitor
  instead would mean signing half a class, and a signature missing an equation reports a real change
  as no change at all. The guards were there first; removing them is what took the file over
  ModelicaParser's 95% bar, and the bar was right to ask.

**Annotations are filtered, not ignored, and the default runs the safe way.**
`SimulationAnnotations` is a list of the display-only names — the drawing, documentation and dialog
ones, plus the few vendor annotations MLQT is prepared to vouch for — and **everything else is
significant**, including every vendor annotation it has not heard of. The two mistakes are not
symmetric: calling a graphical edit significant costs the user a second look at a diagram, while
calling a simulation change graphical hides it from someone who was relying on the marker to tell
them what to read. `absoluteValue` is the borderline case and is deliberately left out of the
display-only list for that reason.

**What the user sees.** The tree marks **M** (orange, affects simulation), **G** (blue, graphical or
documentation) or nothing at all for a class whose file changed but whose own text did not; every
chip now carries a tooltip, which none of them had; the descendant dot is coloured by the strongest
change under it. Above the tree, a **Show** list narrows to Changed / Affects simulation / Cosmetic
only. "Affects simulation" includes anything *not known* to be harmless, unclassifiable changes
included — a filter that hid those would hide exactly what it was asked to find.

**The filter shipped as a dropdown over a flat list and was changed on the same day**, on the
grounds that a chip row shows all four options at once and can carry a count on each — so "nothing
here is cosmetic" is readable without selecting anything — and that a change is worth more with the
packages that contain it than without them. The flat list existed because the tree loads its
children on demand and pruning it looked like it would mean expanding the whole library; the way
round that is that **a filtered tree does not need the lazy loader at all**. The matches are known,
their ancestors come from `AncestorChain`, and the result is small by construction — it is the
uncommitted changes. So the filtered tree is built whole and the `ServerData` callback is withheld
while a filter is on, or expanding a pruned package would fetch all of its children back.

**It opens as far as the user had the tree open and no further**, which was the second thing
reported: it first shipped fully expanded. The two views share one expansion record, so opening
everything for the filter also left the full tree spread out once the filter was cleared — a filter
is a question, not a rearrangement.

**`DescendantKinds` climbed the dotted id and now climbs `AncestorChain` too.** The pruned tree
forced the question, because building one by splitting names would have put a class under packages
that do not exist. That is the mistake B189 already recorded for the reveal walk — a quoted Modelica
identifier carries dots of its own — and it was sitting in the rollup, inherited from the code this
replaced.

**The marker was written out twice** — once per tree template, repository mode and library-only mode
— which is the shape B200 came from. It is now one decision in `ChangeMarker`, asked by both.

**What the marker costs, because it is new work on the startup path.** Classifying a change reads
the committed version of each changed file out of the repository and parses both versions, and
`CheckForUncommittedChangesAsync` runs at startup, after every VCS operation, and on debounced file
activity. Measured on the reported project's own SVN working copy (17,591 `.mo` files):

| | Per changed file |
|---|---|
| `svn info` + `svn cat -r BASE` | **41 ms** — two subprocesses, both local to the working copy |
| Compare, median file (1.9 KB) | **1.9 ms** |
| Compare, p90 file (8.5 KB) | **14.7 ms** |
| Compare, largest file (6.7 MB, 3,932 classes) | **2,230 ms** |

So an ordinary changed file costs about **43–56 ms** and a realistic twenty-file changeset about a
second, on a background thread, once — the result is cached against the file's size and write time
and the repository's revision, so only a file that actually moved pays again. **The number that
would bite is the last row**, and it is one file: changing a 6.7 MB `package.mo` costs two seconds
of classification. Left as it is, and recorded rather than guessed at. Only the kinds are cached,
not the signatures, so the strings that comparison builds are dropped with the call that made them.

**Two of the tests were only true on Windows, and CI is where that showed** — B255's shape, in a
package that had B255 in front of it. `ClassChangeClassifierTests` edits a fixture by searching it,
and two of those searches carried `\r\n`: a raw string literal's endings are the *test file's*, so
on a runner with `core.autocrlf` off the edit matched nothing, the two versions were identical, and
the classification came back `Unchanged`. `LibraryBrowserChangeMarkerTests` wrote its paths as
`@"C:\repo\MyLib\Components.mo"`, which on Linux is one segment, so the file id the fixture built
and the one the browser built from `Path.Combine(VcsRootPath, change.Path)` were different and no
model was ever found in the changed file. **The product was right on both platforms in both cases.**

The fixtures are normalised where they are declared and the paths are built with `Path.Combine`, so
neither test can depend on the checkout again — and the property the fixtures had been leaning on,
that a classification is the same whichever endings a file has, is now a test rather than an
accident. **Verified by simulating the runner**: converting the test file to LF reproduces CI's
message exactly, which is worth saying because B255 records an attempt at the same simulation that
did *not* reproduce and led to a fix being made unconditional instead.

**A class can belong to two loaded libraries, and five callers assumed it could not.** Reported
against `Suspensions.HalfCar.Steering.Experiments.RackAndPinionKinematics`: the browser marked it as
a cosmetic change and the Code Review page opened it with all three diff views disabled. The two
resolve a class differently — the browser goes from the changed *file* to the models in it, Code
Review went from the model to `Libraries.FirstOrDefault(l => l.ModelIds.Contains(id))` — and the
project had the same library twice, checked out as source and shipped encrypted in Dymola's library
folder. **Both entries list the id**; `AddNode` had already resolved the class in favour of source
but the index was never told, so the first claimant was whichever of two parallel loads finished
first. It was the vendor's copy, whose repository is a read-only folder under `Program Files`, so
the class read as not under version control. The log settled it in one line: *737 classes recovered
from documentation; 650 left to the source already loaded for them* — a race, which is why a handful
of classes were affected rather than a library, and why the set moved between runs.
`LibraryOwnership.Owner` is now the one answer, and it asks the graph which copy survived rather
than asking the list who claims it. The index was still untrue, which was **B268** — made true by
WP15, which stopped the two copies being loaded together at all.

**None of it applies to a reference-only repository**, reported by the user as soon as the filter
shipped. The reasoning is the one already written down for that setting — MLQT never formats,
checks, commits or writes to one — and there is a second half that makes it more than tidiness: a
reference repository is **not file-monitored either**, so a marker on one would only ever be
refreshed by loading the project. A stale marker is worse than none. The whole pass is skipped at
`CheckForUncommittedChangesAsync`, which also takes the working-copy query and the per-file
committed-version reads off the startup path for every vendor checkout in a project. This is the
same shape as B200 and as the commit/revert buttons before it: **a promise made in one place and
not carried into a feature added later.**

**Verified by mutation by hand, because the tool would not run.** The phase's ground rule is
verify-by-mutation for UI work, and `build/run-mutation.ps1` refuses to start on this solution —
recorded as **B267**. So the four decisions worth holding were mutated by hand instead and each was
confirmed to fail a test: making every modified class report the plain `M` marker (six tests),
letting the reorder arrows' clicks reach the row underneath (one), and removing the reset that
withdraws the change filter when the changes are committed (one). Each was restored and re-run
green. That is narrower than a mutation pass over the new code and is recorded as such rather than
claimed as one.

### WP8 — Test-harness fidelity — **✅ complete**

**B205 ✅, B212 ✅, B219–B226 ✅** · not a product area, which is why it is its own package

None of WP2–WP7 is a place for it: they are areas of the application, and this is about whether the
suite can tell the truth about them. It is grouped here with the standing pattern below rather than
squeezed into whichever package happened to be open.

**B205 ✅.** There were three settings doubles; two round-tripped through JSON and the most-used one
stored objects by reference. There is now **one**, in `TestSupport/`, shared by both test suites — and
a `SettingsServiceContract` that the double, the desktop `JsonSettingsService` and the MCP server's
`HeadlessSettingsService` all derive from, so they are held to the same storage semantics rather than
trusted to agree. That is the durable part: a double is only useful while it behaves like the thing
it stands in for, and nothing was checking that.

Running the suite afterwards was the point, and it produced **no failures** — nothing had come to
depend on the aliasing. Worth saying rather than leaving implied: the swap was cheap insurance, not a
bug hunt, and the defect it *prevents* is still the one described above, where
`LoadRepositorySettingsAsync` adds a "Default" project it never saves.

**B212 ✅ — `build/run-mutation.ps1`.** A source-scanning guard was attempted first and abandoned.
The obvious rule is "every assertion is behind an `if`" — and it would not have caught the test that
started all this, because `LoadModelicaFile_UnparseableContent_...` had a real unconditional
assertion *and* a conditional block hiding the defect. A scanner written to that rule flagged 62
methods, essentially all environment guards on tests needing a git or SVN working copy. The shape
needs semantics, not syntax.

Mutation testing is the mechanically sound answer, and Stryker.NET does work here — which the tool
itself denies. Every test project is xUnit v3, which *is* Microsoft.Testing.Platform, so Stryker's
VSTest default fails with "not yet supported by Stryker, see issue 3094" listing every test project.
That reads as *this repository cannot be mutation tested*. **`--test-runner mtp` is in `--help` and
not in that message.**

**It found two real gaps on the first file it was pointed at**, both in code written earlier in this
phase: `ProjectNameRules.IsAvailable` had no test at all — inverting it to `is not null` killed
nothing — and the "Enter a name for the project." message could be emptied unnoticed, though the
whole reason `Validate` returns a message rather than a bool is that the screen and the service must
say the same thing. Both fixed; that file is now at 100%.

**A report, not a gate**, and scoped: about three minutes for one file, hours for an assembly, so
`-Mutate` is effectively required. A score is not a number to chase — pointed at `StandardCHeaders`
it reports survivors for individual header names in a literal list, which is exactly the equivalent
mutant nobody should write a test for.

**Deliberately not scheduled in CI.** A nightly run costs hours of runner time, and B143 is the
standing evidence that a scheduled job written blind fails the first time it matters. It is a tool to
reach for when a suite's honesty is in question — after a fix, before trusting a guard — rather than
something to run at 02:00 and stop reading.

**The script's own first version had the bug it exists to find.** Given a filter matching no file,
Stryker reported every mutant as `Ignored` and the script printed "no surviving mutants" — a clean
pass over nothing at all. It now fails loudly unless mutants were actually *executed*, because the
first fix counted every mutant rather than only the ones that ran, and still passed.

**The standing pattern this package exists to keep visible.** Six tests in this phase asserted
something they could not see, and every one was found by accident rather than by looking:

| Where | What it did | How it surfaced |
|---|---|---|
| `LoadModelicaFile_UnparseableContent_...` | assertions inside `if (placeholder != null)`, with a comment excusing the null case | B201 — the excused case *was* the defect |
| `StartCheckingAsync_WhenAlreadyRunning_...` | second call attempted only `if (service.IsRunning)`, closing on `callCount >= 1` | a coverage figure that moved by one line between runs |
| `ExtractLoadResource_WithConcatenation_NotSupported` | asserted the defect *as behaviour* — two resources from a concatenation — while its own name said NotSupported | B210, when the behaviour was corrected and the test failed |
| my first B168 suite | asserted state that was already true of the unfixed code | the mutation check |
| my first B200 test | asserted MudTooltip text, which is never in the markup | the positive control |
| my first B211 suite | looked a header up by file name and asserted on whichever node came back — and the defect's whole shape was **two** nodes of that name, one of them fine | the mutation check |

Four of those are mine, which is the point: writing the test after the fix makes it very easy to
assert the behaviour you just built rather than the one that was missing. **A test that cannot fail
is worse than no test**, because it is counted.

Three sub-shapes, and they need different defences:

- **A conditional assertion** (`if (x != null) { assert }`) — the excused branch is usually the
  defect. Suspect any `if` wrapped around an assertion.
- **An expectation copied from the current behaviour.** `..._NotSupported` pinned the bug in place
  and its own name admitted it. When a test fails after a fix, ask which of the two is wrong before
  changing either.
- **Reading state the defect duplicates.** B211 produced two resource nodes for one file — one
  missing, one fine — so a test that fetched "the" node by name found whichever came first and
  passed either way. **Assert the count, not the contents**, whenever the failure mode is a
  duplicate rather than a wrong value.

### What the first whole-solution run found — 2026-09-19

`-All` over all seven measured assemblies, 5h 40m:

| Project | Mutants run | Killed | Survived | No coverage | Score |
|---|---|---|---|---|---|
| ModelicaParser | 6,361 | 5,292 | 1,069 | 0 | 83.2% |
| ModelicaGraph | 2,354 | 1,738 | 616 | 0 | 73.8% |
| MLQT.Cli | 1,340 | 905 | 435 | 0 | 67.5% |
| MLQT.McpServer | 2,631 | 1,421 | 851 | 359 | 54.0% |
| RevisionControl | 1,561 | 817 | 406 | 338 | 52.3% |
| MLQT.Services | 3,714 | 1,922 | 1,792 | 0 | 51.8% |
| MLQT.Shared | 5,673 | 693 | 821 | 4,159 | 12.2% |

**Two of those numbers mean nothing and have to be said first.** `MLQT.Shared`'s 12.2% is its
`NoCoverage` column: MainLayout at 0%, CodeReview at 0.6%, the VCS dialogs at 0% — every one of them
already in `coverage-baseline.json` with a reason, because they need a render tree or a working copy.
Mutation testing re-reports the coverage ratchet's ledger there and adds nothing. And `ModelicaParser`
scoring highest is the bar working as intended: it is the assembly held to 95%.

**The real column is `Survived`** — covered code the suite runs and does not depend on, which is the
one thing coverage cannot see. 5,990 of them, and reading by hand is hopeless, so the useful question
was *which survivors sit on a line this repository has written a comment to defend*. Grepping the
survivor list for `IsExternalStub`, `ReferenceOnly`, `Encrypted`, `Excluded`, `IsDiagnostic` and
`Suppress` answered it in one pass and produced **B219-B223**. A second pass asked a different
question - *which sit on code that writes to a user's files or their remote* - and produced
**B225-B226**. All closed; what neither question reached is WP9.

The answer is uncomfortable and worth stating plainly: **the invariants documented most carefully
here are the ones with no test behind them.** `LibraryCheckSession.cs:44` carries three lines of
comment explaining why a stub must never be judged, and inverting it kills nothing. So does
`GraphAnalysisContext.cs:47`. `FormattingPipeline.cs:144` excludes encrypted libraries from the full
save — a write into one would be corruption — and nothing notices its removal. The comment was
treated as the safeguard.

One finding is not a missing test at all. `ImpactAnalysisService` survives 121 arithmetic mutations
because it computes an SVG layout — jitter, force relaxation, clamping — that **nothing reads**:
Cytoscape lays the graph out itself. The right answer there is to delete it (B222), and the only test
touching it asserts `SvgWidth >= 700` against a `Math.Max(700, ...)`, which is the second sub-shape
above, found by the tool rather than by accident this time.

**Three of the eight closely investigated survivors were equivalent mutants**, and reading the exact
replacement rather than the line number is what showed it: `GraphAnalysisContext.cs:47` forces a
filter over a list with nothing to filter, `FormattingExclusion.cs:57` and `CoverageDimensions.cs:147`
are short-circuits in front of a slower answer that agrees, and `LibraryCheckSession.cs:44` is the
first of three stub filters so removing it changes no observable result. The first write-up of
B219 and B220 claimed all of these as gaps. **A survivor is a question, not a defect** — and the
cost of the other reading is tests that assert nothing, which is what this package exists to stop.
Each is now noted in the code so a later audit does not re-raise it.

**The audit also damaged this repository while running, which is B224** — mutating a path in
`RepositoryService` to `""` made git fall back to the process working directory, and
`RepositoryServiceTests` created branches in the MLQT checkout, committed to one and discarded the
working tree, twice. Any mutated code doing file or VCS work can act on the repository the run
started in. `SandboxedId` now proves a test repository is under `Path.GetTempPath()` before any
operation names it.

The defences that have actually worked here, in that order: a positive control beside every negative
assertion, a mutation check before believing any guard, and measuring on a sample that is known to
contain the failing case — B169's first fix was verified on real data holding none of it, and looked
complete because the measurement agreed.

### WP9 — The test debt deliberately left

**B267 ✅, B228 ✅, B229 ✅, B227 ✅** · opened by WP8's audit, and held back from it on purpose · plus
**B234 ✅** and **B237 ✅** from WP2, and **B256 ✅** which was the only dated item in the phase - taken first and
out of order because of that, four weeks before the date it would have fired on · **and B274 ✅, B275 ✅, B276 ✅**,
which the re-run campaign opened once B267 had made its numbers mean what they say

**The package reopened on 2026-09-22 rather than closing.** Everything above shipped, the campaign
was re-run to see what was left, and it named two groups that belong here rather than anywhere
else: they are the same kind of work, found by the same tool, and the alternative was a package of
two items or a pair of orphans. B274 first — it is the one with no reason behind it, and it sits on
code that deletes lines from a user's files. B275 second, because half of it genuinely cannot be
tested without a server and the work is deciding which half. B276 arrived from doing B274 and
closed with it as a third: the same question asked by hand in ten more places, and in two of them
around a helper that was already sitting in the same file.

WP8 asked one question of the 5,990 surviving mutants — *which of these sit on a line this
repository has written a comment to defend?* — and then a second — *which sit on code that writes to
a user's files or their remote?* Those produced B219-B226, all closed. What is left is everything the
two questions did not reach, and it is not a backlog of defects: most of it is equivalent mutants and
code nobody should test. Four groups are worth a deliberate pass, in this order.

**B267 before any of them, because nothing else here can be finished without it.** `run-mutation.ps1`
no longer reaches the mutation phase on this solution: Stryker runs every test project that
transitively references the mutated assembly, which for `ModelicaParser` is all of them including the
browser journeys, and aborts on their failure. Found doing WP7, where the guards had to be mutated by
hand instead. **This is a prerequisite, not a peer**: B227 and B228 are lists of surviving mutants,
and every closure in B219-B226 was recorded as *"mutant confirmed dead"* — which is a claim the tool
has to be able to make. Do it first, or the rest of this package cannot be checked, only argued.

**Done, and the fix was not either of the two things the row proposed.** A solution cannot be
taken away by argument, only by not standing where it can be found - Stryker walks up from its
working directory. Running it from the test project's own directory finds no solution, takes the
single-project path and uses the suite it was started from: *Analyzing 1 test project(s)*, 2,078
tests rather than 5,790, and a score in 2m32s where it had been aborting after 3m30s. **The rest
of this package is now checkable**, which is what it was holding up.

**B228 ✅, and it was small and finishable.** Four survivors in `ModelicaFileEncoding`, of
which the real one drops a byte-order mark from a file MLQT was asked to preserve. An afternoon,
and it settles whether the encoding round trip — which CLAUDE.md singles out as progressive
corruption when it goes wrong — holds under its own tests rather than under inspection.

**The first thing it settled was that the row was out of date**, which is what re-reading the
survivors with B267's fixed tool showed: two of the four named were already dead or misread, and
the real gap was one line further in. `DominantNewline`'s counting loop had **no coverage at
all** - nothing tested `ForFile` directly, only through the encoding tests, which never vary the
line endings. 36 survivors before, 26 after. **Read the survivors, not the row about them**: a
backlog entry written from an audit is a snapshot, and the tool is the source.

**B237 ✅, and both causes were in the harness.** Every journey's fixture library is called `Lib`,
so they all produce the same class ids, and nothing ever took one back out of the shared service —
a class resolved to the first journey's node, whose file had been deleted with its fixture. And
**no journey ever closed a page**: every open page is a live circuit while `AppState` is a
singleton shared by all of them, so a stale page went on reacting to events raised by a later one,
`CodeReview.OnModelSelected` ran on the raising circuit's dispatcher, and the render call killed
circuits nothing was looking at. `ResetLibrariesAsync` and a `NewPageAsync` that closes the page it
handed out last are the two fixes, and `CodeSearchJourney` is committed behind them.

**B271 was filed against `CodeReview` and withdrawn.** It is the same thing seen from the stack
rather than from the harness, and the desktop host has exactly one circuit so none of it is
reachable in the product. Worth saying because the row set itself the right question — *is this
reachable?* — and was written before answering it. A stack trace says where a thing failed, not
whether a user can get there.

**The suite is faster for it**: 75 tests in 59s where 71 took 2m05s. The leaked circuits were
costing time as well as reliability, which nothing had attributed to them.

**B229 then, because it is the largest gap and the reason is structural.** ✅ — and the structure
was not the one named here. The paragraph said these services' suites no CI job runs; in fact
`MLQT.Services.Tests` runs on every push, and it is `DymolaInterface.Tests` and
`OpenModelicaInterface.Tests` that need a tool. What kept the services untested was that both
factories returned the **concrete session class**, whose every method ends in a round trip to a
running tool — so nothing past the first call was reachable, with or without a machine that had one.
Extracting an interface for the handful of calls the services make took 11% to 73.6%/73.5% and 176
uncovered mutants to 13, and the tests found a real defect in both tools on the way (B272). **The
third premise in this package to be wrong in the same direction**, after B228 and B234: each
described the obstacle as something no automated run could reach, and each turned out to be
something nobody had looked at.

**B227 last, and only with a sampling plan.** ✅ — and the sampling plan was the whole of it.
`build/survivor-map.py` groups a report's survivors by method, and the map said the four largest
groups held 84 of 272. Two had a specification to test against and were taken; the largest did
not and was left, deliberately, for the reason the paragraph below gives. The score moved 85.38%
to 86.01%, which is the honest size of what was there to find. Two real defects came out of it
(B273, and six dead arguments), and the reason the file-shape group was unguarded turned out to
be the harness deleting the line before asserting rather than anybody forgetting.

The original plan, for the record: 361 survivors in `ModelicaRenderer` at an 85% kill
rate: the best-tested large thing here and still the biggest absolute count anywhere. Read in groups
— indentation state, section ordering, annotation placement, line breaking — and decide per group
whether the behaviour is specified anywhere at all. **The failure mode is specific and likely**:
361 survivors read end to end produces tests that assert the renderer's current output instead of
its contract, which is the "expectation copied from behaviour" sub-shape in WP8's table, at scale
and self-inflicted. If a group has no specification, the honest outcome is to write one down or
leave the survivors alone — not to freeze today's bytes.

**What this package must not become.** A mutation score is not a target, and WP8's own findings are
the argument: of the eight survivors it investigated closely, three were equivalent mutants and one
was dead code to delete rather than test. Half of a careful pass over this material produces no
tests at all. Record what was read and judged equivalent, so the next audit does not re-raise it —
`LibraryCheckSession.cs:43` and the saver's exclusion branch already carry that note in the code.

**B256 is the one item in this phase with a deadline, and it should be taken first for that reason
alone.** `ubuntu-latest` becomes Ubuntu 26 on 19 October 2026, and six jobs across three workflows
ride that floating label. It belongs in this package rather than anywhere else because it is the
same shape as B229 and B234 — a rehearsal that stops being possible, rather than a test somebody
has not written. Playwright ships **no** browser build for 26.04, and the override that rescues
Chromium does not rescue WebKit, whose build wants `libicu74` and `libvpx9` that the release does
not have. `run-all-tests.ps1` already handles a too-new Ubuntu and no workflow does, which is the
gap. **The nightly WebKit job is what is really at stake**: it exists because WebKitGTK is what the
Linux desktop host runs on, so losing it means the Linux GUI is rehearsed nowhere at all. The
failure arrives on a date rather than on a commit, so left alone it lands on whatever push happens
to be next and reads as that change having broken something.

**B234 belongs here for the same reason B229 does**: a group of survivors that no test *could* kill
as things stand. The classifier's emit-loop mutants survive because the only assertion strong enough
to catch them — `RoundTripsOverAWholeLibrary`, exact over 8,367 files and 1.1M lines — is opt-in
behind `MLQT_FIDELITY_CORPUS`, so neither CI nor `run-mutation.ps1` runs it. Three ways out and they
are genuinely different decisions: commit a corpus big enough to exercise the loop (the question is
which *shapes* — multi-line tokens, skipped characters, text `PreprocessCode` tidies — not volume),
have CI fetch a library, or accept it as a manual gate and put it in the release checklist. Until
one is chosen, **a green suite says nothing about fidelity** unless someone remembered a variable,
which is the whole complaint.

**B237 is the other half of the same problem at the journey layer**, and the item to take first here
because the other packages are waiting on it: a class cannot be opened in the shared journey host
once other journeys have run, and nobody knows why. B176's wiring is what noticed it, but the
finding generalises — `ResizablePanesJourney` hits the same wall the moment it needs a class, and so
will every journey the remaining packages want. The useful outcome is the diagnosis, not the one
journey; a host of its own is the fallback, not the answer.

**What WP2 taught this package.** Both items arrived because a check that exists is not a check that
runs. The mutation report could not see either one — it reports on the tests it ran, so a test that
never runs and a test that does not exist are the same entry. That is worth stating as the shape,
because it is the third variant of a defect this repository keeps producing: a guard whose existence
is mistaken for its enforcement.

### WP11 — Code Review, second pass

**B250 ✅, B253 ✅, B254 ✅, B247 ✅, B248 ✅, B249 ✅, B233 ✅** · **complete 2026-09-20** · 7 items · S each · all from using it

Everything here was reported by someone working in the page rather than found by reading it, which is
why they are together: WP2 rebuilt what the Code Review page *shows*, and this is what a fortnight of
using the result turned up. None of them is large, and none of them needs anything the page does not
already have.

- **B250 ✅ first** — a page that scrolls as a whole, taking the class name with it, is the one of
  these that makes the page harder to use rather than merely rougher. It is the double-scrollbar
  shape one level out, and `ResizablePanesJourney` is already driving the splitter that provokes it.

  **✅ Done 2026-09-20, and the row's guess at the cause was wrong** — it was not a panel being
  resized. The viewer's height was `calc(100vh - 185px)` and its top measures 173px, so the page
  came to `100vh - 12px`: not a margin, the absence of one. The parse-error alert is 34px. **What
  made it findable was that three pages carried three different constants for the same distance**
  (185, 140, 135, and 210 for a fourth panel) — a number nobody can keep equal to the sum of five
  things they do not control. All four are gone; the chain from the tab pane down is a flex column
  and each page fills what is left. The fix `.mlqt-findings-pane` already documented, one level out,
  in the words its own note used: *a flex calculation rather than a subtraction*.

  **Two things about this package's method, both borne out here.** The first is that the browser
  answers questions reading cannot: the tab host's stack was already 8px taller than its parent and
  nobody could have known, because `MudExSplitPanelItem`'s `overflow: auto` was absorbing it. The
  second is that a layout fix needs mutation like any other — reverting it and inserting the real
  alert shows the viewer holding its 535px while the page grows, which is the defect, and no
  amount of looking at the rendered page says that.
- **B253 ✅** — done, and worth reading as a method rather than as a fix. **Three explanations, two
  of them wrong**, and each wrong one was arrived at by reading code that looked expensive rather
  than by measuring: a redundant VCS query on the UI thread, then the scroll-position interop. Both
  changes were worth keeping and neither was the cause. What found it was timing the step and then
  following the gap: the eleven seconds was `BaselineStatusService` refreshing **synchronously** on
  whoever raised the file-activity event, which MLQT's own writes do. The throttle's leading edge
  ran inline while everything inside the window was queued — which is exactly the reported "slow
  once, then fast twice" and is a shape worth recognising again: **an intermittent cost that tracks
  an idle period is a throttle or a cache, not the work in front of you.**
- **B254 ✅** — done, and it is the opposite method to B253's: one reading of the grammar answered
  it. `statement : component_reference (':=' expression | function_call_args)` — the reference is a
  call in one form and an assignment target in the other, and the statement path answered "call" for
  both while the equation path beside it had always asked. Worth recording because the classifier had
  **deliberately reproduced** it, with a comment naming the case: B213 built the classifier to match
  the renderer token for token, so a defect in the baseline became a defect in the thing users see,
  and the agreement measurement could not tell the two apart. Fixing both is what keeps that
  measurement meaningful.
- **B247 ✅** (how many findings are showing) is the smallest and repays the most: three filters now
  narrow that table and nothing says what they did.

  **✅ Done 2026-09-20. Four filters, not three** — the baseline toggle is one too, and it was the
  only one the heading already accounted for. Stated precisely, the defect is that the heading was
  computed from one set of findings while the table was filtered from another, so it now counts
  with `FilterFunc1`, the same predicate the table uses, and cannot drift from the row count.

  **Two things worth carrying forward.** The wording work went into a pure `FindingsHeadingText`
  with its own tests, because the interesting part is which of six sentences applies rather than
  the arithmetic — but a pure function being right about numbers it was handed says nothing about
  whether it was handed the right ones, which is exactly the fault. That needed three journey
  tests, verified by putting the old count back and watching all three fail. And the docs were
  stale on two counts beside this one: `code-review.md` described **two** filters where there are
  four, and said search terms are "matched independently" when B187 made them AND. A page that
  grows a control per work package grows a documentation gap per work package unless somebody
  looks.
- **B248 ✅** and **B249 ✅** are placement. They belong together because they are the same judgement
  made twice — a control's parts scattered along a toolbar — and because moving the navigation
  arrows for B249 is what makes its tooltip true.

  **✅ Done 2026-09-20, and taking them together earned its keep within the hour.** The fix for B248
  put the find-in-code field, its count and its arrows in a stack of their own with
  `AlignItems.Center` — which stops flex children stretching, and the toolbar's buttons are sized by
  stretching to the row, so those two arrows came out 22px against 27px everywhere else. That is
  **B249 reintroduced by the fix for B248**, and B249's own test is what caught it.

  **The measurement contradicted B249's row**, which is the second time in this package: "Go to
  class" was 32px where its neighbours were 44 — shorter, not larger — and the mismatch was specific
  to `Size.Medium` on a `MudMenu`, which renders 32 where a `MudIconButton` renders 44. The user
  chose to shrink the whole toolbar to `Size.Small`, which is CLAUDE.md's house style and what the
  rest of the application already uses.

  **Moving the arrows overturns B197, and costs something real.** The history is one history and
  every tab moves the selection, so Dependencies, External Resources, Metrics and Settings now offer
  no way back. That was raised before it was done and chosen knowingly; both `CurrentModelDisplay`
  and the toolbar carry a note saying so and naming the other direction — bringing the used-classes
  menu up beside the class name — if it turns out to bite. **A documented decision is a reason to
  ask, not a reason to refuse**, and writing the cost down where the next reader will find it is
  what makes reversing it cheap.
- **B233 ✅** was outside the phase until 2026-09-20, on the grounds that nobody had said the inline
  annotations mattered. Somebody has: a `connect(...)` equation carries its annotation on the same
  line, so hiding annotations does nothing to an equation section, which is where the noise is. The
  measurement it asks for is still the first step, but it is now a measurement over equation lines.

  **✅ Done 2026-09-20, and taking the measurement first changed the work.** Over 8,367 files:
  31.6% of non-blank equation-section lines carry an annotation and **62% of those were left
  visible**. That is what made this worth an M rather than a ⭐ — and it is now 0%, with 100% of
  116,644 annotations hidden against 65.9% before.

  **The implementation is not the one the row predicted**, and that is the reusable part. The row
  assumed markup had to be spliced, which needs the classifier to say where each token's markup
  begins. But *removing an annotation from Modelica leaves Modelica*, so the text can be cut and the
  caller simply colours what comes back — one more parse, and no second representation of where a
  token starts. Worth remembering the next time something wants to modify what is displayed: ask
  whether the change can be made upstream of the colouring rather than in it.

  **And the corpus found a defect no small test would have.** The continuation lines of a
  multi-line annotation were reported as elided but left in the returned text, so a caller that
  parsed it got the orphaned tail of an annotation whose head had gone: **3,199 of 8,367 files
  stopped parsing**, which in the viewer is a silent drop from parse-tree colouring to lexer-only.
  Every unit test passed. The check that found it was added because "100% hidden" is trivially
  achievable by destroying the text — **a number that can only go one way needs a second number
  beside it that can go the other.**

### WP12 ✅ — Rules, second pass

**B246 ✅, B245 ✅, B252 ✅** · 3 items, all done 2026-09-22 · S–M

**✅ The package is done — 2026-09-22 — and the gate was worth having, again.** Two of the three
were judged by running them over a whole library rather than by reading, and each time that found
something reading had not: B245's parity run turned up `Modelica.Icons.Package`, a second real case
of the same bug that the item did not name, and B252's produced the only number that makes that item
safe to ship (0 findings after formatting, against 5,760 before). B246's own row named two gaps and
the work found two more underneath them, both in the same place — how a reference is *collected* —
which is the shape this package should be remembered for: **all three items read as being about
rules, and two of the three were actually about the layer below.**

Three rule items found by pointing the checker at a real library and then working in it, which is the
only way any of them would have been found. They are separate from WP3 because that package shipped,
and separate from each other except in kind — though **two of the three change the write path**, so
they share WP3's gate and should be taken together rather than a month apart.

- **B246 ✅ — done 2026-09-22, and it was not small in effect.** `MLQT.Structure.UsesUndeclared`
  reported Modelica's own built-ins — `Connections`, `ExternalObject`, `rooted` — and the graphical
  primitives inside annotations, which is why **every** library reported a missing `uses(Line)`. A
  rule that fires on every library is a rule nobody can leave on, so this was the difference between
  the rule being usable and not.

  **Neither gap was in the analyzer.** Both are in how a reference is collected, which is why
  reading the rule could not find them: `ReferenceResolver` binds a simple name to any node it
  happens to find, and nothing told it that annotation content is not code. The fix is in two
  places — `ModelicaLanguage`, the language's own names written down once and checked *before* the
  node lookup, and an annotation gate in `ModelAnalyzer` that keeps walking the subtree (external
  resources and `loadSelector` parameters live there) while recording nothing from it, bar
  `derivative`, `inverse` and `choices`, which do name classes.

  **Two things fell out that this package did not know about**, and both argue for taking the other
  two items with the same suspicion of what the *collection* does rather than what the rule says:
  the `uses(...)` annotation was itself counted as a reference to every library it declares, so
  `MLQT.Structure.UsesDeclaredUnused` **could never fire on a root package** — dead, not quiet; and
  the built-in list compared case-insensitively though Modelica is case-sensitive, so a component
  typed `Sum`, `Max`, `Abs`, `Sign`, `Sqrt`, `Sin`, `Cos`, `Exp` or `Log` — every one a real
  `Modelica.Blocks.Math` class — was silently dropped from the graph.

  The row's own instruction to *check what the sibling rules do with the same names* found three
  copies of the predefined-type list, each different. They now derive from one, with
  `TypeResolver`'s extra `Complex` — an MSL operator record, not a language type — stated as the
  difference it is rather than kept as a second list.
- **B245 ✅ — done 2026-09-22.** A change to the **write** path — which children the saver is
  willing to store as separate files — so it carried WP3's gate: a full-library save compared before
  and after, and the parity number.

  **The larger half of it was not in the row.** `SingleFilePackageAnalyzer` held its own copy of the
  rule, commented *same rule ModelicaPackageSaver writes by*, and it was the same wrong answer
  twice — which is precisely why the rule could stay quiet about MSL's four pairs and nobody asked
  whether the refusal was necessary. Both now ask `ModelicaGraph/PackageFileLayout.cs`, which
  compares the **directory entry** each child would be written as rather than its class name.

  **The gate earned its keep again, twice.** It found a second real case the row did not name —
  `Modelica.Icons.Package`, a package refused because the check for `package.mo` was also on the
  name rather than the entry — and the first parity run came back *no difference*, which was the
  Release CLI not having been rebuilt. That is B174's trap verbatim, and it is now two for two:
  **rebuild before believing a negative result.** On MSL (6,489 classes) nothing was lost, 54 paths
  were gained, 6,382 of the 6,384 common files were byte-identical, and of 5,296 findings exactly
  one message changed.
- **B252 ✅ — done 2026-09-22.** It extends B181 from one boundary to four. **The order was the
  first question, not the code**, and it was settled before anything was built: **one fixed order**,
  inputs and outputs → constants → parameters → variables → components → classes. Not configurable,
  because the formatter has to be able to produce whatever the rule asks for and one order it can
  always produce is worth more than a setting almost nobody would change.

  **A new rule, not a wider one.** `MLQT.Style.DeclarationOrder` sits beside
  `ComponentsBeforeClasses` with that rule as its prerequisite, rather than redefining it: MSL
  reports 3,246 of the new findings, and a repository that had the coarse rule switched on must not
  acquire them silently, with a baseline full of drift, because MLQT changed its mind about what the
  id meant.

  **The thing that actually needed care was the agreement.** `DeclarationKinds` is one classifier
  asked by both the rule and `ModelicaRenderer`, and the renderer is handed the *same* type lookup
  the checker gets — including the class-id tracking that resolves a nested class in its own scope,
  because the rule checks that nested class on its own. Without a graph both fall back to the
  predefined types alone. Either way the two give one answer, which is the only property that makes
  the pair safe: a finding the formatter does not clear is worse than no rule at all, and there is a
  test that formats a class and then checks the result.

  **Gate, on MSL.** With the option off, all 6,438 written files are byte-identical to the previous
  build — the refinement is genuinely inert until switched on. With it on, 724 files are rewritten,
  and a check over the formatted tree reports **0** ordering findings where the library as shipped
  reports 5,760. That number is the gate for this item: every finding the rule raises is one the
  formatter clears. The part
  that is not obvious is telling a *variable* from a *component*: `Real x` against `Resistor r` is
  easy, but `SI.Length x` is a variable by every convention and a class by the grammar, so the
  declared type has to be followed through its alias chain. `MissingUnits` already does that and is
  the precedent. And the renderer writes all four kinds as one group, so it changes too — which is
  why this sits beside B245 rather than on its own.

### WP10 — MCP, whenever ✅ complete 2026-09-23

**B179, B196, B218** · 3 items · S–M · no dependency on any other package

These were "not in any package" until B218 joined them from WP2 and made three. That is the point of
giving them a number: an item filed as separable-at-any-point is an item nothing ever schedules, and
WP2 has just demonstrated what that costs. None of the three depends on the others.

- **B179** returns what is already sitting on the synthesized stub (description, base classes,
  parameters, connectors, inputs and outputs), marked as recovered from documentation rather than
  read from source and still declaring the class not editable (B85).
- **B196** is a question about a dependency — which SVG-to-PNG conversion to take on — before it is
  a question about code.
- **B218** is the one with a prerequisite, and it is already met: with `includeAnnotations: false`
  the tool re-renders through `ModelicaRenderer`, so an agent reads text that is not the file and
  whose line numbers do not match the findings the same server reports. B214's elision now strips
  the annotations without touching anything else, so this is a swap of one call for another. Take it
  first — it is the smallest and it makes the server internally consistent, which the other two
  assume.

**What the package found that its three rows did not say.** Each item turned out to rest on a claim
about existing code that was not true, and in each case the untrue part was the work:

- B218 was "a swap of one call for another", and it was — but the elision was built for the viewer,
  which never parses what it elides, so it takes the semicolon after an annotation away with it. That
  semicolon belongs to the annotation only in a class body; under a declaration it closes the
  declaration. `elidedTextMustParse` is the named difference between a caller that displays the
  result and one that hands it to somebody who will edit it.
- B179 said the members were "already sitting on the synthesized stub". They were not: the parser
  recovered them and `ExternalStubBuilder` dropped them, and nothing in the repository had read a
  `DocumentedMember` since the day it was written. They could not simply join the stub either — a
  declaration needs a type and the generator publishes none — so this is the one stated exception to
  "synthesize source, never metadata", and it is stated because the reason is specific: there is no
  truthful declaration to write.
- B196 said "the SVG is already produced". What is produced is one class's *icon*; composing a
  diagram out of the icons of its components, at their placements, with the lines between them, was
  the larger half of the item and the dependency question was the smaller one.

**The lesson is the one the package was created to demonstrate.** All three had sat unscheduled as
"separable at any point", and all three carried a sentence asserting that most of the work was
already done. Nobody had checked, because nothing schedules an item filed as separable.

### WP13 — Performance, second pass — **✅ complete 2026-09-24**

**B261 ✅, B257 ✅** · 2 items · M and S · **the measurement exists; the explanation does not**

**B257 is here because WP4 closed without it.** It was filed from WP4's own measurements — the
combined analysis path labels its step "Analysing dependencies" while it also style-checks, so the
faster of the two paths reports itself as the slower one — and it was named in that package's prose
and in no item list. WP4 then shipped. It is small, it is the same subject, and a label that
misreports which path is faster is worth fixing in the package that goes looking for where the time
goes.

WP4 closed with the reported symptom fixed and one number left standing: with the unit-resolution
work in, **coverage measurement is 69% of a check at 133 ms/class** — larger than any single rule
and larger than parse. That is a second root cause rather than the tail of the first, which is why it
is a package and not a line in B174.

**Its gate is WP4's gate**, and for the same reason: B174 rejected two hypotheses by measurement
before the profiler found the real mechanism, and one of those rejections was itself wrong because
the binary had not been rebuilt. So re-measure with `--timings` first, record the library and the
build the figure came from (B261 carries the number without them), and reach for `dotnet-trace`
before the second guess rather than after it.

**One hypothesis is already visible in the code and is worth stating so nobody re-derives it**:
`UnitResolver.Resolve` takes an optional `TypeResolver.AncestorCache` and `CoverageMeasurer` passes
none, so measuring a class re-parses its ancestry the way `MissingUnits` did before B174 — which was
worth 3x there. It is a hypothesis, not a plan: the same cache was measured as "no change at all"
once already.

**What was done (2026-09-24).** The gate first. The 69% was **Claytex** — 21,673 classes, from the
application log of 2026-09-22 — and it reproduced with the CLI on the current build before anything
was changed: the whole `ModelicaLibrariesTrunk/Modelica` repository with the Dymola 2026x Refresh 1
library folder on `--dependency`, coverage **2,901.9s (66%)** of 4,374.5s thread-time. The same table
held the control that made a profiler unnecessary: `rule:MissingUnits` asked the same unit question
about the same components for **389.1s**. So the hypothesis held, and for the reason stated — the
measurer had neither of B174's caches. `StyleCheckContext` now builds one unit lookup and gives it to
the rule and the measurer both, so each `(class, type)` question is resolved once between them.

| Claytex, same build and settings | Before | After |
|---|---:|---:|
| coverage (thread-s) | 2,901.9 | 323.6 |
| all measured work (thread-s) | 4,374.5 | 1,542.6 |
| wall clock | 6m51s | 4m21s |
| findings | 104,643 | 104,643, identical |

Every coverage figure is identical as well; the two metrics snapshots differ only in their timestamps.

The question B261 left about `CoverageDimensions.ForClass` was answered by timing it rather than by
reading it: 0.34µs a call on Claytex's class ids and settings, 7.4ms for a whole check. It does no
per-class work that belongs per repository.

**Then the profiler, because coverage was still ~15ms a class.** `dotnet-trace` over the same run:
59% of what remained was `OneOfEachSection.VisitComposition`, called by the layout measurement, and
`RuleContext.GetText` was **30% of all non-idle time in the whole check**. Four loops over a class
body's children found the `public`/`protected`/`external` keywords by asking every child for its text,
which for an element list or an equation section rebuilds that child's whole source — and an outer
class contains its nested ones, each walked again in turn. `SectionKeyword.Of` reads keywords from
terminals only, and all four use it.

| Claytex, same build and settings | After B261 | After `SectionKeyword` |
|---|---:|---:|
| all measured work (thread-s) | 1,542.6 | 1,088.7 |
| coverage (thread-s) | 323.6 | 161.2 |
| `OneOfEachSection` + parameter descriptions + naming (thread-s) | 410 | below the top ten |
| wall clock | 4m21s | 4m12s |
| findings / coverage | identical | identical |

**The wall clock barely moved, and the trace says why.** Of a 257s run, loading is 34s, dependency
analysis **74s**, the per-class checks **68s** spread across 24 cores, and the whole-graph analyses
**72s run one after another on a single thread** — `UnusedMembersAnalyzer` 41s, `ShadowingAnalyzer`
23s, `UnusedImportAnalyzer` 9s. Cutting per-class thread time can only ever buy its share of 68s.
The next wall-clock gain is in the serial phases, and is not in this package.

**B257** was a naming defect in the log, not in the dialog: the combined pass logged its start as the
combined pass, its end as "dependency analysis", and a style-checking completion with no start, so
read by name the dependency half carried the whole four minutes. `DeferredDependencyStep` names the
pass once for both ends and both messages, decided from whether style checking really was carried.

### WP14 — External tools, second pass

**B262, B263** · 2 items · M and M · **B263 is the item; B262 is half of its mechanism, and has grown one of its own**

What the user asked for is one thing: *let me give the tool longer for a model that takes longer*.
Underneath it the two tools are in different states and neither is right — Dymola is about to have a
settable `CommandTimeout` with nothing setting it (PR #9, which grew in review into a per-call timeout **and**
a `CancellationToken` on thirteen wrappers, plus a constructor that can block ~32s waiting out a busy Dymola —
see B262), and OpenModelica has carried
`CommandTimeoutMs` and `StartupTimeoutMs` in its settings since it was written with **nothing reading
either**, so a long `omc` check has no timeout at all and blocks indefinitely.

**Do B263 as one piece across both tools, not one tool at a time.** This is the shape that produced
B170's second and third reports inside two days: a question answered for one tool and left alone for
its sibling, where the sibling's silence looks like agreement. The settings tab, the two interfaces
and both checking services are one change.

**Two things B262 carries that are not about timeouts at all.** `DymolaCheckingService` already holds a
`_cancellationTokenSource` that today only stops *between* models; the new `CancellationToken` parameter is what
makes Cancel interrupt a check already running, which is worth having whatever is decided about settings. And
the constructor blocking for up to ~32s inside `GetOrCreateAsync`’s lock is a UI freeze unless it is moved off
the UI thread — that one is a defect the merge introduces, not an opportunity it offers, so it lands whether or
not B263 is done.

**The Dymola half waits on PR #9 merging; the OpenModelica half does not.** Start there if the review
is still open — it is the tool with no timeout at all, which is the worse of the two states.

---

---

### WP15 — The library index, which claims more than it supplies — **✅ complete 2026-09-23**

**B268 ✅** · 1 item · M · plus **B280 ✅**, found doing it · **done by a fourth option — see the end of this section**

WP7 traced a Code Review defect — a class of the user's own marked as changed by the browser and
opened with every diff view disabled — back to a library index that lists classes it does not
supply. A library loaded twice, the user's checkout and a tool's encrypted build of the same
library, races to fill one graph. `DirectedGraph.AddNode` resolves each collision in favour of
readable source, and **nothing tells the loser's index**, so both `LoadedLibrary` entries go on
claiming the id.

**Nothing is currently wrong because of it.** `LibraryOwnership.Owner` answers "which library owns
this class?" by asking the graph which copy survived, and the five callers that used to search the
list go through it. That is a *read* that compensates for a write that was never made.

**Why it is still a package.** The index is the thing that is untrue, and it is untrue in a way the
project has now paid for twice without fixing: `ILibraryDataService.TotalModelCount` exists only
because summing `ModelIds` over-counts, and did so *differently on each launch* — 77,860 then 76,129
for one project, a difference that was read at the time as a symptom of the host migration.
`DictionaryScope.RepositoryForModel` carried a half-workaround that skipped claimants with no
repository, which does not help when both have one. Each was fixed where it hurt and the cause left
in place. The third occurrence was this one, and the next caller to write
`Libraries.FirstOrDefault(l => l.ModelIds.Contains(id))` gets it back with no test objecting.

**Three candidate fixes, and the choice is the work.** Have the load that wins a collision withdraw
the id from the loser's index — cheap, but it means a load reaching into another library's state,
and the collision is decided inside `DirectedGraph`, which knows nothing about libraries. Or stop
`ModelIds` being a claim at all and derive membership from the graph, which makes the question
unanswerable incorrectly but puts a lookup on paths that currently do a set test. **Measure the
second before choosing it**: `ModelIds` is read per class in the check pipeline.

**The third arrived from reading `MainLayout` while fixing B269, and it removes the race rather
than tolerating it.** Reference material reaches the graph by two routes, and only one of them is
ordered:

- the **`ReferenceLibraries` setting**, loaded by `LoadReferenceLibrariesAsync` *after* the
  project's own repositories. The comment above that call already states the rule — *"a tool's
  library folder ships encrypted builds of libraries the user may have checked out as source, and
  the source copy must win"*;
- a **reference-only repository configured in the project**, which is just one of the repositories
  `LoadRepositorySettingsAsync` loads, in the same parallel pass as the user's own checkout, with no
  ordering at all.

The reported project used the second: four repositories, two of them under
`C:\Program Files\Dymola 2026x Refresh 1\Modelica`. The encrypted `Suspensions` finished at
17:06:27 and the source `Suspensions` at 17:06:37, so 737 classes were stubbed before their source
arrived. **The ordering rule is therefore narrower than its own comment claims.** Making the second
route obey it — reference-only repositories last, as the setting's libraries already are — means no
class is ever stubbed before its source lands, and both indexes come out true. That is a different
kind of fix from the other two: they make a wrong index readable, this one stops it being wrong.

**It is not obviously free.** Loading the project's repositories sequentially by kind gives up some
of the parallelism that load has, and `LoadReferenceLibrariesAsync` is already a serial loop.
Measure the startup cost before committing to it, the same way WP13 is told to.

**A guard is worth more than either.** Whatever is decided, the thing that stops the fourth
occurrence is a test that fails when a resolution goes back to searching the list — the shape
`LibraryOwnershipTests` already has, applied to whichever callers exist then.

**What was done (2026-09-23).** None of the three: each makes the index readable or true, and none
stops the two copies being **mixed**, which is what was actually wrong. The copies are routinely
different releases, so a class-by-class merge kept a stub for every class the newer source had
deleted — in both arrival orders, since the stub builder adds whatever the source lacks. The decision
is per library instead: **an encrypted library is never loaded beside readable source for the same
library, and the source wins whole.** `SourceSupersedesEncrypted` holds the rule (exact top-level
name; an unknown name matches nothing), and it is asked in three places, cheapest first —

- `RepositoryService.LoadLibrariesAsync` skips the encrypted build **before reading it**, from the
  names every repository's discovery already has. That is the reported project's shape, and asking
  discovery rather than the loaded list is what takes the race out without serialising anything,
  so the startup cost this section told us to measure does not arise;
- `AddEncryptedLibraryFromDirectoryAsync` asks before reading the help HTML, for the callers with
  no discovery pass in front of them — the CLI's `--dependency`, the MCP server's `load_library` and
  the Reference Libraries setting. Without it those read the documentation and built a graph of
  stubs only to throw them away;
- `LibraryDataService.Register`, which every load path now goes through, retires whichever copy
  registers second under the same lock. That catches what the first two cannot: source arriving
  while the encrypted build is mid-load, and a folder not named after its library.

All three are logged and not shown. `LoadedLibrary.SupersededBy` records why a library came back
empty, because an empty index is also what a library shipping no documentation looks like and the
two need opposite messages — `load_library` had started calling one "ships no usable
documentation", and the CLI named it as loaded. The CLI no longer records such a library as a
baseline dependency, and `Baseline.DriftFrom` does not report one satisfied by source as missing
from a baseline taken before this change, which would have told the user to pass a `--dependency`
they had already passed. `RemoveLibrary` now removes only the nodes a library supplies — it
removed every id it listed, so removing an encrypted library that had lost classes to source
deleted the user's own classes from the graph. The guard is `LibraryOwnershipPolicyTests`, a ledger
of every production read of a library's `ModelIds`, checked by putting a search back and watching it
fail; the three Code Review searches and two in `LibraryDataService` now ask `LibraryOwnership`.
That and `TotalModelCount`'s distinct count are **kept as safety nets**, not removed: two readable
checkouts of one library in different repositories still produce two claimants, and nothing
prevents that. `DirectedGraph.AddNode`'s source-beats-stub rule is still load-bearing — when the
encrypted build loads first, the source's classes land in the graph before `Register` retires it.

Removing a repository can leave unloaded an encrypted build it was standing in for, so the Manage
Repositories tab offers **Load project** on the active project after one is removed. Wiring that
up found **B280**: a project switch — which is what the reload is — never loaded the Reference
Libraries setting, so every switch ran without them until restart. `OnProjectChanged` loads them
now, after the project's own libraries, as startup does.

Verified: 6,869 tests across the seven CI suites, the 75 browser journeys and the coverage ratchet.
Commit `0314b2f`.

### WP16 — Performance, the serial phases

**B281 ✅, B282** · 2 items · M each · **the per-class phase is no longer where the wall clock goes**

Opened 2026-09-24 from WP13's last profile. Cutting per-class thread time by a third moved a Claytex
check from 4m21s to 4m12s, and the trace says why: of a 257s run, loading is 34s, **dependency
analysis 74s**, the per-class checks 68s across 24 cores, and **the whole-graph analyses 72s one after
another on one thread**. The two items are those two serial phases, and they share a shape — each
parses every class in the graph when the question is only about some of them.

- **B282, dependency analysis.** Phase 1+2 parses all 39,596 classes (42s) and Phase 3, the second
  `loadSelector` pass, parses all of them again (40s). Phase 3 only needs the classes that can reach a
  tracked parameter, and Phase 1+2 has just built the edges that say which.
- **B281, the graph analyses.** Serial by construction, and `UnusedMembersAnalyzer`'s first pass walks
  the whole graph rather than the checked set — ~95% of that analyzer's time, the pass that reports
  being under 5%.

**The gate is WP4's and WP13's**: re-measure on Claytex with `--timings` before touching anything,
profile rather than guess, and hold findings — and for B282 the external-resource graph — identical
on Claytex and MSL. Both narrowings rest on dependency edges meaning what they are assumed to mean,
so **verify the edge first** (that `extends` is recorded, how far a modification can reach) and keep
the full scan as the fallback when edges are absent.

**B281, done 2026-09-24 — and the phase was not serial so much as waiting.** The edge held (16,226 of
16,226 extends clauses into the checked set carried one), so `UnusedMembersAnalyzer` asks only a
candidate's users, and both big analyzers went parallel. The phase stayed at 72s with twenty threads
busy. Read per thread, the earlier serial trace had already said why: the analysing thread spent **53
of 72 seconds suspended in `PollGCWorker`**, waiting for collections. Every host ran workstation GC.

| Claytex, findings identical in all four | Workstation GC | Server GC |
|---|---:|---:|
| before B281 | 242s, 8.2 GB | 104s, 8.5 GB |
| with B281 | 255s, 11.4 GB | **70.5s**, 8.7 GB |

The collector was the gain, and the parallel analyzers are a loss without it. All three hosts now set
`ServerGarbageCollection`, held by `HostGarbageCollectionTests`; the published tree runs its smoke
tests with it. **The desktop app is the host not measured directly** and wants confirming over a real
session. B282's 74s was measured under workstation GC too, so it is re-measured before anything else.

## Sequencing summary

```
WP0 ✅ ▶ WP1 ✅ ▶ WP2 ✅  S0/S1 ▸ B213 classifier ▸ B214 elision ▸ B215 viewer
          │              (closed B182, B183, B185) ▸ B178/B217 diff ▸ B186/B173 ▸ B189 ▸ B197
          │              with B176, B187 in parallel; B230/B231 taken
          │              moved out: B216, B232 ▶ WP3 · B218 ▶ WP10 · B237 ▶ WP9
          ├──▶ WP3 ✅ B236 ▸ B181 ▸ B177/B195 ▸ B175 ▸ B232 ▸ B216 — complete 2026-09-20
          ├──▶ WP5   external tools, independent
          ├──▶ WP6   revision control, independent; B240 is a decision about
          │              whether RevisionControl may depend on anything
          ├──▶ WP11  Code Review, second pass — B250 ▸ B247 ▸ B248/B249 ▸ B233
          │              B253 ✅ B254 ✅ taken early, both reported mid-flight
          │              everything reported from using what WP2 built
          ├──▶ WP12 ✅ rules, second pass — B246 ▸ B245 ▸ B252, complete 2026-09-22
          └──▶ WP4   perf; B184 after WP1 so the graph is trusted first;
                       B235 from WP2, measured already — take it with B174
                       └──▶ WP7 ✅ B188 ▸ B191 — complete 2026-09-21; B191 confirmed in
                                     scope by the user before it was started

WP8 ✅ test-harness fidelity (B205, B212) — independent; before WP2 if the suite is to be trusted there
        └──▶ WP9   B237 first (it blocks other packages' journeys), then B267 before the
                       mutation items — it is what makes "mutant confirmed dead" sayable —
                       ▸ B228 ▸ B229 ▸ B227 ▸ B234, then ▸ B274 ▸ B275 from the
                       re-run campaign; needs no other package
WP10 ✅ B218 ▸ B179 ▸ B196 (MCP) — complete 2026-09-23
WP13 ✅ B261 ▸ B257 — complete 2026-09-24: coverage shares the rule's unit lookup, 6m51s → 4m21s
                       on Claytex; the combined pass names itself at both ends of the log
WP14  B263 across both tools, with B262 as its Dymola half — the omc half is not blocked by PR #9
WP15 ✅ B268 the library index — complete 2026-09-23: an encrypted library is never loaded
                       beside source for the same library; B280 found doing it
WP16  B281 ✅ ▸ B282 — B281 found workstation GC was most of the time: server GC in every host,
                       Claytex 4m13s → 70.5s; B282 (dependency analysis) is re-measured under it first

No package, and deliberately: B152 (photographed screenshots) and B166 (a variance that has not
recurred) — both recorded above as needing no work rather than waiting for someone
```

WP3, WP5 and WP6 depend on nothing in WP2 and can be taken whenever a change of subject is wanted.
WP4's B184 is placed after WP1 on purpose: re-scoping what gets checked is not worth doing while a
malformed file can still drop classes out of the graph underneath it (B201).

**Every open item in this phase names a package**, bar the three recorded above as needing no work
(B152, B166, B198), and that is a property worth keeping rather than a tidy-up. It has been re-checked
after each round of new items, most recently on 2026-09-21 after WP7, which opened B267 and B268 and
made one new package necessary — earlier the same day three items arrived at once and made two, as
six did on 2026-09-20. The two ways an item escaped were "separable at any point" against a package
that later closed, and a mention in a step's prose that no item list counted — both of which read as
*scheduled* right up until the package shipped without them. **The check that found B267 and B268
loose found B257 as well**, and between them they are one of each kind: B267 and B268 were named in
WP7's prose where they were found and in no item list, and B257 was named in WP4's prose and left
behind when WP4 shipped. They are in WP9, WP15 and WP13 now. Run this check when items are opened,
not only when a package closes — WP4 closed on 2026-09-20 and B257 was loose for a day. An item that genuinely belongs nowhere belongs on the roadmap, where it is
at least a candidate, not in the margin of a plan.

WP9 follows WP8 because it is WP8's leftovers, not because anything blocks it: it needs no other
package and can be taken whenever there is appetite for it. **It is the one package with no deadline**
— nothing in it can corrupt a repository or lose a commit, which was the line WP8 used to decide what
had to be fixed the same day. Take B228 on a quiet afternoon and B227 only when there is time to do it properly — and note that
B229 **did not** need the machine with Dymola and `omc` on it that this sentence promised. What
stopped those two services being tested was the concrete session class in the factory's return
type, not the absence of the tool; the suite that tests them runs in CI on every push.

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
