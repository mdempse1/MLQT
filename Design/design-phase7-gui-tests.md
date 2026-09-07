# Design Note — Phase 7a: GUI test harness (pre-migration)

> **Status: PROPOSED (2026-09-02).** Companion to phase 7 of the locked roadmap
> ([roadmap.md](roadmap.md) §1, "Desktop host migration (Photino, retire MAUI)"). This note
> covers the **test harness built before the host migration starts**, so the Photino build can
> be proved equivalent to the known-good MAUI build rather than eyeballed.
>
> MLQT has a large test suite across eight projects and **zero tests covering `MLQT.Shared`**.
> That gap is tolerable while one host exists and a human drives it daily. It stops being
> tolerable the moment the host is swapped, because there is then no mechanical answer to
> "does it still do what it did?".
>
> The headline constraint from the roadmap: MAUI is being *replaced*, not supplemented. There is
> no period where both hosts ship. So the baseline has to be captured **while MAUI still works**.

---

## Purpose

Build a test harness that

1. **survives the host swap unchanged** — the same tests run before and after, so a diff in
   results means a regression, not a rewrite;
2. **catches the regressions the migration will actually cause** — which are *not* in the
   component tree (that code does not change) but in the host, the composition root, the platform
   services, and the webview engine;
3. **is worth having afterwards** — this is the project's first UI test suite and its first
   Linux CI job; neither is throwaway scaffolding.

Non-goals: high line-coverage of Razor markup, screenshot/pixel diffing, testing MudBlazor itself,
and mobile (roadmap: desktop only).

---

## Current state — the portability boundary

`MLQT.Shared` is a plain `net10.0` Razor class library. It references `Microsoft.AspNetCore.Components.Web`,
MudBlazor, NLog and the four domain projects. **It has no MAUI reference of any kind.** That is the
single most important fact for this design: the UI is already host-agnostic, so a test that renders
components without a host is portable for free.

Everything non-portable lives in the `MLQT` project and is small:

| Surface | File | What the port changes |
|---|---|---|
| Composition root | [MauiProgram.cs](../MLQT/MauiProgram.cs) | 20 `AddSingleton` lines + `AddMauiBlazorWebView()` → Photino equivalent |
| Window / webview host | [MainPage.xaml](../MLQT/MainPage.xaml) | `BlazorWebView` + `RootComponent Selector="#app"` → `PhotinoBlazorApp` |
| Host page | [wwwroot/index.html](../MLQT/wwwroot/index.html) | 14 hand-ordered `<script>` tags + 4 `<link>`; `_content/MLQT.Shared/...` asset resolution |
| File dialogs | [Services/FilePickerService.cs](../MLQT/Services/FilePickerService.cs) | MAUI `FilePicker`/`FolderPicker` → GTK / Win32 |
| Settings | [Services/SettingsService.cs](../MLQT/Services/SettingsService.cs) | MAUI `Preferences` (registry-backed on Windows) → JSON file at an XDG/LocalAppData path |
| Power | [Services/PowerManagementService.cs](../MLQT/Services/PowerManagementService.cs) | `SetThreadExecutionState` P/Invoke → `org.freedesktop.ScreenSaver` / `caffeinate` |
| Webview engine | — | **WebView2 (Chromium) → WebKitGTK on Linux.** Not a code change; the largest behavioural risk. |

JS interop in `MLQT.Shared` is 19 call sites over 17 global functions:

```
getDimensions
cytoscapeGraph.init | update | relayout | highlight | clearHighlight | destroy
diffViewer.initSyncScroll | dispose
spellCheck.init | dispose | getScroll | setScroll | scrollWordIntoView | positionContextMenu
eval
open
```

All are `async` (`InvokeAsync`/`InvokeVoidAsync`), all are **global functions, not ES modules**, and
there is **no `IJSInProcessRuntime` usage anywhere**. This matters twice over: it makes bUnit's loose
JSInterop mode sufficient, and it makes a Blazor Server test host viable (see Layer 2), since neither
depends on webview-only synchronous interop.

`eval` and `open` are the two engine-sensitive ones. `window.open` in particular behaves differently
under WebKitGTK and may need routing through a native shell-open — a phase-7 work item, and a probe
in Layer 3.

---

## The central problem: portable ≠ proving

The obvious plan — "write bUnit tests over `MLQT.Shared`, then check they still pass on Photino" —
produces tests that **pass identically on both hosts by construction, while proving nothing about
either host**. They never load a webview, never resolve a static asset, never open a file dialog.
Green on Photino would be green even if the Photino app failed to start.

The inverse trap is just as real: end-to-end tests that drive the *actual* MAUI WebView2 through the
Chrome DevTools Protocol (`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=…`) work
fine on Windows today — and **do not port**, because Photino on Linux is WebKitGTK, which speaks the
WebKit remote inspector protocol, not CDP. Playwright cannot drive it. Tests written that way would
have to be thrown away at exactly the moment they were needed.

So the harness is three layers with explicitly different jobs, and the honesty about what each one
proves is part of the design:

| Layer | Runs against | Portable across hosts? | Proves |
|---|---|---|---|
| 1 — bUnit component tests | no host at all | trivially (never sees a host) | shared UI logic unchanged by the migration's refactoring churn |
| 2 — Playwright over a Blazor Server test host | a real browser + a third host | yes (never references MAUI or Photino) | user journeys work end-to-end; runs on Linux CI |
| 3 — `/selftest` conformance route | the real MAUI app, then the real Photino app | **yes — same route, same assertions, different host** | the host itself: assets, interop, engine, native services |

Layer 3 is the only one that answers the original question, "does Photino behave like MAUI?" Layers 1
and 2 are what stop the migration breaking the shared code on the way there.

---

## Layer 1 — bUnit component tests (`MLQT.Shared.Tests`)

### Project

New `MLQT.Shared.Tests/MLQT.Shared.Tests.csproj`, matching the conventions of the existing eight test
projects (xUnit + Moq + coverlet), adding bUnit:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="bunit" Version="1.40.0" />
    <PackageReference Include="coverlet.collector" Version="10.0.1" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.7.0" />
    <PackageReference Include="Moq" Version="4.20.72" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MLQT.Shared\MLQT.Shared.csproj" />
  </ItemGroup>
  <ItemGroup><Using Include="Xunit" /></ItemGroup>
</Project>
```

Note `Sdk.Razor`, not plain `Sdk` — required so `.razor` test files (bUnit's razor-syntax tests) and
the MudBlazor RCL assets resolve. Pin the bUnit version at whatever is current when the project is
created; 1.40 is the last version verified against xUnit v2.

### Shared test context

MudBlazor needs three things bUnit does not give you by default, and getting them wrong produces
confusing "component not rendering" failures rather than clear errors. Put them in one base class:

```csharp
/// <summary>
/// bUnit context preconfigured for MLQT's MudBlazor components: MudBlazor services,
/// loose JS interop (all of MLQT's interop is async global functions — none of it is
/// meaningful in a headless renderer), and the provider components that MudBlazor's
/// dialogs, popovers and snackbars render into.
/// </summary>
public abstract class MlqtComponentTestBase : TestContext
{
    protected MlqtComponentTestBase()
    {
        Services.AddMudServices(options =>
        {
            // Popover rendering in bUnit needs the provider explicitly rendered (below);
            // disabling the check removes a spurious warning from every test.
            options.PopoverOptions.CheckForPopoverProvider = false;
        });
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<AppState>();
    }

    /// <summary>
    /// Renders MudBlazor's provider components. Required before any test that opens a
    /// dialog, a select/menu popover, or asserts on a snackbar.
    /// </summary>
    protected void RenderProviders()
    {
        RenderComponent<MudPopoverProvider>();
        RenderComponent<MudDialogProvider>();
        RenderComponent<MudSnackbarProvider>();
    }
}
```

Every domain service is injected through an interface (`ILibraryDataService`, `IRepositoryService`,
`IStyleCheckingService`, …), so Moq covers them with no production change. `AppState` is a concrete
class but has no dependencies — register the real one and assert on its events.

### What to test, in priority order

Prioritised by *logic density per line of markup*, not by component size:

1. **`LibraryBrowser.razor`** (1186 lines) — the highest-value target and the one with a known
   regression history. `LoadServerData` lazy-loading, `OnNodeChildrenLoaded` write-back, selection in
   single vs multi mode, VCS status icon mapping, the debounced rebuild that preserves expansion
   state. The MudTreeView lazy-load selection regression — nested-node selection silently breaking on
   a MudBlazor 9.4 upgrade — is precisely a bUnit test: render the tree, expand a node via
   `ServerData`, click a grandchild, assert `AppState.ModelID`. It would have failed on the upgrade
   commit instead of in manual use.
2. **Dialogs** (`CommitChangesDialog`, `CreateBranchDialog`, `RevertFilesDialog`,
   `GitRebaseDialog`, `MergeBranchDialog`, `AddRepositoryDialog`) — self-contained, parameter-in /
   result-out, heavy on validation rules (e.g. commit-message issue-number enforcement from
   `CommitRequiresIssueNumber` / `IssueNumberAtEnd`). Cheapest tests in the suite per unit of value.
3. **`SettingsRepositories.razor`** (885 lines) — change detection driving
   `RepositorySettingsApplied(repositoryId, formattingChanged, styleSettingsChanged)`. The two
   booleans decide whether a full reformat runs; getting them wrong is expensive and invisible.
   Assert the exact flags for each kind of edit.
4. **`ChangeReview.razor`, `CodeViewer.razor`, `DiffViewer.razor`** — findings filtering, baseline
   status grouping (new / touched / accepted), diff view modes.
5. **`SettingsUI.razor`, `NamingStyleSelect.razor`, `RuleSeverityPicker.razor`** — presets and
   two-way binding.

**Deliberately not tested at the DOM level:** `MainLayout.razor` (3054 lines) and
`CodeReview.razor` (2104). See the next section — the answer there is extraction, not a bigger test.

`CytoscapeGraph.razor` is also excluded: it is a thin wrapper whose entire behaviour is the six
`cytoscapeGraph.*` interop calls. Assert the *call sequence and payload* via
`JSInterop.VerifyInvoke("cytoscapeGraph.init")`; whether Cytoscape actually draws is a Layer 3
question, because that is engine-dependent.

### Coverage target

The 80% rule in CLAUDE.md is a poor fit for Razor components — much of a `.razor` file is markup with
no branches. Set the bar as **behavioural rather than numeric** for `MLQT.Shared`: every event
handler that mutates `AppState`, calls a service, or gates on a settings flag has at least one test.
Leave the >80% / >95% numeric targets applying to the existing assemblies as they do today.

---

## The `MainLayout` extraction (prerequisite, and independently worthwhile)

[MainLayout.razor](../MLQT.Shared/Layout/MainLayout.razor) is 3054 lines injecting 13 services and
holding the entire analysis pipeline: `RunStartUpAsync`, `FormatModifiedFilesAsync`,
`SaveAllLibrariesWithFormattingAsync`, `SaveChangedFilesWithFormattingAsync`,
`UpdateFileNodesAfterSave`, `TrimPackageModelicaCode`, `CleanupEmptyDirectories`, the four
`RunDeferred*Async` methods, `OnVcsFilesChanged`, `OnVcsModelsChanged`, `OnRepositorySettingsApplied`,
`RefreshLibrariesAsync`, `FormatChangedFilesForCommitAsync`.

This is the most consequential logic in the application and the least accessible to any test — it can
only be reached by rendering a layout with 13 mocks and firing events at it. It is also the code most
likely to be disturbed by the migration, since the migration edits the composition root feeding it.

**Extract it into `MLQT.Services/AnalysisPipelineService.cs` behind `IAnalysisPipeline`**, leaving
`MainLayout` as event wiring, dialog/progress UI, and theme handling. Then:

- the pipeline is testable in `MLQT.Services.Tests` with the existing patterns (temp directories via
  `Path.Combine(Path.GetTempPath(), "mlqt-…" + Guid.NewGuid().ToString("N"))`, as ~20 test classes
  already do) — no renderer, no mocks-of-mocks;
- Layer 2's test host and the real hosts share one implementation, so a journey test exercises the
  same pipeline the app runs;
- `MainLayout` shrinks to something a bUnit test can reasonably cover.

Scope guard: this is a **move**, not a redesign. The `OnVcsFilesChanged` fallback chain (pending
monitor changes → VCS status → all repo models) and the monitor pause/resume ordering are
load-bearing and already documented; port them verbatim and characterise them with tests *before*
touching them.

Sequencing: do this **before** Layer 2, because the test host needs to register the pipeline as a
service rather than instantiate a layout.

---

## Layer 2 — `MLQT.TestHost` + Playwright

### The idea

Add a **third host** — an ASP.NET Core Blazor Server app that mounts the same `Routes.razor` with the
same DI, fakes the three platform services, and serves a seeded fixture library. Playwright drives a
real browser against it over localhost.

Why this shape rather than automating the desktop app:

- It references neither MAUI nor Photino, so **the tests are unchanged by the migration**.
- It runs headless on **Windows and Linux** CI. The current
  [build-and-test.yml](../.github/workflows/build-and-test.yml) is `windows-latest` for every job;
  this is the project's first Linux job, and a prerequisite for the Linux UI claim anyway.
- **Building it is migration step zero.** Extracting the composition root out of `MauiProgram`,
  writing non-MAUI platform services, and serving `_content/…` through a standard pipeline is
  precisely the work Photino needs. The roadmap already lists Blazor Server as the fallback host — if
  the WebKitGTK spike goes badly, this project *is* the start of the fallback.

### Composition root extraction

Today `MauiProgram.CreateMauiApp` registers 20 services inline. Split it: everything host-independent
moves to `MLQT.Services/ServiceCollectionExtensions.cs`:

```csharp
/// <summary>
/// Registers every MLQT service that is independent of the desktop host. Hosts add
/// their own IFilePickerService, ISettingsService and IPowerManagementService, plus
/// the webview/renderer registrations, on top of this.
/// </summary>
public static IServiceCollection AddMlqtCore(this IServiceCollection services)
{
    services.AddSingleton<AppState>();
    services.AddSingleton<ILibraryDataService, LibraryDataService>();
    // … the 15 other host-independent registrations, verbatim from MauiProgram …
    return services;
}
```

`MauiProgram` becomes `AddMlqtCore()` + the three MAUI services + `AddMauiBlazorWebView()`. The
Photino host will be the same three lines with different implementations. `MLQT.TestHost` will be the
same three lines with fakes. **One list, three hosts** — which removes an entire class of migration
bug (a service quietly missing from the new host's registrations).

The invariant-culture setup at the top of `CreateMauiApp` is not host-specific either; move it into
`AddMlqtCore` so no host can forget it.

### Fake platform services

In `MLQT.TestHost/Services/`:

| Fake | Behaviour |
|---|---|
| `ScriptedFilePickerService` | Returns paths from a queue the test primes (`Enqueue(path)`); returns `null` when empty to simulate cancel. Removes the only truly un-automatable UI from the journeys. |
| `InMemorySettingsService` | Dictionary + the same JSON round-trip for complex types, so serialization bugs still surface. |
| `NoOpPowerManagementService` | Records call counts so "long operation prevented sleep" stays assertable. |

### The host page problem, and a guard worth having

The three hosts need different bootstrap scripts — `_framework/blazor.webview.js` for MAUI and
Photino, `_framework/blazor.server.js` for the test host — but must otherwise load **the same 14
library scripts in the same order**. `index.html` currently hardcodes them, and drift between the
MAUI and Photino copies would be silent and painful (Cytoscape extensions must load after
`cytoscape.min.js`, and `cose-base` after `layout-base`).

Define the list once in `MLQT.Shared/HostAssetManifest.cs` as an ordered `IReadOnlyList<string>`, and
add a test in `MLQT.Shared.Tests` that parses each host's `index.html` and asserts its `<script>` and
`<link>` sequence equals the manifest (ignoring the bootstrap script). Cheap, and it makes "the
Photino host page drifted" a build failure rather than a runtime mystery.

### Fixture data

Journeys need a real repository, not mocks. Build a `LibraryFixture` that, per test collection,
creates a temp directory containing:

- a small hand-written Modelica package (3–4 classes, one deliberately violating each of a handful of
  enabled rules, one `package.order`, one external resource reference) — extend the existing
  `ModelicaGraph.Tests/TestFiles/PackageExample.mo` rather than inventing a new one;
- a Git working copy around it, created with LibGit2Sharp exactly as `RevisionControl.Tests` does
  (`Repository.Init()` → `Commands.Stage()` → `repo.Commit()`), with one committed baseline and one
  uncommitted edit so the new/touched/accepted classification has something to classify;
- a `.mlqt/` directory with settings and dictionary, so per-repository settings paths are exercised.

Deliberately **no SVN fixture**: `RevisionControl.Tests` already documents why SVN integration cannot
run on CI (needs a live working copy and server), and that reasoning applies unchanged here.

### Determinism

The pipeline is asynchronous and partly background-threaded (`StyleCheckingService` workers,
`FileMonitoringService` debouncing). Playwright's auto-waiting handles UI settling, but "analysis has
finished" needs an explicit signal. Rather than sleeping:

- have `MLQT.TestHost` expose a `/testapi/idle` endpoint that awaits pipeline quiescence
  (`EnsureDependenciesAnalyzedAsync` completion + style-check queue empty + no pending monitor
  changes), and call it between journey steps;
- render a `data-mlqt-state="idle|busy"` attribute on the layout root in the test host only, so
  Playwright can `WaitForSelector("[data-mlqt-state=idle]")`.

Both are test-host-only; neither leaks into the shipped hosts.

### Running the host

`WebApplicationFactory`'s `TestServer` has no real socket, so Playwright cannot reach it. Start
Kestrel on an ephemeral port in an `IAsyncLifetime` fixture and read the assigned address:

```csharp
_app = builder.Build();
_app.Urls.Add("http://127.0.0.1:0");
await _app.StartAsync();
BaseUrl = _app.Services.GetRequiredService<IServer>()
    .Features.Get<IServerAddressesFeature>()!.Addresses.First();
```

Packages: `Microsoft.Playwright` + `Microsoft.Playwright.Xunit`. Browsers install via
`pwsh bin/Debug/net10.0/playwright.ps1 install chromium` (a CI step, plus a `webkit` install if the
rehearsal below is adopted).

### Journeys to cover

Six, chosen because each crosses a service boundary the migration disturbs:

1. **Open a project → tree populates → select a class → code shows.** The startup pipeline end to end.
2. **Run a style check → findings appear in Code Review → click a finding → the viewer scrolls to the
   line.** The core product loop.
3. **Edit a file on disk → the monitor reports a pending change → Refresh → the finding count
   updates.** `IFileMonitoringService` + the re-analysis path.
4. **Change a repository setting → formatting reruns → the file on disk changes.**
   `OnRepositorySettingsApplied` with both flags.
5. **Commit a change through the dialog → validation rejects a message with no issue number, accepts
   one with it → the working copy is clean afterwards.** VCS path without SVN.
6. **Open the Dependencies page → the Cytoscape graph reports N nodes → change layout.** Asserted via
   `page.EvaluateAsync` against the Cytoscape instance, not pixels.

### The optional WebKit rehearsal

Playwright ships a WebKit browser. Running the same journeys under `--browser webkit` on Linux is not
the same engine as WebKitGTK, but it is *much* closer than Chromium, and it will surface the CSS and
JS-feature differences that are going to bite in the Photino Linux build — for free, before the
Photino host exists. Recommended as a nightly job rather than a PR gate.

---

## Layer 3 — the `/selftest` conformance route

The piece that actually compares hosts. A route in **`MLQT.Shared`**, so it is literally the same code
running under MAUI, Photino and the test host — no per-host test rewriting.

`MLQT.Shared/Pages/SelfTest.razor` runs a fixed list of probes and renders a pass/fail table plus a
machine-readable JSON blob in a `<pre id="selftest-result">`. Probes, each chosen because it maps to a
specific way the migration can break:

| # | Probe | Breaks when |
|---|---|---|
| 1 | `IJSRuntime.InvokeAsync<BrowserDimension>("getDimensions")` returns non-zero w/h | interop or the inline `<script>` in index.html is lost |
| 2 | `fetch("_content/MLQT.Shared/app.css")` returns 200 | RCL static web assets are not served — **the single likeliest Photino failure** |
| 3 | Each of the 14 library scripts defines its expected global (`cytoscape`, `dagre`, `klay`, …) | script list or order drifted in the new host page |
| 4 | `cytoscapeGraph.init` on a 2-node graph, then read back node count | Cytoscape under a non-Chromium engine |
| 5 | `diffViewer.initSyncScroll` + `spellCheck.init` on a scratch element | the other two interop modules |
| 6 | MudBlazor: open a dialog, a select popover and a snackbar; assert each is in the DOM | MudBlazor's positioning JS under WebKitGTK |
| 7 | Roboto renders at the expected metrics (measure a span) | the Google Fonts `<link>` (network!) fails — likely offline or on a locked-down Linux box |
| 8 | `ISettingsService` write → read → delete round-trip, complex type included | `Preferences` → JSON file port |
| 9 | `ISettingsService` reports its backing path, asserted to exist and be writable | XDG vs LocalAppData path handling |
| 10 | `IPowerManagementService.PreventSleep()`/`AllowSleep()` do not throw | P/Invoke → D-Bus port |
| 11 | `IFilePickerService` reports availability (does **not** open a dialog) | picker not wired on the new host |
| 12 | `NLog` writes and the log file exists | logging path assumptions |
| 13 | `SvnToolLocator` resolves an `svn` client, or reports the fallback | the bundled `svn/` payload is Windows-only; Linux must use PATH |
| 14 | `CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == "."` | invariant-culture setup missed by the new composition root |

Probe 7 deserves a note: `index.html` pulls Roboto from `fonts.googleapis.com` at startup. That is a
network dependency in a desktop app and it will behave differently on a Linux box behind a proxy.
Worth fixing during the migration (bundle the font) — the probe is how you know it is fixed.

### How each host runs it

- **Test host:** a Playwright test navigates to `/selftest` and asserts the JSON. Runs on every PR.
- **MAUI:** a small launcher test starts the app with `MLQT_SELFTEST=1`, which makes the app navigate
  to `/selftest` and, on completion, write the JSON to the file named by `MLQT_SELFTEST_OUT` and exit
  0/1. The test asserts on the file. No UI automation framework, no CDP, ~50 lines.
- **Photino:** identical mechanism, identical assertions, different `Program.cs`.

**Capture the MAUI baseline JSON and commit it** as
`MLQT.Shared.Tests/TestFiles/selftest-baseline-maui.json`. Photino conformance is then a diff against
a file, and any probe that legitimately differs is a deliberate, reviewed change rather than a shrug.
This artefact is the whole point of doing this work before the migration, and it cannot be produced
afterwards.

---

## What this does and does not prove

Stated plainly, because the temptation to over-claim here is strong:

- ✅ Shared UI logic is unchanged by the migration — Layer 1.
- ✅ The user journeys work, on Linux as well as Windows — Layer 2.
- ✅ The host resolves assets, runs interop, renders MudBlazor and Cytoscape, and its native services
  work — Layer 3.
- ❌ **Visual fidelity.** Nothing here catches "MudBlazor looks subtly wrong under WebKitGTK". That is
  a human looking at it, once, per platform. Screenshot diffing across two different engines produces
  false positives on every glyph and is not recommended.
- ❌ **Native window behaviour** — multi-monitor DPI, window state restore, tray/menu integration.
  Photino's surface differs from MAUI's here and the difference is intentional; test manually.
- ❌ **Real file dialogs.** Probe 11 asserts wiring, not that a GTK dialog opens and returns a path.
  Manual, once per platform.

---

## Ordered work breakdown

Each step compiles and leaves the suite green.

| Step | Work | Size |
|---|---|---|
| **7a-1** | `MLQT.Shared.Tests` project + `MlqtComponentTestBase` + first `LibraryBrowser` tests (lazy-load, selection) | S |
| **7a-2** | Dialog tests; `SettingsRepositories` change-detection tests; `ChangeReview`/`CodeViewer`/`DiffViewer` | M |
| **7a-3** | Extract `IAnalysisPipeline` out of `MainLayout`; characterisation tests in `MLQT.Services.Tests` | **L — the long pole** |
| **7a-4** | `AddMlqtCore()` extraction; `HostAssetManifest` + the index.html drift test | S |
| **7a-5** | `SelfTest.razor` + probes; MAUI launcher test; **commit the MAUI baseline JSON** | M |
| **7a-6** | `MLQT.TestHost` + fakes + `LibraryFixture`; first two journeys | M |
| **7a-7** | Remaining journeys; CI wiring (Linux job, Playwright install, nightly WebKit run) | M |

7a-5 is the step with a deadline attached — it must land while the MAUI build is the reference
implementation. If phase 7 has to start early, 7a-1, 7a-3, 7a-4 and 7a-5 are the non-negotiable
subset; 7a-2 and the journeys can trail the migration.

---

## CI changes

Add to [build-and-test.yml](../.github/workflows/build-and-test.yml):

- `dotnet test MLQT.Shared.Tests` in the existing `build-libraries` job (Windows; no new deps).
- A new **`ui-journeys`** job on `ubuntu-latest`: restore, build `MLQT.TestHost` + the Playwright
  test project, `playwright.ps1 install --with-deps chromium`, run. Note this job must **not**
  install the MAUI workload — it is also the first proof that the non-MAUI projects build on Linux,
  which is a phase-7 deliverable in its own right.
- Nightly (`schedule:`) variant running the journeys under `--browser webkit`.
- Once the Photino host exists: a `selftest` job per platform diffing against the committed baseline.

Playwright traces on failure (`--trace on-first-retry`) uploaded as an artifact — journey failures on
a headless Linux runner are otherwise near-undebuggable.

---

## Key decisions & risks

**Blazor Server as the test host is a deliberate approximation.** It is not the webview render mode
the product ships. It differs in: circuit-based reconnection (irrelevant here), no
`IJSInProcessRuntime` (MLQT does not use it — verified), and serialization of interop arguments over
a circuit rather than in-process (same API surface, different latency). The approximation is
acceptable because Layer 3 covers what it misses. **If a journey ever needs behaviour Blazor Server
cannot express, that is a signal to promote the test to Layer 3, not to weaken Layer 3.**

**Photino on .NET 10 is unverified.** `Photino.Blazor` has historically trailed .NET releases. Confirm
a `net10.0`-compatible build exists *in the WebKitGTK spike that opens phase 7* — before any of the
host work is scheduled. If it does not, the fallback host in the roadmap becomes the primary and
`MLQT.TestHost` becomes production code, which is another reason to build it properly now.

**Static web asset serving under Photino is the highest-probability failure.** `_content/<RCL>/…`
resolution in `Microsoft.AspNetCore.Components.WebView` depends on the static web assets manifest
being discoverable at runtime. This is a known rough edge in non-MAUI webview hosts. Probe 2 exists
specifically for it; expect to spend time here.

**Do not drive WebView2 via CDP.** Repeated because it is the tempting shortcut and it produces work
that must be discarded: it cannot be carried to WebKitGTK.

**MudBlazor + bUnit has known friction** — popovers and dialogs need their providers rendered, and
some interactions need `cut.WaitForAssertion(...)`. Budget for a slow first week on 7a-1; the pattern,
once established in the base class, generalises.

**7a-3 is a refactor of the most delicate code in the app.** The mitigation is ordering:
characterisation tests first, capturing current behaviour including its quirks, and only then the
move. If it slips, Layers 1 and 3 still deliver independently — Layer 2 is the only thing that hard
depends on it.

---

## Incidental finding

`MLQT.Shared/wwwroot/filePicker.js` is referenced from no C#, Razor or HTML in the solution —
presumably dead since the picker moved to the native MAUI service. Delete it during 7a-4 rather than
carrying it into the manifest.
