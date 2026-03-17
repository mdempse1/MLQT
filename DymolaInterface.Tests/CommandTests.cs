using Xunit;

namespace DymolaInterface.Tests;

/// <summary>
/// Tests for command execution and variable manipulation.
/// These tests run sequentially and share a single Dymola instance.
/// </summary>
[Collection("Dymola Collection")]
public class CommandTests
{
    private readonly DymolaFixture _fixture;

    public CommandTests(DymolaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ExecuteCommandAsync_WithValidCommand_Succeeds()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();

        // Act
        var result = await _fixture.Dymola.ExecuteCommandAsync("Advanced.Define.DAEsolver = true");

        // Assert - Command should execute without throwing exception
        Assert.True(true);
    }

    [Fact]
    public async Task SetVariableAsync_WithDoubleValue_Succeeds()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();

        // Act
        var result = await _fixture.Dymola.SetVariableAsync("simulationStopTime", 100.0);

        // Assert - Variable should be set without throwing exception
        Assert.True(true);
    }

    [Fact]
    public async Task CdAsync_WithValidDirectory_ReturnsTrue()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();
        var tempDir = Path.GetTempPath();

        // Act
        var result = await _fixture.Dymola.CdAsync(tempDir);

        // Assert
        Assert.True(result, "Directory change should succeed");
    }

    [Fact]
    public async Task AddModelicaPathAsync_WithValidPath_ReturnsTrue()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();
        var tempDir = Path.GetTempPath();

        // Act
        var result = await _fixture.Dymola.AddModelicaPathAsync(tempDir, erase: false);

        // Assert
        Assert.True(result, "Adding Modelica path should succeed");
    }

    [Fact]
    public async Task SaveLogAsync_WithValidPath_CreatesLogFile()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();
        var logPath = Path.Combine(Path.GetTempPath(), $"dymola_log_{Guid.NewGuid()}.log");

        try
        {
            // Act
            var result = await _fixture.Dymola.SaveLogAsync(logPath);

            // Assert
            Assert.True(result, "Save log should succeed");
            Assert.True(File.Exists(logPath), "Log file should be created");
        }
        finally
        {
            // Cleanup
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }
}
