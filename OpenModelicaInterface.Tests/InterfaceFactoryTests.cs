using OpenModelicaInterface;
using OpenModelicaInterface.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace MLQT.Shared.Tests;

/// <summary>
/// Tests for DymolaInterfaceFactory and OpenModelicaInterfaceFactory.
/// Note: These tests focus on the factory's state management and thread safety patterns.
/// Full integration tests would require actual Dymola/OpenModelica installations.
/// </summary>
public class InterfaceFactoryTests
{
    [Fact]
    public void OpenModelicaInterfaceFactory_Constructor_DoesNotThrow()
    {
        // Arrange

        // Act & Assert - Should not throw
        var factory = new OpenModelicaInterfaceFactory();
        Assert.NotNull(factory);
    }

    [Fact]
    public void OpenModelicaInterfaceFactory_IsConnected_InitiallyFalse()
    {
        // Arrange
        var factory = new OpenModelicaInterfaceFactory();

        // Act & Assert
        Assert.False(factory.IsConnected);
    }

    [Fact]
    public async Task OpenModelicaInterfaceFactory_ResetAsync_WhenNoInstance_DoesNotThrow()
    {
        // Arrange
        var factory = new OpenModelicaInterfaceFactory();

        // Act & Assert - Should not throw even with no instance
        await factory.ResetAsync();
        Assert.False(factory.IsConnected);
    }

    [Fact]
    public async Task OpenModelicaInterfaceFactory_ResetAsync_MultipleCallsDoNotThrow()
    {
        // Arrange
        var factory = new OpenModelicaInterfaceFactory();

        // Act & Assert - Multiple reset calls should be safe
        await factory.ResetAsync();
        await factory.ResetAsync();
        await factory.ResetAsync();
        Assert.False(factory.IsConnected);
    }

    [Fact]
    public async Task OpenModelicaInterfaceFactory_GetOrCreateAsync_WithInvalidSettings_ThrowsButDoesNotCorruptState()
    {
        // Arrange
        var settings = new OpenModelicaSettings()
        {
            OmcPath = "invalid_omc_path_that_does_not_exist.exe",
            PortNumber = 9999
        };
        var factory = new OpenModelicaInterfaceFactory();
        factory.UpdateSettings(settings);

        // Act - Will throw because OpenModelica is not installed
        try
        {
            await factory.GetOrCreateAsync();
        }
        catch
        {
            // Expected - OpenModelica is not installed or configured
        }

        // Assert - Factory should still be in valid state
        Assert.False(factory.IsConnected);
    }


    [Fact]
    public async Task OpenModelicaInterfaceFactory_ConcurrentResetCalls_DoNotThrow()
    {
        // Arrange
        var factory = new OpenModelicaInterfaceFactory();

        // Act - Simulate concurrent reset calls
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => factory.ResetAsync())
            .ToArray();

        // Assert - All should complete without exception
        await Task.WhenAll(tasks);
        Assert.False(factory.IsConnected);
    }

    [Fact]
    public void OpenModelicaInterfaceFactory_ImplementsInterface()
    {
        // Arrange

        // Act
        var factory = new OpenModelicaInterfaceFactory();

        // Assert
        Assert.IsAssignableFrom<IOpenModelicaInterfaceFactory>(factory);
    }

    [Fact]
    public void OpenModelicaSettings_DefaultValues()
    {
        // Arrange & Act
        var settings = new OpenModelicaSettings();

        // Assert
        Assert.Equal(@"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe", settings.OmcPath);
        Assert.Equal(13027, settings.PortNumber);
        Assert.False(settings.AutoLoadModelicaLibrary);
        Assert.Equal(1e-6, settings.DefaultTolerance);
        Assert.Equal(500, settings.DefaultNumberOfIntervals);
    }

    [Fact]
    public void OpenModelicaSettings_CanSetAllProperties()
    {
        // Arrange & Act
        var settings = new OpenModelicaSettings
        {
            OmcPath = @"C:\OpenModelica\bin\omc.exe",
            PortNumber = 14000,
            AutoLoadModelicaLibrary = true,
            DefaultTolerance = 1e-8,
            DefaultNumberOfIntervals = 1000
        };

        // Assert
        Assert.Equal(@"C:\OpenModelica\bin\omc.exe", settings.OmcPath);
        Assert.Equal(14000, settings.PortNumber);
        Assert.True(settings.AutoLoadModelicaLibrary);
        Assert.Equal(1e-8, settings.DefaultTolerance);
        Assert.Equal(1000, settings.DefaultNumberOfIntervals);
    }
}
