# Branding

MLQT's icon and logo, as supplied by the designer (phase 7b-5, 2026-09-08). Source assets: edit these
and rebuild, rather than editing a copy inside a project.

| File | Use |
|------|-----|
| `mlqt.ico` | The Windows application icon. `MLQT.Photino.csproj` sets it as `ApplicationIcon` (the icon on the `.exe`, which Explorer and Alt-Tab use) **and** copies it beside the executable, because Photino's `SetIconFile` takes a path rather than a resource. |
| `mlqt-256.png` | The same icon for GTK, which does not read `.ico`. Copied beside the executable and used by the Linux host. |
| `mlqt-16/24/32/48/512.png` | The other raster sizes. Installed by the Linux `.deb` into `/usr/share/icons/hicolor/<size>/apps/mlqt.png`, which is where the shell picks the size it wants (7b-7). |
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

## The taskbar button, and why it was not a code defect

`MLQT.Photino.exe` showed the **generic application icon** on the taskbar while Explorer, Alt-Tab and
the title bar were all correct. Nothing in MLQT was wrong. **A stale Start Menu shortcut was.**

`%AppData%\Microsoft\Windows\Start Menu\Programs\MLQT.lnk` pointed at a publish folder holding a
build from **before the icon existed** — an executable with no icon resources at all. Windows 11
resolves a running window to its matching Start Menu shortcut and takes the taskbar button's icon from
**that**, and the shortcut said "use the target's icon". The target had none, so the button fell back
to the placeholder — for every copy of `MLQT.Photino.exe`, from any folder.

Confirmed by moving the shortcut aside and running the same build: the icon appeared. Fixed by
publishing a current build over the folder the shortcut points at.

**If it happens again**, check the shortcut before touching any code:

```powershell
$sh = New-Object -ComObject WScript.Shell
$sh.CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\MLQT.lnk").TargetPath
```

Note that Windows **recreates** that shortcut, pointing at whatever it last saw run — so a build run
once from a temporary folder leaves a shortcut aimed at a folder that will be deleted. 7b-7 should
create it deliberately, at the installed location.

### How to look at a taskbar button

Eyeballing a screenshot of the whole taskbar is unreliable on a busy desktop. UI Automation names the
button, so the exact rectangle can be cropped:

```powershell
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)
$root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) |
  Where-Object { $_.Current.Name -eq "MLQT - 1 running window" } |
  ForEach-Object { $_.Current.BoundingRectangle }
```

That is what finally made the question answerable — every earlier conclusion came from squinting at a
strip of pixels.

### Ruled out along the way

Each of these was tested against a live taskbar and changed nothing. **Do not spend time on them
again**: the shell icon cache (a reboot), an explicit `AppUserModelID`, setting the icon after the
window exists, `WS_EX_APPWINDOW`, recreating the button by toggling `WS_EX_TOOLWINDOW`, an application
manifest with `supportedOS` and per-monitor DPI, a UI thread too busy to answer `WM_GETICON`, and
renaming the executable. A WinForms window, a bare `PhotinoWindow` and a `PhotinoBlazorApp` probe all
showed the icon correctly throughout — none of them had a shortcut pointing at an iconless build.

The explicit `AppUserModelID` added during the hunt has been **removed**: it did nothing here, and an
id that matches no shortcut is a documented way to confuse the resolver. 7b-7 should set it on the
application and on the shortcut it creates, together, or not at all.

## The GNOME dock, and why it is the same shape of problem

Checked on Ubuntu 26.04.1 / GNOME Shell 50.1 on Wayland (phase 7b-6, 2026-09-09). **On a Wayland
session MLQT has no icon anywhere — dock, Alt-Tab and window list alike — unless a desktop entry is
installed.** It is the Windows taskbar finding again, on a different mechanism and with the same
answer: the fix is outside the process.

> **This section was first written saying the window icon worked and only the dock was wrong. That was
> wrong**, and it was recorded because the checklist question ran the three places together and the
> answer was taken at face value instead of being traced. Alt-Tab was generic too. What follows is what
> the wire says.

**`SetIconFile` does nothing at all on a Wayland session, and cannot.** Photino's Linux library imports
`gtk_window_set_icon_from_file` — `nm -D --undefined-only Photino.Native.so | grep icon` — which is an
X11-era GTK call: Wayland has no protocol for a client to attach an icon to its own window, other than
the recent `xdg-toplevel-icon-v1`, which this compositor does not advertise. The trace agrees, and it
is the whole of the evidence:

```
$ WAYLAND_DEBUG=1 ./MLQT.Photino 2>&1 | grep -E '  -> xdg_toplevel#43\.'
  -> xdg_toplevel#43.set_title("MLQT")
  -> xdg_toplevel#43.set_app_id("MLQT.Photino")
  -> xdg_toplevel#43.set_parent(nil)
  -> xdg_toplevel#43.set_min_size(180, 37)
  -> xdg_toplevel#43.set_max_size(2147483595, 2147483595)
```

Five requests, and **not one of them is an icon**. So GNOME has exactly one thing to go on: it matches
the window to a **desktop entry** by `app_id` and takes the icon from there — for the dock, for
Alt-Tab, and for the window list. With no entry installed there is nothing to read, and no amount of
work inside the process will change that.

`Program.ApplicationIcon` is still right to set it: an X11 session **does** honour
`gtk_window_set_icon_from_file`, and that path is untested here rather than disproved. But it must not
be read as the Linux icon mechanism, because on the default Ubuntu session it is dead code.

**The `app_id` in that trace was observed rather than inferred**, which is the lesson B132 paid for.
The window is a native Wayland toplevel (not XWayland), and its `app_id` is `MLQT.Photino` — GTK's
`g_get_prgname()`, which is the executable's file name, and which is **unchanged by `dotnet run`**
(checked, because the shell resolving to `dotnet` would have broken the match). The entry must
therefore be named
`MLQT.Photino.desktop`, and `StartupWMClass=MLQT.Photino` covers the X11 case where the same value
arrives as `WM_CLASS`.

**Demonstrated, not assumed.** Installing this at `~/.local/share/applications/MLQT.Photino.desktop`
and restarting the app made the dock icon appear:

```ini
[Desktop Entry]
Type=Application
Name=MLQT
Comment=Modelica Library Quality Tool
Exec=/opt/mlqt/MLQT.Photino
Icon=/opt/mlqt/mlqt-256.png
Terminal=false
Categories=Development;
StartupWMClass=MLQT.Photino
```

The file used for the test pointed at a temporary publish and has been removed, which put the icon
straight back to generic — worth knowing before anyone concludes a build regressed.

### What ships (phase 7b-7, 2026-09-10)

The `.deb` installs the real one: `build/packaging/linux/MLQT.Photino.desktop` to
`/usr/share/applications/`, with `Icon=mlqt` naming an icon in the theme rather than an absolute
path — the absolute path is right for a tarball and wrong for a package, because it stops the shell
choosing the size it wants. `mlqt-16/24/32/48/256/512.png` go to
`/usr/share/icons/hicolor/<size>/apps/mlqt.png`, which is what finally gives those "nothing uses
them yet" sizes in the table above a use.

**Packaging turned up a third instance of the same lesson.** `/usr/bin/mlqt-gui` cannot be a
symlink: `app_id` is `g_get_prgname()`, the base name of `argv[0]`, so a symlink of that name makes
the window report `app_id "mlqt-gui"` and match no entry at all — the icon disappears again, from a
change that looks like packaging tidiness. Measured, both ways:

```
$ WAYLAND_DEBUG=1 ./mlqt-gui   2>&1 | grep set_app_id      # a symlink
  -> xdg_toplevel#43.set_app_id("mlqt-gui")
$ WAYLAND_DEBUG=1 bash -c 'exec -a MLQT.Photino ./mlqt-gui' 2>&1 | grep set_app_id
  -> xdg_toplevel#43.set_app_id("MLQT.Photino")
```

So the launcher is a wrapper that overrides `argv[0]`. `MLQT.Shared.Tests/DebianPackageTests` holds
the whole chain — executable name, entry file name, `Exec`, `StartupWMClass`, `Icon=` and the
installed sizes — because every link in it is silent when broken.

**Three statements of one rule now, from two operating systems: a desktop shell identifies an
application by an installed entry, not by the window.**
