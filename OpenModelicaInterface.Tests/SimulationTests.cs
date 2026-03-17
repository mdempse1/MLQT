using Xunit;

namespace OpenModelicaInterface.Tests;

/// <summary>
/// Tests for simulation and model checking functionality.
/// These tests run sequentially and share a single OMC instance.
/// </summary>
[Collection("OpenModelica Collection")]
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
        await _fixture.Omc.LoadModelAsync("Modelica");
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
        await _fixture.Omc.LoadModelAsync("Modelica");
        var modelName = "Modelica.Electrical.Analog.Examples.ChuaCircuit";

        // Act
        var result = await _fixture.Omc.CheckModelAsync(modelName);

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
        var result = await _fixture.Omc.CheckModelAsync(modelName);

        // Assert
        Assert.False(result, "Model check should fail for invalid model");
    }

    [Fact]
    public async Task BuildModelAsync_WithValidModel_ReturnsTrue()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.LoadModelAsync("Modelica");
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
        await _fixture.Omc.LoadModelAsync("Modelica");
        var modelName = "Modelica.Electrical.Analog.Basic.Resistor";

        // Act
        var flatModel = await _fixture.Omc.InstantiateModelAsync(modelName);

        // Assert
        Assert.NotNull(flatModel);
        Assert.NotEmpty(flatModel);
        Assert.Contains("class " + modelName, flatModel);
    }

    [Fact]
    public async Task GetErrorStringAsync_AfterClear_ReturnsEmpty()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();

        // Create an error
        await _fixture.Omc.LoadFileAsync("NonExistent.mo");
        var errorBefore = await _fixture.Omc.GetErrorStringAsync();
        Assert.NotEmpty(errorBefore);

        // Act
        await _fixture.Omc.ClearAsync();
        var errorAfter = await _fixture.Omc.GetErrorStringAsync();

        // Assert
        Assert.True(string.IsNullOrWhiteSpace(errorAfter) || errorAfter == "\"\"");
    }

    [Fact]
    public async Task SimulateModelAsync_WithCustomParameters_ReturnsSuccess()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.LoadModelAsync("Modelica");
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
