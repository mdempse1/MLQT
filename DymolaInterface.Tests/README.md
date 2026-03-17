# DymolaInterface Tests

Comprehensive test suite for the DymolaInterface library.

## Overview

This test project contains unit and integration tests for the DymolaInterface library, organized into the following test classes:

- **DymolaInterfaceTests** - Basic functionality and connection tests
- **SimulationTests** - Model simulation and checking tests
- **PlottingTests** - Plotting and visualization tests
- **CommandTests** - Command execution and variable manipulation tests
- **LibraryTests** - Library and model management tests

**IMPORTANT**: All tests run **sequentially** (not in parallel) and share a single Dymola instance to avoid port conflicts. This is achieved using xUnit's collection fixture feature.

## Prerequisites

### Required Software

1. **Dymola 2025x Refresh 1** (or compatible version)
   - Must be installed at: `C:\Program Files\Dymola 2025x Refresh 1\bin64\Dymola.exe`
   - If installed elsewhere, update the `DYMOLA_PATH` constant in each test class

2. **.NET 9.0 SDK** or later

### Dymola Configuration

The test suite uses a **shared Dymola instance** managed by the `DymolaFixture` class:

- **Automatic startup**: The fixture automatically starts Dymola the first time it's needed
- **Single instance**: All tests share the same Dymola process on port 8082
- **Sequential execution**: Tests run one at a time to avoid conflicts
- **Automatic cleanup**: Dymola exits when all tests complete

Optionally, you can start Dymola manually before running tests (faster):

```cmd
"C:\Program Files\Dymola 2025x Refresh 1\bin64\Dymola.exe" -serverport 8082
```

If Dymola is already running on port 8082, the fixture will connect to it instead of starting a new instance.

## Running the Tests

### Run All Tests

```bash
dotnet test DymolaInterface.Tests/DymolaInterface.Tests.csproj
```

### Run Specific Test Class

```bash
# Run only simulation tests
dotnet test DymolaInterface.Tests/DymolaInterface.Tests.csproj --filter "FullyQualifiedName~SimulationTests"

# Run only plotting tests
dotnet test DymolaInterface.Tests/DymolaInterface.Tests.csproj --filter "FullyQualifiedName~PlottingTests"

# Run only command tests
dotnet test DymolaInterface.Tests/DymolaInterface.Tests.csproj --filter "FullyQualifiedName~CommandTests"
```

### Run Specific Test

```bash
dotnet test DymolaInterface.Tests/DymolaInterface.Tests.csproj --filter "FullyQualifiedName~SimulationTests.SimulateModelAsync_WithValidModel_ReturnsTrue"
```

### Run Tests with Detailed Output

```bash
dotnet test DymolaInterface.Tests/DymolaInterface.Tests.csproj --logger "console;verbosity=detailed"
```

## Test Categories

### DymolaInterfaceTests

Tests basic interface functionality:
- Constructor behavior with valid/invalid paths
- Offline mode management
- Dymola process starting
- Version detection

**Example:**
```csharp
[Fact]
public async Task StartDymolaProcessAsync_WithValidPath_StartsDymola()
{
    _dymola = new DymolaInterface(DYMOLA_PATH, PORT, HOSTNAME);
    await _dymola.StartDymolaProcessAsync();
    await Task.Delay(5000); // Wait for startup

    Assert.False(_dymola.IsOfflineMode());

    await _dymola.ExitAsync();
}
```

### SimulationTests

Tests model simulation capabilities:
- Simulating valid and invalid models
- Model checking without simulation
- Model translation
- Error retrieval after failures
- Result file access
- Workspace clearing

**Example:**
```csharp
[Fact]
public async Task SimulateModelAsync_WithValidModel_ReturnsTrue()
{
    var dymola = await SetupDymolaAsync();
    var result = await dymola.SimulateModelAsync(
        problem: "Modelica.Blocks.Examples.PID_Controller",
        stopTime: 4.0
    );

    Assert.True(result);
    await dymola.ExitAsync();
}
```

### PlottingTests

Tests plotting and visualization:
- Single and multiple variable plots
- Custom colors and line patterns
- Plot export to image files
- Multiple plot window management

**Example:**
```csharp
[Fact]
public async Task ExportPlotAsImageAsync_WithValidPath_ReturnsTrue()
{
    var dymola = await SetupDymolaAsync();
    await dymola.SimulateModelAsync("Modelica.Blocks.Examples.PID_Controller");
    await dymola.PlotAsync(new[] { "inertia1.w" });

    var tempPath = Path.Combine(Path.GetTempPath(), "plot.png");
    var result = await dymola.ExportPlotAsImageAsync(tempPath);

    Assert.True(result);
    Assert.True(File.Exists(tempPath));
}
```

### CommandTests

Tests command execution and manipulation:
- Executing Dymola commands
- Setting variables
- Directory changes
- Modelica path management
- Log file creation
- Script execution

**Example:**
```csharp
[Fact]
public async Task SaveLogAsync_WithValidPath_CreatesLogFile()
{
    var dymola = await SetupDymolaAsync();
    var logPath = Path.Combine(Path.GetTempPath(), "dymola.log");

    var result = await dymola.SaveLogAsync(logPath);

    Assert.True(result);
    Assert.True(File.Exists(logPath));
}
```

### LibraryTests

Tests library and model management:
- Opening libraries and packages
- Installing libraries
- Converting Modelica versions
- Querying package contents
- Class existence checks

**Example:**
```csharp
[Fact]
public async Task ExistClassAsync_WithValidClass_ReturnsTrue()
{
    var dymola = await SetupDymolaAsync();
    var exists = await dymola.ExistClassAsync(
        "Modelica.Blocks.Examples.PID_Controller"
    );

    Assert.True(exists);
    await dymola.ExitAsync();
}
```

## Test Execution Time

- **Individual tests**: 2-15 seconds each
- **Full test suite**: 3-5 minutes (Dymola starts once, tests run sequentially)
- **With pre-started Dymola**: 2-3 minutes (no startup time)

The sequential execution ensures tests don't interfere with each other and prevents port conflicts.

## Troubleshooting

### Tests Fail with "Offline Mode" Errors

**Problem**: Tests cannot connect to Dymola

**Solutions**:
1. Verify Dymola is installed at the expected path
2. Start Dymola manually with `-serverport 8082`
3. Check that port 8082 is not blocked by firewall
4. Ensure no other process is using port 8082

### Tests Timeout

**Problem**: Tests take too long and timeout

**Solutions**:
1. Increase the delay after `StartDymolaProcessAsync()` (currently 5 seconds)
2. Manually start Dymola before running tests
3. Run fewer tests in parallel

### "Model Not Found" Errors

**Problem**: Tests fail because models don't exist

**Solutions**:
1. Ensure Modelica Standard Library is loaded in Dymola
2. Check that Dymola version is compatible (2025x Refresh 1 or later)
3. Manually open the Modelica library in Dymola before running tests

### File Permission Errors

**Problem**: Tests fail to create temporary files

**Solutions**:
1. Ensure write permissions for the temp directory
2. Check that temp files are being properly cleaned up
3. Close any files that might be locked by Dymola

## CI/CD Integration

For automated testing in CI/CD pipelines:

1. Install Dymola on the build agent
2. Set up Dymola license server access
3. Configure environment variables for Dymola path if needed
4. Run tests with timeout configuration:

```bash
dotnet test DymolaInterface.Tests/DymolaInterface.Tests.csproj --logger trx --results-directory ./TestResults
```

## Coverage

The test suite covers:
- ✅ Connection and initialization
- ✅ Model simulation
- ✅ Model checking and translation
- ✅ Plotting and visualization
- ✅ Error handling
- ✅ Command execution
- ✅ Library management
- ✅ File operations

## Test Architecture

### Shared Fixture Pattern

All tests use xUnit's collection fixture to share a single Dymola instance:

```csharp
[Collection("Dymola Collection")]
public class MyTests
{
    private readonly DymolaFixture _fixture;

    public MyTests(DymolaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MyTest()
    {
        // Ensure Dymola is started
        await _fixture.EnsureDymolaStartedAsync();

        // Use shared instance
        var result = await _fixture.Dymola.SimulateModelAsync("MyModel");

        Assert.True(result);
    }
}
```

**Key Points**:
- `[Collection("Dymola Collection")]` marks tests to run sequentially
- `DymolaFixture` is created once before any tests
- `EnsureDymolaStartedAsync()` is idempotent (safe to call multiple times)
- `Dispose()` runs once after all tests complete

### Why Sequential Execution?

Dymola's HTTP server can only bind to one port at a time. Running tests in parallel would cause:
- Port conflict errors (multiple processes trying to use port 8082)
- Resource contention (multiple Dymola processes competing for CPU/memory)
- Test interference (one test's simulation affecting another)

Sequential execution with a shared instance provides:
- ✅ No port conflicts
- ✅ Faster execution (Dymola starts once, not per test)
- ✅ Consistent test results
- ✅ Lower resource usage

## Contributing

When adding new tests:
1. Follow the existing naming convention: `MethodName_Scenario_ExpectedResult`
2. Always use `[Collection("Dymola Collection")]` attribute on test classes
3. Inject `DymolaFixture` and call `await _fixture.EnsureDymolaStartedAsync()`
4. Use `_fixture.Dymola` to access the shared Dymola instance
5. Don't call `Dispose()` or `ExitAsync()` on the shared instance (fixture handles cleanup)
6. Use descriptive assertion messages
7. Group related tests in appropriate test classes
8. Add documentation for complex test scenarios
