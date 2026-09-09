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
