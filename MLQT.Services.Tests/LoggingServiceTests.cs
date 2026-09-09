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

    // The console target: off unless asked for. It was on at Info for as long as MLQT was a
    // Windows-only WinExe with no console attached, so nobody saw it - and the first time the
    // application was started from a Linux terminal it printed its whole startup sequence there.
    // Initialize configures NLog globally and runs once per process, so the decision it makes cannot
    // be tested; ConsoleLevel is that decision, lifted out so it can be.

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConsoleLevel_WithNothingSet_IsOff(string? setting)
    {
        // The default, and the whole point of the change: one place to look when something is wrong.
        Assert.Null(LoggingService.ConsoleLevel(setting));
    }

    [Theory]
    [InlineData("Off")]
    [InlineData("off")]
    [InlineData("  OFF  ")]
    public void ConsoleLevel_WithOff_IsOff(string setting)
    {
        // Off is a real NLog level, so it is the way to say "no console" deliberately rather than by
        // leaving the variable unset. Trimmed and case-insensitive because it is typed by hand.
        Assert.Null(LoggingService.ConsoleLevel(setting));
    }

    [Theory]
    [InlineData("Trace")]
    [InlineData("debug")]
    [InlineData("Info")]
    [InlineData("WARN")]
    [InlineData("Error")]
    [InlineData("Fatal")]
    public void ConsoleLevel_WithALevelName_IsThatLevel(string setting)
    {
        var level = LoggingService.ConsoleLevel(setting);

        Assert.NotNull(level);
        Assert.Equal(setting.Trim(), level.Name, ignoreCase: true);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("Inof")]
    public void ConsoleLevel_WithSomethingThatIsNotALevel_IsInfo(string setting)
    {
        // Deliberately generous, and deliberately not silent. Someone setting this variable is trying
        // to leave silence, so a typo must not return them to it - the failure has to be too much
        // output rather than none, which is the reading they can actually diagnose. "1" and "true"
        // are what a person types, and they work for the same reason.
        Assert.Equal(LogLevel.Info, LoggingService.ConsoleLevel(setting));
    }

    [Fact]
    public void ConsoleLevel_VariableNameIsStable()
    {
        // Named in Documentation/troubleshooting.md, so it is part of the contract with a user
        // following instructions rather than an internal detail.
        Assert.Equal("MLQT_LOG_CONSOLE", LoggingService.ConsoleLevelVariable);
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
