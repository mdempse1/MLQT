using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MLQT.McpServer.Services;
using MLQT.Services;
using MLQT.Services.Interfaces;

// Modelica source and the VCS protocols are culture-invariant: the decimal separator is always
// '.', and ',' is never a thousands separator. Default every thread to the invariant culture so
// number parsing/formatting is never corrupted by the host machine's locale (mirrors MauiProgram).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = Host.CreateApplicationBuilder(args);

// stdout is reserved for the MCP JSON-RPC stream; every log line MUST go to stderr or it will
// corrupt the protocol. Route the console logger to stderr for all levels.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// --- MLQT services (mirrors MauiProgram.cs, minus the MAUI-only services) ---
// ISettingsService is replaced by the headless JSON-file implementation.
// IFilePickerService / IPowerManagementService are MAUI-only and unused by the service layer,
// so they are intentionally omitted. Dymola/OpenModelica checking is out of scope for this server.
builder.Services.AddSingleton<ISettingsService, HeadlessSettingsService>();
builder.Services.AddSingleton<ILibraryDataService, LibraryDataService>();
builder.Services.AddSingleton<IFileMonitoringService, FileMonitoringService>();
builder.Services.AddSingleton<IRepositoryService, RepositoryService>();
builder.Services.AddSingleton<ICodeReviewService, CodeReviewService>();
builder.Services.AddSingleton<IStyleCheckingService, StyleCheckingService>();
builder.Services.AddSingleton<ICustomDictionaryService, CustomDictionaryService>();
builder.Services.AddSingleton<IDictionaryManagerService, DictionaryManagerService>();
builder.Services.AddSingleton<IImpactAnalysisService, ImpactAnalysisService>();
builder.Services.AddSingleton<IExternalResourceService, ExternalResourceService>();

// Tracks whether the opt-in analysis passes have run this session (see DependencyTools).
builder.Services.AddSingleton<MLQT.McpServer.Services.SessionState>();

// --- MCP server over stdio, tools discovered by attribute from this assembly ---
// ServerInstructions is returned in the initialize response so the client/LLM knows, up front,
// what the server does and the key workflow. get_guidance gives fuller, on-demand recipes.
const string serverInstructions =
    """
    MLQT is a server for authoring, checking and formatting Modelica code. Use it to read and understand
    classes, edit them (create, modify, refactor), check them (style, spelling, references, connector
    compatibility), format them, and analyse dependencies and the impact of a change — working directly
    on the .mo files of a loaded library. The library may live in a Git/SVN working copy or a plain
    directory; generic version-control (commit, log, push, branch) is delegated to your own CLI (the two
    VCS tools here only add the Modelica-awareness a CLI lacks — mapping a diff to the classes it changed).

    Getting started:
    - Load first. Call load_repository (a Git/SVN working copy or a directory of libraries) or
      load_library (one library directory containing package.mo, or a single .mo file). To START a NEW
      project, create_library writes and loads an empty top-level library on disk. Almost every other
      tool operates on the loaded in-memory graph; list_libraries shows what is loaded.
    - Class ids are fully-qualified dotted names, e.g. Modelica.Blocks.Continuous.Integrator. Find one by
      name with search_classes, by documentation prose with search_text, or by shape with
      search_by_interface. Library and repository ids are the GUIDs from list_libraries /
      list_repositories, but their names (e.g. "Modelica") also work.
    - To learn a class without reading its source, prefer the compact "views": get_class_interface (its
      public parameters, connectors and, for functions, the signature — with inherited members merged in),
      list_class_elements (every declaration), get_class_documentation (its prose) and get_class_behavior
      (its equations/connections). validate_class_references flags referenced types that do not resolve.
      These need only a loaded library.
    - Analysis is opt-in. get_dependencies, find_usages, analyze_impact, the external-resource tools
      and analyze_change_impact all require analyze_dependencies to have been run first (it can be slow
      on a large library). Style checking is opt-in via check_class / check_library; the rules come from
      each repository's .mlqt/settings.json (read with get_style_settings, change with set_style_settings).
    - Authoring tools that change code on disk (all support preview:true and refresh dependencies):
      * Whole-class: create_class (add a class, placed standalone or nested), update_class_source (replace a
        class's body, same name), rename_class (rename + rewrite every resolved reference), move_class (move
        to a new parent + re-qualify references), delete_class (remove a class, reports dangling references).
      * Element-level (surgical, no need to resend the whole class): add_component / remove_component /
        set_component_modifier, add_extends, add_import, add_equation, add_statement (algorithm),
        add_connection / remove_connection / list_connections. add_connection refuses incompatible connectors.
        The add_* tools take an optional comment (a // line above the element).
      * Documentation: set_class_description, set_component_description (the "..." strings) and
        set_class_documentation (the Documentation(info/revisions) HTML); read with get_class_documentation.
      batch_edit applies a sequence of the element-level ops atomically; set_component_placement positions
      components in the diagram. rename_class and move_class (which also handle whole directory packages)
      need analyze_dependencies. format_code/check_style are stateless; format_class and correct_spelling
      reformat/fix in place. Writes to read-only files (e.g. a reference library under Program Files) are
      refused. After an external change (a manual edit or VCS pull), reload re-reads from disk.

    Call get_guidance (optionally with a topic: workflows, views, editing, dependencies, style, spelling,
    formatting, vcs, resources) for detailed, task-oriented recipes. The 'editing' topic covers all the
    tools that change code.
    """;

// Records every tool call (name, args, duration, error) so tool usage can be reviewed. Writes to
// %LocalAppData%/MLQT/mcp-tool-usage.jsonl (override with MLQT_MCP_TOOL_LOG; "off" to disable).
var toolUsageLogger = new ToolUsageLogger();
builder.Services.AddSingleton(toolUsageLogger);

builder.Services
    .AddMcpServer(options => options.ServerInstructions = serverInstructions)
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
    {
        var toolName = context.Params?.Name ?? "(unknown)";
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

await builder.Build().RunAsync();
