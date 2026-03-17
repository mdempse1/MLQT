using Xunit;

namespace OpenModelicaInterface.Tests;

/// <summary>
/// Tests for OpenModelicaInterface basic functionality and connection.
/// Note: These tests require OpenModelica to be installed.
/// These tests run sequentially and share a single OMC instance.
/// </summary>
[Collection("OpenModelica Collection")]
public class OpenModelicaInterfaceTests
{
    private readonly OpenModelicaFixture _fixture;

    public OpenModelicaInterfaceTests(OpenModelicaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Constructor_WithValidPath_CreatesInstance()
    {
        // Arrange & Act
        using var omc = new OpenModelicaInterface(
            @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe",
            port: 13028  // Use different port to avoid conflict with shared fixture
        );

        // Assert
        Assert.NotNull(omc);
    }

    [Fact]
    public async Task StartAsync_WithValidPath_StartsOmc()
    {
        // Arrange
        using var omc = new OpenModelicaInterface(
            @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe",
            port: 13029  // Use different port to avoid conflict with shared fixture
        );

        // Act
        await omc.StartAsync();

        // Assert
        Assert.True(omc.IsConnected);

        await omc.ExitAsync();
    }

    [Fact]
    public void IsConnected_WhenOmcNotStarted_ReturnsFalse()
    {
        // Arrange
        using var omc = new OpenModelicaInterface(
            @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe",
            port: 13030  // Use different port to avoid conflict with shared fixture
        );

        // Act & Assert
        Assert.False(omc.IsConnected);
    }

    [Fact]
    public async Task GetVersionAsync_WhenConnected_ReturnsVersionString()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();

        // Act
        var version = await _fixture.Omc.GetVersionAsync();

        // Assert
        Assert.NotNull(version);
        Assert.NotEmpty(version);
        Assert.Matches(@"\d+\.\d+\.\d+", version); // Format: X.Y.Z
    }

    [Fact]
    public async Task IsConnected_WhenConnected_ReturnsTrue()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();

        // Act
        var isConnected = _fixture.Omc.IsConnected;

        // Assert
        Assert.True(isConnected);
    }

    [Fact]
    public async Task SendCommandAsync_WithValidCommand_ReturnsResponse()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();

        // Act
        var response = await _fixture.Omc.SendCommandAsync("getVersion()");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
    }

    [Fact]
    public async Task GetWorkingDirectoryAsync_ReturnsDirectory()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();

        // Act
        var workDir = await _fixture.Omc.GetWorkingDirectoryAsync();

        // Assert
        Assert.NotNull(workDir);
        Assert.NotEmpty(workDir);
        Assert.True(Directory.Exists(workDir));
    }

    [Fact]
    public async Task SetWorkingDirectoryAsync_WithValidDirectory_ReturnsTrue()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();
        var tempDir = Path.GetTempPath();

        // Act
        var result = await _fixture.Omc.SetWorkingDirectoryAsync(tempDir);

        // Assert
        Assert.True(result);

        // Verify
        tempDir = tempDir.Replace("\\", "/");
        var currentDir = await _fixture.Omc.GetWorkingDirectoryAsync();
        if (tempDir.EndsWith("/") && !currentDir.EndsWith("/"))
            currentDir += "/";
        Assert.Equal(tempDir, currentDir);
    }

    [Fact]
    public async Task ClearAsync_ClearsOmcState()
    {
        // Arrange
        await _fixture.EnsureOmcStartedAsync();

        // Act
        var result = await _fixture.Omc.ClearAsync();

        // Assert
        Assert.True(result);
    }
}
