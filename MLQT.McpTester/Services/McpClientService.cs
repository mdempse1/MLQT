using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MLQT.McpTester.Services;

/// <summary>
/// Holds a single live connection to an MCP server (over stdio) and exposes its tools. Generic —
/// it can drive any MCP server, not just MLQT's.
/// </summary>
public sealed class McpClientService : IAsyncDisposable
{
    private McpClient? _client;

    public bool IsConnected => _client is not null;
    public string? Command { get; private set; }
    public IList<McpClientTool> Tools { get; private set; } = new List<McpClientTool>();

    /// <summary>The server's name/version and the instructions it returned in the initialize response
    /// (the text a client/LLM sees on connect describing what the server does).</summary>
    public string? ServerName { get; private set; }
    public string? ServerVersion { get; private set; }
    public string? ServerInstructions { get; private set; }

    /// <summary>Connect to (launch) a stdio MCP server. Replaces any existing connection.</summary>
    public async Task ConnectAsync(string command, string[] arguments, string? workingDirectory, CancellationToken ct = default)
    {
        await DisconnectAsync();

        var options = new StdioClientTransportOptions
        {
            Name = "mcp-tester",
            Command = command,
            Arguments = arguments,
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory))
            options.WorkingDirectory = workingDirectory;

        var transport = new StdioClientTransport(options);
        _client = await McpClient.CreateAsync(transport, cancellationToken: ct);
        var tools = await _client.ListToolsAsync(cancellationToken: ct);
        // Present tools alphabetically by name so they are easy to find (the server returns them in
        // discovery order, which is hard to scan).
        Tools = tools.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Command = command;
        ServerName = _client.ServerInfo?.Name;
        ServerVersion = _client.ServerInfo?.Version;
        ServerInstructions = _client.ServerInstructions;
    }

    /// <summary>Call a tool by name with the given arguments.</summary>
    public async Task<CallToolResult> CallToolAsync(string name, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct = default)
    {
        if (_client is null)
            throw new InvalidOperationException("Not connected to an MCP server.");
        return await _client.CallToolAsync(name, arguments, cancellationToken: ct);
    }

    public async Task DisconnectAsync()
    {
        if (_client is not null)
        {
            try { await _client.DisposeAsync(); }
            catch { /* ignore shutdown errors */ }
            _client = null;
        }
        Tools = new List<McpClientTool>();
        Command = null;
        ServerName = null;
        ServerVersion = null;
        ServerInstructions = null;
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
