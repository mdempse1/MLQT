using Xunit;

namespace DymolaInterface.Tests;

/// <summary>
/// Tests for DymolaInterface basic functionality and connection.
/// Note: These tests require Dymola to be installed.
/// These tests run sequentially and share a single Dymola instance.
/// </summary>
[Collection("Dymola Collection")]
public class DymolaInterfaceTests
{
    private readonly DymolaFixture _fixture;

    public DymolaInterfaceTests(DymolaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Constructor_WithValidPath_CreatesInstance()
    {
        // Arrange & Act - use the same auto-discovered path as the fixture.
        using var dymola = new DymolaInterface(
            DymolaFixture.ResolveDymolaPath(),
            8083,
            "127.0.0.1"
        );

        // Assert
        Assert.NotNull(dymola);
    }

    [Fact]
    public void Constructor_WithEmptyPath_CreatesInstanceInOfflineMode()
    {
        // Arrange & Act - Use non-standard port to avoid connecting to shared instance
        using var dymola = new DymolaInterface("", 9999, "127.0.0.1");

        // Assert
        Assert.NotNull(dymola);
        Assert.True(dymola.IsOfflineMode());
    }

    [Fact]
    public void IsOfflineMode_WhenDymolaNotRunning_ReturnsTrue()
    {
        // Arrange - Use non-standard port that won't have Dymola running
        using var dymola = new DymolaInterface(
            DymolaFixture.ResolveDymolaPath(),
            9999,
            "127.0.0.1"
        );

        // Act
        var isOffline = dymola.IsOfflineMode();

        // Assert
        Assert.True(isOffline);
    }

    [Fact]
    public async Task DymolaVersion_WhenConnected_ReturnsVersionString()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();

        // Act
        var version = await _fixture.Dymola.DymolaVersionAsync();

        // Assert - Dymola version strings are of the form "Dymola Version 2026x, 2025-10-10".
        Assert.NotNull(version);
        Assert.NotEmpty(version);
        Assert.Contains("Dymola", version, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DymolaVersionNumber_WhenConnected_ReturnsVersionNumber()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();

        // Act
        var versionNumber = await _fixture.Dymola.DymolaVersionNumberAsync();

        // Assert - any supported release (2020 onwards).
        Assert.True(versionNumber >= 2020.0);
    }

    [Fact]
    public async Task IsOfflineMode_WhenConnected_ReturnsFalse()
    {
        // Arrange
        await _fixture.EnsureDymolaStartedAsync();

        // Act
        var isOffline = _fixture.Dymola.IsOfflineMode();

        // Assert
        Assert.False(isOffline);
    }
}
