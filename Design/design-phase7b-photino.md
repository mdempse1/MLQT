# Design Note — Phase 7b: replacing MAUI with Photino

> **Status: IN PROGRESS (2026-09-08). 7b-0 through 7b-3 are done — `MLQT.Photino` runs MLQT, matches
> the MAUI baseline, and existing users' settings migrate themselves — and 7b-A is under way.**
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

#### ✅ Shipped (2026-09-08)

**`MLQT.Photino` exists, runs MLQT, and matches the MAUI baseline on all 16 probes.**

```
baseline: MLQT (runtime 10.0.8), 16 probes
actual:   MLQT.Photino (runtime 10.0.8), 16 probes

NO DIFFERENCES - every probe answered as it did under MAUI.
```

`Program.cs` is 60 lines and does what `MauiProgram` does: `AddMlqtCore()`, three platform services,
a renderer. That is 7a-6 paying off exactly as designed — the composition root was already shared, so
a host is the renderer plus the three implementations that reach the operating system.

**The application itself starts.** Before the probes were wired up the host was run in its ordinary
mode, and the log shows `MainLayout | Application starting`, the startup sequence, and repository
settings loading. Routing works: the router resolved `/` to the shell.

- **The page is generated from `HostAssetManifest`** and `"MLQT.Photino"` is in
  `HostAssetManifestTests.HostPages()`, so it is held to the same 14 scripts in the same order as
  MAUI's, forever. It also carries `app.css`, which MAUI has and `MLQT.TestHost` does not (B120).
- **`PortabilityTests` gained two guards.** One names the hosts that must never depend on MAUI
  (`MLQT.Photino`, `MLQT.McpTester`); the other asserts that **exactly one project still uses the
  workload**, so the number can only go down deliberately. It was two before 7b-1 and is one now;
  7b-8 makes it none.
- **Window placement is restored across runs**, which MAUI did for us. A saved placement is checked
  for sanity rather than trusted: a window restored onto a monitor that is no longer attached is
  invisible and unrecoverable without editing the settings file.

**A host difference worth recording: Photino has no equivalent of `BlazorWebView.StartPath`.**
`PhotinoBlazorApp.Run()` loads `/` itself and ignores `PhotinoWindow.StartUrl`, so a host cannot be
told to start on a route. Self-test mode therefore roots directly on the probes rather than
navigating to them. Rather than each host inventing its own way of doing that,
`MLQT.Shared/Pages/SelfTestHost.razor` is `EmptyLayout`'s providers plus `SelfTest`, and the Photino
host roots on it when `SelfTest.IsEnabled`. `SelfTest` also now owns the `MLQT_SELFTEST` constant and
the route, beside the two variables it already owned — it was in `MLQT/SelfTestLauncher.cs` while MAUI
was the only host that could capture a baseline, and two hosts reading one environment variable
through two constants is how they come to disagree about its name.

**What is provisional, and named so it is not read as finished.** The settings service is a JSON file
and **does not migrate anything**; the power management service is the MAUI implementation's Win32
call, unchanged, and **does nothing on Linux**. Both are 7b-3's. The second is worth stating twice:
probe 10 asserts only that the two calls return, so it passes on Linux today while the machine can
still sleep in the middle of a long check. That is a limit of what a probe can see, not a pass.

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

#### ✅ Shipped (2026-09-08)

**The migration is done, it lives entirely in the Photino host, and the MAUI app is not changed.**

An existing user's project list, repository settings, themes and external-tool paths live in MAUI
`Preferences`; the new host reads a JSON file; nothing connected the two. This went through two
designs, and the first one shipped for about an hour before being taken back out.

**The design that was wrong.** `Preferences` can only be called from MAUI and cannot be enumerated, so
the first answer was to have the MAUI app copy its own settings into the shared store on startup —
`MLQT`'s `SettingsService` delegating to `JsonSettingsService` and seeding it once from a written-down
list of six keys. It works, and it is unshippable: **it requires a MAUI release that every user
installs and runs *before* the Photino one.** There is no way to depend on that sequence, and the
whole point of 7b is that the MAUI build goes away.

**The design that is right, and why it was not obvious.** For an *unpackaged* Windows app, MAUI's
`Preferences` is a plain JSON file:

```
%LocalAppData%\<publisher>\com.mlqtproject.MLQT\Settings\preferences.dat
{"": {"Repositories": "[...]", "UI": "{...}", ...}}
```

The earlier spike (B122) concluded `Preferences` was "in none of the places it is supposed to be" —
`HKCU\Software`, a WinRT settings container, the application data folder — and that conclusion is what
forced the MAUI-seeds design. It was looking in the packaged-app locations. The unpackaged path was
one directory level away, under a publisher segment that reads `User Name` because that is the MAUI
template's placeholder `Publisher="CN=User Name"`, still in `MLQT`'s appxmanifest today.

So `MauiPreferencesFile` finds and reads that file, `JsonSettingsService.MigrateFrom` copies it in, and
`MLQT.Photino/Program.cs` calls both before the window opens. The MAUI build is read and never written,
so it is not part of the upgrade path at all: **a user goes from any MLQT release straight to this one.**

- **Copying everything beats copying a list, and the evidence is on this machine.** The abandoned
  design's six-key catalogue and the real file share *five* names. The file carries a `StyleChecking`
  key from a version of MLQT that no longer reads it, and has no `ReferenceLibraries`. The list was
  wrong in both directions on the only install it was ever checked against, and would have been wrong
  again the next time a key was added. A file can be enumerated; a migration that moves what it finds
  cannot be wrong about what to look for. `MlqtSettingsKeys` and its guard test are deleted.
- **The publisher segment is searched for, not hard-coded.** One level of wildcard under
  `%LocalAppData%`, newest file wins. A build that set a real publisher would file its settings
  elsewhere and the symptom would be a migration that silently found nothing.
- **Two guards, guarding different things.** A marker in the store stops the migration running twice,
  so a setting the user *deleted* in the new host does not come back on the next launch; a per-key
  check stops it overwriting anything already there, so a theme chosen in the new host survives.
  Anything in the new store is newer by definition, because the MAUI build cannot write to it. The
  marker is written even when nothing was found — "there was nothing to bring across" is an answer.
- **Nothing is deleted and nothing is written back.** Going back to an earlier release still works,
  which during a migration matters more than tidiness.
- **Failure is silent and survivable, deliberately.** A missing folder, a different publisher, an
  unparseable file: each migrates nothing and the host still opens. A user who loses their settings to
  a corrupt file still has an application, and their old install is untouched.

Twelve tests over `MauiPreferencesFile` and seven over `MigrateFrom`, every one verified by mutation:
removing newest-wins, hard-coding the publisher, reading only the default container, keeping
non-string values, dropping either guard, and writing the marker only when something was copied are
each caught by a named test.

**What this replaces in the paragraph above:** there is no longer a "point of no return" release. The
MAUI app never writes to the new store, so the two hosts can be run alternately for as long as anyone
wants, and 7b-8 is the only cutover.

**Linux sleep prevention** holds an inhibit lock through `systemd-inhibit` rather than speaking D-Bus,
which would need a session-bus connection and a dependency to make one and would fail in the same
environments this has to degrade in anyway. Where there is nothing to hold a lock with it does nothing
and logs once. **Probe 10 cannot tell a real inhibit from a no-op** — it asserts only that the calls
return — so the log line matters more than the probe here.

**The file picker** was written in 7b-2 over `PhotinoWindow.ShowOpenFile`/`ShowOpenFolder`. That a
dialog opens and returns a path is still a manual check, once per platform.

Conformance is unchanged: 16 probes, zero differences.

### 7b-4 — remove the network dependency from startup (S)

`index.html` pulls Roboto from `fonts.googleapis.com`. That is a network round-trip on every launch of
a desktop application, and on a locked-down Linux box or an offline machine it fails. Probe 7
(`fonts.roboto`) records it as `Pass`/`available` today and is deliberately not a failure when absent —
it is *how you know* whether it arrived.

Bundle the font, add it to `HostAssetManifest.Stylesheets`, and the drift test propagates the change to
every host page including MAUI's, which is still shipping at this point. Probe 7 should then read
`available` on a machine with no network at all — which is worth testing deliberately, once.

#### ✅ Shipped (2026-09-08)

Roboto is in `MLQT.Shared/wwwroot/fonts`, the manifest names `_content/MLQT.Shared/fonts/roboto.css`
instead of the Google URL, and the drift test propagated it to MAUI's page, Photino's and the test
host's generated one. **No host page loads anything over the network any more, and a test says so** —
`NoAssetIsFetchedFromTheNetwork` fails on a `//` anywhere in either manifest list, because the next
one to arrive is as likely to be a script as a font.

**One variable face per subset, all nine subsets, 231 KB.** The link it replaced asked for four static
weights (300/400/500/700); the variable font covers 100–900 in one file per subset, so MudBlazor can
ask for any weight and there is no written-down list of weights to be wrong about — the same argument
that removed the settings key catalogue in 7b-3, met twice in two days. Bundling every subset rather
than latin + latin-ext costs 130 KB and removes the judgement call about which scripts a Modelica
engineer will never type; `unicode-range` means the engine still only loads what a page uses.

**Licence: SIL Open Font License 1.1**, copied to `OFL.txt` beside the files. Worth stating because
Roboto is widely remembered as Apache 2.0 — it was, up to Roboto 2. The variable Roboto 3 that Google
Fonts serves today is OFL, which the font metadata API confirms and memory does not.

**Probe 7 is now a real assertion, and it fixes a second thing.** While the font came over the network,
absence was legitimate — an offline machine had no Roboto through no fault of the build — so the probe
recorded the answer and always passed. Bundled, absence means a missing file or a wrong path, so it
fails. It also now calls `document.fonts.load(...)` before checking rather than `check()` alone: the
Linux leg found `not available` on WebKitGTK because **nothing on the `/selftest` page requests
Roboto**, so the declared face was never loaded and `check()` correctly said so. That is a question
about lazy loading, not about the font, and asking a face to load before asking whether it loaded
removes it. The engine difference recorded in 7b-0 and 7b-1 should therefore be gone on both.

Verified by running the published Photino host with `MLQT_SELFTEST=1`: **16 probes, all `Pass`,
`fonts.roboto` = `available`** — and again with `wwwroot/_content/MLQT.Shared/fonts` renamed away,
where it reads `Fail  not available (bundled font did not load)`. A probe that cannot fail is not a
probe, and this one had never been watched failing.

**Not done, deliberately:** `MLQT.McpTester` still links `fonts.googleapis.com`. It has no reference
to `MLQT.Shared`, so using the bundle would mean a second copy of 231 KB in its own `wwwroot`, and it
is a manual diagnostic tool rather than something shipped to users. Recorded as B124.

### 7b-5 — conformance on Windows (M)

**Same OS, same engine family, one variable changed.** Photino on Windows uses WebView2, which is what
MAUI's `BlazorWebView` uses, so a difference here is *Photino versus MAUI* and nothing else. Doing
Linux first would confound the two.

- ~~Run `/selftest` under Photino/Windows and diff against the committed baseline~~ — **done, and
  committed.** 16 probes, **zero differences**. See below.
- ~~Point the Playwright journeys at the Photino host, or accept that `MLQT.TestHost` remains the
  journey host and say so.~~ — **decided: `MLQT.TestHost` remains the journey host.** See below.
- ~~**The reported slowness (B125)**~~ — **done.** Not the host: a same-day A/B has Photino at 426 s
  against MAUI 2026.4.0's 434 s on a larger graph. Two real defects came out of the investigation
  (B126, B127) and neither was a migration regression. See below.
- ~~**The manual checklist**~~ — **run on Windows (2026-09-08).** Two differences found, both fixed:
  the window opened smaller than MAUI's, and the taskbar entry had no icon. Nothing else differed.
  See below.

#### The manual checklist, run on Windows (2026-09-08)

Visual fidelity, native window behaviour, and a real file dialog: **two differences from the MAUI
build, and nothing else.** Both are fixed.

**1. The window opened smaller.** The host asked Photino for `1400 x 950` against MAUI's `1200 x 900`
and got a *smaller* window — because **MAUI sizes in device-independent units and Photino in physical
pixels.** On the reporter's 125% display, 1400 physical pixels is 1120 units and 950 is 760, against
MAUI's 1200 x 900: a window a fifth shorter, from code that appears to ask for a larger one. The unit
question was settled by measurement, not documentation — with `SetSize(1400, 950)` the self-test's own
`interop.dimensions` probe reported a 1106-pixel viewport, which is 1400 ÷ 1.25 less the chrome.

`WindowGeometry.Centred` now takes MAUI's numbers, scales them by the monitor, clamps to the **work
area** rather than the monitor bounds, and centres — which is what `App.xaml.cs` did. It lives in
`MLQT.Services` because it is arithmetic and the host has no test project; the host keeps only the
part that reads the monitor.

**The part that had to be measured to be found:** `PhotinoWindow.MainMonitor` and `ScreenDpi` throw
*"the Photino window hasn't been initialized yet"* until after `Run()`. Reading them where the
placement was being decided therefore threw into a `catch` that logs and returns a fallback — so the
first-run size would have been wrong on every machine, forever, with nothing but a log line to say so.
The scaled size is applied from a `WindowCreated` handler instead. Verified end to end on a fresh
profile: `First run: opening 1500x1125 at 1810,127 (work area 5120x1380, scale 1.25)` — which is
1200 x 900 units, exactly what MAUI asked for.

**2. No taskbar icon — and the fix was not in the code.** `ApplicationIcon` puts the icon on the
`.exe`; `Program.ApplicationIcon` also sets it on the window at runtime, per platform, because Photino
creates its own native window (Windows takes the multi-size `.ico`, GTK a PNG, both shipped beside the
executable because `SetIconFile` takes a path rather than a resource). Explorer, Alt-Tab and the title
bar were then correct — **and the taskbar was still generic, for every copy of the executable from
every folder.**

Six code-level hypotheses were tried and disproved. The answer was outside the process: a stale
`MLQT.lnk` in the Start Menu, pointing at a publish holding a build from *before* the icon existed.
Windows 11 resolves a running window to its matching Start Menu shortcut and takes the button's icon
from **that**; the shortcut said "use the target's icon" and the target had none. Moving the shortcut
aside and running the same build unchanged made the icon appear.

**What actually broke the deadlock was looking rather than reasoning.** Every hypothesis until then
was argued from what the window "should" do; the first screenshot of the running taskbar killed three
of them in a minute, and UI Automation — which names the button, so its exact rectangle can be
cropped — made the comparison reliable on a desktop with other windows on it. That technique and the
full list of eliminated causes are in `Branding/README.md`, so the next person starts where this
finished. **7b-7 owns creating the shortcut deliberately**, at the installed location: Windows
recreates it aimed at whatever it last saw run, which for a developer is a temporary folder.

The assets are in `Branding/` with a README covering what each one is for, and what deliberately was
**not** updated: the MAUI host keeps the template icon (it is retired in 7b-8, and changing it means
regenerating the MAUI resource set for a build that is about to go) and `MLQT.McpTester` keeps its own.

#### ✅ Conformance on Windows (2026-09-08): committed, not just observed

`/selftest` under the published Photino/Windows build against the committed MAUI baseline:
**16 probes, zero differences.** `fonts.roboto` now reads `available` from the bundled font (7b-4);
`settings.location` reads `%LocalAppData%\MLQT\settings.json` against MAUI's
`MAUI Preferences (platform key/value store)`, which the comparison ignores by design — it compares
`Status`, never `Detail`, because two hosts answering the same question differently is the point.

**The result is now a file rather than a terminal window.** `MLQT.Shared.Tests/TestFiles/selftest-photino-windows.json`
holds the capture and `DesktopHostConformanceTests` compares it, because until this step the only
evidence Photino had ever matched was a scrollback that had since been closed. It is honestly a
**record, not a live check** — it says what the host answered on the day, and re-capturing is manual
(the class header carries the command). What it does catch is the failure that is actually likely: a
probe added to `SelfTest`, or one host's report re-captured and not the other's, so the two sets drift
apart. Two tests guard the record itself — that the file really came from Photino and not a copy of the
baseline, and that it holds every probe — because `Compare` ignores the `Host` field, so a capture
accidentally overwritten with the baseline would compare clean and prove nothing.

7b-6 captures `selftest-photino-linux.json` the same way.

#### The counts were never going to match (B130)

With B129 fixed, the same project reported **76,129** models where MAUI had reported **77,860**, and
the expectation was that they would now agree exactly. They could not, and neither figure was right.

`Libraries.Sum(l => l.ModelIds.Count)` counts a class once per library that lists it, and the same
library is routinely loaded twice — a tool's folder ships the encrypted build of a library the user
also has checked out. `ExternalStubBuilder` records a documented class **only when its source is not
already in the graph**, so which library ends up listing the id depends on which parallel load
finished first. The encrypted `Claytex` contributed **2,343, 2,290 and 1,637** classes on three
consecutive runs of the same project, across both hosts:

| | Photino 18:21 | MAUI 18:31 | Photino 19:23 |
|---|---|---|---|
| `Claytex` | 2,343 | 2,290 | 1,637 |
| `ClaytexFluid` | 310 | 444 | 0 |
| `FluidPower` | 679 | 679 | 179 |
| `VeSyMA` | 976 | 1,339 | 1,459 |

That `LoadedLibrary` entries share ids was already known and handled where it showed — `Owns`, and the
tree keying by model id. The raw sum was the place left, and it is what the deferred-analysis
threshold is compared against. `TotalModelCount` now counts distinct ids that are in the graph.

**Worth noting how it was found:** by someone expecting two numbers to be equal and saying so when
they were not. The number had been wrong for as long as it had existed, and being wrong by a varying
amount is exactly what makes a figure like that survive — every reading is a bit different, so no
reading looks anomalous.

#### Decision: the journeys stay on `MLQT.TestHost`

Photino is a native window. Playwright needs something to attach to, and the only way in is to push
`--remote-debugging-port` into WebView2 through `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` — which is
**Windows-only, has no WebKitGTK equivalent**, and would mean the Linux leg of 7b-6 either goes
uncovered or gets a second mechanism. A test harness that works on one of the two platforms the phase
exists to support is not worth its cost.

So the division of labour 7a designed stands, and is now stated rather than assumed:

| | Runs on | Answers |
|---|---|---|
| The Playwright journeys | `MLQT.TestHost` (server-rendered, over HTTP) | **Does the application behave correctly?** Real components, real interop, real MudBlazor, on both Windows and Linux runners. |
| `/selftest` + `HostConformance` | Every real host, including the desktop ones | **Does this host answer the same as the last one?** Sixteen probes over the things a host owns. |

The gap this leaves is honest and worth naming: **nothing automated drives Photino's own UI.** A
component that renders under a server host and not under WebView2 or WebKitGTK would be caught only by
a probe that happens to cover it, or by a person. That is what the manual checklist is for, and it is
why the probe set is the thing to extend when a host-specific failure is found — not the journeys.

#### Partly done (2026-09-08): what the slowness actually was

Three slow things were reported from real use. **The application log had already recorded all three**,
on the same library, across both hosts, for weeks — and nobody had read it. Two of the three turned
out not to be about Photino.

| Reported | What the log says |
|---|---|
| Startup style checking ~25% slower | **Not a regression — settled by a same-day A/B, below.** The cross-day figures that suggested one (MAUI 324/325 s on 7 Sep against Photino 420 s on 8 Sep) were variance: MAUI itself ranged 183–468 s on 2 Sep over the same graph. |
| Analysis after adding a repository runs free and makes the UI crawl | Correct, and correctly identified by the reporter as not a regression: that path announced itself with a snackbar and ran in the background while every sibling path uses the modal progress dialog. **Fixed (B127).** |
| Correcting a spelling takes a minute | The file held **4,478 classes**. Removing them from the graph is a loop over an O(*n*) `RemoveNode`, so it cost ~178 million set operations, and the same shape sat beside it in the library index cleanup. **Fixed (B126)** — 5,746 ms → 22 ms and 210 ms → 3 ms at those sizes. Always quadratic; a file this size is what made it visible. |

#### The A/B that settles it (2026-09-08, 18:21 and 18:30)

Both hosts opened the same project one after the other on the same machine — the Photino build, then
the `2026.4.0` MAUI release tagged before this branch started. Same repositories, same 39,860-model
dependency analysis, same `Claytex` check.

| | Photino | MAUI 2026.4.0 |
|---|---|---|
| Repositories and libraries loaded | 70 s | 59 s |
| Encrypted reference libraries loaded | **97** | 53 |
| Models in the graph | **118,612** | 77,860 |
| Dependency analysis (39,860 models) | **75 s** | 78 s |
| Style check (`Claytex`) | **297 s** | 299 s |
| Graph analyses, coverage, final flush | **54 s** | 57 s |
| **Deferred pipeline, total** | **426 s** | **434 s** |

**Photino is 2% faster on a graph half again as large**, which is inside the noise on the timings and
outside it on the workload. The reason the workloads differ is itself worth recording: `ReferenceLibraries`
was **not in `preferences.dat`**, so the migration had nothing to bring across, and the Photino store has
an entry only because it was configured there afterwards. MAUI therefore ran with 44 fewer encrypted
reference libraries and was still the slower of the two — so the conclusion holds a fortiori, and the
comparison would only improve if it were tightened.

**And the mismatch turned out to be a defect, not a configuration.** Both runs loaded the *same 97
distinct libraries*; the Photino one issued **159 loads** to do it. Every one of its three reference
paths was already registered as a reference-only repository, and nothing checked whether the two
mechanisms named the same folder — so sixty-two libraries, the standard library among them, were
discovered, parsed and indexed twice. The graph absorbed it silently, because `AddNode` keys on the
class id; what did not absorb it is everything counted per library, including the `118,612` model
total against a true `77,860` that the deferred-analysis threshold is compared against. **B129, fixed**
in `ReferenceLibraryRules` — and found only because the two hosts were made to disagree about their
settings, which is the sort of thing a migration is good for.

It is also an unplanned check on the invariant from the reference-library work: **nothing may scale
with graph size instead of with the checked set.** Forty thousand extra reference classes moved the
style check by two seconds in 300. That is the invariant holding, measured.

**And the finding nobody was looking for.** Putting every run in the log into one series exposed that
this repository's style check has roughly tripled since late August: ~105 s median over 24–27 August,
~190 s on 1–2 September, ~300 s now. **That is expected growth, not a regression** — the intervening
weeks added the phase-5 suppression reads and the phase-6 whole-graph analyses and coverage
measurement, more rules were enabled, and the project now loads far more libraries including the
encrypted ones. Recorded as **B128** because it is worth knowing what the current figure is made of and
worth speeding up eventually, and explicitly **out of scope for this branch**: it is not the migration's
to fix and chasing it here would mean 7b never finishes.

**The method, worth writing down because it was nearly not used:** the first instinct on all three was
to reason about what Photino does differently, and the phase note already said not to. Every number
above came from `%LocalAppData%/MLQT/*.log`, which the application has been writing all along;
`LogProcessStart`/`LogProcessEnd` pairs are timestamped, so a series is a `grep` away. The one measured
fact that a hypothesis would have got wrong is the Photino host's idle CPU: **0.4% of one core**, which
rules out the spinning-message-pump theory that would otherwise have been the obvious place to start.

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
| **7b-2** | ✅ **shipped 2026-09-08** — `MLQT.Photino` runs MLQT and matches the MAUI baseline on all 16 probes; page held to the manifest, two portability guards, window placement restored | S/M |
| **7b-3** | ✅ **shipped 2026-09-08** — the Photino host reads MAUI's `preferences.dat` directly and copies every key it finds, so no MAUI release is needed; Linux sleep prevention via `systemd-inhibit` | M |
| **7b-4** | ✅ **shipped 2026-09-08** — Roboto bundled (variable, all nine subsets, OFL), no host page fetches anything over the network, and probe 7 is a real assertion that loads the face before checking it | S |
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
