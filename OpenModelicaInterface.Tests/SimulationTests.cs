using Xunit;

namespace OpenModelicaInterface.Tests;

/// <summary>
/// Tests for simulation and model checking functionality.
/// These tests run sequentially and share a single OMC instance.
/// </summary>
[Collection("OpenModelica Collection")]
[Trait("Requires", "OpenModelica")]
public class SimulationTests
{
    private readonly OpenModelicaFixture _fixture;

    public SimulationTests(OpenModelicaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SimulateModelAsync_WithValidModel_ReturnsSuccess()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.LoadModelAsync("Modelica", cancellationToken: TestContext.Current.CancellationToken);
        var modelName = "Modelica.Blocks.Examples.PID_Controller";

        // Act
        var result = await _fixture.Omc.SimulateModelAsync(
            modelName: modelName,
            startTime: 0.0,
            stopTime: 4.0,
            numberOfIntervals: 500,
            tolerance: 0.0001,
            method: "dassl"
        );

        // Assert
        Assert.True(result.Success, "Simulation should succeed");
        Assert.NotEmpty(result.ResultFile);
    }

    [Fact]
    public async Task SimulateModelAsync_WithInvalidModel_ReturnsFalse()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        var modelName = "Invalid.Model.Name";

        // Act
        var result = await _fixture.Omc.SimulateModelAsync(modelName: modelName);

        // Assert
        Assert.False(result.Success, "Simulation should fail for invalid model");
    }

    [Fact]
    public async Task CheckModelAsync_WithValidModel_ReturnsTrue()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.LoadModelAsync("Modelica", cancellationToken: TestContext.Current.CancellationToken);
        var modelName = "Modelica.Electrical.Analog.Examples.ChuaCircuit";

        // Act
        var result = await _fixture.Omc.CheckModelAsync(modelName, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result, "Model check should succeed");
    }

    [Fact]
    public async Task CheckModelAsync_WithInvalidModel_ReturnsFalse()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        var modelName = "Invalid.Model.Name";

        // Act
        var result = await _fixture.Omc.CheckModelAsync(modelName, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result, "Model check should fail for invalid model");
    }

    [Fact]
    public async Task BuildModelAsync_WithValidModel_ReturnsTrue()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.LoadModelAsync("Modelica", cancellationToken: TestContext.Current.CancellationToken);
        var modelName = "Modelica.Mechanics.Rotational.Examples.First";

        // Act
        var result = await _fixture.Omc.BuildModelAsync(modelName);

        // Assert
        Assert.True(result, "Build should succeed");
    }

    [Fact]
    public async Task InstantiateModelAsync_WithValidModel_ReturnsModelCode()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.LoadModelAsync("Modelica", cancellationToken: TestContext.Current.CancellationToken);
        var modelName = "Modelica.Electrical.Analog.Basic.Resistor";

        // Act
        var flatModel = await _fixture.Omc.InstantiateModelAsync(modelName);

        // Assert
        Assert.NotNull(flatModel);
        Assert.NotEmpty(flatModel);
        Assert.Contains("class " + modelName, flatModel);
    }

    [Fact]
    public async Task LoadingAMissingFile_Fails_AndSaysNothingElse()
    {
        // omc 1.26 answers false and leaves the error buffer empty for a file that is not there;
        // 1.27 adds "Failed to load file NonExistent.mo: file does not exist." Either is fine for
        // MLQT, which decides "file not found" with File.Exists and drains the buffer after a failed
        // load. What both must do is fail, say nothing unrelated, and leave nothing behind once read.
        // Asserted that way rather than per version, so the next release's wording does not break it
        // (B116 for the 1.26 half).
        await _fixture.EnsureOmcStartedAsync();

        // The whole collection shares one omc, and its error buffer is global state that only a read
        // empties. Without this drain the test reads whatever an earlier test left behind - which is
        // how it passed alone and failed in the suite.
        await _fixture.Omc.GetErrorStringAsync();

        var loaded = await _fixture.Omc.LoadFileAsync("NonExistent.mo", cancellationToken: TestContext.Current.CancellationToken);
        var error = await _fixture.Omc.GetErrorStringAsync();
        var afterwards = await _fixture.Omc.GetErrorStringAsync();

        Assert.False(loaded);
        Assert.True(string.IsNullOrWhiteSpace(error) || error.Contains("NonExistent.mo"),
            $"omc reported something other than the missing file: {error}");
        Assert.True(string.IsNullOrWhiteSpace(afterwards),
            $"the missing file's error outlived the read that should have emptied it: {afterwards}");
    }

    [Fact]
    public async Task ReadingTheErrorString_ConsumesIt()
    {
        // The property callers actually depend on, and the one the old test was accidentally
        // demonstrating: getErrorString() drains the buffer. Anything that reads errors for logging
        // and then reads them again to report gets nothing the second time.
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.GetErrorStringAsync();          // drain anything left by earlier tests

        // A class that does not exist is a failure every omc reports; a missing file is reported
        // only from 1.27, so it cannot be used to manufacture an error here.
        await _fixture.Omc.InstantiateModelAsync("NoSuchModelForThisTest");

        var first = await _fixture.Omc.GetErrorStringAsync();
        var second = await _fixture.Omc.GetErrorStringAsync();

        Assert.False(string.IsNullOrWhiteSpace(first), "omc reported no error for an unknown class");
        Assert.True(string.IsNullOrWhiteSpace(second),
            $"the second read should be empty because the first consumed the buffer, got: {second}");
    }

    [Fact]
    public async Task ClearDoesNotDiscardPendingErrors()
    {
        // Pinned because it is surprising and because the old test asserted the reverse. clear()
        // resets the loaded classes, not the error buffer - so an error raised before it is still
        // waiting afterwards. Code that calls ClearAsync between operations and then reports errors
        // will attribute the previous operation's failure to the next one.
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.GetErrorStringAsync();          // drain anything left by earlier tests

        await _fixture.Omc.InstantiateModelAsync("AnotherModelThatDoesNotExist");
        await _fixture.Omc.ClearAsync();

        var afterClear = await _fixture.Omc.GetErrorStringAsync();

        Assert.False(string.IsNullOrWhiteSpace(afterClear),
            "clear() was expected to leave the pending error in place");
    }

    [Fact]
    public async Task SimulateModelAsync_WithCustomParameters_ReturnsSuccess()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.LoadModelAsync("Modelica", cancellationToken: TestContext.Current.CancellationToken);
        var modelName = "Modelica.Mechanics.Rotational.Examples.First";

        // Act
        var result = await _fixture.Omc.SimulateModelAsync(
            modelName: modelName,
            startTime: 0.0,
            stopTime: 1.0,
            numberOfIntervals: 100,
            tolerance: 1e-4,
            method: "euler"
        );

        // Assert
        Assert.True(result.Success, "Simulation with custom parameters should succeed");
    }
}
