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

### WP2 — The Code Review page

**B213, B214, B215, B216, B217, B218** (from the decision above) and **B230, B231, B232** (from
running its first step) and **B182, B185, B183, B178, B176, B186, B187, B189, B197**, with **B173**
alongside B186 · 19 items · the bulk of the phase

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
6. **B230 first, then B216** (S1 said yes), and **B218**. B230 is a guard clause in front of an
   existing render and removes 55% / 95% of the trimmed-package population on its own, so it is worth
   taking before the rest of this step whatever happens to B216. B216 and B218 are the same mechanism
   reaching the check pipeline and an agent; neither is needed for the viewer, so both can be taken
   later or in another phase. **B231** (the offset documentation) belongs with whichever of B215 or
   B216 lands first, since both read those fields.
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
8. **B176** (search over the source — now the user's own source) and **B187** (AND plus a rule filter)
   — independent of the classifier, so they can run in parallel with 2–5. B187 must untangle the
   scope-through-search-string smuggling first.
9. **B189** then **B197** — reveal-in-tree, then the navigation stack over it. Both hang off the
   finding-click path step 4 rewrites, so they come last and are cheap once it exists. B197's peek now
   lands in the user's own text, which is the point of it.

**The standing trap in this package** is scope: the change touches the viewer, both diff views, the
MCP source tool and possibly the check pipeline. Steps 1–5 are each shippable alone and step 6 is
explicitly optional — keep them that way.

### WP3 — Rules and formatting

**B236, B181, B177, B195, B175** · 5 items · M

Batched because a new rule id walks the same six places every time: `RuleIds`, `RuleCatalog`,
`RuleSettingsLayout`, the visitor or analyzer, `settings-reference.md`, and the catalogue guard test.
Walk that path once with three rules in hand rather than three times.

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

**B190, B174, B184, B199, B235** · 5 items · M–L

**No change in this package without a measurement first.** The log at `%LocalAppData%/MLQT/*.log`
holds weeks of timestamped phase durations and `mlqt check --timings` prints the per-phase breakdown;
B128 established both. B174 says explicitly not to theorise first.

- **B235 arrived from WP2 with its measurement already done**, which is the one item here that
  starts past this package's gate rather than at it: a run of comments inside an `equation` section
  makes the parse quadratic (69 s for 4,000 of them, against 367 ms for a *larger* class without
  them), on legal input with zero parse errors. **Take it with B174, not apart from it.** B174 is
  "loading and style checking are getting slow" and says to start from the log; B235 is a named,
  reproducible cause of exactly that, and every surface parses — so it is also part of B184's
  subject, CI check time. Whether it explains a useful share of B174's ~300 s is itself a
  measurement, and the honest order is to look for this shape in the real libraries before assuming
  it is the answer.
- **Its blast radius is the largest in the phase**, which nothing else here shares: the fix is in the
  grammar, so it changes `ModelicaParser` — the assembly at a >95% coverage bar that the GUI, the
  CLI, the MCP server and every rule sit on. The gate is finding-count parity (MSL = 34329) on top of
  the usual suites, the same one B216 carries.
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

### WP9 — The mutation survivors deliberately left

**B227, B228, B229** · opened by WP8's audit, and held back from it on purpose

WP8 asked one question of the 5,990 surviving mutants — *which of these sit on a line this
repository has written a comment to defend?* — and then a second — *which sit on code that writes to
a user's files or their remote?* Those produced B219-B226, all closed. What is left is everything the
two questions did not reach, and it is not a backlog of defects: most of it is equivalent mutants and
code nobody should test. Three groups are worth a deliberate pass, in this order.

**B228 first, because it is small and finishable.** Four survivors in `ModelicaFileEncoding`, of
which the real one drops a byte-order mark from a file MLQT was asked to preserve. An afternoon,
and it settles whether the encoding round trip — which CLAUDE.md singles out as progressive
corruption when it goes wrong — holds under its own tests rather than under inspection.

**B229 next, because it is the largest gap and the reason is structural.** 11% of covered mutants
killed in each of the two external-tool services, and they are the only code in the solution whose
suites no CI job runs. Nothing outside one machine exercises them. That is a decision the project
made knowingly — a live Dymola cannot be a runner dependency — and the audit has now priced it.
The work is a live-tool session, not a CI change.

**B227 last, and only with a sampling plan.** 361 survivors in `ModelicaRenderer` at an 85% kill
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
WP0 ──▶ WP1 ──▶ WP2   S0/S1 measure ▸ B213 classifier ▸ B214 elision ▸ B215 viewer
          │              (closes B182, B183, B185) ▸ B178/B217 diff ▸ B186/B173 ▸ B189 ▸ B197
          │              with B176, B187 in parallel; B216, B218 separable at any point
          ├──▶ WP3   rules, independent
          ├──▶ WP5   external tools, independent
          ├──▶ WP6   revision control, independent
          └──▶ WP4   perf; B184 after WP1 so the graph is trusted first;
                       B235 from WP2, measured already — take it with B174
                       └──▶ WP7   B191 only if confirmed in scope

WP8   test-harness fidelity (B205, B212) — independent; before WP2 if the suite is to be trusted there
        └──▶ WP9   B228 ▸ B229 ▸ B227 — the survivors WP8 left; needs no other package
WP-less: B179, B196 (MCP) — separable at any point
```

WP3, WP5 and WP6 depend on nothing in WP2 and can be taken whenever a change of subject is wanted.
WP4's B184 is placed after WP1 on purpose: re-scoping what gets checked is not worth doing while a
malformed file can still drop classes out of the graph underneath it (B201).

WP9 follows WP8 because it is WP8's leftovers, not because anything blocks it: it needs no other
package and can be taken whenever there is appetite for it. **It is the one package with no deadline**
— nothing in it can corrupt a repository or lose a commit, which was the line WP8 used to decide what
had to be fixed the same day. Take B228 on a quiet afternoon, B229 when there is a machine with
Dymola and `omc` on it, and B227 only when there is time to do it properly.

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
