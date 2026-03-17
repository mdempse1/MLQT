using Xunit;

namespace DymolaInterface.Tests;

/// <summary>
/// Tests for library and model management functionality.
/// These tests run sequentially and share a single Dymola instance.
/// </summary>
[Collection("Dymola Collection")]
public class LibraryTests
{
    private readonly DymolaFixture _fixture;

    public LibraryTests(DymolaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OpenModelFileAsync_WithModelicaStandardLibrary_ReturnsTrue()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();

        // Act - Open Modelica.Blocks package
        var result = await _fixture.Dymola.OpenModelFileAsync("Modelica.Blocks");

        // Assert
        Assert.True(result, "Opening Modelica.Blocks should succeed");
    }

    [Fact]
    public async Task OpenModelAsync_WithInvalidPath_ReturnsFalse()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();

        // Act
        var result = await _fixture.Dymola.OpenModelAsync("Invalid/Path/package.mo", mustRead: false);

        // Assert
        Assert.False(result, "Opening invalid path should fail");
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
}
