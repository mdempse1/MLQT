# MLQT Backlog

The working list. Every open item has an id, and **an id is never reused** — they are cited from
code comments, test summaries, build scripts and CI workflows, so a new item takes the next number
above the highest ever issued, whatever has since been closed.

**B1–B167 have been issued.** They were opened between 2026-09-03 and 2026-09-17 by the seventeen
end-of-branch reviews of the CI/CD toolchain and by phases 7a and 7b. All are closed except the three
carried forward below, and the table they lived in was retired with the phase design notes on
2026-09-17 — git history has it if the reasoning behind one of those ids is ever needed.

New items start at **B168**.

`MLQT.Cli.Tests/MarkdownTableTests.cs` holds this file's table structure and the uniqueness of the
ids. A blank line between two rows silently ends the table, and an unescaped `|` in a cell splits it
even inside backticks — both have happened, and neither is visible in review.

---

## Carried forward from phases 7a / 7b

Three items whose id is already in use elsewhere and whose work is not finished.

| # | Item | From | Value | Effort | What is needed |
|---|------|------|-------|--------|----------------|
| B143 | **`nightly-webkit.yml` has never run** | phase 7b-6 2026-09-09 | ⭐ | S | `workflow_dispatch` resolves a workflow on the **default branch**, so the job could not be triggered from the migration branch — not from CI, not from the CLI, and not locally either, because Playwright ships no WebKit build that runs on this development machine (B135). It is the one 7b-6 deliverable that shipped unproven. **The action is specific: run it once by hand immediately after the merge**, rather than letting 02:00 be its first outing. The first run of a job written blind has already failed twice (B142). |
| B152 | **Twelve documentation screenshots are still photographs** | doc review 2026-09-11 | ⭐ | M | 41 of the 53 images are regenerated from the real UI by `DocumentationScreenshots`; the rest cannot be produced that way and are listed in CLAUDE.md and `skill-gui-testing.md`. Two of them need Dymola, six need an SVN server, one needs an SVN repository to render its settings section, one needs the merge dialog's ready-to-merge phase (which needs a VCS operation performed *in the application* to refresh working-copy status), and the rest show the window frame. **Not a defect** — recorded so nobody re-derives the list. Worth revisiting only if a fixture ever gains an SVN server. |
| B166 | **`check_library` once returned a different finding count on each run, and the variance has not reproduced** | 2026-09-17 | ⭐⭐ | M | The three causes found alongside it are fixed — the MCP call passed the wrong dictionary root, `modelsChecked` counted excluded libraries, and one `AnalyzeDependenciesAsync` call omitted the library roots so `modelica://` resolution depended on call order. The original 20,763 / 20,790 / 20,766 spread did **not** reproduce: three calls in one session and three with a full reload between them each returned 21,249. Two details weaken the original report — the reload path returns a **hardcoded** `affectedModelCount: 0`, so "no source change" was read off a literal; and the runs were scoped to `Modelica` alone inside a graph holding seven other libraries, which is a materially different shape. **Left open at low confidence.** If it moves again, capture the **per-rule breakdown of two adjacent runs** — a total says nothing about which rule moved. |

---

## Phase 1 — issues found testing the new build

Opened 2026-09-17 from a pass over the Photino release on both platforms. Mostly UI, not
exclusively. Grouped by area; the ids are in the order they were written down.

### Findings and the Code Review page

| # | Item | Area | Value | Effort | What is needed |
|---|------|------|-------|--------|----------------|
| B176 | **No way to search the code being reviewed** | Code Review | ⭐⭐ | S | Locating a parameter by name in a long class means scrolling. A search box over the rendered source, with match highlighting and next/previous, is what the page is missing to be usable on a real class. |
| B182 | **Line numbers for naming violations do not line up with the rendered code** | Code Review | ⭐⭐⭐ | M | A finding on a naming violation points at the wrong line in the viewer. The likely cause is that the finding's line is computed against the **stored** source while the viewer renders the **reformatted** source — the page reformats in order to colour it, so the two can differ by however many lines the formatter moved. Either the mapping has to go through the same representation the viewer shows, or the viewer has to stop reformatting. Establish which before fixing: the same mismatch would affect every rule whose line comes from a visitor, not only naming. |
| B183 | **Findings do not scroll to their line, and arrive in no particular order** | Code Review | ⭐⭐⭐ | M | Two halves. Clicking a finding should scroll the viewer so the line it is about is **in view** — currently a finding on a parameter can select a class whose relevant line is off screen. And the list order is whatever the check produced; it should be stable and sorted alphabetically, so the same library reviewed twice presents the same list twice. |
| B186 | **The findings table runs off the bottom of the page** | Code Review | ⭐⭐⭐ | M | Part of the table is not reachable, particularly when each finding spans several lines. A splitter between the viewer and the findings list would let the user choose, but the row count shown has to vary with the space given to it as well, or resizing just moves which part is cut off. |
| B187 | **The finding filter cannot filter by rule, and combines its terms with OR** | Code Review | ⭐⭐ | S | Two defects in one control: there is no way to narrow to a rule type, and entering more than one term widens the result instead of narrowing it. It should be **AND** — a partial model name *and* a keyword — which is what anyone typing two things into one box expects. |
| B178 | **The diff view uses its own colours instead of the syntax scheme** | Code Review | ⭐⭐ | S | Side-by-side diff markup does not follow the rendering colour scheme used when viewing a single file, so the same code is coloured two ways in two panes of the same page. The user's chosen preset should reach both. |
| B185 | **A very large class can fail to render at all** | Code Review | ⭐⭐⭐ | M | An imported FMU — `Engines Examples I2 fmu` — was still not rendered after five minutes. Two candidate costs and they need separating before either is addressed: the syntax highlighting over a very large token stream, and the reformat the page performs in order to colour it. The likely answer is a size threshold above which the page shows plain source with no highlighting and no reformat, but measure which half costs the time first. Relates to B182, which is about the same reformat. |

### Library browser and navigation

| # | Item | Area | Value | Effort | What is needed |
|---|------|------|-------|--------|----------------|
| B188 | **Repositories cannot be reordered within a project** | Library browser | ⭐⭐ | S | They appear in the order they were added, which is rarely the order of interest. The user wants the most relevant at the top. Needs a persisted order in the project settings and a way to change it. |
| B189 | **Opening a model from a finding does not reveal it in the tree** | Library browser | ⭐⭐ | S | Clicking a finding opens the model in the viewer but leaves the browser wherever it was, so there is no context for what was just opened. Expand to and select the model, the way a "reveal in tree" action would. |
| B191 | **Nothing in the tree marks a model as modified** | Library browser | ⭐⭐⭐ | M | Add a marker for models with uncommitted changes, and **distinguish the kind of change**: one marker for edits that affect simulation, another for purely graphical or documentation edits. Then a filter view showing only modified models, filterable by that distinction. The classification is the substantial part — it needs a comparison of the parsed old and new class, not a text diff. |
| B197 | **No way to peek at or navigate into a used class** | Library browser / Code Review | ⭐⭐ | M | Reviewing documentation, what looks like a variable name is highlighted and checking what it should be called means finding the base class by hand. Wanted: a quick peek, or navigation into the base class with a way back — ideally back through the last few classes visited, like an editor's navigation stack. |
| B200 | **Only one reference repository is labelled "Reference Only"** | Library browser | ⭐⭐ | S | In repository view mode `ExternData` gains the label and the project's other reference repositories do not, although all of them have the flag set. Almost certainly the label is read from the wrong place for all but one path — the same shape as B80, where a reference-only fact was known in one place and not asked in another. |
| B173 | **The resource tree has no horizontal scrollbar and cannot be resized** | External resources | ⭐⭐ | S | A long path in the External Resources tree is simply cut off: the panel neither scrolls sideways nor resizes. Either would do; both is better, and a splitter here would be the same control B186 wants on the Code Review page. |
| B199 | **The dependency plot is unusable above a few hundred nodes** | Dependencies | ⭐⭐ | S | Over 1,000 nodes the Cytoscape graph is very slow to generate and the nodes are too small to read, so it costs a lot and shows nothing. Above a threshold, skip the plot and show the list alone — with a way to draw it anyway if the user insists. |

### Projects, repositories and startup

| # | Item | Area | Value | Effort | What is needed |
|---|------|------|-------|--------|----------------|
| B192 | **Creating a new project on the startup screen loads the old one instead** | Projects | ⭐⭐⭐ | M | After creating a project and choosing Load Project, MLQT loads the **previously selected** project from the list rather than the new empty one, and the new project appears to take on that project's repositories. Two candidate faults — the selection not moving to the newly created row, and the create path copying the selected project rather than starting empty — and they may both be real. |
| B194 | **The startup dialog only responds to its buttons** | Projects | ⭐ | S | Clicking a row in the startup list does nothing; the action has to be taken from the button. Make the row itself trigger the same action, keeping the button. |
| B198 | **A newly added repository did not load its library until MLQT was restarted** | Repositories | ⭐⭐⭐ | M | Adding `ModelicaEditorTestsGit` to a project added the repository but loaded no library; restarting the tool loaded it. The suspicion to test first is the **layout**: this repository holds a single library with `package.mo` at the top level, rather than libraries in subdirectories. If discovery is the cause it is a defect in `LibraryDiscovery`; if the discovery is right and the load is not triggered, it is in the add-repository path — which already had to be taught to run its analysis through the progress dialog (B127). |
| B190 | **The UI, and sometimes the whole desktop, freezes during loading** | Performance | ⭐⭐⭐ | M | Reported during the loading step: MLQT's window is unresponsive, and at some points other windows are slow to redraw too — moving the MLQT window is sluggish and everything can stall for a few seconds. **Confirm it still happens before investigating**; it predates several fixes since, and the log records phase durations that would say where the time goes. Whatever is blocking is on a thread it should not be on. |

### Analysis correctness

| # | Item | Area | Value | Effort | What is needed |
|---|------|------|-------|--------|----------------|
| B168 | **A file change may be counted twice when a library is in the project and in the reference libraries** | File monitoring | ⭐⭐ | S | Suspected on Photino: the same library reached both through a project repository and through the global reference-library paths appears to produce two change events per edit. B129 fixed the *loading* half of this — the same library reached two ways was parsed and indexed twice — and the monitoring half was not looked at. Confirm by counting events for one edit, then make the watcher set distinct by resolved path, as the loader now is. |
| B169 | **External resources are sometimes attached to the wrong directory** | External resources | ⭐⭐⭐ | M | References get linked to the wrong directory when encrypted libraries have registered resource roots. Encrypted libraries register their unencrypted `Resources/` directory so `modelica://Lib/Resources/…` resolves; the symptom suggests a resolution that picks the wrong registered root when more than one is a prefix of the path. Needs a reproduction with two libraries whose roots share a prefix before anything is changed. |
| B201 | **A file that fails to parse is dropped silently, with no node and no placeholder** | Parser / graph | ⭐⭐⭐ | S | Found from ModelicaTools: a file with an `import` between the `within` clause and the class is not valid Modelica, and the tightened grammar no longer salvages it through error recovery. A probe against the current `GraphBuilder` confirmed the file then yields **zero model nodes** — no diagnostic, no placeholder, despite the placeholder machinery added for exactly this. A real library with one malformed file loses those classes without anyone being told. |
| B172 | **C/C++ standard library headers are reported as missing external files** | External resources | ⭐⭐ | M | An external function's `IncludeDirectory` / `Include` referring to a standard header is flagged as a missing file, because nothing knows where the platform's standard include paths are. Either resolve against the known system include paths, or recognise standard headers by name and never report them. The first is more correct and platform-specific; the second is cheap and would cover the complaint. |
| B184 | **`--changed-from` re-checks everything it loaded** | CLI / performance | ⭐⭐⭐ | M | A changed-file check still runs every rule over every model, then filters. It should apply the rules only to the models in the modified files and compare those against the **baseline records for those models**. Everything still has to be *loaded* — base classes and reference resolution need it — but it need not be re-checked. The one analysis that genuinely has to run over everything is reference validation, and only when a model was deleted or renamed. This is the largest available win on check time in CI. |

### Rules and formatting

| # | Item | Area | Value | Effort | What is needed |
|---|------|------|-------|--------|----------------|
| B175 | **Exclude-from-formatting writes a name into the settings file rather than an annotation** | Formatting | ⭐⭐ | S | The button records the class in `FormattingExcludedModels`, which is name-based and so does not survive a rename. `__MLQT(format=false)` is the rename-safe mechanism the documentation already recommends, both mechanisms are already honoured everywhere (B39, B65), and the writer already exists for suppression. The button should write the annotation. |
| B177 | **No warning when a package is stored as one file instead of a directory** | Rules | ⭐⭐ | M | A package held entirely in a single `.mo` file rather than as a directory of standalone classes is a structural choice worth flagging in some repositories and not others, so it needs a rule id, a severity, a switch in the settings dialog and a row in the Findings list — not a hard-coded warning. Belongs with `PackageOrderAnalyzer`'s family. |
| B181 | **"Components before classes" can only be enforced by the formatter, never reported** | Rules | ⭐⭐ | S | The composition ordering rule is formatting-only: it is applied when the renderer rewrites a class and there is no way to be *told* about it. If the rule is switched on it should raise a finding like every other ordering rule. Note this row is the one in `settings-reference.md` with no rule id to bind its label to (B103), so giving it an id closes that gap as well. |
| B195 | **`package.order` completeness, with an option to align with Dymola** | Rules | ⭐⭐ | M | Dymola emits, of its own accord on load: *"Warning: The package.order is incomplete, since the class Bad in file/directory .../Sub/Bad.mo is missing."* That is exactly the "a warning may be given" the specification anticipates, and it confirms the shape recommended for MLQT's own check. **It fires only for the `package.order` shape** — Dymola never descends into non-package folders, so it gives no signal at all for the stray-file case, and that half of MLQT's check has no upstream equivalent and earns its keep on its own. Worth an explicit "match Dymola" setting so a repository can choose to report only what Dymola would. |
| B174 | **Loading and style checking are getting slow** | Performance | ⭐⭐⭐ | L | Reported from ordinary use. B128 established the series and the per-phase breakdown — `CheckTimings` is in place and `mlqt check --timings` prints it — and concluded the growth to ~300 s was expected rather than a regression, with no next obvious win at the time. **Start from the log**, which records timestamped phase durations going back weeks, and from `--timings` on the current build; do not theorise first. B184 and B147 are the two named candidates. |

### External tools

| # | Item | Area | Value | Effort | What is needed |
|---|------|------|-------|--------|----------------|
| B170 | **An OpenModelica check reports nothing at all** | External tools | ⭐⭐⭐ | S | Checking a class with OpenModelica produces no visible feedback: it opens no window, and there is no dialog reporting success or failure. The Dymola equivalent is only known to have run because Dymola's own window appears. Both need a result dialog of MLQT's own, reporting what the tool said — which also removes the dependence on a vendor window being visible. |
| B171 | **Dymola checking fails on the second attempt if its window was closed** | External tools | ⭐⭐⭐ | M | The first check works; closing the Dymola window and checking again does not. The interface presumably keeps a connection or process handle that is no longer valid and does not detect it. It should notice a dead session and start a new one — and, with B170 in place, say so if it cannot. |

### MCP server and McpTester

| # | Item | Area | Value | Effort | What is needed |
|---|------|------|-------|--------|----------------|
| B179 | **MCP loads an encrypted library but returns almost nothing about it** | MCP | ⭐⭐⭐ | M | An agent can load an encrypted library, but the tools do not return the public interface or the documentation — both of which **are** recovered from the vendor's help HTML and are sitting on the synthesized stub: description, base classes, parameters, connectors, inputs and outputs. `get_class_info` and its neighbours should return them for a stub, clearly marked as recovered from documentation rather than read from source, and still declaring the class not editable (B85). |
| B196 | **The diagram tools give an agent no way to see what it drew** | MCP | ⭐⭐ | M | The diagram tools return a description, so the agent is laying out a picture it cannot look at. Rendering the SVG to **PNG** and returning it would let the agent judge its own layout — and the SVG is already produced, so the question is which conversion to take on (and what it costs in dependencies). Alongside that, the guidance the tools give needs to say how connector positions should be chosen to match MSL and the usual conventions, and it is worth asking what else would make an automatically generated diagram look right. |
| B180 | **`MLQT.McpTester` starts with a path from one developer's machine** | McpTester | ⭐ | S | The default server path is hard-coded to a location that will not exist for anyone else. Blank, or something derivable — the MCP server beside the tester's own binary would be a useful default and would make the first run work. |

### Revision control

| # | Item | Area | Value | Effort | What is needed |
|---|------|------|-------|--------|----------------|
| B193 | **Switch Branch cannot reach a Git tag** | Git | ⭐⭐ | M | Switching to a tagged version of `ExternData` is not offered, although TortoiseGit does it on the same working copy. The branch selector presumably enumerates branches only; checking out a tag produces a detached HEAD, which the surrounding UI also has to be able to describe rather than showing an empty branch name. |
| B202 | **The VCS History file diff compares against the working copy** | Git / SVN | ⭐⭐ | S | Clicking a file in a commit's changed-files popover opens a diff of that revision against the **working copy**. What a user reviewing history wants is what that commit *changed* — the selected commit against its predecessor. B155 corrected the documentation of the current behaviour after finding it reported confusing numbers; this changes the behaviour to the one the dialog is for. |

### Build

| # | Item | Area | Value | Effort | What is needed |
|---|------|------|-------|--------|----------------|
| B203 | **A clean Release build is not warning-free, and the record says it is** | Build | ⭐ | S | `dotnet build MLQT.slnx -c Release --no-incremental` emits **two** xUnit analyzer warnings, both in `ModelicaParser.Tests/SpellChecking/SpellCheckerConcurrencyTests.cs` (commit d1257e3, 2026-09-11): `xUnit1031` on a blocking task operation, and `xUnit1051` on a call taking a `CancellationToken`. They are invisible on an incremental build, which is why the phase-7 record reads 0 warnings — **B77's shape exactly**, one project later. Fix the two call sites, and measure warning counts from a build that actually compiled. |
