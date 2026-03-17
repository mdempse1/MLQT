# DymolaInterface.Tests Overview

## Test Summary

The DymolaInterface.Tests project contains **23 unit and integration tests** organized into **4 test classes**:

| Test Class | Tests | Description |
|------------|-------|-------------|
| **DymolaInterfaceTests** | 7 | Basic connection, initialization, and offline mode tests |
| **SimulationTests** | 10 | Model simulation, checking, and translation tests |
| **PlottingTests** | 5 | Plotting and visualization tests |
| **CommandTests** | 5 | Command execution and file operation tests |
| **LibraryTests** | 4 | Library and model management tests |

**Total: 31 tests**

## Test Categories

### 1. DymolaInterfaceTests (7 tests)

Tests core functionality without requiring Dymola simulation:

- `Constructor_WithValidPath_CreatesInstance` - Verifies constructor with valid path
- `Constructor_WithEmptyPath_CreatesInstanceInOfflineMode` - Tests offline mode initialization
- `IsOfflineMode_WhenDymolaNotRunning_ReturnsTrue` - Validates offline detection
- `SetOfflineMode_ToTrue_SetsOfflineMode` - Tests manual offline mode setting
- `SetOfflineMode_ToFalse_ClearsOfflineMode` - Tests clearing offline mode
- `StartDymolaProcessAsync_WithValidPath_StartsDymola` - Tests Dymola process launching
- `DymolaVersion_WhenConnected_ReturnsVersionString` - Validates version retrieval
- `DymolaVersionNumber_WhenConnected_ReturnsVersionNumber` - Validates version number

### 2. SimulationTests (10 tests)

Tests model simulation and checking capabilities:

- `SimulateModelAsync_WithValidModel_ReturnsTrue` - Simulates PID_Controller example
- `SimulateModelAsync_WithInvalidModel_ReturnsFalse` - Tests error handling
- `CheckModelAsync_WithValidModel_ReturnsTrue` - Model checking without simulation
- `CheckModelAsync_WithInvalidModel_ReturnsFalse` - Invalid model check handling
- `TranslateModelAsync_WithValidModel_ReturnsTrue` - Model translation (compilation)
- `GetLastResultFileNameAsync_AfterSimulation_ReturnsFileName` - Result file retrieval
- `GetLastErrorAsync_AfterFailedSimulation_ReturnsErrorMessage` - Error message retrieval
- `GetLastErrorLogAsync_AfterFailedSimulation_ReturnsErrorLog` - Detailed error log
- `ClearAsync_ClearsWorkspace` - Workspace clearing

### 3. PlottingTests (5 tests)

Tests plotting and visualization:

- `PlotAsync_WithSingleVariable_ReturnsTrue` - Single variable plotting
- `PlotAsync_WithMultipleVariables_ReturnsTrue` - Multi-variable plotting
- `PlotAsync_WithCustomColors_ReturnsTrue` - Custom color configuration
- `PlotAsync_WithLinePatterns_ReturnsTrue` - Line pattern styling
- `ExportPlotAsImageAsync_WithValidPath_ReturnsTrue` - Plot export to PNG

### 4. CommandTests (5 tests)

Tests command execution and manipulation:

- `ExecuteCommandAsync_WithValidCommand_Succeeds` - Arbitrary command execution
- `SetVariableAsync_WithDoubleValue_Succeeds` - Variable setting
- `CdAsync_WithValidDirectory_ReturnsTrue` - Directory change
- `AddModelicaPathAsync_WithValidPath_ReturnsTrue` - Modelica path management
- `SaveLogAsync_WithValidPath_CreatesLogFile` - Log file creation

### 5. LibraryTests (4 tests)

Tests library and model management:

- `OpenModelAsync_WithModelicaStandardLibrary_ReturnsTrue` - Opening standard library
- `OpenModelAsync_WithInvalidPath_ReturnsFalse` - Invalid path handling
- `TranslateModelAsync_WithValidModel_ReturnsTrue` - Model translation
- `CheckModelAsync_WithValidModel_ReturnsTrue` - Model validation

## Running the Tests

### Prerequisites

1. **Dymola 2025x Refresh 1** installed at:
   ```
   C:\Program Files\Dymola 2025x Refresh 1\bin64\Dymola.exe
   ```

2. **.NET 9.0 SDK** or later

### Run All Tests

```bash
dotnet test DymolaInterface.Tests/DymolaInterface.Tests.csproj
```

### Run Specific Test Class

```bash
# Basic functionality tests (fastest)
dotnet test --filter "FullyQualifiedName~DymolaInterfaceTests"

# Simulation tests
dotnet test --filter "FullyQualifiedName~SimulationTests"

# Plotting tests
dotnet test --filter "FullyQualifiedName~PlottingTests"

# Command tests
dotnet test --filter "FullyQualifiedName~CommandTests"

# Library tests
dotnet test --filter "FullyQualifiedName~LibraryTests"
```

### Run Without Starting Dymola

If Dymola is already running with `-serverport 8082`, the tests will connect to the existing instance instead of starting a new one. This is faster for repeated test runs.

Start Dymola manually:
```cmd
"C:\Program Files\Dymola 2025x Refresh 1\bin64\Dymola.exe" -serverport 8082
```

Then run tests normally.

## Test Coverage

The tests cover the following DymolaInterface methods:

### Process Management
- ✅ `StartDymolaProcessAsync()`
- ✅ `IsOfflineMode()`
- ✅ `SetOfflineMode()`
- ✅ `DymolaVersion()`
- ✅ `DymolaVersionNumber()`
- ✅ `ExitAsync()`

### Simulation
- ✅ `SimulateModelAsync()`
- ✅ `CheckModelAsync()`
- ✅ `TranslateModelAsync()`
- ✅ `GetLastResultFileNameAsync()`
- ✅ `ClearAsync()`

### Error Handling
- ✅ `GetLastErrorAsync()`
- ✅ `GetLastErrorLogAsync()`

### Plotting
- ✅ `PlotAsync()` (with various parameters)
- ✅ `ExportPlotAsImageAsync()`

### Commands & Variables
- ✅ `ExecuteCommandAsync()`
- ✅ `SetVariableAsync()`
- ✅ `CdAsync()`
- ✅ `AddModelicaPathAsync()`
- ✅ `SaveLogAsync()`

### Library Management
- ✅ `OpenModelAsync()`

## Expected Test Results

All tests should pass when:
- Dymola 2025x Refresh 1 is properly installed
- Modelica Standard Library is available
- Network/firewall allows localhost:8082 connections
- Temp directory is writable

## Test Execution Time

- **Single test**: ~2-15 seconds
- **Full test suite**: ~5-10 minutes (due to Dymola startup and simulation time)
- **Tests with existing Dymola**: ~2-5 minutes (faster, no startup time)

## Troubleshooting

### All Tests Fail with "Offline Mode"

**Cause**: Cannot connect to Dymola

**Solutions**:
1. Verify Dymola installation path in test constants
2. Manually start Dymola with `-serverport 8082`
3. Check firewall settings for port 8082

### Simulation Tests Timeout

**Cause**: Simulations taking too long

**Solutions**:
1. Increase `Task.Delay()` after `StartDymolaProcessAsync()`
2. Run only basic tests: `dotnet test --filter "FullyQualifiedName~DymolaInterfaceTests"`
3. Pre-start Dymola manually

### Plot Export Tests Fail

**Cause**: File permission or path issues

**Solutions**:
1. Verify temp directory write permissions
2. Check disk space
3. Close any locked plot files

## CI/CD Integration

For automated testing:

```bash
# Run with timeout and detailed logging
dotnet test DymolaInterface.Tests/DymolaInterface.Tests.csproj \
  --logger "trx;LogFileName=test_results.trx" \
  --results-directory ./TestResults \
  -- RunConfiguration.TestSessionTimeout=600000
```

## Next Steps

To extend the test suite:

1. Add tests for additional DymolaInterface methods as they're implemented
2. Add performance benchmarks
3. Add stress tests (many rapid operations)
4. Add concurrent access tests
5. Add tests for error recovery scenarios
