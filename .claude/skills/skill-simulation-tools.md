# Simulation Tools Skill

This skill covers the DymolaInterface and OpenModelicaInterface projects for interacting with Modelica simulation tools.

## Overview

| Project | Tool | Protocol | License |
|---------|------|----------|---------|
| DymolaInterface | Dymola | HTTP JSON-RPC | Commercial (Dymola) |
| OpenModelicaInterface | OpenModelica | ZeroMQ REQ-REP | Open source |

Both implement `IModelCheckingService` for model validation in the editor.

---

## DymolaInterface

**Location**: `DymolaInterface/`

### Purpose
- Provide a C# wrapper around Dymola's HTTP JSON-RPC API
- Enable loading, checking, and simulating Modelica models using Dymola

### Communication Protocol
- HTTP JSON-RPC over configurable port (e.g., 8082)
- Dymola must be started with HTTP server enabled

### Basic Usage

```csharp
using DymolaInterface;

// Create settings
var settings = new DymolaSettings
{
    Port = 8082,
    DymolaPath = @"C:\Program Files\Dymola 2024\bin64\Dymola.exe"
};

// Create factory and interface
var factory = new DymolaFactory(settings);
using var dymola = await factory.CreateAndStartAsync();

// Load a library
await dymola.OpenModelAsync(@"C:\Libraries\Modelica\package.mo");

// Check a model
var result = await dymola.CheckModelAsync("Modelica.Blocks.Examples.PID_Controller");
Console.WriteLine($"Valid: {result.Success}");

// Simulate a model
var simResult = await dymola.SimulateModelAsync(
    "Modelica.Blocks.Examples.PID_Controller",
    startTime: 0.0,
    stopTime: 10.0
);
```

### Key Classes

| Class | Purpose |
|-------|---------|
| `DymolaInterface` | Main interface class with JSON-RPC communication |
| `DymolaSettings` | Configuration (port, path, timeout) |
| `DymolaFactory` | Factory for creating configured instances |
| `DymolaCheckingService` | `IModelCheckingService` implementation |
| `IDymolaInterface` | The five session calls the checking service makes — **what the factory returns** |

### Key Files
- `DymolaInterface/DymolaInterface.cs` - Main implementation
- `DymolaInterface/DymolaSettings.cs` - Configuration
- `DymolaInterface/DymolaFactory.cs` - Factory pattern
- `MLQT.Services/DymolaCheckingService.cs` - Editor integration

## The two checking services

`DymolaCheckingService` and `OpenModelicaCheckingService` do the same thing with two tools: open the
library's root file, check a class or every class in a package, and turn what the tool said into
`ModelCheckResult`s. Since B398 that shape is written **once**, in
`MLQT.Services/ModelCheckingServiceBase<TSession>`: the one-at-a-time run on the thread pool
(`CheckRunGate`), its progress and throttle, taking the session, the package fan-out, stopping at the
first class that timed out or whose tool went away, `CheckModelAsync`, and the check sequence itself
(clear the log, check, read the log on success, read the error and spot the demo-licence limit on
failure, and the failed-check result). Each service is left with only what differs, as
`private protected` hooks:

| Hook | Dymola | OpenModelica |
|------|--------|--------------|
| `GetSessionAsync` / `ResetSessionAsync` | its factory | its factory |
| `LoadLibraryAsync` | `openModel`, retried once; asks `LastOutcome` and `IsGoneAsync` why it failed | `loadFile`, retried once; timeout/cancel/exit are exceptions and drop the session |
| `ClearLogAsync` | `clearlog()` | reads `getErrorString()`, which empties it |
| `IssueCheckAsync` → `CheckAnswer` | `false` + `LastOutcome` says why there is no verdict | the exception says why, and the session is dropped |
| `ReadLogAsync` / `ReadLogOrNullAsync` | `getLastError()` | `getErrorString()` |

`CheckAnswer` (`MLQT.Services/Helpers`) is the one place the two tools' ways of saying "no verdict"
meet: `Checked(passed)`, `WasCancelled`, or `NoVerdict(result)` carrying the timeout or went-away
result. **A change to what both tools do goes in the base**; a change in a service should be about
that tool alone. Two things about them are not obvious from reading any one file.

**The factories return an interface, not the session class.** `IDymolaInterfaceFactory` and
`IOpenModelicaInterfaceFactory` hand back `IDymolaInterface` / `IOpenModelicaInterface` — each a
handful of members, exactly what the checking service calls. They exist so the services can be
tested at all: every method of the concrete session ends in a round trip to a running tool, so with
the concrete type in that signature nothing past the first call was reachable without Dymola or omc
installed, and no automated run has either. Mutation testing priced that at an 11% kill rate with
177 of the two services' mutants covered by no test whatsoever (B229). A member belongs on one of
these interfaces when a caller outside the tool's own assembly needs it, not because the session
offers it.

**`CheckSingleModelAsync` is the only place a check happens.** `CheckModelAsync` opens the library
and then calls it. That was not true until B229: each path had its own copy, and the package path's
copy neither drained the log first nor read it back on success — so a class checked on its own
showed its warnings and the same class checked as part of its package did not, and an error could be
reported against the class after the one that produced it; since B398 there is one copy for both
tools as well as for both paths. `MLQT.Services.Tests/
ModelCheckingServiceContract.cs` asserts the shared promises once and runs them against both tools;
`ToolHarness.cs` holds the fake sessions, which model each tool's **log buffer** rather than
returning fixed strings, because most of these promises are about which check's output a result
carries.

**Running out of time, and being cancelled (B262, B263).** Both tools have a time limit per command,
set in the External Tools tab (`DymolaSettings.CommandTimeoutMs`, `OpenModelicaSettings.CommandTimeoutMs`;
0 is no limit) and applied by the factory on every hand-out, and both services pass the run's
cancellation token into the check itself, so Cancel ends a check in flight. What a timeout leaves
behind differs, and that difference is the whole design:

- **Dymola returns `false`/`null` whatever went wrong**, so `IDymolaInterface.LastOutcome`
  (`Answered` / `Offline` / `TimedOut` / `Cancelled` / `Failed`) says why. A `false` from
  `CheckModelAsync` is the model's verdict only when the outcome is `Answered`. After `TimedOut` the
  service must **not** ask for the log: Dymola is still busy and would answer nothing until it finished,
  so the read waits out a second limit.
- **omc throws `TimeoutException` and closes its own session.** Its REQ socket cannot send again until
  it has received, and omc is still working, so the socket is disposed and omc killed;
  `IsConnected` turns false and the factory replaces the session next time. Send *and* receive are
  bounded against one clock (`Exchange`), because a REQ socket with no peer blocks in `SendFrame`
  until one connects — and no wait is ever under a millisecond, because NetMQ truncates to whole
  milliseconds and a zero did not mean "do not wait": a 1 ms start-up limit waited out omc's whole start.

`ModelCheckResult.TimedOut` marks the result, `ToolTimeLimit` writes it (and names the setting by the
constant the dialog's label uses), a package run stops at the first one, and a cancelled check returns
no result at all. `ModelCheckingServiceContract` holds all of it against both tools.

**`SetOfflineMode(true)` holds commands back** since B262, through a `_forcedOffline` the recovery
probe cannot clear; `StopDymolaProcessAsync` sets it too, so a later command cannot reconnect to some
other Dymola on the same port. The factory builds the session and starts Dymola **on the thread pool**:
the constructor can wait out a busy Dymola for 30 seconds, synchronously, and a caller on the UI thread
used to arrive there holding the window.

**Change both tools in one piece, and use the result before calling it done.** A question answered
for one tool and left alone for its sibling reads as agreement — that is how B170 was reported three
times in two days, and why the time limit (B263) was built across both at once. And the live-tool
classes run in no CI job (CI runs only the classes of those suites not marked `[Trait("Requires", ...)]`,
B399), so the fakes prove the services' promises but not that a tool does what the
fake says: the day after B170/B171 shipped, pressing the button found four more defects (a clean
Dymola check reporting it had checked nothing, omc handed a class's own file instead of the library's
`package.mo`, seconds of silence after the click, a headless `omc` outliving MLQT). Run
`build/run-all-tests.ps1` on a machine with the tools, and then use the feature.

### The factory never ends a Dymola (B331)
`DymolaInterfaceFactory` (what MLQT registers) asks a cached session `GetSessionStateAsync()` —
`Answering`, `Busy`, `Starting` or `Gone` — never a bare ping: a Dymola still working on a check
that timed out or was stopped does not answer a ping, exactly like a closed one. Busy is told from
gone by the TCP connect (a busy Dymola still accepts). A busy session is handed back and its
commands wait; a starting one is waited for; only a gone one is dropped, and it is `Detach()`ed
before it is disposed, because `Dispose()` kills the process a session started. The factory builds
sessions through a `Func<DymolaSettings, IDymolaSession>`, which is how
`MLQT.Services.Tests/DymolaInterfaceFactoryTests` tests it without Dymola.

**Killing a session's Dymola ends its whole process tree** (`Kill(entireProcessTree: true)`, B411). On
Linux `DymolaPath` is usually a launcher script, and one that runs `bin64/dymola` as a child leaves the
interface holding the shell, so a plain `Kill()` ended the shell and left Dymola running.
`MLQT.Services.Tests/DymolaLauncherStopTests` holds it with a fake launcher on both platforms. Not
covered: a launcher that backgrounds Dymola and exits - the handle then describes a dead shell,
`OwnsProcess` is false and the child is out of any tree MLQT can reach without per-platform
process-table walking.

**No handle of the host reaches a Dymola MLQT starts** - it outlives MLQT, so anything it inherited
would too. Under a stdio MCP host that is the protocol channel: started by `Process.Start`, Dymola held
the host's stdout open after the host had exited, until Dymola itself was ended (measured, 2026x
Refresh 1). **Redirecting its streams does not fix that on Windows**: `Process.Start` calls
`CreateProcess` with `bInheritHandles` true, which passes *every* inheritable handle the host holds,
its own inherited stdio included. So on Windows `IsolatedProcess` calls `CreateProcess` itself with
inheritance off and no standard handles (environment, so `SpawnEnvironmentVariables`, still applied).
On Linux only the three standard descriptors reach a child, but they cannot be pipes of MLQT's - a
process .NET starts does not ignore SIGPIPE, so Dymola's first write after MLQT exited would end it -
so Dymola is started as `/bin/sh -c 'exec "$0" "$@" </dev/null >/dev/null 2>&1' <dymola> -serverport N`:
same pid, so `ProcessId` and the tree kill are unchanged. Held by `IsolatedProcessTests` (Windows, an
inheritable pipe and `ping`) and `LinuxStartTests` (a fake Dymola reporting its fds), both tool-free.
omc gets the simpler half - stdin of its own (see the OpenModelica section) - because it is ended when
MLQT exits.

### When MLQT exits: omc ended, Dymola left running (B260, B493)

**The user's decision, on both platforms.** omc is headless and would run on unseen, so it is ended
with every process it started; Dymola has a window the user may carry on working in, so it is let go
of and never killed. "MLQT is exiting" is a **different request** from "stop this session" - for
Dymola the second ends the tree (B411) - and each factory has a `Shutdown()` for the first only:
OpenModelica's disposes the session (`quit()` within 5 s, a moment to exit, then
`Kill(entireProcessTree: true)`); Dymola's only `Detach()`es the session and does not dispose it, so
nothing on the exit path can reach `KillStartedTree`. Both refuse `GetOrCreateAsync` afterwards, so
a check still running as MLQT exits cannot start a tool nobody will end.

`MLQT.Services/ExternalToolShutdown` is the one place: `Run(why)` does the work once, and
`EndWith(AppDomain)` hooks the ways out that never return from `app.Run()` - `ProcessExit`
(`SIGTERM`, `Environment.Exit`), `UnhandledException` (no `finally` runs then), `SIGINT`/`SIGHUP`.
The Photino host calls both; the TestHost's container disposes the omc factory; the MCP server
registers neither factory. Only `SIGKILL`/End task escapes. Held by `ExternalToolShutdownTests`,
`DymolaLauncherStopTests.ExitingMlqt_LeavesWhatItStartedRunning` (fake Dymola process),
`OpenModelicaInterface.Tests/SessionEndTests` (fake omc process tree, tool-free) and
`LiveSessionEndTests` (real omc). On Windows omc's own children died with it even under a plain
`Kill()` in a live check, so the tree kill is proved by the fake process, whose child does not.

### Culture invariance
Modelica command strings always use `.` as the decimal separator and never use `,`
as a thousands separator. When encoding scalar/array values into `name=value` commands
(`SetVariableAsync`, named arguments via `FixNamedArgument`/`FormatModelicaArray`), the
interface formats numeric values through the `FormatScalar` helper, which uses
`CultureInfo.InvariantCulture`. This means correct output regardless of the host
machine's locale — important because DymolaInterface is a shared library consumed by
programs other than MLQT, which may not set an invariant default culture. Any new code
that emits numbers into Modelica command text must route through `FormatScalar` (or
otherwise specify `InvariantCulture`) rather than calling `.ToString()` directly.

---

## OpenModelicaInterface

**Location**: `OpenModelicaInterface/`

### Purpose
- Provide a C# wrapper around OpenModelica's scripting API
- Offer an open-source alternative to DymolaInterface
- Support cross-platform Modelica development workflows

### Communication Protocol
- ZeroMQ (ZMQ) messaging via NetMQ library
- Process-based: Starts OMC as child process with `--interactive=zmq` flag
- REQ-REP pattern for communication
- Port: any free one by default (`PortNumber = 0`); omc announces it through a port file named after the `-z` suffix, found via the `Dumped server port in file:` line on stdout (no trailing newline - read stdout in chunks, never by line). See `OmcPortAnnouncement`
- stdin is omc's own (redirected, then closed - omc reads nothing from it and runs on at end-of-file), never inherited: a host with a read of its stdin pending - any stdio MCP server - kept omc from starting within 30s on Windows, because synchronous I/O on one pipe is serialised. `HostReadingStdinTests` reproduces that in-process with `SetStdHandle`
- Responses in various formats: boolean, string, JSON, array

### Basic Usage

```csharp
using OpenModelicaInterface;

// Create and start interface
var omcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe";
using var omc = new OpenModelicaInterface(omcPath);
await omc.StartAsync();

// Load Modelica Standard Library
await omc.LoadModelAsync("Modelica");

// Check a model
var valid = await omc.CheckModelAsync("Modelica.Blocks.Examples.PID_Controller");

// Simulate a model
var result = await omc.SimulateModelAsync(
    "Modelica.Blocks.Examples.PID_Controller",
    startTime: 0.0,
    stopTime: 4.0
);

Console.WriteLine($"Success: {result.Success}");
Console.WriteLine($"Result file: {result.ResultFile}");
```

### Using Settings and Factory

```csharp
// Auto-detect OpenModelica installation
var factory = OpenModelicaFactory.TryCreate();
if (factory != null)
{
    using var omc = await factory.CreateAndStartAsync();
    var version = await omc.GetVersionAsync();
    Console.WriteLine($"OpenModelica {version}");
}

// Custom settings
var settings = new OpenModelicaSettings
{
    OmcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe",
    AutoLoadModelicaLibrary = true,
    DefaultTolerance = 1e-6,
    DefaultNumberOfIntervals = 1000
};

var factory = new OpenModelicaFactory(settings);
using var omc = await factory.CreateAndStartAsync();
```

### Main API Methods

**Connection Management:**
- `StartAsync()` - Start OMC process
- `IsConnected` - Check if OMC is running
- `ExitAsync()` - Shutdown OMC

**Model Loading:**
- `LoadModelAsync(libraryName, version?)` - Load Modelica library
- `LoadFileAsync(filePath)` - Load .mo file
- `ClearAsync()` - Clear all loaded classes

**Model Checking and Simulation:**
- `CheckModelAsync(modelName)` - Type-check model
- `SimulateModelAsync(...)` - Run simulation
- `BuildModelAsync(modelName)` - Compile model
- `InstantiateModelAsync(modelName)` - Flatten model

**Model Exploration:**
- `GetClassNamesAsync()` - Get all loaded classes
- `GetClassNamesInPackageAsync(packageName)` - Browse package
- `GetComponentsAsync(modelName)` - Get model components
- `GetClassInformationAsync(className)` - Get class details
- `GetClassCommentAsync(className)` - Get documentation

**Utilities:**
- `GetVersionAsync()` - Get OpenModelica version
- `GetErrorStringAsync()` - Get last error message
- `SetWorkingDirectoryAsync(dir)` - Change working directory
- `SendCommandAsync(command)` - Send raw OMC command

### Key Classes

| Class | Purpose |
|-------|---------|
| `OpenModelicaInterface` | Main interface with process and ZMQ management |
| `OpenModelicaSettings` | Configuration (path, port, defaults) |
| `OpenModelicaFactory` | Factory with auto-detection |
| `SimulationResult` | Simulation results (Success, ResultFile, Messages) |
| `OpenModelicaCheckingService` | `IModelCheckingService` implementation |
| `IOpenModelicaInterface` | The four session calls the checking service makes — **what the factory returns** |

### Installation Requirements
- .NET 9.0 or later
- OpenModelica 1.24.0 or later
- Download from: https://openmodelica.org/download/
- Typical path: `C:\Program Files\OpenModelica1.26.0-64bit\`

### Examples Included

`Examples.cs` provides 10 comprehensive examples:
1. Basic Connection
2. Load and Check Model
3. Simulate Model
4. Load Custom File
5. Explore Package
6. Build Model
7. Instantiate Model
8. Get Components
9. Error Handling
10. Custom Commands

```csharp
await Examples.RunAllExamples();
```

### Key Files
- `OpenModelicaInterface/OpenModelicaInterface.cs` - Main implementation
- `OpenModelicaInterface/OpenModelicaSettings.cs` - Configuration
- `OpenModelicaInterface/OpenModelicaFactory.cs` - Factory pattern
- `OpenModelicaInterface/Examples.cs` - Usage examples
- `MLQT.Services/OpenModelicaCheckingService.cs` - Editor integration

### Culture invariance
As with DymolaInterface, OMC commands require '.' as the decimal separator. The
`simulate(...)` command is assembled by the `BuildSimulateCommand` helper, which uses
`FormattableString.Invariant` so interpolated `double` values (startTime, stopTime,
tolerance) are formatted invariantly regardless of the host locale. New commands that
interpolate numbers must do the same (wrap the interpolated string in
`FormattableString.Invariant`, as a single interpolated literal — not `$"..." + $"..."`).

---

## Feature Comparison

| Feature | DymolaInterface | OpenModelicaInterface |
|---------|-----------------|----------------------|
| **Protocol** | HTTP JSON-RPC | ZeroMQ REQ-REP |
| **Port Configuration** | Required (e.g., 8082) | Optional (default 0: omc chooses) |
| **Process Management** | Manual or automatic | Automatic |
| **Multiple Instances** | Requires different ports | Each takes its own free port |
| **Response Format** | Consistent JSON | Mixed (bool, string, JSON) |
| **License** | Commercial (Dymola) | Open source (OpenModelica) |
| **Dependencies** | System.Text.Json | NetMQ (ZeroMQ) |

## IModelCheckingService Interface

Both tools implement a common interface for editor integration:

```csharp
public interface IModelCheckingService
{
    event Action<ModelCheckProgress>? OnProgressChanged;
    event Action<ModelCheckResult>? OnModelChecked;
    event Action? OnCheckingComplete;

    Task CheckModelAsync(string modelName, DirectedGraph graph, CancellationToken cancellationToken);
    Task CheckPackageAsync(string packageName, DirectedGraph graph, CancellationToken cancellationToken);
}
```

## Resources

- [Dymola Documentation](https://www.3ds.com/products-services/catia/products/dymola/)
- [OpenModelica Homepage](https://openmodelica.org/)
- [OpenModelica User's Guide](https://openmodelica.org/doc/OpenModelicaUsersGuide/latest/)
- [OMC Scripting API](https://openmodelica.org/doc/OpenModelicaUsersGuide/latest/scripting_api.html)
