# GUI Testing Skill

Load this skill when writing or changing tests over `MLQT.Shared`, working in `MLQT.Shared.Tests`,
`MLQT.TestHost` or `MLQT.Journeys`, adding a `/selftest` probe, or adding a guard test that holds a
documented rule to the code.

The layered harness exists because 71% of the UI source was once inside `@code { }` blocks and
unreachable by any test without starting a renderer. The **code-behind policy** in
[CODING_GUIDELINES.md](../../CODING_GUIDELINES.md) §Blazor Patterns is what makes Layer 1 possible;
`MLQT.Shared.Tests/CodeBehindPolicyTests.cs` is what holds it.

## The four layers, and which one a test belongs in

| Layer | Where | Runs | Use it when |
|---|---|---|---|
| **1 — direct** | `MLQT.Shared.Tests` | no renderer | The default. The handler is a method on a `.razor.cs` partial; call it and assert |
| **1b — bUnit** | `MLQT.Shared.Tests`, over `MlqtComponentTestBase` | a render tree | The behaviour **cannot exist** without one |
| **2 — journeys** | `MLQT.Journeys` → `MLQT.TestHost` via Playwright | a real browser | A user path crossing service boundaries |
| **3 — probes** | `MLQT.Shared/Pages/SelfTest.razor` | the real desktop host | A question only the shipping host can answer — see `skill-desktop-host.md` |

Promote, never weaken: **if a journey ever needs behaviour Blazor Server cannot express, that is a
signal to promote the test to Layer 3**, not to weaken Layer 3.

Layer 1 priority is by **logic density, not component size**: settings change-detection (the two
booleans that decide whether a full reformat runs), dialog validation predicates, findings filtering
and file-line mapping, metrics scope matching, tree building.

### Layer 1b earns its place or it does not exist

A test belongs in 1b only if there is no behaviour without a render tree. The ones that qualify:

- **`LibraryBrowser` lazy-load selection** — the MudBlazor 9.4 regression fixed by an `ItemsChanged`
  write-back of `ServerData` children. The single most valuable bUnit test in the suite and the reason
  bUnit is here at all.
- **Dialog open → interact → `DialogResult`.** `ShowDialogAsync` on `MlqtComponentTestBase` opens the
  dialog through `IDialogService` inside a rendered provider, which is **the only way to reach what a
  dialog closes with**: `MudDialog.Close` goes to a cascaded instance that a directly rendered
  component does not have, so every Close and Cancel in one is a silent no-op.
- **Two-way binding** round-trips through a parent.
- **`CytoscapeGraph` interop sequence** — assert the calls and payloads with
  `JSInterop.VerifyInvoke("cytoscapeGraph.init")`. Whether Cytoscape actually *draws* is Layer 3.

`MlqtComponentTestBase` supplies the three things MudBlazor needs and bUnit does not give you:
MudBlazor services with `CheckForPopoverProvider = false`, `JSRuntimeMode.Loose` (all of MLQT's
interop is async global functions, none meaningful headless), and `RenderProviders()` for the
dialog/popover/snackbar providers. Getting these wrong produces confusing "component not rendering"
failures rather than clear errors.

`MainLayout` gets **no DOM-level tests**. Its logic was extracted in 7a-4 and is tested where it
landed, in `MLQT.Services.Tests`.

## Verify every test by mutation, not by going green

**This is the rule the harness cost the most to learn, and it changed tests three times.** A test that
passes is not evidence until you have watched it fail against the defect it claims to catch.

Concretely, and each of these was real:

- **Property tests that prove a diff script *valid* pass happily on a non-minimal one.** Going green
  said nothing about the algorithm.
- **Two journeys passed vacuously on first write** — one against a fixture that was already
  canonically formatted, one against a rule that ships off. Both were fixed to bite.
- **`SharedUiConventionTests` was silently defeated by the code-behind sweep.** Its four checks
  enumerate `*.razor`, so after the logic moved they ran over markup with no code in it and all passed.
  Its own can-I-see-the-source guard missed it by **counting files rather than the material the checks
  consume** — it now asserts it can see event subscriptions.
- **`RuleDocumentationTests` opened with `if (docs is null) return;`** — run anywhere the source is not
  present, it returned silently. A guard that cannot fail is worse than no guard, because it is
  counted as coverage. `SharedUiConventionTests` deliberately **throws** when it cannot find the source
  and says why in a comment.
- **A bUnit test found an element in one render and clicked it in the next.** It failed only on a
  slower runner, and nothing in the commit that failed had touched it.
- **A test asserted the *shape* of a VCS path and never whether that shape opens the file it names.**
  It pinned the defect (B137).
- **A shell heredoc turned a test literal into a two-line string** and into a literal backspace in a
  regex, and both tests still passed. A heredoc is not a safe way to write source containing
  backslashes, and the failure is silent exactly where the result still compiles (B115, B124).

**Always write the positive control beside the guard**, so the guard cannot be the reason nothing ran.
For an exclusion test: assert the fixture still reports *without* the exclusion, or the pair rots into
two empty sets matching.

## Guard tests — the recurring MLQT defect shape

The defect this repository keeps producing is **a rule stated in a document, or implied by a rule id,
that nothing enforces**. The answer is always the same: a test over the registry, the layout, or the
markup.

Existing guards worth copying the shape of:

| Guard | Holds |
|---|---|
| `CodeBehindPolicyTests` | Six checks over the code-behind policy, including `NoCodeBehindIsOrphaned` for the failure the compiler cannot see — a renamed `.razor` leaving its `.razor.cs` behind, which builds cleanly and never runs |
| `SharedUiConventionTests` | Every `+=` in a component has a matching `-=`; no component subscribes without declaring `IDisposable` |
| `RuleSettingsLayoutTests` | Every configurable rule has a home in the settings dialog, nothing placed twice or placed unsettable |
| `RuleDocumentationTests` | The catalogue and `settings-reference.md` agree, including the exact labels |
| `DocumentedCommandTests` | Every flag combination the documentation prints is run by some test |
| `HostAssetManifestTests` | Each host page carries the same assets in the same order, and each one loads |
| `MarkdownTableTests` | The tables in `Design/` and `CLAUDE.md` are still tables |
| `WorkflowPlatformParityTests` | The two CI legs hold each other in both directions |
| `TestRunnerScriptTests`, `DebianPackageTests`, `WindowsInstallerTests`, `ReleaseVersionTests` | The build and packaging scripts, read as text, because the file system cannot answer on Windows |

**Both directions, always.** A suite measured but not built fails the run; a suite built but not
measured is wasted time and a misleading job name. Hold the two lists together rather than one against
the other.

**A sweep worth running twice is worth a test.** Three defects across two reviews were found by
fifteen-line scripts that existed only in the review transcript. They are four tests now.

## Layer 2 — journeys

`MLQT.TestHost` is an ASP.NET Core Blazor **Server** host over the same `MLQT.Shared`, driven by
Playwright through Chromium. It is a deliberate approximation of the shipping webview: circuit-based
reconnection (irrelevant), no `IJSInProcessRuntime` (MLQT does not use it — verified), interop
serialised over a circuit rather than in-process. Layer 3 covers what it misses.

Three host differences surfaced in the first hour of building it, which is the whole argument for
building it **before** a migration rather than during one:

- **Prerendering breaks MLQT** — components issue JS interop during initial render, which is legal in
  a webview and an exception during static render.
- **RCL static assets failed to serve twice, for two unrelated reasons** — the second because the
  static-web-assets manifest is named after the *application*, and the in-process entry assembly was
  the test project.
- **The host linked no scoped CSS**, so every `.razor.css` rule was missing — invisible until someone
  looked at a picture. `ScopedCssJourney` asks the browser whether the rules are loaded at all,
  deliberately *not* using a `CodeViewer` selector as its canary, since those are shadowed by
  MudBlazor's injected block and would have passed throughout.

### Determinism: wait for a signal, never sleep, never force

- `WebApplicationFactory`'s `TestServer` has no real socket, so Playwright cannot reach it. Start
  Kestrel on `http://127.0.0.1:0` in an `IAsyncLifetime` fixture and read the assigned address.
- The pipeline is asynchronous and partly background-threaded, so "analysis has finished" needs an
  explicit signal — `PipelineQuiescence` and a `data-mlqt-state="idle|busy"` attribute, **test-host
  only**, neither leaking into the shipped hosts.
- **Wait for the thing in the way, do not force past it.** A tab click timed out at 30s while
  Playwright reported the element "visible, enabled and stable": the click was being intercepted by a
  modal progress dialog. The fix is to wait for the dialog to go, not to force the click. It failed
  about one full-suite run in four before that (B154).
- **`LibraryFixture`** builds a real repository per collection: a small Modelica package violating a
  handful of *enabled* rules, a Git working copy via LibGit2Sharp with a committed baseline and an
  uncommitted edit, and a `.mlqt/` directory. **Two commits and two branches** are what make history,
  merge and pull-request surfaces reachable. Deliberately **no SVN fixture** — SVN integration needs a
  live working copy and server that no runner has.
- **Traces are how a headless failure is debuggable at all.** `--trace on-first-retry`, uploaded as an
  artifact. It was asked for in the plan, not implemented, and the first defect it was turned on for
  (B154) was named by it immediately.
- **An unrecognised `MLQT_JOURNEY_BROWSER` throws rather than falling back**, because a typo that
  silently reverts to Chromium produces a green run that tested nothing.

### Playwright's platform gap

Playwright ships no browser build for Ubuntu 26.04. `install` refuses outright; the newest platform it
knows is `ubuntu24.04-x64`. With `PLAYWRIGHT_HOST_PLATFORM_OVERRIDE=ubuntu24.04-x64` Chromium runs and
all journeys pass, but **WebKit will not launch** — its build links `libicu74` and `libvpx9`, and 26.04
ships `libicu78` and no `libvpx9`. So the WebKit rehearsal is a CI job by necessity, on
`ubuntu-latest`. `run-all-tests.ps1` applies the override and `TestRunnerScriptTests` holds the
string-not-version half of it (B135).

## Documentation screenshots are generated, not taken

`MLQT.Journeys/DocumentationScreenshots` drives the real components through `MLQT.TestHost` against
the fixture library and writes each image as the file the markdown already links to.

```powershell
$env:MLQT_DOC_SCREENSHOTS = "Documentation/Images"
MLQT.Journeys/bin/Release/net10.0/MLQT.Journeys.exe --filter DocumentationScreenshots
```

Run it **on its own, by that filter** — the journeys share one host, so a full-suite run reaches it
with libraries and settings another journey left behind. With the variable unset it writes nothing.

**The caption in the markdown is the specification for the shot.** Where the two disagree it is usually
the picture that is wrong, which is the point of being able to regenerate them. MLQT prints the full
path of what it describes, so the fixture repository is written under the shared documents folder for
these runs — a temp directory named after a GUID under a developer's profile would have gone into the
manual.

**What stays a photograph, and why:** anything needing Dymola (`code-review-4`, and `code-review-5`
because the Finding Details dialog only opens for a finding carrying `Details`, which a style rule does
not produce), the six SVN ones (no server), `settings-reference-4` (that section renders only for an
SVN repository), `git-operations-6` (the merge dialog's ready-to-merge phase needs a clean working copy,
and MLQT only re-reads working-copy status after a VCS operation *in the application*), and anything
showing the window frame.

## Coverage

`MLQT.Shared` is in the ratchet at **80%**, measuring `.razor.cs` and **not** filtering `.razor`.
Measured, a component's `BuildRenderTree` is not counted at all, and the filter the plan called for
would have removed five ordinary classes from the report instead — which is the same failure mode one
step after fixing it. **Check what the merged report attributes before changing a bar or a filter; do
not lower the bar and stop asking.**

Two gate traps, both now guarded: a **baselined class that vanishes from the report reads as debt
paid**, and a suite whose report is older than the assembly it measures is judging yesterday's build.

## What the harness does and does not prove

- ✅ Shared UI logic is unchanged — Layers 1 and 1b, no host at all.
- ✅ The user journeys work, on Linux as well as Windows — Layer 2.
- ✅ The host resolves assets, runs interop, renders MudBlazor and Cytoscape, and its native services
  work — Layer 3.
- ❌ **Visual fidelity.** Nothing catches "MudBlazor looks subtly wrong under WebKitGTK". A human looks,
  once, per platform; screenshot diffing across two engines produces false positives on every glyph.
- ❌ **Native window behaviour** — multi-monitor DPI, state restore. Test manually.
- ❌ **Real file dialogs.** The probe asserts wiring, not that a dialog opens and returns a path.
- ❌ **That the markup is right.** Layer 1 never renders and the coverage gate ignores `.razor`. A
  component whose handler is fully tested can still render the wrong thing; Layer 2 is the only
  defence, and it covers journeys, not components.
