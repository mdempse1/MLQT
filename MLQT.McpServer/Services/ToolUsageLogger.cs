using System.Text.Json;

namespace MLQT.McpServer.Services;

/// <summary>
/// Records every tool invocation (name, arguments, duration, error) to a JSON-lines file for debugging
/// and for reviewing how an agent uses the server — e.g. whether it prefers the compact views or keeps
/// pulling full source. One JSON object per line.
///
/// Logging is <b>off by default</b>. It is enabled by creating a marker file named
/// <see cref="EnableMarkerFileName"/> in the log directory (%LocalAppData%/MLQT) — the same directory the
/// application's NLog files are written to. When enabled the log is written to
/// %LocalAppData%/MLQT/mcp-tool-usage.jsonl.
///
/// The MLQT_MCP_TOOL_LOG environment variable overrides the marker file: set it to a path to force logging
/// on at that path (regardless of the marker), or to "off" to force it off. Logging never throws — a
/// failure here must not break a tool call. stdout is the protocol channel, so when logging is enabled a
/// short line is also written to stderr.
/// </summary>
public sealed class ToolUsageLogger
{
    /// <summary>
    /// Name of the marker file whose presence in the log directory turns tool-usage logging on. The file's
    /// contents are ignored — it only needs to exist.
    /// </summary>
    public const string EnableMarkerFileName = "mcp-tool-logging.enabled";

    private const string LogFileName = "mcp-tool-usage.jsonl";

    private readonly string _logDirectory;
    private readonly string? _path;
    private readonly object _gate = new();

    public ToolUsageLogger() : this(DefaultLogDirectory())
    {
    }

    /// <summary>
    /// Testable constructor: <paramref name="logDirectory"/> is where the marker file is looked for and the
    /// default log is written.
    /// </summary>
    public ToolUsageLogger(string logDirectory)
    {
        _logDirectory = logDirectory;

        var configured = Environment.GetEnvironmentVariable("MLQT_MCP_TOOL_LOG");

        // Explicit "off" wins over everything.
        if (string.Equals(configured, "off", StringComparison.OrdinalIgnoreCase))
            return;

        string path;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            // Explicit path override — logging is forced on at this path, marker file or not.
            path = configured;
        }
        else if (MarkerFileExists())
        {
            // Off by default: only enabled when the marker file is present in the log directory.
            path = Path.Combine(_logDirectory, LogFileName);
        }
        else
        {
            return; // disabled
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _path = path;
        }
        catch
        {
            _path = null; // can't create the directory — stay disabled
        }
    }

    /// <summary>Whether tool-usage logging is currently enabled.</summary>
    public bool IsEnabled => _path is not null;

    /// <summary>The active log file path, or null when logging is disabled/unavailable.</summary>
    public string? LogPath => _path;

    /// <summary>Full path of the marker file a user can create to enable logging.</summary>
    public string EnableMarkerPath => Path.Combine(_logDirectory, EnableMarkerFileName);

    public void Record(string tool, IEnumerable<KeyValuePair<string, JsonElement>>? arguments, long elapsedMs, bool isError)
    {
        if (_path is null)
            return; // logging disabled — no stderr, no file

        try
        {
            Console.Error.WriteLine($"[tool-usage] {tool} {elapsedMs}ms{(isError ? " ERROR" : "")}");
        }
        catch { /* ignore */ }

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

    private bool MarkerFileExists()
    {
        try
        {
            return File.Exists(Path.Combine(_logDirectory, EnableMarkerFileName));
        }
        catch
        {
            return false;
        }
    }

    private static string DefaultLogDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MLQT");

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
