# OpenModelicaInterface

A .NET 10.0 library for interfacing with OpenModelica Compiler (OMC) from C#. This library provides a high-level API for loading, checking, simulating, and analyzing Modelica models using OpenModelica.

## Overview

OpenModelicaInterface provides a C# wrapper around OpenModelica's scripting API, enabling .NET applications to:
- Load and compile Modelica models
- Run simulations
- Check models for errors
- Explore package structures
- Execute custom OMC commands

This library is similar in design to the DymolaInterface project but uses OpenModelica's **ZeroMQ (ZMQ)** communication protocol instead of HTTP JSON-RPC.

## Installation

Add a reference to the OpenModelicaInterface project in your .NET application:

```bash
dotnet add reference path/to/OpenModelicaInterface/OpenModelicaInterface.csproj
```

## Prerequisites

- **.NET 10.0** or later
- **OpenModelica 1.24.0 or later** installed on your system
  - Download from: https://openmodelica.org/download/
  - Typical installation path: `C:\Program Files\OpenModelica1.26.0-64bit\`

## Quick Start

### Basic Usage

```csharp
using OpenModelicaInterface;

// Create interface with OMC path
var omcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe";
using var omc = new OpenModelicaInterface(omcPath);

// Start OMC
await omc.StartAsync();

// Get OpenModelica version
var version = await omc.GetVersionAsync();
Console.WriteLine($"OpenModelica Version: {version}");

// Load Modelica Standard Library
await omc.LoadModelAsync("Modelica");

// Simulate a model
var result = await omc.SimulateModelAsync(
    "Modelica.Blocks.Examples.PID_Controller",
    startTime: 0.0,
    stopTime: 4.0
);

Console.WriteLine($"Simulation successful: {result.Success}");
Console.WriteLine($"Result file: {result.ResultFile}");
```

### Using Settings and Factory

```csharp
using OpenModelicaInterface;

// Auto-detect OpenModelica installation
var factory = OpenModelicaInterfaceFactory.TryCreate();
if (factory == null)
{
    Console.WriteLine("OpenModelica not found");
    return;
}

// Create and start interface
using var omc = await factory.CreateAndStartAsync();

// Use the interface
var version = await omc.GetVersionAsync();
Console.WriteLine($"Version: {version}");
```

### Custom Settings

```csharp
using OpenModelicaInterface;

var settings = new OpenModelicaSettings
{
    OmcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe",
    AutoLoadModelicaLibrary = true,  // Auto-load Modelica on start
    DefaultTolerance = 1e-6,
    DefaultNumberOfIntervals = 1000
};

var factory = new OpenModelicaInterfaceFactory(settings);
using var omc = await factory.CreateAndStartAsync();

// Modelica library already loaded due to AutoLoadModelicaLibrary = true
var classes = await omc.GetClassNamesAsync();
```

## Communication Protocol

Unlike Dymola's HTTP JSON-RPC protocol, OpenModelica uses **ZeroMQ (ZMQ) with REQ-REP pattern**:

- **Protocol**: TCP sockets via ZMQ
- **Default Port**: 13027
- **Pattern**: Request-Reply (REQ-REP)
- **Commands**: Sent as text strings via ZMQ frames
- **Responses**: Returned as complete ZMQ message frames, which may be:
  - Boolean: `true` or `false`
  - String: `"value"` (with quotes)
  - JSON: `{"key": "value"}`
  - Array: `{"item1", "item2"}`

The library automatically handles response parsing based on the expected return type. Message boundaries are handled by ZMQ, eliminating the need for manual delimiter detection.

## API Reference

### Core Methods

#### Connection Management

- `StartAsync()` - Starts the OMC process and establishes communication
- `IsConnected` - Property indicating if OMC is running
- `ExitAsync()` - Gracefully shuts down OMC

#### Version Information

- `GetVersionAsync()` - Returns OpenModelica version string (e.g., "1.26.0")
- `GetVersionNumberAsync()` - Returns version as double (e.g., 1.26)

#### Model Loading

- `LoadModelAsync(libraryName, version?)` - Loads a Modelica library by name
- `LoadFileAsync(filePath)` - Loads a .mo file from disk
- `ClearAsync()` - Clears all loaded classes and resets OMC state

#### Model Checking

- `CheckModelAsync(modelName)` - Type-checks a model
- `InstantiateModelAsync(modelName)` - Flattens and instantiates a model
- `GetErrorStringAsync()` - Gets the last error message from OMC

#### Simulation

- `SimulateModelAsync(modelName, startTime, stopTime, numberOfIntervals, tolerance, method)` - Runs a simulation
- `BuildModelAsync(modelName)` - Builds/compiles a model without simulating

#### Model Exploration

- `GetClassNamesAsync()` - Gets all loaded class names
- `GetClassNamesInPackageAsync(packageName)` - Gets classes within a specific package
- `GetComponentsAsync(modelName)` - Gets components of a model
- `GetClassInformationAsync(className)` - Gets detailed class information
- `GetClassCommentAsync(className)` - Gets documentation string for a class

#### Working Directory

- `SetWorkingDirectoryAsync(directory)` - Changes current working directory
- `GetWorkingDirectoryAsync()` - Gets current working directory

#### Custom Commands

- `SendCommandAsync(command)` - Sends a raw OMC scripting command

## Examples

The library includes 10 comprehensive examples in the `Examples.cs` file:

1. **Basic Connection** - Start OMC and get version
2. **Load and Check Model** - Load Modelica library and check a model
3. **Simulate Model** - Run a simulation with custom parameters
4. **Load Custom File** - Load your own .mo files
5. **Explore Package** - Browse package structure and documentation
6. **Build Model** - Compile without simulating
7. **Instantiate Model** - Flatten a model to see all equations
8. **Get Components** - Inspect model components
9. **Error Handling** - Handle errors and recover
10. **Custom Commands** - Send raw OMC commands

### Running Examples

```csharp
using OpenModelicaInterface;

// Run all examples
await Examples.RunAllExamples();

// Or run individual examples
await Examples.Example1_BasicConnection();
await Examples.Example3_SimulateModel();
```

## Comparison with DymolaInterface

| Feature | DymolaInterface | OpenModelicaInterface |
|---------|-----------------|----------------------|
| **Protocol** | HTTP JSON-RPC | ZeroMQ (ZMQ) REQ-REP |
| **Port** | Configured (e.g., 8082) | 13027 (default) |
| **Process Start** | Manual or automatic | Automatic |
| **Parallel Instances** | Requires different ports | Multiple instances possible (different ports) |
| **Response Format** | Consistent JSON | Mixed (boolean, string, JSON) |
| **Command Format** | JSON method calls | Text commands via ZMQ frames |
| **Message Boundaries** | HTTP request/response | ZMQ message frames |
| **License** | Commercial (Dymola) | Open source (OpenModelica) |

## Thread Safety

The interface uses a `SemaphoreSlim` to ensure commands are sent sequentially. Multiple threads can safely call methods on the same instance, and commands will be queued automatically.

## Error Handling

When OMC encounters errors, methods return appropriate failure values (e.g., `false`, empty strings) rather than throwing exceptions. Use `GetErrorStringAsync()` to retrieve detailed error messages:

```csharp
var success = await omc.CheckModelAsync("Invalid.Model.Name");
if (!success)
{
    var errors = await omc.GetErrorStringAsync();
    Console.WriteLine($"Errors: {errors}");
}
```

## Performance Considerations

- **Startup Time**: OMC takes ~2 seconds to start ZMQ server. Reuse instances when possible.
- **ZMQ Communication**: Very fast - microsecond latency for small messages
- **Simulation Time**: Depends on model complexity and simulation parameters
- **Response Parsing**: Complex JSON responses may take additional time to parse

## Troubleshooting

### "OMC executable not found"

**Solution**: Verify the OMC path is correct:

```csharp
var omcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe";
if (!File.Exists(omcPath))
{
    Console.WriteLine("OMC not found at: " + omcPath);
}
```

Or use auto-detection:

```csharp
var factory = OpenModelicaInterfaceFactory.TryCreate();
if (factory == null)
{
    Console.WriteLine("Could not auto-detect OpenModelica installation");
}
```

### "Failed to establish communication with OMC"

**Solution**:
- Ensure OMC can run from command line: `omc --version`
- Check for antivirus/firewall blocking OMC
- Try running as administrator
- Verify OpenModelica installation is complete

### Simulation fails with no error message

**Solution**:
- Check that all required libraries are loaded
- Verify model name is fully qualified (e.g., `Modelica.Blocks.Examples.PID_Controller`)
- Use `GetErrorStringAsync()` to get detailed error messages
- Try `CheckModelAsync()` first to validate the model

### Response parsing errors

**Solution**:
- OMC response formats can vary between versions
- Use `SendCommandAsync()` for raw access and custom parsing
- Update to latest OpenModelica version for better JSON support

## Advanced Usage

### Custom OMC Commands

You can send any OMC scripting command using `SendCommandAsync()`:

```csharp
// Get installation directory
var installDir = await omc.SendCommandAsync("getInstallationDirectoryPath()");

// Set compiler flags
var flagSet = await omc.SendCommandAsync("setCommandLineOptions(\"+d=initialization\")");

// Get model source code
var source = await omc.SendCommandAsync("list(Modelica.Blocks.Continuous.PID)");
```

See the [OpenModelica Scripting API documentation](https://openmodelica.org/doc/OpenModelicaUsersGuide/latest/scripting_api.html) for all available commands.

### Multiple OMC Instances

You can create multiple independent OMC instances:

```csharp
using var omc1 = new OpenModelicaInterface(omcPath);
using var omc2 = new OpenModelicaInterface(omcPath);

await Task.WhenAll(omc1.StartAsync(), omc2.StartAsync());

// Each instance has independent state
await omc1.LoadModelAsync("Modelica");
await omc2.LoadModelAsync("ModelicaServices");
```

## Dependencies

- **.NET 10.0** - Target framework
- **NetMQ 4.0.1.13** - ZeroMQ implementation for .NET
- **System.Text.Json** - For JSON parsing (built-in)
- **System.Diagnostics.Process** - For OMC process management (built-in)

The NetMQ package is automatically installed when you reference this project.

## Related Projects

- **DymolaInterface** - Similar interface for Dymola
- **ModelicaParser** - ANTLR-based Modelica parser
- **ModelicaGraph** - Dependency graph for Modelica models
- **ModelicaComparer** - Library comparison tool

## License

MIT License — see [LICENSE](../LICENSE) for details.

## Contributing

When adding new functionality:
1. Add the method to `OpenModelicaInterface.cs`
2. Add an example to `Examples.cs`
3. Update this README with API documentation
4. Consider adding tests (future: OpenModelicaInterface.Tests project)

## Resources

- [OpenModelica Homepage](https://openmodelica.org/)
- [OpenModelica User's Guide](https://openmodelica.org/doc/OpenModelicaUsersGuide/latest/)
- [Scripting API Documentation](https://openmodelica.org/doc/OpenModelicaUsersGuide/latest/scripting_api.html)
- [Modelica Language Specification](https://modelica.org/documents/ModelicaSpec34.pdf)
- [OMPython (Python equivalent)](https://github.com/OpenModelica/OMPython)
