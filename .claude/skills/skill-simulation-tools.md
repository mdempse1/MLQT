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

### Key Files
- `DymolaInterface/DymolaInterface.cs` - Main implementation
- `DymolaInterface/DymolaSettings.cs` - Configuration
- `DymolaInterface/DymolaFactory.cs` - Factory pattern
- `MLQT.Services/DymolaCheckingService.cs` - Editor integration

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
- Default port: 13027 (configurable)
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

---

## Feature Comparison

| Feature | DymolaInterface | OpenModelicaInterface |
|---------|-----------------|----------------------|
| **Protocol** | HTTP JSON-RPC | ZeroMQ REQ-REP |
| **Port Configuration** | Required (e.g., 8082) | Required (default: 13027) |
| **Process Management** | Manual or automatic | Automatic |
| **Multiple Instances** | Requires different ports | Requires different ports |
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
