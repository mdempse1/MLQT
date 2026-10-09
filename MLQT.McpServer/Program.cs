using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MLQT.McpServer.Services;
using MLQT.Services;
using MLQT.Services.Checking;
using MLQT.Services.Interfaces;

// Modelica source and the VCS protocols are culture-invariant: the decimal separator is always
// '.', and ',' is never a thousands separator. Default every thread to the invariant culture so
// number parsing/formatting is never corrupted by the host machine's locale (as AddMlqtCore does
// for the desktop host).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

// The generic host's defaults, minus the appsettings.json file watcher — on Linux that watcher is an
// inotify instance the server never needed, and taking it is enough to kill the process at startup on
// a machine at the per-user limit (B163). See McpHostSettings.
var builder = new HostApplicationBuilder(McpHostSettings.Create(args));

// stdout is reserved for the MCP JSON-RPC stream; every log line MUST go to stderr or it will
// corrupt the protocol. Route the console logger to stderr for all levels.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// --- MLQT services (the desktop host's list, minus what only a desktop host needs) ---
// ISettingsService is replaced by the headless JSON-file implementation.
// IFilePickerService / IPowerManagementService need a window and are unused by the service layer,
// so they are intentionally omitted. Dymola/OpenModelica checking is out of scope for this server.
builder.Services.AddSingleton<ISettingsService, HeadlessSettingsService>();
builder.Services.AddSingleton<ILibraryDataService, LibraryDataService>();
builder.Services.AddSingleton<IFileMonitoringService, FileMonitoringService>();
builder.Services.AddSingleton<IRepositoryService, RepositoryService>();
builder.Services.AddSingleton<ICodeReviewService, CodeReviewService>();
builder.Services.AddSingleton<IBaselineStatusService, BaselineStatusService>();
builder.Services.AddSingleton<IStyleCheckingService, StyleCheckingService>();
builder.Services.AddSingleton<ICustomDictionaryService, CustomDictionaryService>();
builder.Services.AddSingleton<IDictionaryManagerService, DictionaryManagerService>();
builder.Services.AddSingleton<IImpactAnalysisService, ImpactAnalysisService>();
builder.Services.AddSingleton<IExternalResourceService, ExternalResourceService>();

// Tracks whether the opt-in analysis passes have run this session (see DependencyTools).
builder.Services.AddSingleton<MLQT.McpServer.Services.SessionState>();

// Records every tool call (name, args, duration, error) so tool usage can be reviewed. Off by default;
// enabled by creating a marker file (mcp-tool-logging.enabled) in %LocalAppData%/MLQT, which writes to
// %LocalAppData%/MLQT/mcp-tool-usage.jsonl. MLQT_MCP_TOOL_LOG overrides: a path forces it on, "off" forces
// it off.
var toolUsageLogger = new ToolUsageLogger();
builder.Services.AddSingleton(toolUsageLogger);

// Map of tool name -> which parameters are boolean/numeric, built by reflecting the tool method
// signatures. Used by the request filter below to coerce scalars that LLM clients commonly send as JSON
// strings ("standalone":"true", "count":"5") into the JSON type the parameter expects. Doing it here —
// rather than via a JSON converter — keeps each tool's input schema precise (a bool still advertises
// "type":"boolean"); a custom converter would make the schema exporter drop the type.
var toolScalarParameters = ToolArgumentCoercion.BuildParameterMap(System.Reflection.Assembly.GetExecutingAssembly());

// --- MCP server over stdio, tools discovered by attribute from this assembly ---
// The instructions are returned in the initialize response; see ServerInstructions for what they
// must fit in and why. mlqt_get_guidance gives fuller, on-demand recipes.
builder.Services
    .AddMcpServer(options => options.ServerInstructions = ServerInstructions.Text)
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
    {
        var toolName = context.Params?.Name ?? "(unknown)";

        // Coerce string-encoded booleans/numbers ("true", "5") to their JSON type before the SDK binds
        // the arguments, so an LLM that quotes a scalar gets the intended behaviour instead of an opaque
        // invocation error. Type-directed by the tool's own signature, so real string parameters are safe.
        if (context.Params is { Name: { } name, Arguments: { } args } &&
            toolScalarParameters.TryGetValue(name, out var scalars))
            context.Params.Arguments = ToolArgumentCoercion.Coerce(args, scalars);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await next(context, cancellationToken);
            toolUsageLogger.Record(toolName, context.Params?.Arguments, stopwatch.ElapsedMilliseconds, result.IsError ?? false);
            return result;
        }
        catch
        {
            toolUsageLogger.Record(toolName, context.Params?.Arguments, stopwatch.ElapsedMilliseconds, isError: true);
            throw;
        }
    }));

if (toolUsageLogger.LogPath is { } logPath)
    await Console.Error.WriteLineAsync($"[mcp] tool-usage log: {logPath}");
else
    await Console.Error.WriteLineAsync(
        $"[mcp] tool-usage logging is off; create the file '{toolUsageLogger.EnableMarkerPath}' to enable it.");

await builder.Build().RunAsync();
