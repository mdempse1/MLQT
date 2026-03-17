# OpenModelicaInterface Tests

Comprehensive test suite for the OpenModelicaInterface library.

## Overview

This test project contains unit and integration tests for the OpenModelicaInterface library, organized into the following test classes:

- **OpenModelicaInterfaceTests** - Basic functionality and connection tests
- **SimulationTests** - Model simulation and checking tests
- **LibraryTests** - Library and model management tests
- **ModelExplorationTests** - Model exploration and introspection tests

**IMPORTANT**: All tests run **sequentially** (not in parallel) and share a single OMC instance. This is achieved using xUnit's collection fixture feature.

## Prerequisites

### Required Software

1. **OpenModelica 1.24.0 or later**
   - Must be installed at: `C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe`
   - If installed elsewhere, update the `OMC_PATH` constant in `OpenModelicaFixture.cs`
   - Download from: https://openmodelica.org/download/

2. **.NET 9.0 SDK** or later

### OpenModelica Configuration

The test suite uses a **shared OMC instance** managed by the `OpenModelicaFixture` class:

- **Automatic startup**: The fixture automatically starts OMC the first time it's needed
- **Single instance**: All tests share the same OMC process on port 13027
- **Sequential execution**: Tests run one at a time to avoid port conflicts
- **Automatic cleanup**: OMC exits when all tests complete
- **Port isolation**: Tests that create separate OMC instances use different ports (13028-13030) to avoid conflicts

## Running the Tests

### Run All Tests

```bash
dotnet test OpenModelicaInterface.Tests/OpenModelicaInterface.Tests.csproj
```

### Run Specific Test Class

```bash
# Run only simulation tests
dotnet test OpenModelicaInterface.Tests/OpenModelicaInterface.Tests.csproj --filter "FullyQualifiedName~SimulationTests"

# Run only library tests
dotnet test OpenModelicaInterface.Tests/OpenModelicaInterface.Tests.csproj --filter "FullyQualifiedName~LibraryTests"

# Run only exploration tests
dotnet test OpenModelicaInterface.Tests/OpenModelicaInterface.Tests.csproj --filter "FullyQualifiedName~ModelExplorationTests"
```

### Run Specific Test

```bash
dotnet test OpenModelicaInterface.Tests/OpenModelicaInterface.Tests.csproj --filter "FullyQualifiedName~SimulationTests.SimulateModelAsync_WithValidModel_ReturnsSuccess"
```

### Run Tests with Detailed Output

```bash
dotnet test OpenModelicaInterface.Tests/OpenModelicaInterface.Tests.csproj --logger "console;verbosity=detailed"
```

## Test Categories

### OpenModelicaInterfaceTests

Tests basic interface functionality:
- Constructor behavior
- Process startup and connection
- Version detection
- Working directory operations
- Basic command execution

**Example:**
```csharp
[Fact]
public async Task GetVersionAsync_WhenConnected_ReturnsVersionString()
{
    await _fixture.EnsureOmcStartedAsync();
    var version = await _fixture.Omc.GetVersionAsync();

    Assert.NotNull(version);
    Assert.NotEmpty(version);
    Assert.Matches(@"\d+\.\d+\.\d+", version);
}
```

### SimulationTests

Tests model simulation capabilities:
- Simulating valid and invalid models
- Model checking without simulation
- Model building/compilation
- Model instantiation (flattening)
- Error retrieval after failures
- Workspace clearing

**Example:**
```csharp
[Fact]
public async Task SimulateModelAsync_WithValidModel_ReturnsSuccess()
{
    await _fixture.EnsureOmcStartedAsync();
    await _fixture.Omc.LoadModelAsync("Modelica");

    var result = await _fixture.Omc.SimulateModelAsync(
        modelName: "Modelica.Blocks.Examples.PID_Controller",
        startTime: 0.0,
        stopTime: 4.0
    );

    Assert.True(result.Success);
    Assert.NotEmpty(result.ResultFile);
}
```

### LibraryTests

Tests library and model management:
- Loading Modelica Standard Library
- Loading specific library versions
- Loading custom .mo files
- Listing loaded classes
- Clearing loaded libraries
- Library dependency checking

**Example:**
```csharp
[Fact]
public async Task LoadModelAsync_WithModelicaStandardLibrary_ReturnsTrue()
{
    await _fixture.EnsureOmcStartedAsync();
    var result = await _fixture.Omc.LoadModelAsync("Modelica");

    Assert.True(result);
}
```

### ModelExplorationTests

Tests model exploration and introspection:
- Browsing package structures
- Getting class names in packages
- Getting model components
- Getting class information
- Getting class documentation
- Instantiating (flattening) models
- Custom OMC commands

**Example:**
```csharp
[Fact]
public async Task GetClassNamesInPackageAsync_WithValidPackage_ReturnsClasses()
{
    await _fixture.EnsureOmcStartedAsync();
    await _fixture.Omc.LoadModelAsync("Modelica");

    var classes = await _fixture.Omc.GetClassNamesInPackageAsync("Modelica.Blocks");

    Assert.NotNull(classes);
    Assert.NotEmpty(classes);
}
```

## Test Execution Time

- **Individual tests**: 1-10 seconds each
- **Full test suite**: 2-4 minutes (OMC starts once, tests run sequentially)
- **Startup overhead**: ~1 second for OMC initialization

The sequential execution ensures tests don't interfere with each other and prevents process conflicts.

## Troubleshooting

### Tests Fail with "OMC executable not found"

**Problem**: Tests cannot find OpenModelica installation

**Solutions**:
1. Verify OpenModelica is installed at the expected path
2. Check the `OMC_PATH` constant in `OpenModelicaFixture.cs`
3. Ensure the path points to `omc.exe` (not the directory)

### Tests Timeout

**Problem**: Tests take too long and timeout

**Solutions**:
1. OpenModelica may be slow to start on first run
2. Increase the delay in `EnsureOmcStartedAsync()` (currently 1 second)
3. Check if antivirus is scanning OMC executable

### "Model Not Found" Errors

**Problem**: Tests fail because models don't exist

**Solutions**:
1. Ensure Modelica Standard Library is available in OpenModelica
2. Check that OpenModelica version is 1.24 or later
3. Verify OpenModelica installation is complete

### Communication Errors

**Problem**: Tests fail with "Not connected to OMC" errors

**Solutions**:
1. Check that OMC can run from command line: `omc --version`
2. Verify no other process is using the OMC installation
3. Check Windows permissions for running OMC

### Inconsistent Test Results

**Problem**: Tests pass sometimes and fail other times

**Solutions**:
1. Ensure tests are running sequentially (they should by default)
2. Check that `[Collection("OpenModelica Collection")]` attribute is present
3. Verify no manual OMC processes are running in background

## CI/CD Integration

For automated testing in CI/CD pipelines:

1. Install OpenModelica on the build agent
2. Configure environment to point to OMC installation
3. Run tests with timeout configuration:

```bash
dotnet test OpenModelicaInterface.Tests/OpenModelicaInterface.Tests.csproj --logger trx --results-directory ./TestResults
```

## Coverage

The test suite covers:
- ✅ Connection and initialization
- ✅ Model loading from libraries
- ✅ Model simulation
- ✅ Model checking and building
- ✅ Model instantiation (flattening)
- ✅ Package exploration
- ✅ Component inspection
- ✅ Error handling
- ✅ Custom command execution
- ✅ Working directory operations

## Test Architecture

### Shared Fixture Pattern

All tests use xUnit's collection fixture to share a single OMC instance:

```csharp
[Collection("OpenModelica Collection")]
public class MyTests
{
    private readonly OpenModelicaFixture _fixture;

    public MyTests(OpenModelicaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MyTest()
    {
        // Ensure OMC is started
        await _fixture.EnsureOmcStartedAsync();

        // Use shared instance
        var result = await _fixture.Omc.SimulateModelAsync("MyModel");

        Assert.True(result.Success);
    }
}
```

**Key Points**:
- `[Collection("OpenModelica Collection")]` marks tests to run sequentially
- `OpenModelicaFixture` is created once before any tests
- `EnsureOmcStartedAsync()` is idempotent (safe to call multiple times)
- `Dispose()` runs once after all tests complete

### Why Sequential Execution?

Unlike DymolaInterface which uses ports (limiting to one instance per port), OpenModelicaInterface uses process-based communication. However, we still use sequential execution because:

- **Consistent test results**: Tests don't interfere with each other
- **Faster execution**: OMC starts once, not per test
- **Resource efficiency**: Lower memory and CPU usage
- **Simpler debugging**: Easier to trace issues

## Comparison with DymolaInterface.Tests

| Feature | DymolaInterface.Tests | OpenModelicaInterface.Tests |
|---------|----------------------|----------------------------|
| **Process Management** | External (manual start) | Automatic (fixture starts) |
| **Port Configuration** | Required (8082) | Not needed (process-based) |
| **Startup Time** | ~15 seconds | ~1 second |
| **Communication** | HTTP JSON-RPC | stdin/stdout text |
| **Response Parsing** | Consistent JSON | Mixed formats |
| **Number of Tests** | 29 tests | 40+ tests |

## Contributing

When adding new tests:
1. Follow the existing naming convention: `MethodName_Scenario_ExpectedResult`
2. Always use `[Collection("OpenModelica Collection")]` attribute on test classes
3. Inject `OpenModelicaFixture` and call `await _fixture.EnsureOmcStartedAsync()`
4. Use `_fixture.Omc` to access the shared OMC instance
5. Don't call `Dispose()` or `ExitAsync()` on the shared instance (fixture handles cleanup)
6. Use descriptive assertion messages
7. Group related tests in appropriate test classes
8. Add documentation for complex test scenarios

## Example Test Structure

```csharp
using Xunit;

namespace OpenModelicaInterface.Tests;

[Collection("OpenModelica Collection")]
public class MyNewTests
{
    private readonly OpenModelicaFixture _fixture;

    public MyNewTests(OpenModelicaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MyMethod_WithValidInput_ReturnsExpectedResult()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        // Setup test data

        // Act
        var result = await _fixture.Omc.MyMethodAsync("input");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success, "Operation should succeed");
    }
}
```

## Resources

- [OpenModelica Testing Documentation](https://openmodelica.org/doc/OpenModelicaUsersGuide/latest/testing.html)
- [xUnit Documentation](https://xunit.net/)
- [OpenModelica Scripting API](https://openmodelica.org/doc/OpenModelicaUsersGuide/latest/scripting_api.html)
