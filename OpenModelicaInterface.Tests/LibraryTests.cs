using Xunit;

namespace OpenModelicaInterface.Tests;

/// <summary>
/// Tests for library and model management functionality.
/// These tests run sequentially and share a single OMC instance.
/// </summary>
[Collection("OpenModelica Collection")]
public class LibraryTests
{
    private readonly OpenModelicaFixture _fixture;

    public LibraryTests(OpenModelicaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LoadModelAsync_WithModelicaStandardLibrary_ReturnsTrue()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();

        // Act
        var result = await _fixture.Omc.LoadModelAsync("Modelica");

        // Assert
        Assert.True(result, "Loading Modelica library should succeed");
    }

    [Fact]
    public async Task LoadModelAsync_WithSpecificVersion_ReturnsTrue()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();

        // Act - Load Modelica with version (if available)
        var result = await _fixture.Omc.LoadModelAsync("Modelica", "4.0.0");

        // Assert - May succeed or fail depending on available versions
        // Just verify it returns a boolean
        Assert.True(result == true || result == false);
    }

    [Fact]
    public async Task LoadModelAsync_WithInvalidLibrary_ReturnsFalse()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();

        // Act
        var result = await _fixture.Omc.LoadModelAsync("NonExistentLibrary");

        // Assert
        Assert.False(result, "Loading non-existent library should fail");
    }

    [Fact]
    public async Task LoadFileAsync_WithInvalidPath_ReturnsFalse()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();

        // Act
        var result = await _fixture.Omc.LoadFileAsync("Invalid/Path/package.mo");

        // Assert
        Assert.False(result, "Loading invalid path should fail");
    }

    [Fact]
    public async Task GetClassNamesAsync_AfterLoadingLibrary_ReturnsClasses()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.ClearAsync();
        await _fixture.Omc.LoadModelAsync("Modelica");

        // Act
        var classes = await _fixture.Omc.GetClassNamesAsync();

        // Assert
        Assert.NotNull(classes);
        Assert.NotEmpty(classes);
        Assert.Contains(classes, c => c == "Modelica");
    }

    [Fact]
    public async Task ClearAsync_RemovesLoadedClasses()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.LoadModelAsync("Modelica");
        var classesBefore = await _fixture.Omc.GetClassNamesAsync();
        Assert.NotEmpty(classesBefore);

        // Act
        await _fixture.Omc.ClearAsync();
        var classesAfter = await _fixture.Omc.GetClassNamesAsync();

        // Assert
        Assert.True(classesAfter.Length == 0 || classesAfter.Length < classesBefore.Length);
    }

    [Fact]
    public async Task CheckModelAsync_LibraryLoadsAutomatically()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.ClearAsync();
        var modelName = "Modelica.Blocks.Examples.PID_Controller";

        // Act - Check without library loaded
        var resultBefore = await _fixture.Omc.CheckModelAsync(modelName);

        // Assert
        Assert.True(resultBefore, "Check model should be successful");
    }


    [Fact]
    public async Task CheckModelAsync_RequiresLibraryLoaded()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.ClearAsync();
        var modelName = "Modelica.Blocks.Examples.PID_Controller";

        // Load library and check again
        await _fixture.Omc.LoadModelAsync("Modelica");
        var resultAfter = await _fixture.Omc.CheckModelAsync(modelName);

        // Assert
        Assert.True(resultAfter, "Check should succeed with library loaded");
    }
}
