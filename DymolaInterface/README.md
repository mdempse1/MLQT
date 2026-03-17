# DymolaInterface for C#

A C# implementation of the Dymola interface based on the JavaScript interface provided in Dymola 2025x Refresh 1.

## Overview

This library provides a .NET API for communicating with Dymola via its HTTP JSON-RPC interface. It allows you to:

- Start and manage Dymola processes
- Execute Modelica commands
- Simulate models
- Check and translate models
- Generate plots
- Open and manage libraries
- And more...

## Requirements

- **Dymola 2025x Refresh 1** or compatible version
- **.NET 10.0** or later
- Dymola must be started with the `-serverport` command-line option (default: 8082)

## Usage

### Using the Factory (Recommended)

The factory pattern manages a singleton `DymolaInterface` instance configured from application settings:

```csharp
using DymolaInterface;

// Inject the factory (registered as IDymolaInterfaceFactory in DI)
var dymola = await dymolaFactory.GetOrCreateAsync();

// Check connection status
bool connected = dymolaFactory.IsConnected;

// Reset when settings change (forces recreation on next GetOrCreateAsync)
await dymolaFactory.ResetAsync();
```

See [FACTORY_USAGE.md](FACTORY_USAGE.md) for detailed factory documentation.

### Direct Construction

```csharp
using DymolaInterface;

// Option 1: Connect to an already running Dymola instance
var dymola = new DymolaInterface();

// Option 2: Start Dymola programmatically
var dymola = new DymolaInterface(
    dymolaPath: @"C:\Program Files\Dymola 2025x Refresh 1\bin64\Dymola.exe",
    portNumber: 8082,
    hostname: "127.0.0.1"
);
await dymola.StartDymolaProcessAsync();

try
{
    // Simulate a model
    var result = await dymola.SimulateModelAsync(
        "Modelica.Mechanics.Rotational.Examples.CoupledClutches"
    );

    if (result)
    {
        Console.WriteLine("Simulation successful!");

        // Plot results
        result = await dymola.PlotAsync(new[] { "J1.w", "J2.w", "J3.w", "J4.w" });

        if (result)
        {
            // Export plot as image
            result = await dymola.ExportPlotAsImageAsync(@"C:\temp\plot.png");
        }
    }
    else
    {
        // Get error details
        var errorLog = await dymola.GetLastErrorLogAsync();
        Console.Error.WriteLine($"Simulation failed: {errorLog}");
    }
}
finally
{
    // Clean up
    dymola.Dispose();
}
```

### Checking a Model

```csharp
var dymola = new DymolaInterface();

// Check model without simulating
var success = await dymola.CheckModelAsync("MyPackage.MyModel");
if (!success)
{
    var error = await dymola.GetLastErrorAsync();
    Console.WriteLine($"Check failed: {error}");
}
```

### Opening Libraries

```csharp
var dymola = new DymolaInterface();

// Open a Modelica library
await dymola.OpenModelAsync(@"C:\MyLibrary\package.mo");

// Add library path
await dymola.AddModelicaPathAsync(@"C:\Libraries");
```

### Custom Commands

```csharp
var dymola = new DymolaInterface();

// Execute arbitrary Dymola commands
await dymola.ExecuteCommandAsync("Advanced.Define.DAEsolver = true");
await dymola.SetVariableAsync("myVariable", 42.0);
```

## Available Methods

### Process Management
- `StartDymolaProcessAsync()` - Start Dymola process
- `StopDymolaProcess()` - Stop Dymola process
- `IsOfflineMode()` - Check if in offline mode
- `SetOfflineMode(bool)` - Enable/disable offline mode

### Model Operations
- `CheckModelAsync(problem, simulate, constraint)` - Check a model
- `SimulateModelAsync(problem, startTime, stopTime, ...)` - Simulate a model
- `TranslateModelAsync(problem)` - Translate (compile) a model
- `OpenModelAsync(path, mustRead, changeDirectory)` - Open a library/package

### Plotting
- `PlotAsync(y, legends, plotInAll, colors, patterns, markers, thicknesses, axes)` - Create plots
- `ExportPlotAsImageAsync(fileName, id, includeInLog, onlyActiveSubplot)` - Export plot as image

### Error Handling
- `GetLastErrorAsync()` - Get last error message
- `GetLastErrorLogAsync()` - Get detailed error log

### Utilities
- `ExecuteCommandAsync(cmd)` - Execute arbitrary Dymola command
- `SetVariableAsync(name, value)` - Set a Dymola variable
- `AddModelicaPathAsync(path, erase)` - Add to Modelica library path
- `CdAsync(dir)` - Change working directory
- `ClearAsync(fast)` - Clear Dymola workspace
- `SaveLogAsync(logfile)` - Save log to file
- `GetLastResultFileNameAsync()` - Get result file name
- `DymolaVersion()` - Get Dymola version string
- `DymolaVersionNumber()` - Get Dymola version number

## Enumerations

### LinePattern
- Default, None, Solid, Dash, Dot, DashDot, DashDotDot

### MarkerStyle
- Default, None, Cross, Circle, Square, FilledCircle, FilledSquare, TriangleDown, TriangleUp, Diamond, Dot, SmallSquare, Point, BarChart, AreaFill

### TextStyle
- Bold, Italic, UnderLine

### TextAlignment
- Left, Center, Right

### SignalOperator
- Min, Max, ArithmeticMean, RectifiedMean, RMS, ACCoupledRMS, SlewRate, THD, FirstHarmonic

## Communication Protocol

The interface uses HTTP JSON-RPC to communicate with Dymola's built-in server. Each command is sent as a JSON request:

```json
{
    "method": "simulateModel",
    "params": ["MyModel", 0.0, 1.0, 0, 0.0, "Dassl", 0.0001, 0.0, "dsres"],
    "id": 1
}
```

And receives a JSON response:

```json
{
    "result": true,
    "error": null,
    "id": 1
}
```

## Implementation Notes

- Based on the JavaScript interface in `Dymola 2025x Refresh 1\Modelica\Library\javascript_interface\dymola_interface.js`
- Uses `HttpClient` for JSON-RPC communication
- All methods are async for non-blocking operations
- Implements `IDisposable` for proper resource cleanup
- Automatically checks Dymola version compatibility on connection
- Supports both connecting to existing Dymola instances and starting new ones
- **Boolean result handling**: Dymola returns different result types for different commands:
  - Some commands return `true` or `false`
  - Others return an empty object `{}` to indicate success (e.g., `AddModelicaPath`, `cd`)
  - The interface automatically handles both cases and treats empty objects as success

## Error Handling

The interface provides multiple ways to handle errors:

1. **Return values**: Most methods return `bool` indicating success/failure
2. **Exception handling**: Connection and communication errors throw exceptions
3. **Error messages**: Use `GetLastErrorAsync()` and `GetLastErrorLogAsync()` to retrieve detailed error information

## Thread Safety

The `HttpClient` is thread-safe, but the Dymola instance itself may not support concurrent operations. It's recommended to use the interface from a single thread or implement proper synchronization.

## Dependencies

- **Microsoft.Extensions.DependencyInjection** (v10.0.2) - DI container for factory pattern

## License

MIT License — see [LICENSE](../LICENSE) for details.

This C# implementation is based on the JavaScript interface distributed with Dymola 2025x Refresh 1
(`Modelica\Library\javascript_interface\dymola_interface.js`), copyright (c) 2013-2025 Dassault Systèmes.
Using this library requires a valid Dymola license.
