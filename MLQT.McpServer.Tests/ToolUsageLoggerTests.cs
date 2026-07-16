using MLQT.McpServer.Services;

namespace MLQT.McpServer.Tests;

/// <summary>
/// Tests for tool-usage logging enablement: off by default, on when the marker file is present, with the
/// MLQT_MCP_TOOL_LOG environment variable overriding the marker.
/// </summary>
public sealed class ToolUsageLoggerTests : IDisposable
{
    private const string EnvVar = "MLQT_MCP_TOOL_LOG";

    private readonly string _dir;
    private readonly string? _originalEnv;

    public ToolUsageLoggerTests()
    {
        // Each test gets a fresh, isolated log directory.
        _dir = Path.Combine(Path.GetTempPath(), "mlqt-tul-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        // Snapshot the env var and clear it so the marker file drives behaviour unless a test sets it.
        _originalEnv = Environment.GetEnvironmentVariable(EnvVar);
        Environment.SetEnvironmentVariable(EnvVar, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, _originalEnv);
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string MarkerPath => Path.Combine(_dir, ToolUsageLogger.EnableMarkerFileName);
    private string DefaultLogPath => Path.Combine(_dir, "mcp-tool-usage.jsonl");

    [Fact]
    public void Disabled_ByDefault_WhenNoMarkerAndNoEnv()
    {
        var logger = new ToolUsageLogger(_dir);

        Assert.False(logger.IsEnabled);
        Assert.Null(logger.LogPath);
    }

    [Fact]
    public void Enabled_WhenMarkerFilePresent()
    {
        File.WriteAllText(MarkerPath, "");

        var logger = new ToolUsageLogger(_dir);

        Assert.True(logger.IsEnabled);
        Assert.Equal(DefaultLogPath, logger.LogPath);
    }

    [Fact]
    public void Record_WhenEnabled_WritesLine()
    {
        File.WriteAllText(MarkerPath, "");
        var logger = new ToolUsageLogger(_dir);

        logger.Record("get_class_info", null, elapsedMs: 12, isError: false);

        Assert.True(File.Exists(DefaultLogPath));
        var content = File.ReadAllText(DefaultLogPath);
        Assert.Contains("get_class_info", content);
    }

    [Fact]
    public void Record_WhenDisabled_WritesNothing()
    {
        var logger = new ToolUsageLogger(_dir);

        logger.Record("get_class_info", null, elapsedMs: 12, isError: false);

        Assert.False(File.Exists(DefaultLogPath));
    }

    [Fact]
    public void EnvOff_ForcesDisabled_EvenWithMarkerPresent()
    {
        File.WriteAllText(MarkerPath, "");
        Environment.SetEnvironmentVariable(EnvVar, "off");

        var logger = new ToolUsageLogger(_dir);

        Assert.False(logger.IsEnabled);
    }

    [Fact]
    public void EnvPath_ForcesEnabled_AtThatPath_WithoutMarker()
    {
        var custom = Path.Combine(_dir, "custom", "usage.jsonl");
        Environment.SetEnvironmentVariable(EnvVar, custom);

        var logger = new ToolUsageLogger(_dir);

        Assert.True(logger.IsEnabled);
        Assert.Equal(custom, logger.LogPath);

        logger.Record("t", null, 1, false);
        Assert.True(File.Exists(custom));
    }

    [Fact]
    public void EnableMarkerPath_IsInLogDirectory()
    {
        var logger = new ToolUsageLogger(_dir);

        Assert.Equal(MarkerPath, logger.EnableMarkerPath);
    }
}
