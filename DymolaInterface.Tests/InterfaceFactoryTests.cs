using DymolaInterface;
using DymolaInterface.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace DymolaInterface.Tests;

/// <summary>
/// Tests for DymolaInterfaceFactory and OpenModelicaInterfaceFactory.
/// Note: These tests focus on the factory's state management and thread safety patterns.
/// Full integration tests would require actual Dymola/OpenModelica installations.
/// </summary>
public class InterfaceFactoryTests
{
    [Fact]
    public void DymolaInterfaceFactory_Constructor_DoesNotThrow()
    {
        // Arrange

        // Act & Assert - Should not throw
        var factory = new DymolaInterfaceFactory();
        Assert.NotNull(factory);
    }

    [Fact]
    public void DymolaInterfaceFactory_IsConnected_InitiallyFalse()
    {
        // Arrange
        var factory = new DymolaInterfaceFactory();

        // Act & Assert
        Assert.False(factory.IsConnected);
    }

    [Fact]
    public async Task DymolaInterfaceFactory_ResetAsync_WhenNoInstance_DoesNotThrow()
    {
        // Arrange
        var factory = new DymolaInterfaceFactory();

        // Act & Assert - Should not throw even with no instance
        await factory.ResetAsync();
        Assert.False(factory.IsConnected);
    }

    [Fact]
    public async Task DymolaInterfaceFactory_ResetAsync_MultipleCallsDoNotThrow()
    {
        // Arrange
        var factory = new DymolaInterfaceFactory();

        // Act & Assert - Multiple reset calls should be safe
        await factory.ResetAsync();
        await factory.ResetAsync();
        await factory.ResetAsync();
        Assert.False(factory.IsConnected);
    }

    [Fact]
    public async Task DymolaInterfaceFactory_GetOrCreateAsync_WithInvalidSettings_ThrowsButDoesNotCorruptState()
    {
        // Arrange
        var settings = new DymolaSettings()
            {
                DymolaPath = "invalid_path_that_does_not_exist.exe",
                PortNumber = 9999
            };
        var factory = new DymolaInterfaceFactory();
        factory.UpdateSettings(settings);

        // Act - Will throw because Dymola is not installed
        try
        {
            await factory.GetOrCreateAsync();
        }
        catch
        {
            // Expected - Dymola is not installed or configured
        }

        // Assert - Factory should still be in valid state
        Assert.False(factory.IsConnected);
    }

    [Fact]
    public void DymolaSettings_CanSetAllProperties()
    {
        // Arrange & Act
        var settings = new DymolaSettings
        {
            DymolaPath = @"C:\Program Files\Dymola\bin64\dymola.exe",
            PortNumber = 9000
        };

        // Assert
        Assert.Equal(@"C:\Program Files\Dymola\bin64\dymola.exe", settings.DymolaPath);
        Assert.Equal(9000, settings.PortNumber);
    }

    [Fact]
    public void DymolaSettings_DefaultValues()
    {
        // Arrange & Act
        var settings = new DymolaSettings();

        // Assert - DymolaPath is discovered at construction from ProgramFiles.
        // It should either be the empty string (no Dymola installed) or point
        // at an existing dymola.exe under a recognised "Dymola <year>x[ Refresh 1]"
        // directory. We do not hard-code the version — that makes the test
        // portable across machines with different installations.
        Assert.Equal("127.0.0.1", settings.HostAddress);
        Assert.Equal(8082, settings.PortNumber);

        if (!string.IsNullOrEmpty(settings.DymolaPath))
        {
            Assert.True(File.Exists(settings.DymolaPath),
                $"Discovered DymolaPath '{settings.DymolaPath}' should exist on disk.");
            Assert.EndsWith("dymola.exe", settings.DymolaPath, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Dymola", settings.DymolaPath);
        }
    }

    [Fact]
    public void DymolaInterfaceFactory_ImplementsInterface()
    {
        // Arrange

        // Act
        var factory = new DymolaInterfaceFactory();

        // Assert
        Assert.IsAssignableFrom<IDymolaInterfaceFactory>(factory);
    }

    [Fact]
    public async Task DymolaInterfaceFactory_ConcurrentResetCalls_DoNotThrow()
    {
        // Arrange
        var factory = new DymolaInterfaceFactory();

        // Act - Simulate concurrent reset calls
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => factory.ResetAsync())
            .ToArray();

        // Assert - All should complete without exception
        await Task.WhenAll(tasks);
        Assert.False(factory.IsConnected);
    }

}