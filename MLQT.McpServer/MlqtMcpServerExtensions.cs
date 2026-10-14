using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MLQT.McpServer.Services;
using MLQT.McpServer.Tools;
using MLQT.Services;
using MLQT.Services.Checking;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer;

/// <summary>
/// MLQT's MCP server as a set of registrations, so another server can be built on it without copying
/// it: MLQT's own <c>Program.cs</c> is this plus a stdio transport, and a host serving tools of its own
/// adds them to the builder this returns.
/// </summary>
/// <remarks>
/// <para><b>This and <see cref="MlqtMcpServerOptions"/> are the supported surface</b> of this assembly
/// for a host built on it, with the tool names, which <c>ToolNamingTests</c> holds. The tool classes and
/// their result types are public because the SDK needs them to be, not as a promise.
/// <c>MlqtMcpServerSurfaceTests</c> holds their members and those of the <see cref="ToolUsageLogger"/>
/// the options name, in place of the public-API analyzer, which cannot track a few types of an assembly
/// without listing every other.</para>
/// <para><b>What it deliberately leaves to the host</b>: the transport, logging providers, the NLog file
/// log (MLQT's server never starts it), and the <see cref="ToolUsageLogger"/>, which reads a marker file
/// from MLQT's folder when it is constructed.</para>
/// </remarks>
public static class MlqtMcpServerExtensions
{
    /// <summary>
    /// Registers MLQT's services, its tools, its instructions and the call filter that turns a quoted
    /// boolean or number into the scalar the tool expects. Call <c>WithStdioServerTransport()</c>, or
    /// another transport, on the result.
    /// </summary>
    /// <remarks>
    /// <para>Every service is registered with <c>TryAdd</c>, so a host that registers its own first keeps
    /// it: an in-memory <see cref="ISettingsService"/>, say, where MLQT's writes <c>mcp-settings.json</c>
    /// under its own folder.</para>
    /// <para>The coercion filter is added first, which makes it the outermost: a call filter the host
    /// adds afterwards sees the arguments already coerced. It works from each tool's input schema, so it
    /// covers the host's tools as well as MLQT's.</para>
    /// </remarks>
    public static IMcpServerBuilder AddMlqtMcpServer(this IServiceCollection services, MlqtMcpServerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Process-wide, deliberately: it reaches every thread of the host that sets no culture of its
        // own, which the README says, so a host formatting for its users sets that culture itself.
        // Modelica source and the VCS protocols are culture-invariant: the decimal separator is always
        // '.', and ',' is never a thousands separator. Default every thread to the invariant culture so
        // number parsing/formatting is never corrupted by the host machine's locale, as AddMlqtCore does
        // for the desktop host. Here rather than in a host, because a host that forgot it would be wrong
        // only on a machine whose locale writes 1,5.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        // The desktop host's list, minus what only a desktop host needs. ISettingsService is the headless
        // JSON-file implementation; IFilePickerService / IPowerManagementService need a window and are
        // unused by the service layer, so they are omitted. Dymola/OpenModelica checking is out of scope
        // for this server.
        services.TryAddSingleton<ISettingsService, HeadlessSettingsService>();
        services.TryAddSingleton<ILibraryDataService, LibraryDataService>();
        services.TryAddSingleton<IFileMonitoringService, FileMonitoringService>();
        services.TryAddSingleton<IRepositoryService, RepositoryService>();
        services.TryAddSingleton<ICodeReviewService, CodeReviewService>();
        services.TryAddSingleton<IBaselineStatusService, BaselineStatusService>();
        services.TryAddSingleton<IStyleCheckingService, StyleCheckingService>();
        services.TryAddSingleton<ICustomDictionaryService, CustomDictionaryService>();
        services.TryAddSingleton<IDictionaryManagerService, DictionaryManagerService>();
        services.TryAddSingleton<IImpactAnalysisService, ImpactAnalysisService>();
        services.TryAddSingleton<IExternalResourceService, ExternalResourceService>();

        // Tracks whether the opt-in analysis passes have run this session (see DependencyTools).
        services.TryAddSingleton<SessionState>();

        // A build whose tool parameters were renamed would bind no client's arguments, silently; fail
        // here instead, saying why (B502).
        ToolParameterCheck.Verify(typeof(ViewTools).Assembly);

        var toolUsageLogger = options?.ToolUsageLogger;
        var configureServer = options?.ConfigureServer;

        // The instructions are returned in the initialize response; see ServerInstructions for what they
        // must fit in and why. mlqt_get_guidance gives fuller, on-demand recipes.
        return services
            .AddMcpServer(o =>
            {
                o.ServerInstructions = ServerInstructions.Text;
                configureServer?.Invoke(o);
            })
            .WithToolsFromAssembly(typeof(ViewTools).Assembly, McpServerJson.Options)
            .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                var toolName = context.Params?.Name ?? "(unknown)";

                // Coerce string-encoded booleans/numbers ("true", "5") to their JSON type before the SDK
                // binds the arguments, so an LLM that quotes a scalar gets the intended behaviour instead of
                // an opaque invocation error. Directed by the tool's own input schema, so real string
                // parameters are safe, and a host's tools are covered as well as MLQT's.
                if (context.Params is { Name: { } name, Arguments: { } args } &&
                    context.Server.ServerOptions.ToolCollection is { } tools &&
                    tools.TryGetPrimitive(name, out var tool) &&
                    ToolArgumentCoercion.For(tool) is { IsEmpty: false } scalars)
                    context.Params.Arguments = ToolArgumentCoercion.Coerce(args, scalars);

                if (toolUsageLogger is null)
                    return await next(context, cancellationToken);

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
    }
}
