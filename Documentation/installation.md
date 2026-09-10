# Installation

MLQT ships as **one installer per platform, carrying all three tools**:

| Tool | What it is |
|------|------------|
| **MLQT** | The desktop application — the main way to browse, review and commit Modelica libraries |
| **`mlqt`** | The headless CLI that style-checks a library in CI. See [cli.md](cli.md) |
| **MLQT MCP server** | A Model Context Protocol server exposing MLQT's Modelica capabilities to AI agents. See [mcp-server.md](mcp-server.md) |

They are not separate downloads. The three share almost every assembly, so packaging them together
costs one copy rather than three, and it removes the question of which piece you wanted.

---

## Linux

A `.deb` for Debian-based distributions, x86-64.

```bash
sudo apt install ./mlqt_<version>_amd64.deb
```

`apt` pulls the dependencies; `dpkg -i` does not, so prefer the form above.

### What you need

| | |
|---|---|
| **Distribution** | Ubuntu 22.04 or Debian 12, or newer. Anything with WebKitGTK **4.1** — Ubuntu 20.04 ships 4.0 and cannot run MLQT |
| **Architecture** | x86-64 |
| **.NET** | **Nothing to install.** The runtime is bundled |
| **Git / Subversion** | `Recommends`, so apt installs them by default. If you only use Git you can decline Subversion |

The .NET runtime is bundled deliberately: neither Ubuntu nor Debian carries .NET 10 in its archive,
so depending on it would mean adding Microsoft's apt feed before `apt install` would work at all.
That costs about 80 MB and removes the step entirely.

### What it installs

| Path | |
|------|---|
| `/opt/mlqt/` | The application itself |
| `/usr/bin/mlqt` | The CLI |
| `/usr/bin/mlqt-gui` | The desktop application, from a terminal |
| `/usr/bin/mlqt-mcp-server` | The MCP server — **this is the path to register with an agent** |
| `/usr/share/applications/MLQT.Photino.desktop` | The menu entry |
| `/usr/share/icons/hicolor/*/apps/mlqt.png` | The icon |

### SVN and Git on Linux

Neither client is bundled, unlike the Windows build. MLQT runs the `svn` and `git` commands from
your `PATH`, which is what a Linux user expects and avoids shipping someone else's binaries. Both
are `Recommends`, so:

```bash
sudo apt install subversion git    # if you declined them
```

### Why the desktop entry matters

Installing the `.deb` is the **only** way MLQT gets an icon on a Wayland session — not just in the
dock, but in Alt-Tab and the window list too.

Wayland has no protocol for an application to give its own window an icon. The shell matches the
window to an installed desktop entry instead, by the application id the window reports, and takes
the icon from there. So a copy of MLQT run straight out of a build directory is unbranded
everywhere, and there is nothing wrong with it.

One consequence worth knowing if you move things around: the match is on the **executable's file
name**, `MLQT.Photino`. `/usr/bin/mlqt-gui` is a small wrapper rather than a symlink for exactly
this reason — a symlink would make the window report `mlqt-gui`, match no entry, and lose the icon.
If you launch MLQT some other way, launch `/opt/mlqt/MLQT.Photino` itself.

### Uninstalling

```bash
sudo apt remove mlqt        # keeps your settings
sudo apt purge mlqt         # same; MLQT's own settings live in your home directory
```

Your projects, settings and logs are under `~/.local/share/MLQT/` and are not touched by either.
Delete that directory by hand if you want MLQT to forget everything.

### Building the package yourself

```bash
pwsh build/publish-tools.ps1 -Runtime linux-x64 -SelfContained -AllowMissingSvn \
     -Version 1.2.3 -Output publish/linux-x64
build/package-deb.sh --version 1.2.3 --stage publish/linux-x64 --output artifacts
```

The second command builds the `.deb` **and then proves it works**: it extracts it, runs `mlqt`,
completes an MCP `initialize` handshake, and runs the desktop application's 16 `/selftest` probes
against the packaged tree. On a machine with no display, run it under `xvfb-run -a` so the last of
those runs rather than being skipped.

---

## Windows

An Inno Setup installer, x86-64.

Run `MLQT-<version>-win-x64-setup.exe`. It installs **just for you** by default, with a per-machine
option in the same dialog, and puts `mlqt` on your `PATH` if you let it.

| | |
|---|---|
| **Windows** | 10 or 11, x86-64 |
| **.NET 10** | Downloaded and installed if absent. This raises a UAC prompt even for a per-user install, because the runtime is machine-wide |
| **WebView2** | Downloaded and installed if absent. Present already on any up-to-date Windows 11 |
| **Subversion** | **Bundled.** Nothing to install |
| **Git** | Not bundled. Install it from [git-scm.com](https://git-scm.com/) and make sure it is on your `PATH` |

Uninstall from Apps & Features. It removes the files, the Start Menu shortcut and its `PATH` entry.

---

## Where MLQT keeps things

| | Windows | Linux |
|---|---|---|
| Settings and projects | `%LocalAppData%\MLQT\` | `~/.local/share/MLQT/` |
| Log file | `%LocalAppData%\MLQT\` | `~/.local/share/MLQT/` |
| Imported dictionaries | `%LocalAppData%\MLQT\Dictionaries\` | `~/.local/share/MLQT/Dictionaries/` |

Per-repository settings — style rules, the accepted-debt baseline and the custom dictionary — live
with the code in `<repo>/.mlqt/`, so they are committed and shared with everyone working on that
library.

---

## Next

[getting-started.md](getting-started.md) — setting up your first project.
