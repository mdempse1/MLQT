# Releasing MLQT

Maintainer-facing. `.github/workflows/release.yml` does the work; this says what it produces, how to
set it off, and what has caught people out.

## What a release contains

Four assets, all stamped with the release version:

| Asset | What it is | Built by |
|-------|------------|----------|
| `MLQT-<version>-win-x64.zip` | The desktop app, self-contained, with the SlikSVN command-line client bundled under `svn/` | `dotnet publish MLQT/MLQT.csproj` |
| `MLQT.McpServer-<version>-win-x64.zip` | The headless MCP server, self-contained | `dotnet publish MLQT.McpServer/MLQT.McpServer.csproj` |
| `MLQT.Cli.<version>.nupkg` | The `mlqt` CLI, as a .NET tool package — Windows, Linux **and** macOS | `dotnet pack MLQT.Cli/MLQT.Cli.csproj` |
| `mlqt-<version>-linux-x64.tar.gz` | The CLI as a self-contained Linux binary, carrying its own runtime | `dotnet publish MLQT.Cli/MLQT.Cli.csproj -r linux-x64` |

Only the CLI is a NuGet package, and only because it is a `dotnet tool`. The MCP server is a plain
executable on purpose: an MCP client launches it by absolute path, which is what
[mcp-server.md](Documentation/mcp-server.md) documents for both the built output and the release zip.
Packaging it would give it a `dotnet tool` install nothing asks for.

**There is no separate Linux package, and none is needed.** `MLQT.Cli` targets plain `net10.0` with
no runtime identifier and nothing Windows-only beneath it, so the one `.nupkg` installs and runs on
all three platforms. The `linux-x64` tarball is not a second package filling a gap — it exists only
for machines where you would rather not install a .NET runtime at all. If you find yourself adding a
per-platform *package*, check that assumption first.

The desktop app and MCP server remain win-x64 only.

Neither package goes to a public feed. Users install the CLI from the release with `--add-source`, as
[cli.md](Documentation/cli.md) describes.

## Versioning

Tags are bare `YYYY.N.P` — `2026.1.0`, `2026.3.0`, `2026.3.1`, `2026.4.0`. **No `v` prefix.**

The tag *is* the version. It becomes `ApplicationDisplayVersion` on the desktop app and the package
version of the CLI, overriding the `<Version>` in `MLQT.Cli.csproj` — so that number is not bumped by
hand and does not need to track releases.

## Cutting one

1. Check that `main` is green — Build and Test on the exact commit you are releasing.
2. Tag it and push the tag:

   ```bash
   git tag 2026.4.0
   ```

   ```bash
   git push origin 2026.4.0
   ```

3. The Release workflow runs the test suites, bundles the SVN client, publishes the app and the MCP
   server, packs the CLI, cross-publishes the Linux CLI binary, and fails loudly if the bundled
   `svn.exe`, the CLI package or the Linux binary is missing from the output.
4. It opens a **draft** release with generated notes and all four assets attached. Review it and
   publish.

## Dry runs

The workflow also takes a manual `workflow_dispatch` with a version input, from the Actions tab or:

```bash
gh workflow run release.yml -f version=2026.4.0 --ref main
```

That builds and uploads the same four assets as a *workflow artifact*, but does **not** create a
release — the release step runs only for a tag. It is the way to prove a release will build without
committing to one.

## Before you tag

- The `SLIKSVN_ZIP_URL` repository variable (Settings → Secrets and variables → Actions → Variables)
  must point at a SlikSVN 1.14+ x64 `.zip`, or the bundling step fails.
- If what the release ships changes, `Documentation/cli.md` and `Documentation/mcp-server.md` change
  with it — they tell users how to install these artifacts, and a release that ships something the
  docs do not describe is a release nobody can use.

## What bit people before

Releases 2026.1.0 through 2026.3.1 were cut by hand, and not by choice: the trigger matched `v*`
while every tag was bare, so pushing a tag started nothing at all, and the release-creation step was
gated on the same pattern. Each release was made by dispatching the workflow and attaching its
artifacts to a release created separately — which looked like the process, and was really a bug
being worked around. Fixed for 2026.4.0; both now accept a bare tag, and `v*` still works.

If you ever find yourself uploading zips by hand again, that is the symptom, not the process.

## Deliberately not automated

- **Publishing the CLI to nuget.org.** Needs a `NUGET_API_KEY` secret and a decision about public
  distribution. Until then the `.nupkg` on the release is the distribution.
- **Anything but win-x64, for the desktop app and MCP server.** Both are MAUI/Windows-shaped today;
  see [Design/design-phase7-gui-tests.md](Design/design-phase7-gui-tests.md) for where that is
  heading. The CLI is already cross-platform and is built and tested on Linux by the `CLI on Linux`
  job in `build-and-test.yml`, which also produces and runs the tarball the release ships.
- **macOS and arm64 binaries.** The tool package covers those machines when a .NET 10 runtime is
  present; only linux-x64 gets a self-contained build, because that is where the CI agents are.
