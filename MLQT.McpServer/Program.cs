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
    MLQT manages Modelica libraries held in Git/SVN revision control. Use it to load libraries, browse
    and read classes, analyse dependencies and the impact of changes, check style and spelling, and
    format code.

    Getting started:
    - Load first. Call load_repository (a Git/SVN working copy or a directory of libraries) or
      load_library (one library directory containing package.mo, or a single .mo file). Almost every
      other tool operates on the loaded in-memory graph; list_libraries shows what is loaded.
    - Class ids are fully-qualified dotted names, e.g. Modelica.Blocks.Continuous.Integrator; use
      search_classes to find one. Library and repository ids are the GUIDs from list_libraries /
      list_repositories, but their names (e.g. "Modelica") also work.
    - Analysis is opt-in. get_dependencies, find_usages, analyze_impact, the external-resource tools
      and analyze_change_impact all require analyze_dependencies to have been run first (it can be slow
      on a large library). Style checking is opt-in via check_class / check_library; the rules come from
      each repository's .mlqt/settings.json (read with get_style_settings, change with set_style_settings).
    - Generic git/svn operations (commit, log, push, branch) are intentionally NOT provided — use your
      own CLI. The two VCS tools here map file changes to Modelica classes, which the CLI cannot do.
    - Tools that transform code: format_code and check_style are stateless; format_class and
      correct_spelling update the graph and write the .mo file (unless preview:true).

    Call get_guidance (optionally with a topic: workflows, dependencies, style, spelling, formatting,
    vcs, resources) for detailed, task-oriented recipes.
    """;

builder.Services
    .AddMcpServer(options => options.ServerInstructions = serverInstructions)
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
