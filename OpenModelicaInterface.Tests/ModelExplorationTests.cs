using Xunit;

namespace OpenModelicaInterface.Tests;

/// <summary>
/// Tests for model exploration and introspection functionality.
/// These tests run sequentially and share a single OMC instance.
/// </summary>
[Collection("OpenModelica Collection")]
public class ModelExplorationTests
{
    private readonly OpenModelicaFixture _fixture;

    public ModelExplorationTests(OpenModelicaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetClassNamesInPackageAsync_WithValidPackage_ReturnsClasses()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.LoadModelAsync("Modelica");
        var packageName = "Modelica.Blocks";

        // Act
        var classes = await _fixture.Omc.GetClassNamesInPackageAsync(packageName);

        // Assert
        Assert.NotNull(classes);
        Assert.NotEmpty(classes);
        // Should contain subpackages like Continuous, Sources, etc.
        Assert.Contains(classes, c => c.Contains("Continuous") || c.Contains("Sources"));
    }

    [Fact]
    public async Task GetClassNamesInPackageAsync_WithEmptyPackage_ReturnsEmptyArray()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        var packageName = "NonExistentPackage";

        // Act
        var classes = await _fixture.Omc.GetClassNamesInPackageAsync(packageName);

        // Assert
        Assert.NotNull(classes);
        Assert.Empty(classes);
    }

    [Fact]
    public async Task GetComponentsAsync_WithValidModel_ReturnsComponents()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.LoadModelAsync("Modelica");
        var modelName = "Modelica.Blocks.Continuous.PID";

        // Act
        var components = await _fixture.Omc.GetComponentsAsync(modelName);

        // Assert
        Assert.NotNull(components);
        Assert.NotEmpty(components);
    }

    [Fact]
    public async Task GetClassInformationAsync_WithValidClass_ReturnsInformation()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.LoadModelAsync("Modelica");
        var className = "Modelica.Blocks.Continuous.PID";

        // Act
        var info = await _fixture.Omc.GetClassInformationAsync(className);

        // Assert
        Assert.NotNull(info);
        Assert.NotEmpty(info);
    }

    [Fact]
    public async Task GetClassCommentAsync_WithValidClass_ReturnsComment()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.LoadModelAsync("Modelica");
        var className = "Modelica.Blocks.Continuous.PID";

        // Act
        var comment = await _fixture.Omc.GetClassCommentAsync(className);

        // Assert
        Assert.NotNull(comment);
        // Comment may be empty for some classes, so just verify it's not null
    }

    [Fact]
    public async Task GetClassCommentAsync_WithInvalidClass_ReturnsEmpty()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        var className = "Invalid.Class.Name";

        // Act
        var comment = await _fixture.Omc.GetClassCommentAsync(className);

        // Assert
        Assert.NotNull(comment);
        Assert.True(string.IsNullOrEmpty(comment) || comment == "\"\"");
    }

    [Fact]
    public async Task InstantiateModelAsync_WithSimpleModel_ReturnsFlatCode()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.LoadModelAsync("Modelica");
        var modelName = "Modelica.Electrical.Analog.Basic.Resistor";

        // Act
        var flatCode = await _fixture.Omc.InstantiateModelAsync(modelName);

        // Assert
        Assert.NotNull(flatCode);
        Assert.NotEmpty(flatCode);
        Assert.Contains("class", flatCode);
    }

    [Fact]
    public async Task GetClassNamesAsync_ReturnsTopLevelClasses()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        await _fixture.Omc.ClearAsync();
        await _fixture.Omc.LoadModelAsync("Modelica");

        // Act
        var classes = await _fixture.Omc.GetClassNamesAsync();

        // Assert
        Assert.NotNull(classes);
        Assert.Contains(classes, c => c == "Modelica");
    }

    [Fact]
    public async Task SendCommandAsync_GetInstallationDirectoryPath_ReturnsPath()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();

        // Act
        var response = await _fixture.Omc.SendCommandAsync("getInstallationDirectoryPath()");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        // Remove quotes from response
        var path = response.Trim('"');
        Assert.True(Directory.Exists(path) || path.Contains("OpenModelica"));
    }

    [Fact]
    public async Task SendCommandAsync_CustomCommand_ReturnsResponse()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();

        // Act - Get list of available commands (help)
        var response = await _fixture.Omc.SendCommandAsync("getAvailableLibraries()");

        // Assert
        Assert.NotNull(response);
        // Response should be an array of library information
        Assert.True(response.StartsWith("{") || response.StartsWith("["));
    }
}
