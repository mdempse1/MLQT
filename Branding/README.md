# Branding

MLQT's icon and logo, as supplied by the designer (phase 7b-5, 2026-09-08). Source assets: edit these
and rebuild, rather than editing a copy inside a project.

| File | Use |
|------|-----|
| `mlqt.ico` | The Windows application icon. `MLQT.Photino.csproj` sets it as `ApplicationIcon` (the icon on the `.exe`, which Explorer and Alt-Tab use) **and** copies it beside the executable, because Photino's `SetIconFile` takes a path rather than a resource. |
| `mlqt-256.png` | The same icon for GTK, which does not read `.ico`. Copied beside the executable and used by the Linux host. |
| `mlqt-16/24/32/48/512.png` | The other raster sizes, as supplied. Nothing uses them yet; they are here so the next thing that needs one does not go back to the designer. |
| `mlqt-mark.svg`, `mlqt-mark-on-dark.svg`, `mlqt-mark-mono.svg` | The mark alone, for light, dark and single-colour contexts. |
| `mlqt-lockup*.svg`, `mlqt-lockup*-2x.png` | Mark plus wordmark. `-full` includes the strapline. |

## Why the icon is set three times

Three places show it and each reads from somewhere different, which is why one of them can be wrong
while the other two are right:

| Where | Comes from |
|-------|-----------|
| Explorer, and the Alt-Tab list | The icon resource compiled into the `.exe` by `ApplicationIcon`. |
| The window's title bar | `WM_SETICON` `ICON_SMALL`. |
| The taskbar button | `WM_SETICON` `ICON_BIG`, **under the process's Application User Model ID**, taken when the button appears. |

The taskbar being the odd one out is the interesting case: a button is grouped by AUMID, and an
application that declares none is given whatever the shell derives from the process — `dotnet.exe` for
anything started with `dotnet run`, or a stale entry for a path it has cached against. The button then
stops following the window. `Program.ClaimTaskbarIdentity` declares `MLQTProject.MLQT` before the first
window is created, which is the documented fix and is also what lets a pinned shortcut survive the
executable moving. `WindowIcon.Apply` then re-attaches the icon once the window exists — a button
keeps whatever it had when it appeared — at the size the display wants rather than the size at 100%:
at 125% the taskbar wants 40x40, and Photino attaches 32x32 for it to stretch.

## Why the icon is set twice

The executable's embedded icon is what Windows shows in Explorer and falls back to elsewhere, but
**Photino creates its own native window and that window has no icon unless one is set on it** — which
is why the taskbar entry was the generic default while the `.exe` looked correct. `Program.ApplicationIcon`
sets it at runtime, per platform, and logs rather than throws if the file is missing: Photino would
rather have no icon than no window.

## Not updated

`MLQT/` (the MAUI host) still carries the .NET template's icon, deliberately — it is retired in 7b-8
and changing it means regenerating the MAUI resource set for a build that is about to go. `MLQT.McpTester`
likewise keeps its own default; it is a manual diagnostic tool, not something shipped to users.

## The taskbar button is still wrong, and what has been ruled out

`MLQT.Photino.exe` shows the **generic application icon** on the taskbar while Explorer, Alt-Tab and
the title bar are all correct. This is B132 and it is unresolved. Everything measurable is right — the
window's `ICON_SMALL`/`ICON_BIG` are the DPI-scaled 20x20 and 40x40, and the executable embeds exactly
one `RT_GROUP_ICON` at `#32512`, byte-identical to a probe that shows the icon correctly.

Ruled out by experiment, each confirmed with a screenshot of the taskbar taken while the application
ran — **do not spend time on these again**:

| Suspected | Result |
|-----------|--------|
| The shell icon cache | A reboot changed nothing |
| An explicit `AppUserModelID` | A probe shows the icon **with** the same id and without it |
| Setting the icon after the window exists | No change |
| `WS_EX_APPWINDOW` on the window | No change |
| Recreating the taskbar button (`WS_EX_TOOLWINDOW` toggle) | No change |
| An application manifest (`supportedOS`, per-monitor DPI) | No change |
| A saturated UI thread not answering `WM_GETICON` | No change |

What **does** work: a WinForms window with the same icon file, and a bare `PhotinoWindow` whose
executable embeds the icon. So the difference is in the `MLQT.Photino` process rather than in the
icon, the window style or the identity. The next experiment is a probe built on `PhotinoBlazorApp`
instead of a bare `PhotinoWindow`.
