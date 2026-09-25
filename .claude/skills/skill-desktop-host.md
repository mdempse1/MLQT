# Desktop Host Skill (Photino)

Load this skill when working on `MLQT.Photino`, `MLQT.McpTester`, `HostAssetManifest`, the host page,
window placement or icons, the three platform services, or any behaviour that differs between
WebView2 on Windows and WebKitGTK on Linux.

MLQT was a .NET MAUI application until 2026-09-10. It is now a **Photino.Blazor** application on
Windows and Linux from one `net10.0` project, with **no .NET workload required**. `PortabilityTests`
is what keeps MAUI out.

## The shape, and why a host is small

`MLQT.Photino/Program.cs` is the whole host, and it is deliberately the same shape the MAUI host had:

```
AddMlqtCore()                      // everything that is not this host's business
+ IFilePickerService               // the three that reach the operating system
+ ISettingsService
+ IPowerManagementService
+ a renderer, a window, its icon and placement
```

`AddMlqtCore()` in `MLQT.Shared/MlqtServiceCollectionExtensions.cs` is the shared composition root —
it registers the services, MudBlazor, the invariant culture **and logging**. A host is the renderer
plus the three implementations that reach the operating system, and nothing else. That is what made
swapping hosts a small change, and it is the rule to preserve: **a service registered in one host and
not another is a class of bug that stops existing** when everything goes through `AddMlqtCore()`.

There are three hosts over the same `MLQT.Shared`: `MLQT.Photino` (ships), `MLQT.TestHost` (ASP.NET
Core Blazor Server, for the Playwright journeys) and `MLQT.McpTester` (a second Photino app). Anything
a host needs is a candidate for `AddMlqtCore()` or for `HostAssetManifest`.

## Things that fail silently on this host

On Photino a page that fails to load is **indistinguishable from a page that loads and never starts**:
the window opens, logging initialises, and nothing else happens. There is no error anywhere. Each of
these was found by running into it.

| Trap | What happens | The rule |
|---|---|---|
| `autostart="false"` in the host page | Blazor never starts | That attribute is a **MAUI contract** — `BlazorWebView` calls `Blazor.start()` and Photino does not. Generate the page from `HostAssetManifest` and the question does not arise |
| `PhotinoBlazorAppBuilder.CreateDefault()` with no file provider | No static assets at all | The provider must be rooted at `wwwroot` **explicitly**; `HostPage` is `"index.html"` with no directory part |
| `dotnet build` rather than `dotnet publish` | Window opens, loads nothing | Photino serves `wwwroot` through a bare `PhysicalFileProvider` with no static-web-assets support. `StaticWebAssetsFileProvider` is what makes F5 work; **what ships is the published layout**, so that is what the smoke tests measure (B133, and B167 when the same defect survived in `MLQT.McpTester`) |
| The static-web-assets manifest is named after the **application** | RCL assets 404 | Bit `MLQT.TestHost` first, because the in-process entry assembly was the test project |
| Logging a failure before `AddMlqtCore()` | The loud failure is itself silent | `LoggingService.Initialize()` is called first in `Program.Main` for exactly this reason |

**There is no equivalent of `BlazorWebView.StartPath`.** `PhotinoBlazorApp.Run()` loads `/` itself and
ignores `PhotinoWindow.StartUrl`, so a host cannot be told to start on a route. Self-test mode roots
directly on `SelfTestHost` instead of navigating. `SelfTest` owns the `MLQT_SELFTEST` constant and the
route so that two hosts reading one environment variable cannot come to disagree about its name.

**`app.MainWindow.LogVerbosity` must be 0.** Photino logs every message it exchanges with the webview
to stdout, and a Blazor render batch is one of those — base64, tens of kilobytes per UI update,
written synchronously on the thread producing the update. That was the shape of the "Photino feels
slower than MAUI" report (B121).

**A native dialog is never opened from inside WebView2's callback (B283).** A click reaches a
component through WebView2's `WebMessageReceived`, and Photino.Blazor handles the message *inline* on
that callback's stack (its `SynchronousTaskScheduler` runs a task where it is queued). A picker opened
straight from a click therefore ran the dialog's nested message loop inside WebView2's event handler;
anything Blazor rendered while the user browsed reached `SendWebMessage` re-entrantly, and WebView2
runtime 153 stops the process on that — `0x80000003` in `EmbeddedBrowserWebView.dll`, and **nothing in
MLQT's log**, because no managed code ever saw it. The only evidence is the Windows Application event
log. `PhotinoFilePickerService.OnTheMessageLoopAsync` opens every dialog by handing it to
`PhotinoWindow.Invoke` from a pool thread, so it runs from the top of the message loop once the
callback has returned; `NativeDialogPolicyTests` holds every dialog call in both Photino apps to it. Not
`await Task.Yield()`, which goes wherever the current synchronization context sends it — with none, a
native dialog would open on a pool thread.

**`InvokeAsync` from a pool thread waits for the UI thread.** Photino.Blazor's
`PhotinoSynchronizationContext` is a copy of ASP.NET's renderer context with one change:
`ExecuteSynchronously` hands the work to `PhotinoWindow.Invoke`, which on Windows is a synchronous
`SendMessage`. So a component's `InvokeAsync` called from a background thread — and every queued
continuation — **parks that pool thread until the UI thread runs it**. Under Blazor Server the same
call returns at once. Two consequences worth designing around: anything synchronous and slow on the
UI thread starves the thread pool too (it grows by about a thread a second, which is what the gaps
look like in the log), and a lock held while raising an event whose handler calls `InvokeAsync` is a
deadlock waiting for the UI thread to want that lock. B293 was 55 working-copy status scans behind
one that was running on the UI thread; B299 is a handler still in that shape.

**Webview chrome settings go in before `Run`.** `SetContextMenuEnabled(false)` and
`SetDevToolsEnabled(...)` turn off the engine's own right-click menu and its **Inspect** entry, which
were on in both release builds. Reading them *during* `RegisterWindowCreatedHandler` segfaults the
process. The DOM `contextmenu` event still fires on both platforms, which is what MLQT's own menus
(the spell-check correction menu) are built on — re-check that by hand if these lines are revisited.

## `HostAssetManifest` — the host page is generated, not copied

`MLQT.Shared/HostAssetManifest.cs` is the single list of scripts, styles and the bootstrap script, and
every host's page is generated from it. `HostAssetManifestTests.HostPages()` enumerates the hosts and
holds every page to the same assets **in the same order**, and
`EveryAssetTheHostPageAsksFor_Loads` asserts each one actually resolves — which is how B120 (the test
host asking for an `app.css` it did not have) was found and fixed in both directions.

Adding a host is one line in that theory. Copying a page from another host by hand is how
`autostart="false"` gets carried across.

## Window behaviour — the part no probe can see

- **Photino sizes windows in pixels; MAUI sized them in units.** Asking for `1400 x 950` on a 125%
  display produced a window a fifth *shorter* than MAUI's `1200 x 900`. The scaled size is applied
  from a `WindowCreated` handler (B131).
- **A restored placement is checked, not trusted.** `WindowGeometry` decides whether a saved position
  is still reachable against the *current* monitor layout — a screen left of or above the primary is
  still a screen, a sliver off the edge is not enough to grab, and no monitors at all means there is
  nothing to judge against rather than "move everything". An undocked laptop otherwise gets a window
  it cannot reach and cannot recover without editing the settings file (B149).
- **`WindowIcon.Apply` runs after the window exists.** Photino attaches the icon during creation,
  which leaves the taskbar button showing whatever it had, and attaches the 100% size.

### Icons: the desktop shell identifies an application by an installed entry

This is one problem with two spellings, and **neither Windows nor GNOME will take the icon off the
window instead**:

- **Windows** — the taskbar button reads the Start Menu shortcut, not the window. The installer owns
  creating it at the installed location (B132).
- **Wayland** — `SetIconFile` is a **no-op**. Photino's Linux library imports
  `gtk_window_set_icon_from_file`, an X11-era GTK call; a full `WAYLAND_DEBUG` trace shows the toplevel
  receiving five requests and not one of them an icon. GNOME matches the window's `app_id` — which is
  the GUI executable's file name — to an installed **desktop entry** and takes the icon from there,
  for the dock, Alt-Tab and the window list alike. Without the `.deb`'s desktop entry MLQT is
  unbranded everywhere on a Wayland session (B134). Rename the executable or the entry without the
  other and the icon silently disappears; `MLQT.Shared.Tests/DebianPackageTests` holds the chain.
- **The icon carries its own light plate**, because a dark-variant icon does not exist on either
  platform: a desktop entry's `Icon=` is a single key, Ubuntu's dark style does not switch the *icon*
  theme, and the `altform-lightunplated` variants that would do it on Windows are an MSIX feature Inno
  Setup does not produce. The 1px edge on the plate is load-bearing — without it the near-white plate
  dissolves into a light Windows 11 taskbar. 16px needs its own source artwork; scaled from 96px the
  three bars merge into a grey smudge. Rasters are generated by `Branding/render-icons.py`.
  *Diagnostic note:* an empty `~/.local/share/icons/hicolor` left by an old hand-install still carries
  an `icon-theme.cache` naming `mlqt`, and GTK trusts it — check that before concluding a package
  regressed.

## The three platform services

| Service | Windows | Linux |
|---|---|---|
| `IFilePickerService` | `PhotinoWindow.ShowOpenFile` / `ShowOpenFolder` — native dialogs through `Photino.Native` on both platforms. The Photino version reads the file itself, because it has a real path | same |
| `IPowerManagementService` | `SetThreadExecutionState` via `DllImport("kernel32.dll")` — plain Win32, moved from MAUI as a file copy | an inhibit lock held through `systemd-inhibit`. Not D-Bus: that needs a session-bus connection and a dependency to make one, and would fail in the same environments this has to degrade in. Where there is nothing to hold a lock with it does nothing and **logs once** |
| `ISettingsService` | `JsonSettingsService` under `%LocalAppData%/MLQT` | the same class under `~/.local/share/MLQT` |

`ISettingsService` is not host-specific at all any more, which is why it lives in `MLQT.Services`.
`BackingStore` reports the real path, because a write-read round-trip cannot tell persistence from the
appearance of it.

**Probe 10 cannot tell a real inhibit from a no-op** — it asserts only that the calls return. The log
line matters more than the probe there. The same is true of probe 11: a picker resolving is not a GTK
dialog opening and returning a path, which stays a manual check once per platform.

### The MAUI settings migration

`MauiPreferencesFile` reads the retired host's store directly and `JsonSettingsService.MigrateFrom`
copies it in, once, before the window opens. Worth knowing even though MAUI is gone, because the
design is the reusable part:

- For an **unpackaged** Windows app, MAUI `Preferences` is a plain JSON file at
  `%LocalAppData%\<publisher>\com.mlqtproject.MLQT\Settings\preferences.dat`. An earlier spike
  concluded it was "in none of the places it is supposed to be" because it looked in the *packaged*-app
  locations. **A search that finds nothing is evidence about the search.**
- **Copying everything beats copying a list.** The abandoned design's six-key catalogue and the real
  file shared five names — wrong in both directions on the only install it was ever checked against.
  A file can be enumerated; a migration that moves what it finds cannot be wrong about what to look for.
- **The publisher segment is searched for, not hard-coded** (one level of wildcard, newest file wins).
- **Two guards guarding different things**: a marker stops the migration running twice, so a setting
  the user *deleted* does not come back; a per-key check stops it overwriting anything already there.
  The marker is written even when nothing was found — "there was nothing to bring across" is an answer.
- **Nothing is deleted and nothing is written back**, so going back to an earlier release still works.
- **Failure is silent and survivable**: a missing folder, a different publisher, an unparseable file
  each migrate nothing and the host still opens.

## WebKitGTK differences the shared code must have an opinion about

These are not host bugs; they are places where `MLQT.Shared` had never been asked the question.

- **Overlay scrollbars.** WebKitGTK's scrollbars take no layout width and float above the content, so
  one sat on top of the library browser's buttons and swallowed clicks. WebView2's are classic and
  occupy layout, so nothing is ever underneath one. MudBlazor's own injected CSS was disabling the
  fixes — `document.styleSheets` in the live document finds what grepping the source cannot (B140).
- **Asynchronous scroll events.** A pane-sync guard written as `syncing = true; …; syncing = false`
  around an assignment suppresses nothing, because scroll events are dispatched asynchronously.
  Harmless on WebView2; on WebKitGTK, where the wheel scrolls smoothly over several frames, every
  echoed write **cancelled the animation the user had just started** (B139).
- **`window.open` through JS interop does nothing.** It launches a browser on WebView2 and silently
  nothing on WebKitGTK. `MLQT.Services/ExternalBrowser.cs` is the single shell-open (B138).
- **Path separators.** Canonicalising VCS paths on `\` yields, on Linux, one file name containing
  backslashes. `RevisionControl/VcsRelativePath.cs` is the one rule, on `/`. The test that existed
  pinned the defect: it asserted the *shape* of the path and never whether that shape opens the file
  it names (B137).
- **inotify instances are a limited resource.** One watcher per resource directory took 85 of the
  machine's 128 instances and killed the MCP server at startup. Watch a single root, not a directory
  each (B163/B164).
- **Software rendering.** `GraphicsEnvironment.Probe()` logs one line on Linux at startup saying
  whether a DRM render node exists. Where there is none the webview paints every frame on the CPU and
  **any rendering measurement from that machine is a lower bound**, not a measurement (B141).

**Method note, and it is the general one:** running the manual checklist on Linux alone found one
difference; running it again **with a Windows build open beside it** found four more. *"Does this look
right?"* and *"does this match that?"* are different questions and only the second one has an answer.

## Conformance — `/selftest` and the frozen baseline

`MLQT.Shared/Pages/SelfTest.razor` runs **16 probes** and renders a machine-readable JSON blob. The
route lives in `MLQT.Shared`, so every host runs literally the same probes; each declares itself
through `MLQT_SELFTEST_HOST` (the *process* name is not good enough — a first capture was labelled
with it and was wrong). Each host is started with `MLQT_SELFTEST=1` and writes the JSON to
`MLQT_SELFTEST_OUT`.

The probes, each chosen for a specific way a host swap can break: JS interop returns real dimensions;
RCL static assets serve; each library script defines its global (and `klayjs` defines none, so that
probe asks Cytoscape which layouts it *registered*); Cytoscape initialises and reads back;
`diffViewer.initSyncScroll` and `spellCheck.init` (they take **elements, not ids**); MudBlazor
dialogs, select popovers, snackbars and overlays are in the DOM; Roboto renders at the expected
metrics; settings round-trip; settings report their backing path; power management returns; the file
picker resolves; NLog wrote *this run's* line; `SvnToolLocator` resolves a client or reports the
fallback; the culture's decimal separator is `.`.

**The MAUI baseline is frozen.** The host that produced it no longer exists, so it cannot be re-taken
and **no probe can ever be added** — the drift guard refuses a probe with no MAUI answer.
`HostConformance.Compare` reports a probe present on only one side as a difference, because the
failure worth guarding against is two probe sets drifting apart while their intersection still
matches. The `desktop-selftest` CI job publishes the real host per platform, overwrites that
platform's committed capture and diffs it against the baseline on every push.

**What conformance does not prove:** visual fidelity (a human looks, once, per platform — screenshot
diffing across two engines produces false positives on every glyph), native window behaviour, a real
file dialog opening, and that settings survive an upgrade.

## Platform facts worth not rediscovering

- **The host runs server GC**, as the CLI and MCP server do (B281). A style check allocates parse trees from
  every core, and under the default workstation collector a Claytex check spent most of its time with every
  thread suspended for collections. `HostGarbageCollectionTests` holds the setting. It was measured in the CLI;
  if the desktop app ever shows memory or responsiveness trouble over a long session, this is the first
  setting to question, and `System.GC.ConserveMemory` / `GCHeapCount` the first knobs before turning it off.
- **`libnotify4` is a required Linux dependency** and nothing else pulls it in. `Photino.Native.so`
  lists it among its `NEEDED` entries and `libwebkit2gtk-4.1-0` does not depend on it. Take the
  install list from `objdump -p`, not from what seemed necessary. This puts the floor at
  Ubuntu 22.04 / Debian 12, the first releases carrying WebKitGTK **4.1**.
- **`MLQT.Photino` is a `WinExe`**, so a shell does not block on it. A CI step that starts it and
  reads its output immediately returns while the host is still starting — and passes by timing. Delete
  the capture first and fail if nothing replaced it (B142).
- **Run under `xvfb-run -a`** on a Linux machine with no display.
- **Photino.Blazor 4.0.13 builds and runs on `net10.0`** under both engines; `Photino.Native` links
  the current webkit2gtk-**4.1** ABI rather than the removed 4.0.
- **Do not drive WebView2 via CDP.** It is the tempting shortcut and it produces work that cannot be
  carried to WebKitGTK.
