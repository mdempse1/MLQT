using Xunit;

namespace DymolaInterface.Tests;

/// <summary>
/// Tests for simulation and model checking functionality.
/// These tests run sequentially and share a single Dymola instance.
/// </summary>
[Collection("Dymola Collection")]
public class SimulationTests
{
    private readonly DymolaFixture _fixture;

    public SimulationTests(DymolaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SimulateModelAsync_WithValidModel_ReturnsTrue()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();
        var modelName = "Modelica.Blocks.Examples.PID_Controller";

        // Act
        var result = await _fixture.Dymola.SimulateModelAsync(
            problem: modelName,
            startTime: 0.0,
            stopTime: 4.0,
            numberOfIntervals: 500,
            method: "Dassl",
            tolerance: 0.0001
        );

        // Assert
        Assert.True(result, "Simulation should succeed");
    }

    [Fact]
    public async Task SimulateModelAsync_WithInvalidModel_ReturnsFalse()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();
        var modelName = "Invalid.Model.Name";

        // Act
        var result = await _fixture.Dymola.SimulateModelAsync(problem: modelName);

        // Assert
        Assert.False(result, "Simulation should fail for invalid model");
    }

    [Fact]
    public async Task CheckModelAsync_WithValidModel_ReturnsTrue()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();
        var modelName = "Modelica.Electrical.Analog.Examples.ChuaCircuit";

        // Act
        var result = await _fixture.Dymola.CheckModelAsync(modelName, simulate: false);

        // Assert
        Assert.True(result, "Model check should succeed");
    }

    [Fact]
    public async Task CheckModelAsync_WithInvalidModel_ReturnsFalse()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();
        var modelName = "Invalid.Model.Name";

        // Act
        var result = await _fixture.Dymola.CheckModelAsync(modelName, simulate: false);

        // Assert
        Assert.False(result, "Model check should fail for invalid model");
    }

    [Fact]
    public async Task TranslateModelAsync_WithValidModel_ReturnsTrue()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();
        var modelName = "Modelica.Mechanics.Rotational.Examples.First";

        // Act
        var result = await _fixture.Dymola.TranslateModelAsync(modelName);

        // Assert
        Assert.True(result, "Translation should succeed");
    }

    [Fact]
    public async Task GetLastErrorAsync_AfterFailedSimulation_ReturnsErrorMessage()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();
        var modelName = "Invalid.Model.Name";

        // Act
        await _fixture.Dymola.SimulateModelAsync(problem: modelName);
        var error = await _fixture.Dymola.GetLastErrorAsync();

        // Assert
        Assert.NotNull(error);
        Assert.NotEmpty(error);
        Assert.Contains($"Did not find model {modelName}", error);
    }

    [Fact]
    public async Task ClearAsync_ClearsWorkspace()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();
        await _fixture.Dymola.SimulateModelAsync("Modelica.Blocks.Examples.PID_Controller", stopTime: 1.0);

        // Act
        var result = await _fixture.Dymola.ClearAsync(fast: true);

        // Assert
        Assert.True(result);
    }
}
