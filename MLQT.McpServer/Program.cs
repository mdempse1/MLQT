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

// --- MCP server over stdio, tools discovered by attribute from this assembly ---
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
