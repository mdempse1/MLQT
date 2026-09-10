# Releasing MLQT

Maintainer-facing. `.github/workflows/release.yml` does the work; this says what it produces, how to
set it off, and what has caught people out.

## What a release contains

Two assets, one per platform, both stamped with the release version:

| Asset | What it is | Built by |
|-------|------------|----------|
| `MLQT-<version>-win-x64-setup.exe` | Windows installer (Inno Setup 6). Carries the desktop app, the `mlqt` CLI and the MCP server, plus the SlikSVN command-line client under `svn/` | `build/publish-tools.ps1` → `build/installer/mlqt.iss` |
| `mlqt_<version>_amd64.deb` | Debian/Ubuntu package. The same three tools, self-contained (its own .NET runtime), with `subversion` and `git` as recommends | `build/publish-tools.ps1` → `build/package-deb.sh` |

**One installer per platform, carrying all three tools** (phase 7b-7). They are published into a
single tree and share every assembly below `MLQT.Shared`, so together they cost one copy rather than
three. Nothing goes to a package feed: there is no `.nupkg`, no `.zip` and no `.tar.gz` any more. A
user who wants only the CLI still installs the package and gets `mlqt` on their `PATH` — which is a
smaller ask than installing the .NET SDK, and the SDK is what a `dotnet tool` needs.

The version in the Debian filename is the semver with a pre-release marker rewritten: `1.2.3-rc1`
becomes `1.2.3~rc1`, which sorts before `1.2.3` exactly as the semver does. One job computes both
spellings and both build jobs take them from it.

**Both installers are unsigned.** Windows SmartScreen warns on first run, and
[Documentation/installation.md](Documentation/installation.md) tells users what they will see. A code
signing certificate is a decision that has not been taken; the zips this replaced were flagged
identically, so it is not a cost of having an installer.

## Versioning

Tags are bare `YYYY.N.P` — `2026.1.0`, `2026.3.0`, `2026.3.1`, `2026.4.0`. **No `v` prefix**, though
`v*` is still accepted.

**The tag is the version, everywhere.** It is passed as `-Version` to `publish-tools.ps1`, which
hands it to `dotnet publish` as `-p:Version=`, so it reaches `AssemblyInformationalVersion` in all
three tools; the installer takes it as `/DAppVersion`, and the `.deb` as `--version`.
`Directory.Build.props` supplies `0.0.0-dev` for a local build and nothing else sets a version, so
there is no number to bump by hand — and no way for a release to ship a GUI reporting something else,
which is what backlog B145 was.

## Cutting one

1. Check that `main` is green — Build and Test on the exact commit you are releasing.
2. Tag it and push the tag:

   ```bash
   git tag 2026.4.0
   ```

   ```bash
   git push origin 2026.4.0
   ```

3. Each platform's job publishes the three tools, **runs all three from the published tree**, builds
   its installer, **installs it and runs all three again from where they landed** — the CLI answers
   `--version`, the MCP server completes an `initialize` handshake, and the GUI runs its 16
   `/selftest` probes. The Windows job then uninstalls and checks nothing was left behind.
4. The Windows job opens a **draft** release with generated notes; the Linux job adds the `.deb` to
   it. Review it and publish.

Step 3 is the point of the whole arrangement. Building an installer around a tree nobody has run is
how this phase produced a host that resolved no web assets (B133) and one that shipped no svn client
(B144) — both of which built, published and installed perfectly.

## Dry runs

The workflow also takes a manual `workflow_dispatch` with a version input, from the Actions tab or:

```bash
gh workflow run release.yml -f version=2026.4.0 --ref main
```

That builds, installs and exercises both installers and uploads them as *workflow artifacts*, but
does **not** create a release — the release steps run only for a tag. It is the way to prove a
release will build without committing to one.

## Before you tag

- The `SLIKSVN_ZIP_URL` repository variable (Settings → Secrets and variables → Actions → Variables)
  must point at a SlikSVN 1.14+ x64 `.zip`. `publish-tools.ps1` fails on a missing or non-running svn
  client unless `-AllowMissingSvn` is passed, and the release job does not pass it.
- If what the release ships changes, [Documentation/installation.md](Documentation/installation.md),
  [cli.md](Documentation/cli.md) and [mcp-server.md](Documentation/mcp-server.md) change with it —
  they tell users how to install and find these tools, and a release that ships something the docs do
  not describe is a release nobody can use.

## Running it locally

Both halves run on a developer machine, which is how to debug a packaging failure without pushing a
tag:

```powershell
./build/publish-tools.ps1 -Version 1.2.3 -Output publish/win-x64 -AllowMissingSvn
```

```bash
./build/publish-tools.ps1 -Runtime linux-x64 -SelfContained -AllowMissingSvn -Version 1.2.3 -Output publish/linux-x64
./build/package-deb.sh --version 1.2.3 --stage publish/linux-x64 --output artifacts
```

Run the `.deb` build under `xvfb-run -a` on a machine with no display, or its strongest check — the
GUI's self-test probes, from the extracted package — is skipped.

## What bit people before

Releases 2026.1.0 through 2026.3.1 were cut by hand, and not by choice: the trigger matched `v*`
while every tag was bare, so pushing a tag started nothing at all, and the release-creation step was
gated on the same pattern. Each release was made by dispatching the workflow and attaching its
artifacts to a release created separately — which looked like the process, and was really a bug being
worked around. Fixed for 2026.4.0; both now accept a bare tag, and `v*` still works.

If you ever find yourself uploading installers by hand again, that is the symptom, not the process.

There was a second bug hiding behind the first, and it could only be reached once the first was
fixed. The very first tag that did fire built everything and then failed on the last step with
`403 Resource not accessible by integration`: creating a release is a write, this repository's
default workflow token is read-only, and no run had ever reached that step to find out. Both jobs now
declare `permissions: contents: write`. Nothing had been published when it failed, so the fix was to
move the tag — see below.

## When a release run fails

**The workflow that runs for a tag is the one committed at that tag**, not the one on `main`. So
fixing `release.yml` on `main` does nothing for an existing tag: re-running the failed run replays the
old file and fails the same way. The tag has to move to a commit that contains the fix.

Provided nothing was published — check with `gh release list` before assuming — that is:

```bash
git push origin :refs/tags/2026.4.0        # delete the remote tag
```

```bash
git tag -d 2026.4.0 && git tag 2026.4.0 && git push origin 2026.4.0
```

Re-pointing a tag is only safe while no release exists behind it and nobody has pulled it. Once a
release is published, the tag is part of the record: fix forward with a new patch version instead.

The two build jobs are independent, and the Linux one attaches its `.deb` to the draft the Windows one
creates, so a failure on one side leaves a partial release rather than none.
`softprops/action-gh-release` adds files to an existing draft rather than replacing it, so re-running
the failed job fills the gap.

## Deliberately not automated

- **Code signing.** Both installers are unsigned; a certificate and a supplier are still to be
  decided.
- **arm64, and macOS.** Only `win-x64` and `linux-x64` are built. The desktop host is a plain
  `net10.0` application with no platform-specific project, so arm64 is a runner and a test pass
  rather than a port; macOS is phase 7b-9, deferred and unsized.
- **Publishing to a feed.** Nothing goes to nuget.org or to an apt repository, so `apt` will not offer
  upgrades — a user downloads the new `.deb` and installs it over the old one.
