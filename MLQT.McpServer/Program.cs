using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MLQT.McpServer;
using MLQT.McpServer.Services;

// The culture, MLQT's services, its tools, its instructions and the argument-coercion filter are
// AddMlqtMcpServer's, which another server can build on. What is here is what makes this one MLQT's
// stdio server.

// The generic host's defaults, minus the appsettings.json file watcher — on Linux that watcher is an
// inotify instance the server never needed, and taking it is enough to kill the process at startup on
// a machine at the per-user limit (B163). See McpHostSettings.
var builder = new HostApplicationBuilder(McpHostSettings.Create(args));

// stdout is reserved for the MCP JSON-RPC stream; every log line MUST go to stderr or it will
// corrupt the protocol. Route the console logger to stderr for all levels.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// Records every tool call (name, args, duration, error) so tool usage can be reviewed. Off by default;
// enabled by creating a marker file (mcp-tool-logging.enabled) in %LocalAppData%/MLQT, which writes to
// %LocalAppData%/MLQT/mcp-tool-usage.jsonl. MLQT_MCP_TOOL_LOG overrides: a path forces it on, "off" forces
// it off.
var toolUsageLogger = new ToolUsageLogger();

builder.Services
    .AddMlqtMcpServer(new MlqtMcpServerOptions { ToolUsageLogger = toolUsageLogger })
    .WithStdioServerTransport();

if (toolUsageLogger.LogPath is { } logPath)
    await Console.Error.WriteLineAsync($"[mcp] tool-usage log: {logPath}");
else
    await Console.Error.WriteLineAsync(
        $"[mcp] tool-usage logging is off; create the file '{toolUsageLogger.EnableMarkerPath}' to enable it.");

await builder.Build().RunAsync();
