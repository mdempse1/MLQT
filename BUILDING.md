# Building MLQT

Contributor-facing: how to build the solution, run the suites, and produce a distributable build on
either platform. For what a release contains and how it is cut, see [RELEASING.md](RELEASING.md).

## Prerequisites

| | |
|---|---|
| **.NET 10 SDK** | [download](https://dotnet.microsoft.com/download/dotnet/10.0) — and nothing else. **No .NET workload is required**: the desktop host is a plain `net10.0` application. If a build ever asks for a workload, something non-portable has reached a project that should be portable |
| **Linux, additionally** | `libwebkit2gtk-4.1-0` and `libnotify4`. The second is easy to miss and nothing else pulls it in — it provides `libnotify.so.4`, which `Photino.Native` links. That puts the floor at Ubuntu 22.04 / Debian 12, the first releases carrying WebKitGTK **4.1** |
| **Git or SVN** *(optional)* | Only for exercising the VCS integration by hand |

The solution builds and the application runs on **Windows and Linux** from the same source; there is
no platform-specific project.

```bash
git clone https://github.com/mdempse1/MLQT.git
cd MLQT
dotnet build MLQT.slnx
dotnet run --project MLQT.Photino/MLQT.Photino.csproj
```

On a Linux machine with no display — a container or a CI agent — run the application under
`xvfb-run -a`.

Three environment variables exist for debugging the host itself, all off by default:
`MLQT_LOG_CONSOLE` turns the console log target back on (logging is file-only, so a window that opens
and renders nothing says nothing in the terminal it was started from — look in the log file);
`MLQT_DEVTOOLS` re-enables the webview's developer tools; `MLQT_PHOTINO_LOG` raises Photino's own
message verbosity, which is **0 in an ordinary run** because every render batch would otherwise be
base64-written to stdout synchronously on the thread producing it.

## Running the tests

Test projects run on **xUnit v3 / Microsoft.Testing.Platform**, opted into repository-wide by
`global.json`. VSTest-only flags (`--nologo`, `-v`, `--logger trx`) are errors there and present as
"0 tests ran" rather than as an error; use `--report-trx` instead. `--filter` keeps its VSTest syntax.

```bash
dotnet test MLQT.Services.Tests          # one suite
pwsh ./build/run-all-tests.ps1           # all 10 suites, ~4 minutes
pwsh ./build/run-all-tests.ps1 -CoreOnly # the 7 that CI runs
```

`run-all-tests.ps1` runs **more than CI does**, which is the point of it. Three suites need something
a runner does not have:

| Suite | Needs |
|-------|-------|
| `DymolaInterface.Tests` | A live Dymola. **No CI job runs it** |
| `OpenModelicaInterface.Tests` | A live `omc`. **No CI job runs it** |
| `MLQT.Journeys` | Playwright: `pwsh MLQT.Journeys/bin/Release/net10.0/playwright.ps1 install chromium`, once |

Use `-CoreOnly` on a machine without Dymola or OpenModelica. A failure is a failure whichever suite
it is in — excusing a suite by category is how a real failure once sat unnoticed beside the
legitimately unrunnable ones.

**On Ubuntu 26.04 Playwright ships no browser at all.** `install` refuses — the newest platform it
knows is `ubuntu24.04-x64`. `run-all-tests.ps1` sets `PLAYWRIGHT_HOST_PLATFORM_OVERRIDE=ubuntu24.04-x64`
for you, after which Chromium downloads and every journey passes. **WebKit still will not launch**: its
build links `libicu74` and `libvpx9`, and 26.04 ships `libicu78` and no `libvpx9`. So the WebKit
rehearsal is a CI job by necessity — `nightly-webkit.yml`, on `ubuntu-latest`.

Journeys pick their browser from `MLQT_JOURNEY_BROWSER` (chromium by default). An unrecognised value
throws rather than falling back, because a typo that silently reverts to Chromium produces a green run
that tested nothing.

### Coverage

The bar is over 80% per class, and over 95% for `ModelicaParser`. CI enforces it as a **ratchet**
rather than a flat threshold: `build/coverage-baseline.json` records the classes currently below their
bar, each with a reason, and the build fails when one goes backwards, when a class that met the bar
stops meeting it, or when a new class arrives below it.

```bash
dotnet build MLQT.slnx -c Release
./build/check-coverage.ps1                  # the gate, as CI runs it
./build/check-coverage.ps1 -SkipTests       # re-judge coverage already collected
./build/check-coverage.ps1 -UpdateBaseline  # re-record accepted debt; review the diff
./build/run-all-tests.ps1 -Coverage         # reports, does not gate — and measures more
```

`run-all-tests.ps1 -Coverage` sees two things the gate cannot: the two tool-dependent suites, and the
points the browser journeys add to `MLQT.Shared` by exercising the real UI.

## Bundling the SVN client

All SVN operations go through the `svn` command-line client; there is no managed fallback. On
**Windows** MLQT ships its own private copy so end users need nothing installed, resolving the
executable from `MLQT_SVN_PATH`, then the bundled copy under the app's `svn/` folder, then `svn` on
the system `PATH`. On **Linux** the `.deb` declares `subversion` as a recommend instead.

The bundled binaries are **not** in source control. For local development this is fine as long as you
have `svn` on your `PATH` — the folder may be empty. For a **distributable** Windows build, populate
it first:

```pwsh
# From a SlikSVN .zip (verify the current URL at https://sliksvn.com/download/)
pwsh build/fetch-svn-tools.ps1 -ZipUrl <SlikSVN-x64-zip-url>
```

See [svn-tools/README.md](svn-tools/README.md) for the other ways to populate the folder and how the
binaries reach the app output.

## Building the installers

One installer per platform carries all three tools — the desktop application, the `mlqt` CLI and the
MCP server. They are published into a **single tree**, because they share every assembly below
`MLQT.Shared` and so cost one copy rather than three.

```bash
build/publish-tools.sh --version 1.2.3 --output publish/win-x64 --allow-missing-svn
build/publish-tools.sh --runtime linux-x64 --self-contained --allow-missing-svn --version 1.2.3 --output publish/linux-x64
build/package-deb.sh   --version 1.2.3 --stage publish/linux-x64 --output artifacts
```

Both are shell rather than PowerShell, and deliberately: `dpkg-deb` exists only on a Debian machine
and pwsh is not installed on a plain Ubuntu desktop, so a packaging step you cannot run on the
machine that makes the package is the wrong trade. On Windows they run under Git Bash, which every
Git install provides.

**The smoke tests are the point of the script.** Each tool is asked something only a working build can
answer: the CLI prints its version, the MCP server completes an `initialize` handshake over stdio, and
the GUI runs its 16 `/selftest` probes against the published tree — which resolves the web assets,
runs interop, renders MudBlazor and exercises settings, logging and the svn locator in the layout that
ships. Run `package-deb.sh` under `xvfb-run -a` on a machine with no display, or that last and
strongest check is silently skipped.

`-AllowMissingSvn` is needed locally because the SVN payload is fetched rather than committed. It
defaults to **failing**, so a release cannot ship without the client by nobody remembering a flag.

The Windows installer is `build/installer/mlqt.iss` (Inno Setup 6), built from that tree; it refuses
to compile if any of the three tools is missing. The Linux installer is `build/package-deb.sh`.

## Continuous integration

`.github/workflows/build-and-test.yml` runs on a push to **any** branch and on pull requests to
`main` — deliberately every branch, so a long-lived working branch does not reach its first CI run at
the moment it is being merged.

| Job | What it does |
|-----|--------------|
| **Build & Test Libraries** | Builds every project and runs the seven measured suites on Windows |
| **Build & Test Libraries (Linux)** | The same seven on Ubuntu — what keeps the code portable rather than portable-looking |
| **UI Journeys** | The Playwright journeys against the test host, on both platforms |
| **Desktop Self-Test** | Publishes the real Photino host on each platform, runs its 16 `/selftest` probes and diffs them against the committed baseline. The parity gate |
| **Code Coverage** | Runs the suites with coverage collection and gates on the per-class ratchet |

`.github/workflows/nightly-webkit.yml` runs the whole journey suite under Playwright's WebKit at
02:00 UTC — the only automated thing that drives MLQT's real UI on a WebKit core, and the rehearsal
for WebKitGTK. It cannot be dispatched from a branch: `workflow_dispatch` resolves a workflow on the
**default branch** only.

No job installs a .NET workload. `build/validate-sarif.ps1` also runs on every push, checking a report
generated from `TestFixtures/SarifSmoke/` against the SARIF 2.1.0 schema.

## Project documentation

Each project has a README with API documentation. Architectural conventions for contributors are in
[CLAUDE.md](CLAUDE.md) and [CODING_GUIDELINES.md](CODING_GUIDELINES.md); the forward plan and the
working list of open items are in [Design/roadmap.md](Design/roadmap.md) and
[Design/backlog.md](Design/backlog.md).
