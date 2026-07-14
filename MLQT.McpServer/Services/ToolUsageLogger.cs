using System.Text.Json;

namespace MLQT.McpServer.Services;

/// <summary>
/// Records every tool invocation (name, arguments, duration, error) to a JSON-lines file for debugging
/// and for reviewing how an agent uses the server — e.g. whether it prefers the compact views or keeps
/// pulling full source. One JSON object per line. The file is %LocalAppData%/MLQT/mcp-tool-usage.jsonl,
/// overridable with the MLQT_MCP_TOOL_LOG environment variable (set it to "off" to disable). Logging
/// never throws — a failure here must not break a tool call. stdout is the protocol channel, so a short
/// line is also written to stderr.
/// </summary>
public sealed class ToolUsageLogger
{
    private readonly string? _path;
    private readonly object _gate = new();

    public ToolUsageLogger()
    {
        var configured = Environment.GetEnvironmentVariable("MLQT_MCP_TOOL_LOG");
        if (string.Equals(configured, "off", StringComparison.OrdinalIgnoreCase))
            return; // disabled

        _path = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MLQT", "mcp-tool-usage.jsonl");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        }
        catch
        {
            _path = null; // can't create the directory — fall back to stderr only
        }
    }

    /// <summary>The active log file path, or null when logging to file is disabled/unavailable.</summary>
    public string? LogPath => _path;

    public void Record(string tool, IEnumerable<KeyValuePair<string, JsonElement>>? arguments, long elapsedMs, bool isError)
    {
        try
        {
            Console.Error.WriteLine($"[tool-usage] {tool} {elapsedMs}ms{(isError ? " ERROR" : "")}");
        }
        catch { /* ignore */ }

        if (_path is null)
            return;

        string line;
        try
        {
            var entry = new Dictionary<string, object?>
            {
                ["ts"] = DateTime.UtcNow.ToString("o"),
                ["tool"] = tool,
                ["ms"] = elapsedMs,
                ["error"] = isError,
                ["args"] = SummarizeArgs(arguments),
            };
            line = JsonSerializer.Serialize(entry);
        }
        catch
        {
            line = $"{{\"ts\":\"{DateTime.UtcNow:o}\",\"tool\":\"{tool}\",\"ms\":{elapsedMs},\"error\":{isError.ToString().ToLowerInvariant()}}}";
        }

        try
        {
            lock (_gate)
                File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch { /* logging must never break a tool call */ }
    }

    // Keep argument values readable but bounded — large source strings are truncated.
    private static Dictionary<string, string>? SummarizeArgs(IEnumerable<KeyValuePair<string, JsonElement>>? arguments)
    {
        if (arguments is null)
            return null;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
        {
            var text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
            if (text.Length > 200)
                text = text[..200] + "…";
            result[key] = text;
        }
        return result.Count == 0 ? null : result;
    }
}
