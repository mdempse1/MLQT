# Design Note — Phase 7b: replacing MAUI with Photino

> **Status: IN PROGRESS (2026-09-08). 7b-0 is complete (both legs), 7b-1 is shipped, and 7b-A is under way.**
> The gating spike answered its three questions on Linux — Photino.Blazor 4.0.13 runs on `net10.0`,
> `/selftest` produces 16 `Pass`, and `HostConformance.Compare` reports **zero differences** against
> the MAUI baseline under WebKitGTK. The Windows leg of that spike is outstanding. See
> [7b-0](#7b-0--the-spike-s--gating). Companion to
> [design-phase7-gui-tests.md](design-phase7-gui-tests.md), which is complete: 7a built the harness
> and captured the conformance baseline this phase is measured against. Roadmap §1 and locked
> sequencing item 7 ([roadmap.md](roadmap.md)).
>
> The one-line statement of the job: **MLQT stops being a MAUI application and becomes a Photino
> application, on Windows and Linux, without anybody being able to tell from the inside.**

---

## Purpose

The roadmap's reasons, unchanged: an in-process webview keeps direct filesystem and git/svn access,
`MLQT.Shared` is reused near-unchanged, the three platform services are reimplemented once, and MAUI
is retired. What it *delivers* is the **Linux UI** — the thing MAUI cannot give us here, because the
MAUI project targets `net10.0-windows10.0.19041.0` and always has.

MAUI is **replaced, not supplemented**. There is no period where both hosts ship. That constraint is
what forced 7a to happen first, and it is what makes the cutover step (7b-8) irreversible in
practice.

---

## What 7a changed about this job

This is the most important section in the note, because the 7b sketch was written before 7a shipped
and it assumes a harder job than the one now in front of us.

**A second, non-MAUI host already exists and works.** `MLQT.TestHost` has been running `MLQT.Shared`
outside MAUI since 7a-6, driven by Playwright, with 33 journeys passing. The question "does the
shared code work when MAUI is not underneath it?" is **already answered, in the affirmative, and it
has been answered for a month of commits.** Photino is the *second* port, not the first, and the
three host differences that were going to bite (prerendering, static assets, the static-web-assets
manifest name) already bit — in the test host, where they cost an afternoon instead of a migration.

Concretely, what 7b inherits:

| Asset | What it removes from this phase |
|---|---|
| `AddMlqtCore()` (7a-6) | The composition root is already extracted. `MauiProgram` is 47 lines and adds four things; the Photino equivalent adds the same four. |
| `HostAssetManifest` + drift test (7a-6) | The host page is *generated from* a list, not copied. `HostAssetManifestTests.HostPages()` enumerates hosts — adding `"MLQT.Photino"` is one line, after which its page is held to the same script order as MAUI's, forever. |
| The 16-probe MAUI baseline (7a-7) | Conformance is a diff against a committed file, not a judgement. |
| `HostConformance.Compare` (7a-7) | The comparison is written and has been watched working, against a host that is not MAUI. |
| `PortabilityTests` (7a-6) | The MAUI boundary is guarded by a test, so "did we accidentally keep a MAUI dependency?" is answered on every build. |
| 317 `MLQT.Shared` tests, 33 journeys | Regressions in shared logic are caught without a host at all. |

**What this means for sequencing:** the risky, unbounded part of a host swap has been paid for. What
is left is mostly *bounded, enumerable* work — three services, two platforms, one window model — plus
one genuinely open question (WebKitGTK) that a spike answers in a day.

---

## What is genuinely new

Everything below is work 7a did not touch and nothing currently tests.

1. **Photino's application model.** MAUI has `App.xaml` / `MainPage.xaml` / `MauiProgram`; Photino has
   `PhotinoBlazorAppBuilder` and a `PhotinoWindow`. There is no XAML. The window is configured in C#.
2. **The three platform services, twice** — once for Windows, once for Linux. See 7b-3; this is the
   long pole and it is smaller than it looks.
3. **Native window behaviour** — size and position restore, DPI, minimise/maximise, title, icon,
   multi-monitor. 7a says explicitly that it does not cover this, and it still does not.
4. **Settings migration.** Existing users have settings in MAUI `Preferences`. A Photino host reading
   a JSON file finds nothing and silently starts every user from defaults — including their project
   list. *The sketch does not mention this and it is the most likely way to annoy a real user.*
5. **Linux packaging and distribution.** How a user installs MLQT on Linux has never been decided.
6. **`MLQT.McpTester`, the second MAUI app.** "Retire the MAUI workload" is impossible while it
   exists. *Also absent from the sketch.*
7. **macOS**, which the roadmap's sequence names ("Windows → Linux → macOS") and the sketch omits.

---

## The steps

Sizes are relative, in the same currency as 7a's table.

### 7b-A — widen the journeys first (M)

**The first step, before the spike**, and the one that pays for itself twice.

`MLQT.Shared` sits at 19.4% covered when every suite runs, and the review that asked whether to fix
that before starting reached a specific answer: **more unit tests would not reduce migration risk, and
more journeys would.** A host swap does not change `MLQT.Shared`'s C# — the same assembly runs under
Photino — so what breaks is asset loading, interop, platform services and window behaviour. Unit tests
of component orchestration address none of that.

The journeys do, and the measurement says so: the six that exist took `MLQT.Shared` from **11.5% to
19.4%**, which is the largest single movement anything has made on that number. They are also the
only tests that run on both platforms and would run against the Photino host too.

What is at 0% and is journey-reachable:

| Component | Coverable lines |
|---|---|
| `VCSHistory` | 230 |
| `BranchSelector` | 112 |
| `Dependencies` | 112 |
| `SettingsUI` | 82 |
| `SettingsRepositoryDictionary` | 80 |
| `SettingsExternalTools` | 56 |
| `SettingsReferenceLibraries` | 43 |

Roughly 700 lines that no test of any kind reaches today, in components a user touches constantly.

**Why this is step A rather than step 8.** Every journey written here is a journey that runs against
Photino in 7b-5 and 7b-6 for free. Written afterwards, they prove nothing about the migration, because
by then there is no MAUI build left to have proved they passed on. It is the same argument the
`/selftest` baseline turns on, applied to the other half of the harness — and unlike the baseline,
journeys can still be *added* after the port, they just stop being evidence about it.

Not a blocker on the spike (7b-0), which answers a question nothing else can and should be run as soon
as somebody has a Linux box. Do them in parallel if two people are on this.

**Progress (2026-09-08).** Two journeys written, 11 tests, `MLQT.Shared` from 19.4% to **27%**:

| Component | Before | After |
|---|---|---|
| `Dependencies` | 0% | 52.7% |
| `SettingsUI` | 0% | 36.6% |
| `MetricsDashboard` | 4% | 35.3% |
| `ExternalResources` | 9% | 19.5% |
| `MainLayout` | 6% | 15.9% |
| `CodeReview` | 14% | 16.6% |

`MainLayout` moving without being touched is the point of journeys rather than unit tests: it is the
startup sequence running, which is what 7b changes and what no unit test can reach.

Both journeys needed a throwaway diagnostic to learn their selectors rather than a guess — the five
top-level tabs carry an icon and a tooltip and **no text**, so they can only be addressed
positionally, and two landmarks taken from the markup by eye were wrong. That is recorded on the
classes. It also turned up **B120**: the test host 404s on `app.css` every page load, which the
existing journey's 404 check does not see because it only inspects `_content/` URLs.

### 7b-0 — the spike (S) — **gating**

Throwaway host, not the real one. It exists to answer questions whose answers change the rest of this
list.

**The questions, in the order they can kill the plan:**

1. **Does `Photino.Blazor` run on .NET 10?** The latest release is **4.0.13, published 2025-01-23**,
   targeting `net8.0`/`net9.0` with *computed* `net10.0` compatibility — that is NuGet asserting the
   assemblies will load, not the maintainers asserting they work. It is also **~20 months old** as of
   this note. A stale native dependency (WebView2 loader, WebKitGTK ABI) is the realistic failure.
2. **Does the `/selftest` route run under it, and what do the 16 probes say?** This is the spike's
   real deliverable and it exists *because of 7a*: the spike does not report "a window opened", it
   reports a probe table that can be diffed against the MAUI baseline. A spike that produces 16
   `Pass` on Windows has de-risked far more than a screenshot.
3. **Cytoscape, MudBlazor and the syntax highlighting under WebKitGTK** — run the same route on Linux.
   This is the question the roadmap has been deferring since it was first raised.

**Exit criteria:** two probe reports (Photino/Windows, Photino/Linux) and a written answer on each of
the three questions. **Not** a host anyone intends to keep.

**If question 1 fails**, the fallback the 7a note already names applies: promote `MLQT.TestHost` to
the shipping host (Blazor Server behind a local Kestrel, in a browser or a thin window) and rewrite
this list. That fallback is *much* more credible than it was in the sketch, because the test host now
exists, has 33 journeys and matches the MAUI baseline on all 16 probes.

#### What the spike found — Linux leg, 2026-09-08

**Run on Ubuntu 26.04, .NET SDK 10.0.111, WebKitGTK 2.52.6 (`libwebkit2gtk-4.1-0` 2.52.6-0ubuntu0.26.04.1).
The Windows leg is separate and is not covered here.**

**Verdict: question 1 does not kill the plan, and question 3 is answered in the affirmative.** The
`MLQT.TestHost`-as-shipping-host fallback is not needed. Nothing found here changes the shape of
7b-1 through 7b-9; four findings change details inside 7b-2, 7b-4, 7b-7 and open decision 5, and they
are listed below.

| Question | Answer |
|---|---|
| 1. `Photino.Blazor` 4.0.13 on `net10.0` | **Yes.** Restores, builds with no warnings, runs. Runtime reported 10.0.11. |
| 2. `/selftest` under it, and the 16 probes | **All 16 `Pass`, and `HostConformance.Compare` reports zero differences against the committed MAUI baseline.** |
| 3. Cytoscape, MudBlazor, syntax highlighting under WebKitGTK | **All three work.** The third turns out not to be a JavaScript question at all — see below. |

**The stale-native-dependency risk did not materialise, and it is worth saying why rather than just
that.** `Photino.Native` 4.0.22's `linux-x64/Photino.Native.so` links `libwebkit2gtk-4.1.so.0`,
`libjavascriptcoregtk-4.1.so.0`, `libgtk-3.so.0` and `libnotify.so.4`; all eleven `NEEDED` entries
resolve on a current Ubuntu with no `not found`. It is the **4.1** ABI — and Ubuntu 26.04 offers no
`libwebkit2gtk-4.0` package at all, only `-4.1`, so a binary built against 4.0 would have been
unrunnable here. That is the specific thing that would have broken it, and it did not. That is a
fact about the shipped binary, checkable with `objdump -p`, and it should be re-checked rather than
assumed on whatever distribution 7b-7 decides to target.

**Package resolution.** NuGet selects Photino's `net9.0` asset group, which pulls
`Microsoft.AspNetCore.Components.WebView` **9.0.1**. The spike was run both ways — left at 9.0.1
against the .NET 10 runtime, and pinned forward to 10.0.9 — and **both produce 16 `Pass`**. So the
pin is not required. 7b-2 should add it anyway, so the whole graph sits on the 10.0.x line
`MLQT.Shared` already uses, but that is tidiness rather than a fix.

**The conformance result, in full:**

```
baseline: MLQT (runtime 10.0.8), 16 probes
actual:   MLQT.Photino (runtime 10.0.11), 16 probes

NO DIFFERENCES - every probe answered as it did under MAUI.
```

Produced by the shipped `HostConformance.Compare` rather than by reading two tables side by side —
which is the whole point of 7a-7, and this is the first time it has been run in anger. The report was
captured three times and the status vector was identical each time, with exit code 0 (the route's own
contract: 0 when every probe passed). Six probes differ in **detail** only, and they are exactly the
ones this note predicts as expected-and-correct: `settings.location` → `~/.local/share/MLQT/…`,
`logging.writes` → `~/.local/share/MLQT`, `svn.client` → none on PATH, plus window size, the picker's
type name, and `fonts.roboto`. `Compare` looks at `Status` and never `Detail`, so it stays silent on
all six — the 7a-7 decision paying off precisely where it was meant to.

**Question 3, in detail.** The probe route covers two thirds of it and a diagnostics page built for
the spike covered the rest.

- **Cytoscape.** All four layout extensions register against the global (`dagre`, `klay`, `fcose`,
  `spread`), and a visible six-node, six-edge graph laid out with `dagre-tb` produced real non-origin
  positions for every node and three canvas elements. The probe's version draws off-screen; this one
  was on screen and laid out.
- **MudBlazor.** Dialog, snackbar and popover layer all reach the DOM (probe 7), and the diagnostics
  page additionally rendered dense buttons, a select with its popover, a text field, a switch, a
  progress circular and a table without incident. **Where** the overlays land is still a human's
  judgement, deliberately — comparing pixel geometry across two engines produces a difference on
  every glyph, and that has not changed.
- **The syntax highlighting was never a JavaScript question, and that is the finding.**
  `ModelicaRenderer` emits tagged text (`<KEYWORD>`, `<STRING>`, `<COMMENT>` …) and
  [`CodeViewer`](../MLQT.Shared/Components/CodeViewer.razor.cs) turns it into spans that CSS colours
  — there is no highlighting library in the page at all. So the WebKitGTK risk was a CSS risk, which
  is much smaller than the roadmap's framing implied. Checked anyway, on a real parse of a sample
  model: all ten `code-*` classes resolved to their expected colours, `white-space: pre` survived, 46
  spans rendered. **The roadmap has been carrying this as an open engine risk since it was first
  raised; it can stop.**

The engine surface the application relies on is all present: CSS custom properties, `display: grid`,
`ResizeObserver`, `IntersectionObserver` and `structuredClone`. The user-agent string is
`Photino WebView`, which is worth knowing for anything that ever sniffs it.

**Three things 7b-2 assumed, now confirmed by running rather than by reading:**

- Photino **does** bootstrap with `_framework/blazor.webview.js`, so
  `HostAssetManifest.WebViewBootstrapScript` is the right constant. This note called that a
  one-minute check; the answer is yes.
- A host page **generated from `HostAssetManifest`** works unchanged, exactly as `MLQT.TestHost`'s is.
  Nothing about the MAUI page needed copying.
- **Blazor routing works under Photino.** The spike was run a second way, rooted on `MLQT.Shared`'s
  real `Routes` component and navigating to `/selftest`, rather than rendering the page's tree
  directly. Also 16 `Pass`. This mattered because the direct-render root was chosen for determinism
  (letting the router resolve `/` would start `MainLayout` and the whole application underneath the
  probes), and it would have left routing — which the real host depends on — untested.

`PhotinoWindow.ShowOpenFile` and `ShowOpenFolder` also exist and compile against the real API, so
7b-3's claim that the picker is a thin adapter holds at the API level. No dialog was opened; that
stays a manual check, as this note says.

#### What the spike found — Windows leg, 2026-09-08

**Run on Windows 11 26200, .NET SDK 10.0, WebView2 (evergreen), Photino.Blazor 4.0.13 with
`Microsoft.AspNetCore.Components.WebView` pinned forward to 10.0.9.**

**Verdict: 16 `Pass`, and `HostConformance.Compare` reports zero differences against the committed
MAUI baseline.** Captured three times; identical each time, exit code 0 each time. With the Linux leg
this completes 7b-0's exit criteria — two probe reports and an answer to each of the three questions.

```
baseline: MLQT (runtime 10.0.8), 16 probes
actual:   MLQT.Photino (runtime 10.0.8), 16 probes

NO DIFFERENCES - every probe answered as it did under MAUI.
```

Only three probes differ in **detail**, all of them expected: the window size, `settings.location`
(the spike's JSON file rather than MAUI `Preferences`) and `filepicker.wired` (a stub). `Compare`
looks at `Status` and never `Detail`, so it is correctly silent on all three.

**Question 1 is now fully retired.** `Photino.Blazor` 4.0.13 restores, builds at zero warnings and
runs on `net10.0` on Windows as well as Linux. NuGet selects the same `net9.0` asset group and pulls
`Components.WebView` 9.0.1 — the Linux leg's package-resolution finding, reproduced exactly — and the
forward pin to 10.0.9 works.

**The Linux leg's publish finding is confirmed and is platform-independent.** A plain
`dotnet build` leaves `bin/` with **no** `wwwroot/_content` and **no** `_framework/blazor.webview.js`;
`dotnet publish` materialises both. It is not a Linux packaging quirk, and 7b-2 and 7b-7 own it on
both platforms.

**`fonts.roboto` differs between the engines, and the Windows leg supplies the half the Linux leg
declined to assert.** Linux/WebKitGTK recorded `not available (network font)`; Windows/WebView2
records `available`, from the same generated page and the same stylesheet. The Linux note said the
likely cause was that WebView2 answers `true` for a declared-but-unloaded face where WebKit answers
`false`, and explicitly refused to state it as fact without testing that half. **It is now tested:
that is exactly what happens.** Both are `Pass`, so the comparison stays silent — and 7b-4's bundling
of the font removes the question entirely.

#### Two things the Windows leg found that 7b-2 must not rediscover

**1. `autostart="false"` must not be carried over from MAUI's page, and the failure is silent.**
MAUI's `index.html` loads the bootstrap as
`<script src="_framework/blazor.webview.js" autostart="false">` — because MAUI's `BlazorWebView`
handler is what calls `Blazor.start()`. **Photino does not.** With the attribute present the window
opens, `AddMlqtCore` initialises logging, and then *nothing whatsoever happens*: no component is
created, no error is raised, no exception is logged, and the window sits showing the loading `div`.
It took instrumenting a root component with a log line to establish that Blazor had never started.

This is worth recording as a mistake rather than a discovery: **the page must be generated from
`HostAssetManifest`, and this note already says so.** The attribute arrived because the generator was
written by copying MAUI's hand-written page, which is precisely what the manifest exists to stop. The
manifest carries the script list and `WebViewBootstrapScript`; it does not carry that attribute, and
a page generated strictly from it does not have the problem.

**2. The file provider must be rooted at `wwwroot` explicitly.**
`PhotinoBlazorAppConfiguration.HostPage` is `"index.html"` with no directory part and `AppBaseUri` is
`http://localhost/`, so the provider is expected to be `wwwroot`-rooted already;
`PhotinoBlazorAppBuilder.CreateDefault(args)` did not resolve the page, and
`CreateDefault(IFileProvider, args)` with a `PhysicalFileProvider` over
`AppContext.BaseDirectory/wwwroot` did. The symptom is the same silent nothing as above, which is why
both are recorded here together: **on this host a page that fails to load looks identical to a page
that loads and does not start.**

Incidentally, `Photino.NET` logs `File "/" could not be found` during startup and then loads
`http://localhost/`. That is normal — the custom scheme handler serves it — and it is noise, not a
fault. Worth knowing so it is not chased.

**One small limitation of the shipped comparator**, found by using it from outside the repository:
`HostConformance.MauiBaseline()` locates the baseline by walking up for `MLQT.slnx`, so it only works
from inside the tree. `Compare` itself takes two reports and does not, so the spike loaded the
baseline explicitly and still used the shipped comparison. 7b-5 should decide whether the Photino
host's conformance check runs from inside the repository or needs the baseline passing in.


#### Findings that change later steps

**1. Photino needs `dotnet publish`, not `dotnet build` — and this is the failure this note predicted.**
`Photino.Blazor` serves `wwwroot` through a bare `PhysicalFileProvider`: it has no support for the
static-web-assets manifest, so a plain build leaves `bin/` with no `wwwroot/_content` at all and
probe 2 (`assets.rcl`) fails. Publishing materialises `_content/…` and `_framework/blazor.webview.js`
and everything passes. This is the same shape as the test host's `UseStaticWebAssets()` problem in
7a-6, arriving a second time through a different mechanism, and it is the reason probe 2 exists.
**7b-2 owns the developer story** (what F5 does, since `dotnet run` alone gives a host with no
assets) and **7b-7 owns the shipping one.** Neither can be left to be discovered later: the failure
does not look like a missing file, it looks like an application that loads and renders nothing.

**2. `fonts.roboto` answers differently on the two hosts, benignly, and 7b-4 removes the question.**
MAUI recorded `available`; Photino/WebKitGTK records `not available (network font)`. Both are `Pass`,
so the comparison is correctly silent, and it is stable across three runs rather than a race. The
cause is that the `/selftest` page's own text is `system-ui, sans-serif`, so **nothing on the page
requests Roboto** and WebKit never loads the declared face — `document.fonts.check` is false for a
face that is declared but unloaded. A diagnostics page that *does* set `font-family: Roboto` reports
`check() = true` with the 400/500/700 faces `loaded`, from the same stylesheet, on the same machine.
So the font arrives; the probe is observing lazy loading. The likely difference is that WebView2
answers `true` for a declared-but-unloaded face where WebKit answers `false`, but that half was not
tested here and should not be stated as fact. **Bundling Roboto in 7b-4 makes the whole question
disappear**, which is a second reason to do it beyond removing the network round-trip.

**3. Startup emits Mesa/EGL noise on a machine with no working GPU driver** —
`libEGL warning: failed to get driver name`, `MESA: error: ZINK: failed to choose pdev`. WebKit falls
back to software rendering and nothing is affected: Cytoscape draws on a 2D canvas, and all its
probes pass. Worth knowing for 7b-7 so it is recognised as noise rather than diagnosed as a fault,
and worth suppressing in whatever launcher the packaging step produces.

**4. `svn.client` reports none on PATH here**, which is the answer open decision 5 assumes.
`SvnToolLocator` did the right thing with no code change, as this note predicts — the probe passes
and records the absence. The decision still needs making; it is now a decision with an observation
behind it.

#### What the Linux leg did not cover

Unchanged from the list under *What conformance proves*, and stated here so the spike is not read as
saying more than it does:

- **Visual fidelity.** Not captured. GNOME refuses programmatic screenshots to an unprivileged caller
  on this machine (`org.gnome.Shell.Screenshot` → `AccessDenied`), and ImageMagick's `import` is built
  without the X11 delegate. This is a human looking at the window, once, per platform — 7b-6 — and the
  spike host is runnable for exactly that purpose.
- **A real file dialog opening**, native window behaviour, DPI, multi-monitor and state restore.
- **The Windows leg**, which is the other half of this step's exit criteria.
- **Settings migration**, which no probe can see. The spike's settings service is a plain JSON file
  and deliberately does *not* migrate — 7b-3 owns that, and it remains the highest silent risk in the
  phase.

The spike host itself was built outside the repository and is not committed, per this step's own
instruction that it is not a host anyone intends to keep. What survives it is this section, the
`HostConformance` result above, and the finding list.

### 7b-1 — port `MLQT.McpTester` as a rehearsal (S)

**Recommended, and new in this plan.** `MLQT.McpTester` is 751 lines across 20 files, MudBlazor-based,
with **no project references** — a self-contained MAUI Blazor app. That makes it the ideal first real
Photino port:

- It exercises the whole Photino host bootstrap, MudBlazor under the new engine, and the app model.
- It has no users to disappoint and no data to lose.
- It has to be dealt with anyway (see 7b-8 — the MAUI workload cannot be retired while it exists).
- Ported, it also gains Linux, which is genuinely useful for a tool that tests MCP servers.

Doing it here converts a cutover blocker into a rehearsal. See **Open decisions** for the alternative.

#### ✅ Shipped (2026-09-08)

`MLQT.McpTester` is a Photino app. `MauiProgram.cs`, `App.xaml`, `App.xaml.cs`, `MainPage.xaml`,
`MainPage.xaml.cs`, `Platforms/` and `Resources/` are gone, replaced by a single `Program.cs`; the
target framework drops `-windows`, so it builds on Linux for the first time. It builds at zero
warnings, and it **runs**: Photino's log shows `AttachToDocument #app` followed by `RenderBatch`,
which is Blazor attaching and rendering rather than a window merely opening.

**Both of 7b-0's silent traps were hit in advance rather than discovered again**, which is the whole
value of having run the spike first — the explicit `wwwroot` file provider and the removal of
`autostart="false"` were written into `Program.cs` and `index.html` from the start, with the reason
on each. Neither cost any time here. That is the rehearsal working.

**It confirms the publish constraint applies to any Photino app, not just one referencing
`MLQT.Shared`.** This project has no project references at all and its build output still has no
`wwwroot/_content` and no `_framework/blazor.webview.js`; publishing materialises both. The README's
run instructions now say so, because `dotnet run` produces an empty window and no error — the
symptom looks like a broken app rather than a missing build step. **7b-2 owns making that less
awkward for the main application.**

**CI: the MAUI workload is now needed by one project rather than two.** `build-maui` no longer builds
the tester; both library jobs do, on Windows and Linux. The workload cannot be retired until 7b-8
retires `MLQT` itself, but this removes the reason it would have had to stay afterwards.

Not attempted here, deliberately: window state restore, an icon, and any packaging. This is a
developer tool and the point was the host model, not the polish.

### 7b-2 — the `MLQT.Photino` host (S/M)

The project 7a-6 was designed to make small.

- `Program.cs`: `AddMlqtCore()`, the three platform services, `PhotinoBlazorAppBuilder`. The shape is
  already proven twice (`MauiProgram`, `TestHostFactory`).
- `wwwroot/index.html` **generated from `HostAssetManifest`** — as `MLQT.TestHost`'s page is, and as
  the manifest's own doc comment says the Photino host should. Expected to use
  `HostAssetManifest.WebViewBootstrapScript`, because Photino.Blazor builds on the same
  `Microsoft.AspNetCore.Components.WebView` infrastructure as MAUI's `BlazorWebView` — **confirm in
  the spike**, it is a one-minute check and the manifest already has the constant either way.
- Add `"MLQT.Photino"` to `HostAssetManifestTests.HostPages()`. One line; the drift test then holds
  the new page to the same scripts, in the same order, as MAUI's.
- A guard that the Photino host does not reference MAUI, in the spirit of `PortabilityTests`.
- Window lifecycle: title, icon, initial size, and restoring size/position across runs.

### 7b-3 — the three platform services (M — the long pole, but shorter than the sketch says)

The sketch treats this as three ports to two platforms. Two of the three are much smaller than that.

**`IPowerManagementService` — Windows is already done.** The current implementation is
`SetThreadExecutionState` via `DllImport("kernel32.dll")`. That is **plain Win32 with no MAUI
involvement whatsoever** — it moves to the Photino host unchanged, as a file copy. Only Linux needs
new code: `org.freedesktop.ScreenSaver` inhibit over D-Bus, or shelling out to `systemd-inhibit`.
Probe 10 (`power.sleep`) is the acceptance criterion, and it only asserts the calls return — which is
honest, because whether the machine actually stayed awake is not something a probe can see.

**`IFilePickerService` — Photino supplies the dialogs.** `PhotinoWindow` exposes `ShowOpenFile` and
`ShowOpenFolder`, which call native dialogs on Windows, Linux and macOS through `Photino.Native`. So
this is not the Win32 `IFileDialog` + GTK `FileChooser` pair the sketch implies; it is a thin adapter
onto an API that already exists. Note the current MAUI implementation returns *file content* as well
as paths (`PickAndReadFileAsync`, `PickModelicaFileAsync` read the stream) — the Photino version reads
the file itself, which is simpler, not harder, since it has a real path. Probe 11
(`filepicker.wired`) only asserts a picker resolves; **actually opening a dialog stays a manual
check**, as 7a says.

**`ISettingsService` — the real work, and the one with a data-loss hazard.** MAUI `Preferences` → a
JSON file at an XDG/`%LocalAppData%` path.

- It must report the **real path** from `BackingStore`, because probe 16 (`settings.location`) records
  it and the MAUI baseline says `MAUI Preferences (platform key/value store)`. A Photino host
  answering with a temp directory is then a visible difference rather than a support call.
- **Migration is a required sub-step, not a nicety.** On first run the Photino host must read the
  existing MAUI `Preferences` store and write it into the new file — otherwise every existing user
  loses their project list, repository settings, theme and window state on upgrade, silently. On
  Windows that store is reachable without the MAUI workload. Decide explicitly whether to migrate or
  to accept the loss and *tell* people; do not decide it by omission.

### 7b-4 — remove the network dependency from startup (S)

`index.html` pulls Roboto from `fonts.googleapis.com`. That is a network round-trip on every launch of
a desktop application, and on a locked-down Linux box or an offline machine it fails. Probe 7
(`fonts.roboto`) records it as `Pass`/`available` today and is deliberately not a failure when absent —
it is *how you know* whether it arrived.

Bundle the font, add it to `HostAssetManifest.Stylesheets`, and the drift test propagates the change to
every host page including MAUI's, which is still shipping at this point. Probe 7 should then read
`available` on a machine with no network at all — which is worth testing deliberately, once.

### 7b-5 — conformance on Windows (M)

**Same OS, same engine family, one variable changed.** Photino on Windows uses WebView2, which is what
MAUI's `BlazorWebView` uses, so a difference here is *Photino versus MAUI* and nothing else. Doing
Linux first would confound the two.

- Run `/selftest` under Photino/Windows and diff against the committed baseline with
  `HostConformance.Compare`. Target: **no differences.**
- Point the Playwright journeys at the Photino host, or accept that `MLQT.TestHost` remains the
  journey host and say so. (The journeys drive a *server* host; whether they can drive Photino at all
  is a spike question.)
- **The manual checklist**, which is the part no automation covers and 7a says so explicitly:
  visual fidelity, native window behaviour (multi-monitor, DPI, state restore), and a real file dialog
  opening and returning a path. Once, and written down.

### 7b-6 — Linux (L)

The step that delivers the actual value.

- `/selftest` under Photino/WebKitGTK, diffed against the baseline.
- The nightly WebKit journey run — **an outstanding 7a item that lands here**, because until now there
  has been no Linux host to run it against.
- The per-platform CI `selftest` job — **the other outstanding 7a item**, same reason.

**Differences that are expected and correct**, and which the comparison is already built to tolerate:

| Probe | Linux answer | Why it is not a failure |
|---|---|---|
| `svn.client` | `svn` on PATH, or none | The bundled SlikSVN payload is Windows-only. `SvnToolLocator` already handles this — env var → bundled → PATH, with `svn` vs `svn.exe` chosen by `RuntimeInformation` — so **no code change is needed**, only a packaging decision and a line of documentation. |
| `settings.location` | an XDG path | Different by design; that is what the probe is for. |
| `logging.writes` | `~/.local/share/MLQT` | `Environment.SpecialFolder.LocalApplicationData` resolves there. |

`HostConformance.Compare` compares **`Status`, never `Detail`** — a decision made in 7a-7 that pays off
exactly here: all three of the above stay `Pass` with different detail, so they do not need an
allowance list, and a genuine regression still shows.

### 7b-7 — packaging and distribution (M)

Never previously discussed, and it is not optional: a Linux UI nobody can install is not a Linux UI.
Decide the format (AppImage / `.deb` / tarball / `dotnet tool`-style), how `Photino.Native`'s shared
libraries and the WebKitGTK dependency are satisfied, and what the Windows installer becomes now that
it is not a MAUI unpackaged app. Include the Roboto bundle from 7b-4 and, if the SVN decision goes
that way, a Linux svn payload.

### 7b-8 — cutover (M)

Irreversible, so it goes last and only after 7b-5 and 7b-6 are both green.

- Delete the `MLQT` MAUI project; the Photino host takes its place.
- Remove `dotnet workload install maui-windows` from CI — possible **only** once `MLQT.McpTester` is
  dealt with (7b-1). The `build-maui` job goes with it.
- The per-platform `selftest` job becomes the standing parity gate that replaces the baseline diff.
- `PortabilityTests` loses its reason to exist in its current form — every project is portable then.
  Decide whether it becomes "no project references MAUI" (a tombstone that keeps it from coming back)
  or is deleted.
- **Documentation**: CLAUDE.md opens by describing MLQT as a MAUI application and says so in several
  more places; `Documentation/getting-started.md` and the platform-service tables need the same pass.
  The 7a note and this one become historical records and should say so rather than be edited into
  claiming Photino was always the plan.

### 7b-9 — macOS (deferred, sized when reached)

The roadmap sequence says Windows → Linux → macOS. Photino supports macOS and the current MAUI
`FilePickerService` already has a `#if MACOS` NSOpenPanel branch, so the intent predates this note.
Deferred rather than dropped, because nothing in the plan above assumes it and no macOS machine has
been mentioned as available.

---

## What conformance proves, and what it still does not

Unchanged from 7a, restated because this is the phase where it matters:

- ✅ The host resolves assets, runs interop, renders MudBlazor and Cytoscape, and its native services
  work — 16 probes, diffed against a real MAUI capture.
- ✅ Shared UI logic is unchanged — 317 tests that never start a host.
- ✅ The user journeys work — 33 journeys, on Linux as well as Windows.
- ❌ **Visual fidelity.** Nothing catches "MudBlazor looks subtly wrong under WebKitGTK". Screenshot
  diffing across two engines produces false positives on every glyph. A human looks, once, per
  platform.
- ❌ **Native window behaviour.** Photino's surface differs from MAUI's here deliberately.
- ❌ **A real file dialog opening.** Probe 11 asserts wiring, not a GTK dialog returning a path.
- ❌ **That settings survive an upgrade.** No probe can see this; 7b-3's migration step is the only
  thing standing between an existing user and a reset application.

---

## Key decisions and risks

**Already earned its keep.** The first CI run after the 16-probe capture failed on Linux with
`logging.writes: MAUI=Pass, this host=Fail`, and the cause was not the host: `LoggingService.Initialize()`
was the first line of `MainLayout.OnInitializedAsync`, so `/selftest` — which runs under `EmptyLayout`
by design — had no logging, and the probe meant to catch that passed anyway because it asked whether
the log folder contained *any* file rather than whether this run wrote one. Both are fixed (B111), and
initialisation now lives in `AddMlqtCore()` beside the invariant-culture setup. **The Photino host
would have inherited the same defect**, and nothing but a clean machine was ever going to say so.

| Risk | Standing | Mitigation |
|---|---|---|
| ~~`Photino.Blazor` has no `net10.0` release and is ~20 months stale~~ | **Retired (2026-09-08), both platforms** | 7b-0 ran it: builds and runs on `net10.0` on Linux and Windows, `Photino.Native` links the current webkit2gtk-**4.1** ABI rather than the removed 4.0, and both hosts produce 16 `Pass` with zero differences. The test-host fallback is not needed. |
| ~~WebKitGTK breaks Cytoscape or MudBlazor~~ | **Retired (2026-09-08)** | 7b-0 question 3, answered against the real probe route: both pass, and the syntax highlighting turns out to be CSS over server-rendered spans rather than a JS library, so it was never at risk. |
| A page copied from MAUI's does not start Blazor at all, silently | New, found by 7b-0's Windows leg | `autostart="false"` is a MAUI contract — its `BlazorWebView` calls `Blazor.start()` and Photino does not. The window opens, logging initialises and no component is ever created, with no error anywhere. Generating the page from `HostAssetManifest`, which this note already requires, avoids it. |
| A plain build produces a host with no static assets | New, found by 7b-0 | Photino serves `wwwroot` through a bare `PhysicalFileProvider`, so `dotnet publish` is required. 7b-2 owns the F5 story, 7b-7 the shipping one. |
| Settings lost on upgrade | **High, and silent** | 7b-3 migration sub-step. The failure mode is a user opening MLQT to an empty project list. |
| `MLQT.McpTester` blocks retiring the MAUI workload | Certain, low cost | 7b-1 turns it into a rehearsal. |
| Native window behaviour regresses | Medium | Manual checklist in 7b-5; no automation is proposed and none is honest. |
| Linux has no install story | Medium | 7b-7. |
| Journeys cannot drive Photino | Medium | Acceptable: `MLQT.TestHost` stays the journey host, and `/selftest` covers the Photino host. Decide, do not drift into it. |

---

## Open decisions

These need an answer from the project, not from whoever picks up the work. None of them blocks 7b-0.

1. **`MLQT.McpTester`** — port to Photino (recommended: it is a rehearsal and it has to be dealt with
   anyway), drop it, or move it to its own repository with its own MAUI workload?
2. **Settings migration** — migrate from MAUI `Preferences` on first run (recommended), or accept the
   reset and announce it?
3. **Linux packaging format** — AppImage, `.deb`, or tarball?
4. **macOS** — in scope after Linux, or dropped from the sequence?
5. **SVN on Linux** — bundle a client, or require `svn` on PATH and document it (recommended;
   `SvnToolLocator` already does the right thing either way)?

---

## Ordered work breakdown

| Step | Work | Size |
|---|---|---|
| **7b-A** | Widen the journeys over the ~700 lines of UI no test reaches, **before** the port, so they are evidence about it | M — **first** |
| **7b-0** | The spike: `net10.0` compatibility, `/selftest` under Photino on Windows *and* Linux, WebKitGTK verdict | ✅ **done 2026-09-08, both legs** |
| **7b-1** | ✅ **shipped 2026-09-08** — `MLQT.McpTester` is a Photino app, builds and runs on Windows, builds on Linux, and is out of the MAUI job | S |
| **7b-2** | `MLQT.Photino` host: composition root, manifest-generated page, window lifecycle, drift + portability guards | S/M |
| **7b-3** | The three platform services; power is a file copy on Windows, the picker is an adapter, settings is the real work — **including migration** | M |
| **7b-4** | Bundle Roboto; remove the startup network dependency | S |
| **7b-5** | Windows conformance against the baseline, plus the manual checklist | M |
| **7b-6** | Linux: conformance, the nightly WebKit journeys, the per-platform `selftest` job | L |
| **7b-7** | Packaging and distribution | M |
| **7b-8** | Cutover: retire MAUI, the workload, the `build-maui` job, and the documentation that says MAUI | M |
| **7b-9** | macOS | deferred |

7b-A comes first and 7b-0 can run alongside it: the spike answers a question nothing else can, and it
needs a Linux box rather than a keyboard. Everything up to 7b-4 is small or medium and each step leaves
the solution building and green. The phase does not become irreversible until 7b-8.

**What was decided not to do first.** `MLQT.Shared` is at 19.4% covered and `MainLayout` and
`CodeReview` account for nearly 2,000 uncovered lines between them. Chasing those with unit tests was
considered and rejected: what is left in them after 7a-4 is orchestration that ends in
`StateHasChanged`, tests over it would mostly assert that mocks were configured, and 7b **changes**
that startup path anyway — testing its current shape means pinning something about to move, which is
the argument B73 made against extracting twice. The remainder of that extraction is recorded as
**B119** and is ordinary debt, not a prerequisite. `AppState` was the exception and was done: 70 lines,
no dependencies, no renderer needed, and its ledger entry read "Nobody has written the tests", which is
an admission rather than a constraint.
