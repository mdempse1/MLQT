# Design Note — Phase 7a: making the UI testable, then testing it

> **Status: COMPLETE (proposed 2026-09-02, restructured and delivered 2026-09-07). A historical
> record — read it as one.** All seven steps are shipped, including the MAUI conformance baseline —
> the one piece with a deadline, which could not have been captured after the migration started, and
> which is now the only surviving artefact of the MAUI host:
> `MLQT.Shared.Tests/TestFiles/selftest-baseline-maui.json`, frozen, because the host that produced it
> was deleted in 7b-8. Nothing should ever regenerate it. §7b, the Photino migration it unblocked, is
> complete too ([design-phase7b-photino.md](design-phase7b-photino.md)). Each step's own section
> carries a *Shipped* note recording what actually landed and where it differed from the sketch.
> Companion to phase 7 of the locked roadmap
> ([roadmap.md](roadmap.md) §1, "Desktop host migration (Photino, retire MAUI)"). This note covers
> everything built **before the host migration starts**, so the Photino build can be proved
> equivalent to the known-good MAUI build rather than eyeballed. §7b at the end sketches the
> migration itself.
>
> The problem this note opened on: MLQT had a large test suite across eight projects and **zero tests
> covering `MLQT.Shared`**. That gap is tolerable while one host exists and a human drives it daily.
> It stops being tolerable the moment the host is swapped, because there is then no mechanical answer
> to "does it still do what it did?".
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

Non-goals: high line-coverage of Razor *markup*, screenshot/pixel diffing, testing MudBlazor itself,
and mobile (roadmap: desktop only).

---

## The decision this note now turns on: code-behind

**Component logic moves out of `@code { }` blocks into `.razor.cs` partial classes, and the project
adopts that as policy** — recorded in [CODING_GUIDELINES.md](../CODING_GUIDELINES.md) §Blazor
Patterns, *Code-Behind Files vs `@code` Blocks*, and adapted from the workspace-wide
`Claytex.Net/Coding_Guidelines.md`, where the same pattern is already established.

This was not in the first draft of this note, which was bUnit-first throughout. It changes the plan
more than it looks like it should, so the reasoning is worth stating plainly.

The measurement this turned on, taken before 7a-1: `MLQT.Shared` was **16,652 lines of `.razor`
across 39 components, of which 11,935 sat inside `@code { }` blocks** — 71% of the project's UI
source was C# that no test could reach without starting a renderer, and there were **zero `.razor.cs`
files**. Every one of those 11,935 lines was reachable only by rendering the component, wiring
MudBlazor's service graph and provider tree, faking JS interop, and driving the DOM.

With the logic in a partial class, most of it becomes an ordinary C# type: construct it, set
`[Inject]` and `[Parameter]` properties, call the handler, assert. No renderer at all. That is the
difference between a suite that is expensive to write and slow to run, and one that looks like the
other eight test projects in this solution and runs in the same seconds.

So the layering changes:

| | First draft | This plan |
|---|---|---|
| Primary component tests | bUnit render + DOM assertions | **plain xUnit + Moq against `.razor.cs` partials** |
| bUnit | everything | **only what genuinely needs a render tree** — lazy-load trees, dialog/popover flows, parameter reactivity, two-way binding |
| Prerequisite | none | **the code-behind sweep (7a-1)**, which every later step depends on |

bUnit does not go away. It earns its place for a specific and small set of behaviours — the
MudTreeView lazy-load selection regression cannot be reproduced without a render tree, and neither
can "the dialog closed with the right result". But it stops being the default answer, which is what
made the original plan's first week look like a slow one.

### The sweep is also worth doing on its own merits

Two of the four borderline components inspected while sizing this work had defects visible the moment
the code was read as C# rather than as part of a `.razor` file:

- `Dialogs/ProjectSelectionDialog.razor` — `_projects.FirstOrDefault(p => p.Id == settings.ActiveProjectId)!.Id ?? _projects.FirstOrDefault()!.Id`
  throws `NullReferenceException` when no project matches the active id, and the `??` fallback is
  dead code because `.Id` is non-nullable. It is also two uses of the null-forgiving operator, which
  [CODING_GUIDELINES.md](../CODING_GUIDELINES.md) forbids outright and its summary checklist asks
  about on every commit.
- `Components/CurrentModelDisplay.razor` — the same eight-line file-name resolution written twice in
  two adjacent handlers.

Neither is a phase-7 problem. Both are the ordinary consequence of 11,935 lines living somewhere no
test, no coverage gate and no reviewer's habits reach. **Fix them in their own commits after the
move, not during it** — see the conversion rule below.

---

## Current state — the portability boundary

`MLQT.Shared` is a plain `net10.0` Razor class library. It references `Microsoft.AspNetCore.Components.Web`,
MudBlazor, NLog and the four domain projects. **It has no MAUI reference of any kind.** That is the
single most important fact for this design: the UI is already host-agnostic, so a test that renders
components without a host is portable for free — and a test that does not render at all is more so.

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
under WebKitGTK and may need routing through a native shell-open — a phase-7b work item, and a probe
in Layer 3.

---

## The central problem: portable ≠ proving

The obvious plan — "write tests over `MLQT.Shared`, then check they still pass on Photino" —
produces tests that **pass identically on both hosts by construction, while proving nothing about
either host**. They never load a webview, never resolve a static asset, never open a file dialog.
Green on Photino would be green even if the Photino app failed to start.

The inverse trap is just as real: end-to-end tests that drive the *actual* MAUI WebView2 through the
Chrome DevTools Protocol (`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=…`) work
fine on Windows today — and **do not port**, because Photino on Linux is WebKitGTK, which speaks the
WebKit remote inspector protocol, not CDP. Playwright cannot drive it. Tests written that way would
have to be thrown away at exactly the moment they were needed.

So the harness is four layers with explicitly different jobs, and the honesty about what each one
proves is part of the design:

| Layer | Runs against | Portable across hosts? | Proves |
|---|---|---|---|
| 1 — direct unit tests on `.razor.cs` partials | no renderer at all | trivially | the bulk of shared UI logic; the cheapest and fastest tests in the suite |
| 1b — bUnit render tests | no host, but a real render tree | trivially (never sees a host) | the render-dependent minority: lazy-load trees, dialog results, parameter reactivity |
| 2 — Playwright over a Blazor Server test host | a real browser + a third host | yes (never references MAUI or Photino) | user journeys work end-to-end; runs on Linux CI |
| 3 — `/selftest` conformance route | the real MAUI app, then the real Photino app | **yes — same route, same assertions, different host** | the host itself: assets, interop, engine, native services |

Layer 3 is the only one that answers the original question, "does Photino behave like MAUI?" Layers 1
and 2 are what stop the migration breaking the shared code on the way there.

---

## 7a-1 — the code-behind sweep

**One behaviour-neutral pass over `MLQT.Shared`, before any test is written.**

### Why first, and not per-component-as-tested

The alternative — convert a component only when its tests are about to be written — was rejected. It
keeps each diff smaller, but it leaves the policy unenforced for the length of phase 7a, which means
new components keep arriving in the shape the phase exists to eliminate, and the convention guard
(7a-2) cannot be switched on until the very end. Enforcement that arrives last enforces nothing.

### Why before the service extraction, not after

The roadmap's B73 declined to touch `MainLayout.razor` on the grounds that doing so "would move the
same code twice". That reasoning applies to *extracting* it twice, not to this. The sweep is a
contiguous cut-and-paste that the compiler verifies: the generated type is the same type, the members
are the same members. What it changes is the ground the extraction then happens on — a 2,606-line C#
file where "move this method to a service" is a refactoring the IDE can perform and `git diff` can
show, instead of a Razor file where it is a manual edit nobody can review with confidence. The first
move is free; the second is the work; doing the free one first makes the expensive one cheaper.

### Inventory

Of 39 components (excluding `_Imports.razor`), **31 convert and 8 stay as they are**:

| Convert | `@code` lines | Convert | `@code` lines |
|---|---:|---|---:|
| `Layout/MainLayout.razor` | 2,606 | `Components/SettingsUI.razor` | 186 |
| `Pages/CodeReview.razor` | 1,988 | `Dialogs/CommitChangesDialog.razor` | 141 |
| `Components/LibraryBrowser.razor` | 917 | `Components/SettingsRepositoryDictionary.razor` | 140 |
| `Pages/MetricsDashboard.razor` | 726 | `Components/CodeViewer.razor` | 138 |
| `Components/DiffViewer.razor` | 666 | `Dialogs/RevisionDiffDialog.razor` | 126 |
| `Pages/ExternalResources.razor` | 586 | `Components/SettingsExternalTools.razor` | 121 |
| `Components/SettingsRepositories.razor` | 470 | `Components/CytoscapeGraph.razor` | 114 |
| `Dialogs/VCSHistory.razor` | 468 | `Dialogs/CreateBranchDialog.razor` | 112 |
| `Components/ChangeReview.razor` | 357 | `Components/SettingsReferenceLibraries.razor` | 90 |
| `Dialogs/GitRebaseDialog.razor` | 305 | `Dialogs/CreatePullRequestDialog.razor` | 80 |
| `Dialogs/MergeBranchDialog.razor` | 269 | `Dialogs/SwitchBranchDialog.razor` | 73 |
| `Dialogs/GitMergeBranchDialog.razor` | 267 | `Dialogs/RevertFilesDialog.razor` | 58 |
| `Dialogs/AddRepositoryDialog.razor` | 246 | `Components/NamingStyleSelect.razor` | 54 |
| `Components/BranchSelector.razor` | 219 | `Components/ColorPicker.razor` | 43 |
| `Pages/Dependencies.razor` | 208 | `Components/CurrentModelDisplay.razor` | 42 |
| | | `Dialogs/ProjectSelectionDialog.razor` | 36 |

**Stay as `@code`** — no logic worth a test: `RuleSeverityPicker` (a colour switch and a two-way
binding relay), `RuleSeverityRow`, `ErrorDialog`, `ConfirmDeleteProjectDialog`, `ConflictDiffDialog`,
`Pages/Settings`, `Routes`, `Pages/Index`.

The three smallest in the convert column are there on judgement, not line count: `ColorPicker` has
hex validation and formatting, `CurrentModelDisplay` subscribes to two `AppState` events, and
`ProjectSelectionDialog` reads settings and builds a placeholder project — and, as noted above, has a
live `NullReferenceException` in that code.

### The rule for each conversion commit

**One component per commit, and the commit is a move.** No renames, no signature changes, no fixes,
no reordering beyond the member order the guidelines specify. Specifically:

1. Create `Foo.razor.cs` with `namespace MLQT.Shared.<Folder>;` and `public partial class Foo`,
   carrying across the interface list (`IDisposable`, `IAsyncDisposable`) from the `.razor` file's
   `@implements` directives.
2. Move the whole `@code { }` body across unchanged; delete the block.
3. Convert every `@inject X Y` directive to `[Inject] private X Y { get; set; } = null!;` — **114
   directives across the project**, mechanical. This is required, not cosmetic: `@inject` generates
   the property in the `.razor.g.cs` half, where a test cannot set it.
4. Add the `using` directives the partial needs — a `.razor.cs` does not see `_Imports.razor`.
5. Build. The compiler is the check: same type, same members, same generated component.

Anything the move reveals — the two defects above, a dead field, a `!` that should not be there —
gets its own follow-up commit with its own reason. Mixing them makes the move unreviewable, which is
the one thing that would make this sweep a bad idea.

`MLQT.Shared` gains `[assembly: InternalsVisibleTo("MLQT.Shared.Tests")]` in 7a-2, so members the
tests call directly can be `internal` rather than reached by reflection.

**Size: L, but shallow.** 31 mechanical commits, of which three (`MainLayout`, `CodeReview`,
`LibraryBrowser`) are large enough to want their own review.

### ✅ Shipped (2026-09-07)

All 31 converted, in ten commits — the three largest one each, the rest batched by kind. Solution
builds clean in Release with 0 warnings; all 4,453 tests pass. `MainLayout`, `CodeReview` and
`LibraryBrowser` were verified line-by-line against their pre-move source and are byte-identical.

Four things the sketch above did not anticipate, each of which landed as its own commit:

- **`MLQT.Shared/GlobalUsings.cs`.** A `.razor.cs` does not read `_Imports.razor`, so without this
  every converted file would open with the same twenty-line using block and the two halves of one
  class would disagree about what is in scope. The list is `_Imports.razor`'s minus the entries only
  markup references. `Microsoft.AspNetCore.Components.Web` looks like one of those and is not — the
  DOM event-argument types live there and appear in handler signatures.
- **`<SupportedPlatform Include="browser" />` removed from the csproj.** Moving
  `CreatePullRequestDialog` surfaced three CA1416 warnings on its `Process.Start`: Razor-generated
  code is excluded from analyzers, so the `@code` block had been hiding them. The call is not the
  problem — the declaration is template boilerplate that was never true, in a project that reads and
  writes files directly in three other components. Removing it also stopped the rest of the sweep
  regenerating the same false warning each time it moved a file-writing handler out of markup.
- **`SharedUiConventionTests` broke silently, and that is the finding worth keeping.** All four
  sweeps enumerate `*.razor`, which was the whole of the C# when they were written. After the sweep
  those files are markup: every check still ran, over nothing, and all five tests still passed — the
  exact failure the file's own comment says it exists to prevent, reintroduced by the commit that
  moved the code. `TheSweepCanSeeTheSource` did not catch it because it counted *files*, and
  `*.razor` still returned 39 of them. It now asserts the enumeration actually contains event
  subscriptions, verified by restoring the markup-only scan and watching the guard fail. **The
  lesson for 7a-2's three new guards: a guard that counts inputs does not prove it read the right
  ones.**
- **`@inherits` stays in the markup** while `@implements` moves to the partial. The base class
  belongs to the component declaration and the compiler wants it in one half only; the interfaces
  belong next to the methods that implement them.

Deferred to their own commits, as the rule requires: `ProjectSelectionDialog`'s
`NullReferenceException` and `CurrentModelDisplay`'s duplicated resolution are both still there.

---

## 7a-2 — `MLQT.Shared.Tests`, and the convention guards

### Project

New `MLQT.Shared.Tests/MLQT.Shared.Tests.csproj`, matching the conventions of the existing eight test
projects (xUnit + Moq + coverlet), adding bUnit for Layer 1b:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="bunit" Version="2.9.0" />
    <PackageReference Include="coverlet.MTP" Version="10.0.1" />
    <PackageReference Include="Microsoft.Testing.Extensions.TrxReport" Version="2.3.3" />
    <PackageReference Include="Moq" Version="4.20.72" />
    <PackageReference Include="xunit.v3" Version="4.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MLQT.Shared\MLQT.Shared.csproj" />
  </ItemGroup>
  <ItemGroup><Using Include="Xunit" /></ItemGroup>
</Project>
```

Note `Sdk.Razor`, not plain `Sdk` — required so `.razor` test files (bUnit's razor-syntax tests) and
the MudBlazor RCL assets resolve. The project is also `OutputType=Exe` with the
Microsoft.Testing.Platform runner properties, like every other suite here. Add it to `MLQT.slnx`.

### The four existing sweeps move here

[`MLQT.Services.Tests/SharedUiConventionTests.cs`](../MLQT.Services.Tests/SharedUiConventionTests.cs)
already reads the `MLQT.Shared` source from a service test, and its own doc comment says it belongs in
`MLQT.Shared.Tests` "the day it exists". Move it, unchanged, as the first file in the new project —
including its deliberate refusal to skip when it cannot find the source.

### Three new guards, which is what makes the policy hold

The recurring defect shape in this repository is a rule stated in a document that nothing enforces —
the roadmap's B97 is the entry that named it, and B101–B103 are the most recent instance. The
code-behind policy is exactly that shape, so it gets tests in the same file:

1. **No `@code` block over 25 lines**, except for components in a committed ledger carrying a
   `reason` per entry — the same shape as `build/coverage-baseline.json`, and for the same reason:
   "this one is genuinely a display surface" and "nobody has converted this yet" are different facts
   and the ledger should say which. The eight thin components above are its initial contents.
2. **No `@inject` directive in a component that has a `.razor.cs`** — a directive-injected service
   cannot be set by a test, so this is the guard that stops the sweep quietly unravelling.
3. **Every `.razor.cs` declares a `partial class` matching its file name, in the namespace matching
   its folder.** The failure is a build error rather than a silent one, but the test names the rule
   where someone will read it.

### ✅ Shipped (2026-09-07)

`MLQT.Shared.Tests` exists, `SharedUiConventionTests` moved into it, and `CodeBehindPolicyTests`
holds six checks rather than the three sketched above. All six were verified by breaking the source
and watching each fail — which is the only reason two of them are worth having, and which changed
one of them:

- The namespace/partial-class guard **can only fire against sources the build has not seen**, because
  a mismatch is a compile error. On its own that is close to the vacuous guard 7a-1 warned about, so
  a sixth check was added beside it — **`NoCodeBehindIsOrphaned`** — for the failure the compiler
  genuinely cannot see: rename or delete a `.razor` and leave its `.razor.cs`, and everything still
  builds while a whole class in a plausible namespace is paired with no component and never runs.
- The size limit is **30 lines**, not 25. The largest block left after 7a-1 is `RuleSeverityPicker`
  at 25, and a limit one line above the largest survivor trips on the next parameter anyone adds.
- **The ledger is empty**, against the sketch above, which said to seed it with the eight thin
  components. None of them needs exempting — all eight are comfortably under the limit — and an
  empty ledger states that fact, where eight entries would have read as eight accepted debts.

`MLQT.Shared` gained `[InternalsVisibleTo]` for the test project, and the suite is wired into the
`build-libraries` CI job.

bUnit was pinned at 1.40.0 for one day, because 2.x requires xUnit v3 and every other suite was on
v2. **That was undone immediately**: rather than start the project's first UI tests on a superseded
framework, all nine test projects moved to xUnit v3 — see the note below. bUnit is 2.9.0, which also
drops the AngleSharp advisory the 1.40 pin existed to work around.

### Shared bUnit context (Layer 1b only)

MudBlazor needs three things bUnit does not give you by default, and getting them wrong produces
confusing "component not rendering" failures rather than clear errors. Put them in one base class,
used only by the render tests:

```csharp
/// <summary>
/// bUnit context preconfigured for MLQT's MudBlazor components: MudBlazor services,
/// loose JS interop (all of MLQT's interop is async global functions — none of it is
/// meaningful in a headless renderer), and the provider components that MudBlazor's
/// dialogs, popovers and snackbars render into.
///
/// Most component tests should not need this: with the logic in a .razor.cs partial,
/// the handler can be called directly. Use it only where the behaviour under test is
/// the render tree itself.
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
`IStyleCheckingService`, …), so Moq covers them with no production change — in both layers.
`AppState` is a concrete class with no dependencies: construct the real one and assert on its events.

---

## Interlude — the whole solution moved to xUnit v3 (2026-09-07)

Not a planned step. 7a-2 pinned bUnit at 1.40 because 2.x needs xUnit v3 and the other eight suites
were on v2 — which meant starting the project's first UI tests on a superseded framework, and that
was the wrong trade. Measuring the migration rather than guessing at it showed why:

**The source cost was nearly nothing.** No `IAsyncLifetime` anywhere (the usual breaker); two files
importing `Xunit.Abstractions`, which is where `ITestOutputHelper` used to live; 14 fixture and
collection usages, API unchanged; 8,669 `Assert.*` calls, API unchanged.

**The cost is the runner, and it is all-or-nothing.** On the .NET 10 SDK the VSTest target refuses to
run a Microsoft.Testing.Platform project at all, and `global.json`'s MTP opt-in applies to every
project in the repository — *"All projects must use that test runner"*. So a mixed repo was never an
option; nine projects or none. What changed:

| was | now |
|---|---|
| `xunit` + `xunit.runner.visualstudio` + `Microsoft.NET.Test.Sdk` | `xunit.v3`, projects as `OutputType=Exe` |
| `--collect:"XPlat Code Coverage"` (coverlet.collector) | `--coverlet --coverlet-output-format cobertura` (coverlet.MTP) |
| `--logger "trx;LogFileName=x"` | `--report-trx --report-trx-filename x` |
| `--nologo`, `-v q` | not MTP options — they are errors, and the failure looks like "0 tests ran" |
| `--filter "FullyQualifiedName!~Svn"` | unchanged; MTP keeps VSTest filter syntax |

**Every suite kept its exact test count** — 1871 / 819 / 795 / 11 / 295 / 291, and 377 with the SVN
filter — which is what proves nothing was silently dropped.

Four things it turned up that were already there:

- **`ModelicaParser.Tests/TestDebug.cs`** was a committed top-level-statements scratch file with no
  tests in it, present since the initial commit. As a library it was harmless dead weight, and the
  `NoWarn CS7022` in that csproj existed to hide the warning it caused. As an `Exe` its statements
  *become the entry point*, so the whole 1,871-test suite silently ran nothing. Deleted, along with
  the suppression that had been covering for it.
- **An unused `using Newtonsoft.Json.Bson`** in `ComplexModelTests.cs`, which only compiled because
  `Microsoft.NET.Test.Sdk` supplied the package transitively.
- **230 `xUnit1051` warnings** from a new v3 analyzer — pass `TestContext.Current.CancellationToken`
  to anything that takes one. Suppressed in the three affected projects beside the `xUnit1031` that
  was already suppressed there, with the reasoning written in the csproj rather than left implicit.
- **The coverage ratchet has a hole**, recorded as backlog **B104**. The collector swap moved several
  classes' *coverable line* counts by one or two and shifted their percentages by under a point, all
  re-recorded. But `MLQT.McpServer::Program` left the report entirely — it is top-level statements,
  so compiler-generated, and `coverlet.MTP` excludes generated code where `coverlet.collector`
  measured it at 0%. That exclusion is *correct*, and matches what the script's own header says it
  wants. The problem is that the gate reported it identically to a class that had genuinely been
  fixed: a baselined class absent from the report reads as debt paid.

---

## 7a-3 — Layer 1: direct component tests

### What to test, in priority order

Prioritised by *logic density*, not component size:

1. **`SettingsRepositories.razor.cs`** — change detection driving
   `RepositorySettingsApplied(repositoryId, formattingChanged, styleSettingsChanged)`. The two
   booleans decide whether a full reformat runs; getting them wrong is expensive and invisible.
   Assert the exact flags for each kind of edit. Pure logic over a settings object — no renderer.
2. **Dialogs** (`CommitChangesDialog`, `CreateBranchDialog`, `RevertFilesDialog`, `GitRebaseDialog`,
   `MergeBranchDialog`, `AddRepositoryDialog`) — parameter-in / result-out, heavy on validation rules
   (commit-message issue-number enforcement from `CommitRequiresIssueNumber` / `IssueNumberAtEnd`).
   The validation predicates test directly; only "the dialog closed with `DialogResult.Ok(x)`" needs
   Layer 1b.
3. **`CodeReview.razor.cs`** — after the extraction in 7a-4, what remains is findings filtering
   (`FilterFunc`, `FilterFunc1`), the file-line mapping through `ClassLocation` (`FileLineOf`), the
   suppression eligibility rule (`CanSuppressRule`), element-prefix formatting, and view-mode
   selection. All pure functions over data; all currently unreachable.
4. **`MetricsDashboard.razor.cs`** — scope matching (`InScope`, `CountFindingsForScope`), sub-package
   derivation, trend series construction, `IsStyleDebt`. Pure; several are already `static`.
5. **`LibraryBrowser.razor.cs`** — `ToTreeItems`, `AnnotateVcsStatus`, `IsLibraryInRepository`,
   `GetParserErrorTooltip`, expansion-state capture and restore. The tree *building* is data; only
   the lazy-load *selection* needs Layer 1b.
6. **`ChangeReview`, `CodeViewer`, `DiffViewer`, `ExternalResources`** — findings filtering, baseline
   status grouping (new / touched / accepted), diff view modes, resource type filters.
7. **`ColorPicker`, `NamingStyleSelect`, `SettingsUI`** — validation, presets, hex round-tripping.

### Layer 1b — the render tests that earn their place

Deliberately short. A test belongs here only if the behaviour cannot exist without a render tree:

- **`LibraryBrowser` lazy-load selection.** The known regression: MudBlazor 9.4 broke nested-node
  selection, fixed by an `ItemsChanged` write-back of `ServerData` children. Render the tree, expand
  a node via `ServerData`, click a grandchild, assert `AppState.ModelID`. It would have failed on the
  upgrade commit instead of in manual use. This is the single most valuable bUnit test in the suite,
  and the reason bUnit is in the plan at all.
- **Dialog open → interact → `DialogResult`** for the six dialogs, one test each.
- **Two-way binding** on `ColorPicker`, `NamingStyleSelect`, `RuleSeverityPicker` — `ValueChanged`
  round-trips through a parent.
- **`CytoscapeGraph` interop sequence.** It is a thin wrapper whose entire behaviour is the six
  `cytoscapeGraph.*` calls; assert the call sequence and payload via
  `JSInterop.VerifyInvoke("cytoscapeGraph.init")`. Whether Cytoscape actually *draws* is a Layer 3
  question, because that is engine-dependent.

`MainLayout` gets **no DOM-level tests**. After 7a-4 it is event wiring and progress UI; the logic is
tested where it lands, in `MLQT.Services.Tests`.

### Progress (2026-09-07)

Started, not finished. **51 tests** in `MLQT.Shared.Tests`, covering the first three targets:

| Target | What is pinned |
|---|---|
| `SettingsRepositories.EffectOfEdit` | The two booleans that decide whether the repository is reformatted and re-checked, including the two rules that are wrong in opposite directions — a formatting rule changed while formatting is off requires nothing, and switching formatting on requires everything |
| `CommitChangesDialog` | The commit-message policy: when a commit is allowed, and where the issue number goes. The only implementation in the solution |
| `CodeReview` | `FileLineOf` (class-relative line → file line, through `ClassLocation`), `CanSuppressRule`, `ReportPathOf` |
| `LibraryBrowser` (Layer 1b) | The MudTreeView lazy-load selection regression, at two depths |
| `MetricsDashboard` | Scope matching (the dot that keeps `Modelica.BlocksExtra` out of a `Modelica.Blocks` scope), sub-package listing, what counts as style debt |
| `ColorPicker` (both layers) | What it accepts as a colour, and that a change round-trips back in a form the next render accepts |
| `CytoscapeGraph` (Layer 1b) | The six interop calls: init once with its elements, update rather than re-init, nothing on an unchanged re-render, destroy on disposal |
| `CodeViewer` | The HTML conversion: encoding, line numbering, and spell-check markup confined to strings and comments |
| `DiffViewer` | `ComputeLcsDiff`, the edit script every diff view is built on — minimality, operation order, and that replaying it reproduces either file |
| `ChangeReview` | The commit dialog's folder tree: Git `/` and SVN `\` normalised to one shape, folders before files, status on files only |
| `ExternalResources` | File-type classification, plus a guard that every category has a filter chip and every chip is a category |
| `NamingStyleSelect` | What the settings page accepts as a naming exception — anything accepted must compile |

**Every one was verified by mutation, not by going green.** Breaking the rule under test and watching
the specific test fail is the only thing that distinguishes a test from a formality — this phase has
already produced two guards that passed over nothing (`SharedUiConventionTests` after 7a-1, and the
namespace check in 7a-2), so it is the standing bar here rather than an extra.

The pattern that has emerged for Layer 1: **the decision extracts, the wiring stays**. Each of the
three took a rule out of a method that ends in `StateHasChanged()` — which needs a renderer — and
left it as an `internal static` function of its inputs, with the handler calling it. That is not a
test-shaped contortion; in every case the extracted rule is the part worth reading, and two of the
three shed a null-forgiving operator on the way out.

All three Layer 1b patterns now exist to copy: a lazy-loaded tree (`LibraryBrowser`), a two-way
binding (`ColorPicker`), and an interop call sequence (`CytoscapeGraph`). The bUnit 2 names that
differ from every v1 example online are recorded on `MlqtComponentTestBase`, along with the one that
is not a rename: raising a component's own callback has to go through `cut.InvokeAsync`, and a
planned `JSInterop.SetupVoid` stays pending until `SetVoidResult`, which stalls the component's own
`await` — under the loose mode this suite uses, read `JSInterop.Invocations[...]` instead.

**Mutation testing earned its place on the diff.** Three mutations of `ComputeLcsDiff` were tried;
the first pass of tests caught only one. Replaying the script and checking every line is accounted
for proves a script is *valid*, and an all-delete-then-all-insert script is valid too — so
`Math.Min` for `Math.Max` in the LCS table survived, and so did flipping the backtrack tie-break.
Catching them needed two more tests: one asserting the script is *minimal* on a pair where the
equality shortcut cannot answer, and one asserting a changed line is delete-then-insert, which the
side-by-side renderers depend on to pair the two halves of an edit on one row. Property tests that
check validity are not enough for an algorithm whose whole value is optimality.

**168 tests.** Mutation testing has now changed the tests three times rather than merely confirming
them, and the pattern in all three was the same: the obvious case does not discriminate. A diff's
property tests pass on a non-minimal script; the file tree's depth test used siblings that were
already in order, because the builder walks its input in path order; and the resource classifier's
`ToLowerInvariant` turned out to be redundant against an `OrdinalIgnoreCase` set — an *equivalent*
mutation, which is a legitimate outcome and got the dead call removed. Assume a test does not bite
until it has been watched failing.

### ✅ Shipped (2026-09-07) — the long tail, and the five defects in it

**317 tests.** The outstanding items were `SettingsUI` and the dialogs' results, and finishing them
found the same shape five times over.

**The three VCS dialogs were one dialog written three times.** `GitMergeBranchDialog`,
`MergeBranchDialog` (SVN) and `GitRebaseDialog` each held a **byte-identical** private copy of the
blocking-status list, the conflict-state map, the relative-path rule and the status glyph table — 127
lines of duplication, none of it reachable by a test. All three copies were wrong in the same way,
which is the argument against duplication stated as a defect rather than a principle: the
blocking-status list named five of `VcsFileStatus`'s six members, and the omitted one, `Renamed`, is
produced for a staged `git mv`. `GetWorkingCopyChanges` returns only files with real changes, so that
filter's *entire* effect was letting renames through — a user with a staged rename was told the
working copy was clean, offered the merge, and had git refuse it. **B106.**

The glyph copies had also drifted from `VcsStatusHelper`, which already held the same table for the
library browser and the change list and held it *more* completely. The extraction very nearly added a
fifth copy before that turned up; what stopped it was asking who else maps a status to an icon. An
untracked file was a green "new" badge in one window and a grey question mark in another.

**Two defects in `AddRepositoryDialog`, both invisible without a renderer.** The spinner went up
before four validation checks that `return` early, and the only place it came down was the `finally`
of a `try` that started after them — so a path that does not exist left the dialog spinning for the
rest of the session (**B109**). And a failed add set an error message and then fell through to
`Close(DialogResult.Ok(null))`, so the message was discarded with the dialog that would have shown it
and `MainLayout`, which checks `!result.Canceled`, treated the failure as success (**B110**). Both
fixes were watched failing against the old code before being kept.

**Two in the theme presets.** Changing the UI theme re-applies the syntax preset, which set
"is this palette hand-edited?" to `false` unconditionally — so switching light↔dark on a custom syntax
palette hid the colour pickers while the stored name still said Custom (**B108**, fixed: one function
answers that question now). And choosing "Light" resets all ten custom colours and persists
immediately, while "Dark" does not, and an unrecognised name falls back to Light *without* the reset
(**B107**, pinned by tests and left open — what happens to colours a user typed in is a product
decision, not a refactor). The ten literals were also a third copy of the `UISettings` defaults; that
half is fixed, by copying from a default instance.

**Layer 1b now has its dialog-result pattern**, which the note asked for and nothing had. It has to go
through `IDialogService` inside a rendered `MudDialogProvider`: `MudDialog.Close(DialogResult.Ok(x))`
reaches a cascaded `IMudDialogInstance` that a directly rendered component does not have, so every
`Close` and `Cancel` in one is a silent no-op and a test awaiting the result waits for ever.
`ShowDialogAsync` on `MlqtComponentTestBase` is that, and one more bUnit note joins the others there:
a MudBlazor field with `Immediate` wires `oninput` and never `onchange`, so `Change()` throws
`MissingEventHandlerException` — loudly, which is the one thing that made it quick to find.

Still open, deliberately: the merge and rebase dialogs have **no** result test. Their result is
reached through a multi-phase flow needing the whole `IRepositoryService` surface mocked, and the
decisions inside that flow — which changes block, how conflicts carry across rounds, when the commit
button lights — are now tested directly as `VcsConflictRules`. A test that mocked its way to
`Ok(_mergeResult)` would assert that the mocks were configured correctly.

Writing them turned up **B105** — `CodeReview.ReportPathOf` and the CLI's
`CheckReport.RelativeFileFor` implementing one rule twice, and `FileLineOf`/`LineFor` doing the same
with the other — **since fixed**: `MLQT.Services/Checking/ReportLocation.cs` is the single answer and
both surfaces call it, so their agreement is structural rather than promised.

They also caught two things written in this session rather than inherited: `SharedUiConventionTests`
found a doc comment I had just stranded above a better one on `IsStyleDebt`, which is the first time
7a-2's guards have fired on new work; and `ReportLocationTests` found that both copies of the
relative-path rule documented a fallback to absolute paths that neither ever performed.

---

## 7a-4 — the extraction out of the three largest components (roadmap B20/B73)

The sweep makes the logic testable. It does not make it *right* that the analysis pipeline lives in a
layout component, against this repository's own instruction to keep business logic in services, not
Razor components. B20 is that item, widened by B73 to name `MainLayout.razor` first of the three.

[MainLayout.razor](../MLQT.Shared/Layout/MainLayout.razor) is 3,080 lines, of which 2,606 are C#,
injecting 13 services and holding the entire analysis pipeline. Three destinations:

**→ `MLQT.Services/AnalysisPipelineService.cs` behind `IAnalysisPipeline`** — the work that would run
identically with no UI attached: `RunStartUpAsync`'s body, `LoadReferenceLibrariesAsync`,
`SaveAllLibrariesWithFormattingAsync`, `SaveChangedFilesWithFormattingAsync`,
`FormatModifiedFilesAsync`, `FormatChangedFilesForCommitAsync`, `GetModifiedFilePathsFromVcs`,
`UpdateFileNodesAfterSave`, `TrimPackageModelicaCode`, `CleanupEmptyDirectories`, `SkipReferenceOnly`,
`BuildModelToStyleSettingsMap`, `BuildModelToRepositoryMap`, `AnyEnabledRuleNeedsDependencies`, the
four `RunDeferred*Async` bodies, and the bodies of `OnVcsFilesChanged`, `OnVcsModelsChanged`,
`OnRepositorySettingsApplied` and `RefreshLibrariesAsync`. Progress is reported through a callback or
`IProgress<T>`, not by touching a dialog.

**→ `MLQT.Shared/Theming/MlqtTheme.cs`** — `GetDefaultPaletteLight`, `GetDefaultPaletteDark`,
`BuildCustomPalette`, `BuildTheme`. Four static pure functions over `UISettings`: a static class and
four tests.

**→ `MainLayout.razor.cs`** — event subscriptions, the startup/progress dialog state machine,
`ResetStartupSteps`, `CloseStartupDialog`, `GetRefreshTooltip`, `ApplyThemeFromSettings`, the
`SurfaceParserErrors` presentation, `Dispose`.

The same split applies, smaller, to the other two:

- **`CodeReview.razor`** — suppression writing (`ResolveClassSourceTarget`, `ReadTargetFileAsync`,
  `SaveAnnotatedFileAsync`, `SuppressRuleForFinding`) and the dictionary/spelling actions
  (`AddToDictionary`, `IgnoreSpellingFinding`, `ApplyCorrectionCore`) write files and belong in
  `MLQT.Services`; `ExportFindingsAsync`'s report construction belongs beside the other report
  writers. Filtering and formatting stay in the code-behind and get Layer 1 tests.
- **`MetricsDashboard.razor`** — snapshot persistence (`SaveSnapshot`, `LoadAllHistory`),
  `StorageGroups`, `OwningRepository`, `ReportableModels` and `StyleSettingsLookup` are computation
  over the graph and settings, and belong with the other metrics code in `ModelicaGraph/Analysis/` or
  a service beside it. Counting, scope matching and trend building stay in the code-behind.

**Scope guard: this is a move, not a redesign.** The `OnVcsFilesChanged` fallback chain (pending
monitor changes → VCS status → all repo models) and the monitor pause/resume ordering are
load-bearing and already documented in CLAUDE.md; port them verbatim and characterise them with tests
*before* touching them. B65 was a defect that lived in this code and nowhere else — which is both the
argument for the extraction and the warning about it.

Sequencing: this must precede Layer 2, because the test host needs to register the pipeline as a
service rather than instantiate a layout.

**Size: L — the long pole**, and the only step in 7a whose risk is not shallow.

### ✅ Shipped (2026-09-07) — ten extractions, and what deliberately stayed

Started from the outside in, smallest first, each move landing with its own tests before the next.
`MainLayout.razor.cs` is **2,635 → 1,898 lines**, and 907 tests now cover the pieces taken out of it.

| Moved | To | Why it went first |
|---|---|---|
| The four theme builders | `MLQT.Shared/Theming/MlqtTheme.cs` | Pure functions of a `UISettings` with no dependencies at all — it proves the shape of a move without risking anything |
| `BuildModelToRepositoryMap`, `BuildModelToStyleSettingsMap`, `AnyEnabledRuleNeedsDependencies` | `MLQT.Services/Checking/ModelScope.cs` | The first genuinely pipeline-shaped piece: they decide what every re-analysis pass does, and they only needed the *loaded libraries and repositories*, not the services holding them |
| The `OnVcsFilesChanged` fallback chain | `MLQT.Services/Checking/VcsChangeScope.cs` | The load-bearing one, and the first to need genuine characterisation rather than tests-after-the-fact |
| `GetModifiedFilePathsFromVcs` | `VcsChangeResolver.FormattableModelicaFiles` | Four narrowings that decide what the formatter is allowed to rewrite in a user's working copy |
| `CleanupEmptyDirectories` | `MLQT.Services/Helpers/EmptyDirectoryCleaner.cs` | It deletes things. Now driven against real temp directories rather than reasoned about |
| `UpdateFileNodesAfterSave` | `ModelicaGraph/FileNodeReconciler.cs` | Pure graph work, so it belongs with the graph — and a real `DirectedGraph` drives the tests |
| `SaveChangedFilesWithFormattingAsync` | `MLQT.Services/Helpers/IncrementalFormatter.cs` | The path B65 lived in — the formatter most users meet, run at startup and after every VCS operation |
| The orphan and save-directory rules | `OrphanedFileSelector`, `ModelicaPackageSaver.ResolveSaveDirectory` | Both decide what happens to files on disk: what gets deleted, and where a library is written |
| `FormatModifiedFilesAsync`, `SaveAllLibrariesWithFormattingAsync` | `MLQT.Services/Helpers/FormattingPipeline.cs` behind `IFormattingPipeline` | The named deliverable: a service the 7a-6 test host can register, rather than two private methods only the running app could reach |
| The combined dependency + style-check pass | `MLQT.Services/Checking/CombinedStyleCheckPass.cs` | The one piece of the deferred-analysis code with real substance — and the one whose own comment records that it had drifted from the shared path **twice** |

**Taking the collections rather than the services is the pattern the rest of the move should
follow.** It is what makes each piece answerable in a test, and it is why these two steps needed no
mocks at all — `ModelToStyleSettings` takes a `Func<string, Repository?>` where the component passed
`RepositoryService.GetRepository`.

Two behaviours are now written down that were previously only implied by the code:
`ModelToRepository` deliberately *omits* classes from a library with no repository, while
`ModelToStyleSettings` deliberately *includes* them against the defaults — a class missing from the
second map is checked against nothing, and an unchecked class reads as a clean one. And every
defaulted class shares one settings instance, because the checker groups its work by distinct
settings object and a fresh instance per library would split one pass into several.

**The VCS chain is where "port it verbatim" stopped being a slogan.** Two behaviours would have been
lost by writing out what the code appears to do:

- The whole-repository fallback hands the formatter **nothing**. Re-analysing every class after a
  branch switch is cheap and correct; reformatting every file on the strength of *not knowing* what
  changed would rewrite the working copy. "Affected" and "changed" are not the same set, and the
  first draft of the extraction collapsed them.
- Pending changes are cleared whenever there **were** any, not only when they answered. The first
  draft cleared them only when they won the fallback, which would have left changes queued that had
  already been handled — and the Refresh button reads that queue.

A third is preserved without being endorsed: VCS-reported paths whose classes do not resolve stay on
the formatter's list while the chain falls through to the whole-repository case. That is what
`MainLayout` has always done; a refactor is the wrong place to find out whether it matters, so the
test says so in as many words.

The directory cleaner produced the session's second *equivalent* mutation, and a useful one:
removing its emptiness check changes nothing, because `Directory.Delete` is called without
`recursive` and therefore refuses a directory with anything in it. The check is a shortcut that
avoids one thrown exception per non-empty directory — not the safety net it reads as. Both the
source and a test now say which is which, since the next person to tidy that line needs to know
what they are removing.

The file reconciler produced the session's third finding of this kind, and the most useful:
`MainLayout` carried its own copy of the detach-from-the-old-file step, which
`DirectedGraph.AddFileContainsModel` already performs — and whose comment names *this exact case*
as the reason it does. Two mutations survived by removing the copy and changing nothing, which is
what said so. That is the "one rule in two places" shape this repository keeps finding, and the copy
is gone; the test that covered it asserts the invariant (a class lives in one file) rather than the
mechanism, which is why it holds either way and why it was not the thing that noticed.

### What "done" means here, and what is not done

**The formatting half is finished.** Both ways MLQT writes formatted Modelica now live behind
`IFormattingPipeline`, registered in `MauiProgram`, and the write timestamps the file monitor uses to
tell MLQT's own writes from the user's have **one owner** — they were briefly split between the
service and the component during the move, which would have made the monitor start a formatting pass
on the formatter's own output.

**The analysis half is substantially done, and what is left is genuinely UI.** The combined
dependency + style-check pass — the only part of `RunDeferredDependenciesAsync` with real substance,
and the part whose own comment records two past drifts from the shared path — is now
`CombinedStyleCheckPass`, tested. What remains in `RunStartUpAsync`, the four `RunDeferred*Async`
methods and `RefreshLibrariesAsync` is the sequencing: guard on an `AppState` flag, raise the
progress dialog, call a service, set a step colour, raise a snackbar. That is exactly what this note
said should stay — "event wiring, dialog/progress UI" — so it is left there rather than moved behind
a progress seam for its own sake.

Two mutations of the combined pass survived the first set of tests, and both pointed at real gaps
rather than at equivalent code: nothing asserted that each repository gets **its own** context
(sharing one would check a class against another team's accepted spellings), and nothing asserted
that a class in a library with **no rules enabled is still measured for coverage** — coverage reports
the state of the code, not the result of the rules, so dropping those classes would report a
percentage against a smaller denominator than the library has. Making the second assertable is why
`Build` returns its contexts: the `CoverageMeasurer` inside each one accumulates as the pass runs, so
coverage is a result of the pass in the same way the findings are.

What this means for **7a-6**: the test host can register `IFormattingPipeline` today, so journey 4
("change a repository setting → formatting reruns → the file on disk changes") is reachable. Journey
1 drives the startup sequence, which is still `MainLayout`'s — the host renders it, so the journey
reaches it the way a user does. Those are the ones with `StateHasChanged`, dialog state and
background threads woven through them, and they need the characterisation tests the note calls for
before they move.

---

## 7a-5 — the coverage gate

`MLQT.Shared` is excluded from the ratchet today, in two places:
`-assemblyfilters:-MLQT.Shared` in [check-coverage.ps1](../build/check-coverage.ps1), and no entry in
`$bars`. Its comment gives the reason honestly: there are no tests. Once 7a-1 through 7a-4 land, that
reason has expired, and leaving it excluded would let the gap reopen silently — which is the failure
mode this project has now written three memory notes about.

**Bring `MLQT.Shared` into the gate at the 80% bar, at the end of 7a**, with three specifics:

1. ~~**Add `-filefilters:-*.razor`**~~ — **the measurement said no, and this is why the step told
   itself to measure.** The premise was that a component's generated `BuildRenderTree` would be
   attributed to its class and swamp the percentage. It is not counted at all: `MainLayout` reports
   **1,071 coverable lines against a 1,898-line code-behind**, which is its C# and nothing else.
   Adding the filter would have moved the assembly by 0.1 of a point and **removed five classes from
   the report entirely** — the components that kept an `@code` block, whose code lives in a `.razor`
   file. A class that is not measured reads as one that is fine, which is exactly B104. Everything is
   measured instead.
2. **Seed `build/coverage-baseline.json` with `-UpdateBaseline`, then write a real `reason` on every
   entry.** The gate already fails on a `TODO` placeholder, deliberately. "Not yet tested" and
   "renders only, no logic" are different facts and the ledger must distinguish them, exactly as it
   does for the SVN classes today.
3. **Add `MLQT.Shared.Tests` to the `$suites` list**, and to the `build-libraries` job in
   [build-and-test.yml](../.github/workflows/build-and-test.yml).

### ✅ Shipped (2026-09-07)

`MLQT.Shared` is in the ratchet at 80% per class, `MLQT.Shared.Tests` is in `$suites`, and the
assembly filter that excluded it is gone. The assembly sits at **7.5%**, with **29 classes** newly in
the ledger, each carrying its own reason.

Writing those reasons was the useful part, because the ledger exists to keep three different facts
apart and all three are present here:

- **Reachable only by rendering** — the pages and settings panels whose decisions 7a-3 tested and
  whose remaining code is lifecycle, subscriptions and dialog state. The Layer 2 journeys cover these.
- **Needs a working copy** — the VCS dialogs, whose validation is tested and whose operation needs a
  real repository. A LibGit2Sharp fixture in 7a-6, not a unit test.
- **Nobody has written them** — `AppState`, `SyntaxHighlightingSettings` and `VcsStatusHelper` are
  plain classes with no dependencies, directly testable with no renderer at all. They are in the
  ledger because they were never anyone's next task, and the entry says so rather than borrowing one
  of the other two excuses. `AppState` is named as the first thing to fix.

One entry is not `MLQT.Shared` at all: **`FormattingPipeline` at 25%**, which is debt *this branch
created* in 7a-4 rather than inherited. Its two incremental entry points are tested and every
decision inside the full save is tested separately; what is uncovered is the 250-line sequencing of
`SaveAllLibrariesWithFormattingAsync`, which needs a library on disk with a real layout to
restructure. The entry says that, and says it is worth writing.

Verified in both directions: claiming a `MLQT.Shared` class was better than it is fails the gate, and
removing one from the ledger fails it as new debt.

The 80% rule remains a poor fit for markup, which is why it is applied to `.cs` files only. Alongside
it, keep the **behavioural** bar as the rule a reviewer applies: every event handler that mutates
`AppState`, calls a service, or gates on a settings flag has at least one test. The numeric gate
catches drift; the behavioural rule catches "technically covered, asserts nothing".

---

## 7a-6 — `MLQT.TestHost` + Playwright (Layer 2)

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
run on CI (it needs a live working copy and server), and that reasoning applies unchanged here.

### Determinism

The pipeline is asynchronous and partly background-threaded (`StyleCheckingService` workers,
`FileMonitoringService` debouncing). Playwright's auto-waiting handles UI settling, but "analysis has
finished" needs an explicit signal. Rather than sleeping:

- have `MLQT.TestHost` expose a `/testapi/idle` endpoint that awaits pipeline quiescence
  (`EnsureDependenciesAnalyzedAsync` completion + style-check queue empty + no pending monitor
  changes), and call it between journey steps;
- render a `data-mlqt-state="idle|busy"` attribute on the layout root in the test host only, so
  Playwright can `WaitForSelector("[data-mlqt-state=idle]")`.

Both are test-host-only; neither leaks into the shipped hosts. `IAnalysisPipeline` from 7a-4 is what
makes the first one implementable without reaching into a component.

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

### ✅ Shipped (2026-09-07) — the host, the fixtures, and the first journeys

`MLQT.TestHost` runs, `MLQT.Journeys` drives it through Chromium, and **7 journeys pass**.

`AddMlqtCore()` is in `MLQT.Shared`, not `MLQT.Services` as this note said: two of its registrations
(`AppState`, `BrowserService`) are types in `MLQT.Shared`, which `MLQT.Services` does not reference
and must not. `MauiProgram` is now 47 lines and adds four things — the three platform services and
its renderer.

The test host's page is **generated from `HostAssetManifest`**, which is the pattern 7b should copy
for Photino; the MAUI page stays hand-written and is held to the manifest by a test.

**Three host differences surfaced in the first hour, which is the entire argument for building this
before the migration rather than during it:**

1. **Prerendering breaks MLQT.** Blazor Server renders statically on the server first; a webview host
   is interactive from the first frame. MLQT's components issue JS interop during their initial
   render — legal in a webview, an exception during static render. The very first request was a 500.
   Prerendering is off and the page says why: leaving it on would fail journeys on a difference the
   product does not have.
2. **RCL static assets were not served.** `CreateBuilder` only wires that up in Development. This is
   **probe 2**, the one this note calls the likeliest Photino failure — and it was the first thing to
   go wrong here too, which is some evidence the probe list is aimed correctly.
3. **And then not served again, for a different reason.** With `UseStaticWebAssets()` in place it
   worked under `dotnet run` and 404'd under the journeys, because the manifest is named after the
   *application* and the entry assembly in-process is `MLQT.Journeys`. Setting `ApplicationName`
   explicitly fixes it. A configuration bug that works one way of running and fails the other is the
   worst shape available, and it is the shape the Photino host will meet.

The journeys are verified by breaking things rather than by being green: dropping
`UseStaticWebAssets` fails 3, removing Cytoscape from the generated page fails 1, and re-enabling
prerendering fails 2.

**23 journeys** now, across three files. Journey 6 (Cytoscape) is the one that matters most for 7b:
it asserts the graph *instantiates*, that its nodes end up at distinct positions — a layout extension
that failed to register does not throw, it silently stacks every node at the origin — and that all
five layouts the UI offers actually run. `CytoscapeGraphInteropTests` already pins that the right
calls are made; only a browser can say whether Cytoscape then does anything, and that is precisely
the WebKitGTK question.

Journey 2 also passed by asserting nothing on its first run: checking with default settings found no
findings, because **every rule ships off**. Correct behaviour, useless test. It now enables a rule
explicitly, and a fourth test pins the default so nobody reads the others as "MLQT finds naming
problems" when they mean "MLQT finds them once asked to".

**CI**: a `ui-journeys` job on `ubuntu-latest`, deliberately installing **no MAUI workload**. It is
the project's first Linux job and the only proof that everything except the MAUI app builds there —
otherwise not discovered until the Photino port is underway. `PortabilityTests` holds that: the
closure of `MLQT.Journeys`, `MLQT.TestHost`, `MLQT.Shared`, `MLQT.Cli` and `MLQT.McpServer` may not
reference MAUI in any of its three spellings, nor the `MLQT` host project. Without it, a MAUI
reference in a shared project builds on Windows and fails on the runner with a missing workload — at
which point the obvious fix is to install the workload in CI, burying the problem instead of showing
it.

Journey 4 (settings change → formatting reruns → the file changes) is driven through
`IFormattingPipeline` rather than by clicking, because 7a-4 put both formatting paths behind that
interface precisely so this could reach them. Its first version passed by doing nothing — the fixture
file was already canonically formatted — so the fixture now writes a badly laid-out file *and* the
test asserts the library actually loaded before asserting the formatter changed it.

### The optional WebKit rehearsal

Playwright ships a WebKit browser. Running the same journeys under `--browser webkit` on Linux is not
the same engine as WebKitGTK, but it is *much* closer than Chromium, and it will surface the CSS and
JS-feature differences that are going to bite in the Photino Linux build — for free, before the
Photino host exists. Recommended as a nightly job rather than a PR gate.

---

## 7a-7 — the `/selftest` conformance route (Layer 3)

The piece that actually compares hosts. A route in **`MLQT.Shared`**, so it is literally the same code
running under MAUI, Photino and the test host — no per-host test rewriting.

`MLQT.Shared/Pages/SelfTest.razor` (with a `.razor.cs`, per the policy — it is all logic) runs a fixed
list of probes and renders a pass/fail table plus a machine-readable JSON blob in a
`<pre id="selftest-result">`. Probes, each chosen because it maps to a specific way the migration can
break:

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

### Shipped (2026-09-07) — the route, the baseline, and the comparison

**The MAUI baseline is captured and committed** as
`MLQT.Shared.Tests/TestFiles/selftest-baseline-maui.json`: 16 probes, all passing, `Host: MLQT`,
runtime 10.0.8, produced by the real MAUI app driving WebView2. This is the artefact with the
deadline, and it now exists. The step it was blocked on was mundane and worth recording: the running
desktop app holds a lock on `MLQT.Shared.dll`, so the MAUI project cannot build while MLQT is open.

**16 probes, and not the 14 sketched above.** The list changed on contact:

- Probe 2 of the sketch is `assets.rcl`. Probe 9 was folded into the round-trip in the first pass,
  which was a mistake worth naming: **a write-read-delete cannot tell persistence from the appearance
  of it.** An implementation holding values in a dictionary passes it and loses everything on
  restart — and the Photino port replaces MAUI's `Preferences` with exactly the kind of JSON-file
  implementation that could cache and never flush. `settings.location` does not prove persistence
  either; it makes the store *visible*, so the baseline records `MAUI Preferences (platform key/value
  store)` and a host that answers with a temporary directory is a difference rather than a surprise.
- The sketch's probes 6 and 9 were dropped in the first pass and **put back before 7b started**, which
  is the only window in which that was possible — see the note below on when the probe set freezes.
  Probe 6 is `mudblazor.overlays`: a dialog, a snackbar and the popover layer, asserted to reach the
  DOM. `EmptyLayout` renders the four MudBlazor providers, so the objection that stopped it the first
  time was wrong. It proves the overlays *arrive*, not that they are positioned correctly — comparing
  geometry across two engines is ruled out elsewhere in this note and stays a human's judgement.
  Probe 9 is `settings.location`, which required a new `ISettingsService.BackingStore`.
- `cytoscape.layouts` replaced the sketch's assumption that each layout script defines a global named
  after it. **`klayjs` does not** — it publishes `klayregister`/`klaycallback`. The probe now asks
  Cytoscape which layouts it has registered, which is the question that actually matters and is not
  sensitive to how a given extension chooses to advertise itself.

**Two probes were wrong on their first run**, both in the direction that matters — they failed
against a host that works, rather than passing against one that does not: the `klay` global above,
and `diffViewer.initSyncScroll`, which takes elements and not element ids.

**The report says which host produced it, and hosts declare themselves.** The first capture named
the *process* — so a report captured from the journeys was labelled `MLQT.Journeys`, which is exactly
the kind of quietly-wrong field that makes a baseline diff untrustworthy. Each host now sets
`MLQT_SELFTEST_HOST`.

**The comparison is written and exercised now, not at the migration.** `HostConformance.Compare` in
`MLQT.Journeys` diffs a report against the committed baseline, and reports a probe present on one
side only as a difference rather than skipping it — the failure it guards against is not a host that
answers differently, which is loud, but two probe sets that have drifted apart while their
intersection still matches, so the diff comes back empty and reads as success. Phase 7b points the
same code at Photino.

**The test host matches the MAUI baseline on all 16 probes, with no allowances.** That was not
assumed: the journey was written with an empty allowance list so the run would say what actually
differs, and there was nothing. A Blazor Server host over Kestrel and a WebView2 host answer these
questions identically, which raises the confidence that a difference under Photino will be a real
difference.

**Five guards on the artefact itself**, in `MLQT.Shared.Tests/SelfTestBaselineTests.cs`, each watched
failing against a deliberately corrupted copy: it was captured under MAUI; every probe in it passed;
its probe ids match the ones the route actually runs, read from the route's own source (the drift
guard, and the one that matters most); no duplicates; and the four probes the migration is most
likely to break are named individually, so dropping one is a decision rather than an omission.

**The probe set freezes when MAUI is retired, not when 7a ended.** `ItRecordsEveryProbeTheRouteRuns`
fails if a probe is added without re-capturing, and re-capturing needs a MAUI build that runs — so a
probe added after the migration starts has no MAUI answer and never can have. That is why the two
dropped probes went back in first, and why the question to ask before 7b is not "is the baseline
captured?" but "is this the right set of probes?". Re-capturing also confirmed the additions disturbed
nothing: no carried-over probe changed status, so the 16-probe file is a clean superset of the 14.

Still open: the per-platform CI `selftest` job, and the nightly WebKit journey run.


---

## What this does and does not prove

Stated plainly, because the temptation to over-claim here is strong:

- ✅ Shared UI logic is unchanged by the migration — Layers 1 and 1b.
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
- ❌ **That the markup is right.** The coverage gate measures `.razor.cs` and ignores `.razor` by
  design, and Layer 1 never renders. A component whose handler is fully tested can still render the
  wrong thing; Layer 2 is the only defence, and it covers six journeys, not 39 components.

---

## Ordered work breakdown

Each step compiles and leaves the suite green.

| Step | Work | Size |
|---|---|---|
| **7a-1** | ✅ **shipped 2026-09-07** — the code-behind sweep: 31 components, `@inject` → `[Inject]` throughout, plus `GlobalUsings.cs` and the browser-platform removal | **L, shallow** |
| **7a-2** | ✅ **shipped 2026-09-07** — `MLQT.Shared.Tests`; `SharedUiConventionTests` moved; six convention guards, each verified by breaking the source; `MlqtComponentTestBase` | S |
| **7a-3** | ✅ **shipped 2026-09-07** — 317 tests over `MLQT.Shared`; the Layer 1b dialog-result pattern; five defects found and four fixed (B106–B110) | M |
| **7a-4** | ✅ **shipped 2026-09-07** — ten extractions out of `MainLayout` and its neighbours, each with tests verified by mutation; `MainLayout` 2,635 → 1,898 lines of logic | **L — the long pole** |
| **7a-5** | ✅ **shipped 2026-09-07** — `MLQT.Shared` into the coverage ratchet: `$bars`, `$suites`, baseline with 29 reasons. No file filter: the measurement said it would hide five classes | S |
| **7a-6** | ✅ **shipped 2026-09-07** — `AddMlqtCore()`, `HostAssetManifest` + drift test, `MLQT.TestHost` + fakes + `LibraryFixture`, **23 journeys**, the Linux CI job and `PortabilityTests` | M |
| **7a-7** | ✅ **shipped 2026-09-07** — `SelfTest.razor` + 16 probes, the MAUI launcher, **the captured MAUI baseline**, `HostConformance.Compare`, 5 baseline guards and 5 conformance journeys | M |

**7a-7 is the step with a deadline attached** — the MAUI baseline must be captured while the MAUI
build is still the reference implementation. If phase 7 has to start early, **7a-1, 7a-2, 7a-4 and
7a-7** are the non-negotiable subset; 7a-3's long tail and the journeys can trail the migration, and
7a-5 can follow whenever the suite is large enough to have a meaningful baseline.

---

## CI changes

Add to [build-and-test.yml](../.github/workflows/build-and-test.yml):

- `dotnet test MLQT.Shared.Tests` in the existing `build-libraries` job (Windows; no new deps), and
  the suite in `check-coverage.ps1`'s `$suites`.
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

**The code-behind sweep is the load-bearing decision.** If it is done half-way — some components
converted, `@inject` left in place, the convention guard deferred — the project ends up with two
shapes and no enforcement, which is worse than either shape alone. 7a-2's guards exist to make that
outcome a build failure.

**Blazor Server as the test host is a deliberate approximation.** It is not the webview render mode
the product ships. It differs in: circuit-based reconnection (irrelevant here), no
`IJSInProcessRuntime` (MLQT does not use it — verified), and serialization of interop arguments over
a circuit rather than in-process (same API surface, different latency). The approximation is
acceptable because Layer 3 covers what it misses. **If a journey ever needs behaviour Blazor Server
cannot express, that is a signal to promote the test to Layer 3, not to weaken Layer 3.**

**Photino on .NET 10 is unverified.** `Photino.Blazor` has historically trailed .NET releases. Confirm
a `net10.0`-compatible build exists *in the WebKitGTK spike that opens phase 7b* — before any of the
host work is scheduled. If it does not, the fallback host in the roadmap becomes the primary and
`MLQT.TestHost` becomes production code, which is another reason to build it properly now.

**Static web asset serving under Photino is the highest-probability failure.** `_content/<RCL>/…`
resolution in `Microsoft.AspNetCore.Components.WebView` depends on the static web assets manifest
being discoverable at runtime. This is a known rough edge in non-MAUI webview hosts. Probe 2 exists
specifically for it; expect to spend time here.

**Do not drive WebView2 via CDP.** Repeated because it is the tempting shortcut and it produces work
that must be discarded: it cannot be carried to WebKitGTK.

**MudBlazor + bUnit has known friction** — popovers and dialogs need their providers rendered, and
some interactions need `cut.WaitForAssertion(...)`. The code-behind policy shrinks the exposure to
Layer 1b, roughly a dozen tests rather than the whole suite; budget for a slow day there rather than
a slow week.

**7a-4 is a refactor of the most delicate code in the app.** The mitigation is ordering: 7a-1 puts it
in reviewable C# first, characterisation tests capture current behaviour including its quirks, and
only then does anything move. If it slips, Layers 1 and 3 still deliver independently — Layer 2 is
the only thing that hard depends on it.

**The coverage bar for `MLQT.Shared` is a measurement, not an assumption.** 7a-5 says to check what
the merged report attributes to a component class *before* fixing the bar. If `-filefilters:-*.razor`
does not behave as expected, the fix is to change the filter, not to lower the bar and stop asking.

---

## 7b — the host migration

**Planned in full on 2026-09-08 in [design-phase7b-photino.md](design-phase7b-photino.md).** The
sketch that stood here was written before 7a shipped and assumed a harder job than the one now in
front of us: a second non-MAUI host already exists and works, the composition root and host page are
already extracted, and conformance is a diff against a committed 16-probe capture rather than a
judgement. The sketch also missed three things the plan names — `MLQT.McpTester` is a *second* MAUI
app and blocks retiring the workload, existing users' settings live in MAUI `Preferences` and are lost
on cutover unless migrated, and Linux has no install story at all.

Two of this note's own leftovers land there rather than here, because until there is a Linux host
there is nothing to run them against: the per-platform CI `selftest` job, and the nightly WebKit
journey run.

---

## Incidental finding

`MLQT.Shared/wwwroot/filePicker.js` is referenced from no C#, Razor or HTML in the solution —
presumably dead since the picker moved to the native MAUI service. Delete it during 7a-6 rather than
carrying it into the manifest.
