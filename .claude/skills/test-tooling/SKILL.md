---
name: test-tooling
description: "How to run and judge MLQT's tests and measurements - run-all-tests.ps1 and which suites CI runs, live-tool and svn test filters, the installer smoke tests (publish-tools.sh, package-deb.sh), the coverage ratchet (check-coverage.ps1), the opt-in real-library corpora (fidelity, resolution, supplied-load speed), profiling with dotnet-trace, and mutation testing (run-mutation.ps1, survivor-map.py). Use before running a suite you have not run in this session, when a test needs Dymola, omc or svn, when changing the coverage baseline, after changing ModelicaTokenClassifier or a resolver, when investigating performance, or when judging whether tests check anything."
---

# Test Tooling Skill

Comprehensive tests are required for all classes with the goal being >80% coverage for each class.
The ModelicaParser assembly requires >95% coverage for all classes as this is critical to the project.

Two scripts answer two different questions. Run the first before pushing; run the second when you
have added a class or moved code between them.

## "Do all the tests pass?" — `build/run-all-tests.ps1`

```powershell
./build/run-all-tests.ps1                        # all 10 suites, ~4 minutes
./build/run-all-tests.ps1 -Coverage              # ...with a per-assembly coverage summary
./build/run-all-tests.ps1 -CoreOnly -SkipBuild   # what CI runs, against the current build
./build/run-all-tests.ps1 -Configuration Debug   # Release by default, to match CI
```

`-Coverage` **reports; it does not gate**, and it measures more than the gate can. Two things are
only visible here: what the classes needing a live Dymola or omc add to `DymolaInterface` and
`OpenModelicaInterface` (the gate measures those two from their tool-free classes only, B438), and the
~8 points the browser journeys add to `MLQT.Shared` by exercising the real UI. With `-CoreOnly` the
two tool assemblies are still in the summary, measured from their tool-free classes and marked so
(B437). Both scripts take their assembly lists from
`build/CoverageAssemblies.ps1`, so they cannot disagree about what "our code" means.

It runs **every** suite, which is more than CI does and more than the coverage gate does:

| | Suites |
|---|---|
| `run-all-tests.ps1` | all 10 — the 7 below, plus `DymolaInterface.Tests`, `OpenModelicaInterface.Tests` and `MLQT.Journeys` |
| CI `build-libraries` (Windows) and `linux-tests` (Linux) | the same 7 on each platform, plus the Dymola and OpenModelica suites **without their tool classes** (B399); `ui-journeys` runs the journeys on both |
| `check-coverage.ps1` | the same as CI's test jobs — the 7, plus the Dymola and OpenModelica suites without their tool classes (B438); the journeys contribute no gated coverage |

**The suite list is read from `MLQT.slnx`**, not written out in the script, so a test project added to
the solution is picked up without anyone remembering a list. This repository has been bitten by the
same rule having two implementations often enough that a list would be a defect waiting to happen.

**Three suites need something the machine may not have** and are the reason this script exists at all:
`DymolaInterface.Tests` (a live Dymola) and `OpenModelicaInterface.Tests` (a live `omc`) run in CI
**only without the classes that drive the tool**, so this is the only thing that runs those;
`MLQT.Journeys` needs `pwsh MLQT.Journeys/bin/Release/net10.0/playwright.ps1 install chromium` once.
On a machine without Dymola or OpenModelica, use `-CoreOnly`, which runs the two suites as CI does.

**A class that needs a live tool says so with a trait** — `[Trait("Requires", "Dymola")]` or
`[Trait("Requires", "OpenModelica")]` — and CI runs the rest with `--filter "Requires!=Dymola"` /
`"Requires!=OpenModelica"` (B399). Most of the Dymola suite needs no Dymola: wire format against a
fake handler, socket stubs, spawn environment. Each suite's `ToolTraitTests` fails when a class using
the tool's fixture is not marked; a class that starts the tool some other way (`TimeLimitTests`) has
to be marked by hand. `LiveToolTestFilterTests` holds the filter strings to the traits.

**A fourth is partly in that position.** `RevisionControl.Tests` has three classes that need an svn
client — `SvnIntegrationTests`, `SvnIntegrationAdvancedTests` and `SvnMergeCommitTests` — and
`MLQT.Services.Tests` has one, `RepositoryServiceSvnIntegrationTests` (B471), so CI runs both suites
with a filter that excludes exactly those four, and this script applies it **only when the machine
has no svn**. The filter is the same string in four files and a test holds them together
(`MLQT.Shared.Tests/SvnTestFilterTests.cs`), because it was once the substring `Svn`: that also
excluded six classes needing no svn at all, hid 281 of the suite's 673 tests from every automated
run, and hid a failing one among them (B266). **Classify a test by what it needs, never by what it is
called.**

**Every SVN test builds its own repository** — `TestSupport/SvnTestRepository.cs`, with
`svnadmin create` into a temporary folder, the way the Git tests use `Repository.Init`. It is linked
into `RevisionControl.Tests` and `MLQT.Services.Tests` as `InMemorySettingsService` is, not copied.
The tests once shared one repository and working copy kept at a fixed path on the developer's
machine, and because they commit, two runs at once (two worktrees, two agents) moved HEAD under each
other and failed a test that passed alone (B426); on every other machine they returned before
asserting anything (B471). Never point a test at a fixed repository or working copy on the machine.
The `RevisionControl.Tests` classes CI runs take `SvnWorkingCopyFixture`, which is empty where svn is
not installed, so there they still return early.

**A raw string literal's line endings are the checkout's, and a fixture must never be searched for
one.** `"""..."""` carries whatever the *.cs file* carries — CRLF on a Windows checkout, LF on a
runner with `core.autocrlf` off — so `fixture.Replace("x;\r\n", "")` matches on one platform and
silently does nothing on the other, leaving a test that asserts against an edit that was never made.
Normalise the fixture where it is declared and search for `\n`. The same rule in the other
direction: **never write a path as `@"C:\repo\..."`** — on Linux a backslash is an ordinary
character, so such a path is one segment and every `Path.Combine` against it disagrees. Build test
paths with `Path.Combine` from `Path.GetTempPath()`, and give a VCS a forward-slashed relative path,
which is what Git reports on both. Both of these have shipped green on Windows and failed on the
Linux job — see B255, and again in WP7. **Neither is worth a guard test**: the shapes are
indistinguishable from the forty-odd files that use a drive-letter string as test data or search
text they built with CRLF themselves, so the check would be noise.

**A failure is a failure, whichever suite it is in.** An earlier version excused the tool-dependent
suites by category on the grounds that the machine might not have the tool, and immediately excused a
real one — OpenModelica *is* installed on the main development machine, and a test in that suite
was failing against omc 1.26 (backlog B116, since fixed: the test was wrong about what omc does, and
nobody looked while the category was excused). Excusing by
category hides the thing you wanted to find; `-CoreOnly` is a decision, reading past a red line is
not.

## "Would the installer work?" — `build/publish-tools.sh`

One installer per platform carries the GUI, the `mlqt` CLI and the MCP server, so all three are
published into a **single tree** — they share every assembly below `MLQT.Shared`, and each carries its
own `.deps.json`, so together they cost one copy rather than three.

```bash
build/publish-tools.sh --version 1.2.3 --output publish/win-x64 --allow-missing-svn
build/publish-tools.sh --runtime linux-x64 --self-contained --version 1.2.3 --output publish/linux-x64
```

**The smoke tests are the point of it.** Building an installer around a tree nobody has run is how this
phase produced a host that resolved no web assets (B133) and one that shipped no svn client (B144) —
both of which built, published and installed perfectly. So each tool is asked something only a working
build can answer: the CLI prints its version, the MCP server completes an `initialize` handshake over
stdio, and the GUI runs the **16 `/selftest` probes against the published tree**. That last is the
strongest check available anywhere in this repository — it resolves the RCL assets, runs interop,
renders MudBlazor and exercises settings, logging and the svn locator, in the layout that ships.

`--allow-missing-svn` is needed locally because the payload is fetched by `build/fetch-svn-tools.ps1`
and is not committed. It defaults to **failing**, so a release cannot ship without the client by nobody
remembering a flag — which is exactly how B144 happened.

**Shell, not PowerShell**, and it is `build/package-deb.sh`'s reason applied to its own input: pwsh is
not installed on a plain Ubuntu desktop, so a PowerShell publish step made the Linux half of the build
unrunnable on an ordinary Linux box even though the packaging script beside it was shell. On Windows it
runs under Git Bash. One consequence worth knowing: `mkfifo` under Git Bash makes an MSYS-emulated fifo
that a native `.exe` cannot read from, so the MCP handshake here holds stdin open with an ordinary
pipeline rather than the fifo `package-deb.sh` uses.

The Windows installer is `build/installer/mlqt.iss` (Inno Setup 6), built from that tree. It refuses to
compile if any of the three tools is missing.

The Linux installer is `build/package-deb.sh`, which takes the same tree and writes a `.deb`.
**Shell rather than PowerShell**: `dpkg-deb` exists only on a Debian machine, and pwsh is not on a
plain Ubuntu desktop, so a `.ps1` would be a packaging script you cannot run on the machine that makes
the package. It applies the same "prove it before shipping
it" rule one layer further out — `publish-tools.sh` proves the three tools run, this proves the
package puts them somewhere they still run from, by extracting it and asking each of them again,
self-test probes included. Run it under `xvfb-run -a` on a machine with no display, or that last and
strongest check is skipped.

Its inputs are in `build/packaging/linux/`. **The desktop entry there is not cosmetic**: on a Wayland
session it is the only thing that gives MLQT an icon anywhere — dock, Alt-Tab and window list — and
it works by being named after the window's `app_id`, which is the GUI executable's file name. Rename
either without the other and the icon silently disappears. `MLQT.Shared.Tests/DebianPackageTests`
holds that chain together, as `WindowsInstallerTests` does for the Inno script.

## "Would the coverage gate pass?" — `build/check-coverage.ps1`

**CI enforces the per-class bar** — this script runs the measured suites, merges their reports,
and fails the build per class. `DymolaInterface` and `OpenModelicaInterface` are measured from their
suites' tool-free classes, with the same `Requires!=` filters CI's test jobs use; code only a live tool
reaches is ledger debt saying so (B438). Run it locally the same way:

```powershell
dotnet build MLQT.slnx -c Release
./build/check-coverage.ps1                 # gate
./build/check-coverage.ps1 -SkipTests      # re-judge coverage already collected
./build/check-coverage.ps1 -UpdateBaseline # re-record accepted debt; review the diff
```

It is a **ratchet, not a flat threshold**, for the same reason MLQT offers its users one: some debt
predates the bar, and some of it cannot be paid on a runner at all — the SVN tests need an svn client
no runner has. `build/coverage-baseline.json` records the classes currently below their
bar, and the build fails when one goes further backwards, when a class that met the bar stops meeting
it, or when a new class arrives below it.

That file is a debt ledger, so **every entry carries a `reason` and the build fails on one that does
not** — `-UpdateBaseline` writes a `TODO` placeholder for anything new, which the gate then refuses.
"Needs a working SVN server" and "nobody has written the tests yet" are both acceptable reasons and
are *different facts*: the ledger existed for a while with six ordinary in-process classes sitting
beside the untestable ones, indistinguishable from them and never asked about again.

Classes under 25 coverable lines are measured but not gated (a four-line record whose only uncovered
lines are the compiler's `Equals`/`GetHashCode` reads as 50%, and chasing that produces tests that
assert nothing), as is source-generated code. `MLQT.Shared` joined the ratchet in phase 7a-5 at 80%,
measuring `.razor.cs` and **not** filtering `.razor` — measured, a component's `BuildRenderTree` is not
counted at all, and the filter the plan called for would have removed five ordinary classes from the
report instead. See the `gui-testing` skill.

## "Does the viewer still show the user's own file?" — a real library, by hand

**The strongest test in this repository runs nowhere automatically** (backlog B234).
`ModelicaTokenClassifierTests.RoundTripsOverAWholeLibrary` is the whole fidelity claim — strip the
highlight tags and what comes back is the source, character for character — and it is asserted over
**8,367 files and 1.1M lines** of real Modelica. A library that size cannot live in the repository,
so it is opt-in, which means **neither CI nor `run-mutation.ps1` ever executes it**, and the
surviving mutants in the classifier's emit loop are exactly the ones it would kill.

**Run it by hand after any change to `ModelicaTokenClassifier`, and before a release that carries
one:**

```powershell
$env:MLQT_FIDELITY_CORPUS = "C:\Projects\Modelica\ModelicaStandardLibrary;C:\Projects\Modelica\Modelica-Buildings-Original"
dotnet test ModelicaParser.Tests --filter "FullyQualifiedName~RoundTripsOverAWholeLibrary"
```

Several libraries are separated by `;`. With the variable unset the test returns immediately, so an
ordinary run is unaffected — which is the trap, not a convenience: a green suite says nothing about
fidelity unless this has been run.

## "Does every type still resolve?" — two real libraries, by hand

`ModelicaGraph.Tests/ResolutionCorpusTests` holds the two facts `Design/unit-consistency.md` is built
on: every component type of the loaded libraries resolves, and a package redeclared on an instance
gives each member the unit the constraining type does — so a unit check may resolve against the
constraining type. Opt-in like the fidelity corpus, and for the same reason. **Run it after any change
to `TypeResolver`, `ClassElementResolver` or `UnitResolver`:**

```powershell
$msl = "C:\Projects\Modelica\ModelicaStandardLibrary"
$env:MLQT_RESOLUTION_CORPUS = "$msl\Modelica;$msl\ModelicaServices;$msl\Complex.mo;C:\Projects\Modelica\Modelica-Buildings\Buildings"
dotnet test ModelicaGraph.Tests --filter "FullyQualifiedName~ResolutionCorpusTests"
```

Everything named is loaded into **one graph**, because a library resolves only beside its
dependencies — so name each library's directory, not MSL's repository root, which also holds a test
library written against 3.2.3. On 2026-10-03: 124,977 types, none unresolved; 6,239 redeclarations,
no unit changed; about a minute. Taking away the lookup among inherited classes fails both tests.

## "How long does a supplied library take to load?" — one real library, by hand

`MLQT.Services.Tests/ExternalDocs/SuppliedLoadSpeedTests` reads a library's files into memory and
times `AddLibraryFromSourceAsync` alone — what a host holding a library in memory pays when a session
starts. Opt-in, and it reports rather than asserts a time:

```powershell
$env:MLQT_SUPPLIED_CORPUS = "C:\Projects\Modelica\ModelicaStandardLibrary\Modelica"
dotnet test MLQT.Services.Tests --filter "FullyQualifiedName~SuppliedLoadSpeedTests" --output Detailed
```

On 2026-10-09: 2,939 files, 6,487 classes in 4.3 s, about 1,500 classes a second.

## "Where is the time actually going?" — `dotnet-trace`

`mlqt check --timings` says which *phase* a run spent its time in, and the application log keeps the
same breakdown for every check the GUI runs. That is the first question and usually enough. When it
is not — when a phase is 91% of the run and nothing in it looks expensive — the answer is a sampling
profiler, not a third guess.

```bash
dotnet tool install --global dotnet-trace          # once

dotnet-trace collect --format nettrace --output run.nettrace \
  --providers Microsoft-DotNETCore-SampleProfiler \
  -- MLQT.Cli/bin/Release/net10.0/mlqt.exe check <library> --config <settings>

dotnet-trace report run.nettrace topN -n 25        # the flat answer
dotnet-trace convert run.nettrace --format Speedscope   # ...for anything else
```

**`topN` is rarely the answer on its own**, because the top of it is thread-pool idle
(`PollGCWorker`, `LowLevelLifoSemaphore`, `GetQueuedCompletionStatus`) and the rest is whatever
library the work happens to be inside — for MLQT that is always ANTLR, which says nothing about
*which* of MLQT's callers asked for a parse. The useful question is a stack question: convert to
Speedscope, whose profiles are an evented open/close stream per thread, walk it keeping a stack, and
attribute each sample to the frame you care about. Three that earned their keep on B174:

- **self time per leaf**, excluding idle frames and the converter's `CPU_TIME` /
  `UNMANAGED_CODE_TIME` pseudo-leaves — skip those and take the frame below or everything reads 50%
- **which of our frames entered the parser**, i.e. the nearest MLQT frame above the first ANTLR one
- **the path between two frames** — the one that found B174, by printing every frame between
  `MissingUnits` and the parse underneath it

**A 19-second run traces to about 9 MB**, so there is no need to shrink the input first.

**Rebuild before you believe a negative result.** B174 cost an hour to a cached experiment that
reported no improvement because the binary under test had not been rebuilt; the same change measured
properly was a 56% cut. If an experiment says a change did nothing, check that the change is in the
assembly before concluding anything about the change.

## "Do the tests actually check anything?" — `build/run-mutation.ps1`

Coverage says a line ran. **Mutation testing says it was checked**: Stryker changes the code in small
ways — an operator flipped, a condition inverted, a string emptied — and reports the changes no test
objected to. A surviving mutant is a statement the suite executes and does not depend on.

```powershell
dotnet tool install --global dotnet-stryker      # once

# One file, a few minutes - the everyday use, after a fix or before trusting a guard
./build/run-mutation.ps1 -Mutate '**/ProjectNameRules.cs'
./build/run-mutation.ps1 -Project ModelicaParser -Mutate '**/Helpers/*.cs'

# Every measured assembly, about an hour, resumable - the audit
./build/run-mutation.ps1 -All
./build/run-mutation.ps1 -Summarise          # rebuild the report from runs already done
```

**For a single run, always pass `-Mutate`**: one file takes about three minutes, most of it the build
and the baseline test run, and a whole assembly takes many times as long.

**`-All`** mutates the seven assemblies the coverage gate measures other than the two tool interfaces,
smallest first so the early
ones calibrate the machine before anything committing starts. **It takes about an hour**, not the
many hours it took before B267: each assembly is now judged by its own suite rather than by every
suite that references it, so a run of 24,901 mutants took 52 minutes on 2026-09-22 where 23,634
took 5h 40m in September's audit. That also means **scores from before and after B267 are not
comparable** for any assembly something else depends on. It writes to `MutationReport/`
(git-ignored) and **is resumable** — a project whose report is already there is skipped, so Ctrl-C and
run it again to continue. Each project's result is printed as it finishes, and the run ends with
`MutationReport/mutation-survivors.md`: one list of every surviving mutant, grouped by project and
file, with the line each one changed. `-Summarise` rebuilds that file from whatever is on disk
without mutating anything.

**Read a large survivor list by group, never end to end** — `build/survivor-map.py` takes a
Stryker report and prints one line per method, so the question becomes *which decisions are
unguarded?* rather than *what are these 272 mutants?*:

```bash
python build/survivor-map.py $TEMP/mlqt-run/reports/mutation-report.json --file ModelicaRenderer
python build/survivor-map.py <report> --file ModelicaRenderer --method VisitComposition   # then read one
python build/survivor-map.py <report> --file Foo --status NoCoverage                      # the other list
```

It exists because B227 was 272 survivors in one 3,500-line file, and reading that many in order is
how such a pass produces tests that assert the code's **current output** instead of its contract.
Grouped, the answer came out as: two groups had something written down to test against, the largest
group had only thresholds nobody had chosen, and the reason one group was unguarded at all was that
the test harness deleted the line before asserting. **Judge a group as a whole, and leave the ones
with no specification alone** — or write the specification down first.

**It reports, it does not gate** — some survivors are equivalent mutants no test can kill, and some
are in code nobody should test (an entry in a literal list of C header names). Read the survivors, not
the score.

**Two survivor shapes are equivalent mutants, not missing tests**, and each is easily reported as a
gap before it is understood: a short-circuit in front of a slower answer that agrees with it (forcing
the slow path changes nothing), and the first of two filters for the same thing (removing it changes
no observable result while the second, downstream, still runs). **Nor is a ratio a finding** — the
two worst files of the 2026-09-22 run were theme hex-colour literals and a colour table. Read what
survived before opening an item on a score.

**The last whole-solution run, for comparison** (2026-09-22, post-B267, 52m 53s, 24,901 mutants).
*Unreached* is code no test runs, which is the coverage ratchet's business. *Kill on reached* is what
a suite does with the code it does execute, and it is the number to compare:

| Project | Mutants | Unreached | Survived | Kill on reached |
|---------|--------:|----------:|---------:|----------------:|
| ModelicaParser | 7,034 | 250 | 1,229 | 81.9% |
| ModelicaGraph | 2,512 | 196 | 620 | 73.2% |
| RevisionControl | 1,606 | 340 | 383 | 69.7% |
| MLQT.Cli | 1,360 | 0 | 438 | 67.8% |
| MLQT.Services | 3,756 | 535 | 1,039 | 67.7% |
| MLQT.McpServer | 2,630 | 356 | 847 | 62.8% |
| MLQT.Shared | 6,003 | 4,375 | 735 | 54.9% |

**`DymolaInterface` and `OpenModelicaInterface` stay out of `-All`, by decision (B453).** The
coverage gate measures them from their suites filtered with B399's `Requires!=Dymola` /
`Requires!=OpenModelica`, but that filter cannot reach Stryker's MTP runner: Stryker 5.0.0's
`test-case-filter` (config file only) is read by its VSTest runner alone. Measured with the filter set
in a `stryker-config.json`: coverage was captured for 266 tests where the filtered suite has 240, and
Dymola started two minutes in. VSTest is not a way round it — xUnit v3 is MTP. A single `-Project
DymolaInterface` run still works, and warns that it starts the tool. Revisit when a Stryker release
passes the filter to MTP; `LiveToolTestFilterTests` holds the list until then.

Three things about it are not discoverable and cost an afternoon between them:

- **Where it is run from decides which suites it runs.** Stryker walks up looking for a solution,
  and from the repository root it finds `MLQT.slnx` and switches to solution mode — where it works
  out the test projects itself, every one that transitively references the mutated assembly, and
  **`--test-project` is ignored**. For `ModelicaParser` that is all of them, `MLQT.Journeys`
  included: 5,790 tests, the journeys erroring while sharing one host with six other suites, and
  Stryker aborting with "Initial testrun has more than 50% failing tests" before mutating anything.
  The script now runs from the test project's own directory, where no solution is found and the
  suite it was started from is the one used — 2,078 tests for the same run (B267). **A solution
  cannot be taken away by argument, only by not standing where it can be found.**

- **`--test-runner mtp` is required.** Every test project here is xUnit v3, which *is*
  Microsoft.Testing.Platform, and Stryker defaults to VSTest. Without it Stryker fails with "not yet
  supported by Stryker, see issue 3094" naming every test project, which reads as *this repository
  cannot be mutation tested*. It can; the option is in `--help` and not in that message.
- **A filter that matches nothing reports every mutant as `Ignored` and a clean score.** The script
  fails loudly on that instead, because a run that mutated nothing looks exactly like a suite that
  caught everything. `-Mutate` is relative to `-Project`, so naming a file from another assembly is
  the easy way to get a hollow pass.

It exists because phase 1 produced six tests that asserted something they could not see, each found
by accident. Pointed at its first file it immediately found two real gaps: a public method with no
test at all, and a user-facing message that could be emptied unnoticed (B212).
