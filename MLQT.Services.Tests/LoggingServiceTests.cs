using MLQT.Services;
using NLog;

namespace MLQT.Services.Tests;

/// <summary>
/// Unit tests for the LoggingService class.
/// </summary>
public class LoggingServiceTests
{
    [Fact]
    public void Initialize_DoesNotThrow()
    {
        // Should be safe to call without crashing
        LoggingService.Initialize();
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        // Calling twice should not throw
        LoggingService.Initialize();
        LoggingService.Initialize();
    }

    [Fact]
    public void GetLogger_ByName_ReturnsLogger()
    {
        var logger = LoggingService.GetLogger("TestLogger");

        Assert.NotNull(logger);
        Assert.Equal("TestLogger", logger.Name);
    }

    [Fact]
    public void GetLogger_ByType_ReturnsLogger()
    {
        var logger = LoggingService.GetLogger<LoggingServiceTests>();

        Assert.NotNull(logger);
        Assert.Contains("LoggingServiceTests", logger.Name);
    }

    [Fact]
    public void Info_DoesNotThrow()
    {
        LoggingService.Info("TestSource", "Test info message");
    }

    [Fact]
    public void Debug_DoesNotThrow()
    {
        LoggingService.Debug("TestSource", "Test debug message");
    }

    [Fact]
    public void Warn_DoesNotThrow()
    {
        LoggingService.Warn("TestSource", "Test warning message");
    }

    [Fact]
    public void Error_WithMessage_DoesNotThrow()
    {
        LoggingService.Error("TestSource", "Test error message");
    }

    [Fact]
    public void Error_WithMessageAndException_DoesNotThrow()
    {
        var ex = new InvalidOperationException("test exception");
        LoggingService.Error("TestSource", "Test error message", ex);
    }

    [Fact]
    public void Error_WithException_DoesNotThrow()
    {
        var ex = new InvalidOperationException("test exception");
        LoggingService.Error("TestSource", ex);
    }

    [Fact]
    public void Fatal_WithMessageAndException_DoesNotThrow()
    {
        var ex = new InvalidOperationException("test fatal exception");
        LoggingService.Fatal("TestSource", "Fatal error", ex);
    }

    [Fact]
    public void LogProcessStart_DoesNotThrow()
    {
        LoggingService.LogProcessStart("TestSource", "TestProcess");
    }

    [Fact]
    public void LogProcessEnd_DoesNotThrow()
    {
        LoggingService.LogProcessEnd("TestSource", "TestProcess");
    }

    [Fact]
    public void LogProcessFailed_DoesNotThrow()
    {
        var ex = new InvalidOperationException("process failed");
        LoggingService.LogProcessFailed("TestSource", "TestProcess", ex);
    }

    [Fact]
    public void Shutdown_DoesNotThrow()
    {
        // Note: this shuts down NLog - re-initialize after
        LoggingService.Shutdown();
        // Re-initialize so subsequent tests can still use logging
        LoggingService.Initialize();
    }
}
