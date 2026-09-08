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
