using System.Globalization;
using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MLQT.McpServer.Services;
using MLQT.Services.Interfaces;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace MLQT.McpServer.Tests;

/// <summary>
/// <see cref="MlqtMcpServerExtensions.AddMlqtMcpServer"/> as a host built on it sees it: a real server
/// and client over in-process pipes, so what is asserted is what goes over the protocol.
///
/// <para>Three things a host needs from it, each measured to fail without it: the services (tool calls
/// fail), the coercion filter (a quoted boolean fails with an opaque error) and the instructions
/// (initialize returns none).</para>
/// </summary>
public sealed class MlqtMcpServerExtensionsTests
{
    [Fact]
    public async Task ServesEveryMlqtTool()
    {
        await using var server = await InProcessServer.StartAsync();

        var listed = (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Select(t => t.Name).Order(StringComparer.Ordinal);

        Assert.Equal(ToolCountTests.DeclaredTools().Order(StringComparer.Ordinal), listed);
    }

    [Fact]
    public async Task AcceptsAQuotedBoolean()
    {
        await using var server = await InProcessServer.StartAsync();
        var directory = server.TempDirectory();

        var result = await server.CallAsync("mlqt_create_library",
            new() { ["name"] = "Lib", ["directory"] = directory, ["preview"] = "true" });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("package Lib", Text(result));
        // preview=true was honoured as true, not merely tolerated: nothing was written.
        Assert.False(Directory.Exists(Path.Combine(directory, "Lib")));
    }

    [Fact]
    public async Task CoercesTheHostsOwnTools()
    {
        await using var server = await InProcessServer.StartAsync(after: b => b.WithTools<HostTools>());

        var result = await server.CallAsync("host_echo", new() { ["flag"] = "true", ["count"] = "5", ["label"] = "7" });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("True:5:7", Text(result));
    }

    [Fact]
    public async Task AHostsFilterRunsInsideTheCoercion()
    {
        JsonValueKind? seen = null;
        await using var server = await InProcessServer.StartAsync(after: b => b.WithRequestFilters(f =>
            f.AddCallToolFilter(next => (context, ct) =>
            {
                if (context.Params?.Arguments?.TryGetValue("preview", out var preview) == true)
                    seen = preview.ValueKind;
                return next(context, ct);
            })));

        await server.CallAsync("mlqt_create_library",
            new() { ["name"] = "Lib", ["directory"] = server.TempDirectory(), ["preview"] = "true" });

        Assert.Equal(JsonValueKind.True, seen);
    }

    [Fact]
    public async Task SendsMlqtsInstructions()
    {
        await using var server = await InProcessServer.StartAsync();

        Assert.Equal(ServerInstructions.Text, server.Client.ServerInstructions);
    }

    [Fact]
    public async Task ConfigureServerRunsAfterTheInstructionsAreSet()
    {
        string? before = null;
        await using var server = await InProcessServer.StartAsync(new MlqtMcpServerOptions
        {
            ConfigureServer = o =>
            {
                before = o.ServerInstructions;
                o.ServerInstructions = "A pack server.";
            },
        });

        Assert.Equal(ServerInstructions.Text, before);
        Assert.Equal("A pack server.", server.Client.ServerInstructions);
    }

    [Fact]
    public async Task AServiceTheHostRegisteredFirstIsKept()
    {
        var settings = new InMemorySettingsService();
        await using var server = await InProcessServer.StartAsync(settings: settings);

        Assert.Same(settings, server.Services.GetRequiredService<ISettingsService>());
        Assert.Single(server.Services.GetServices<ISettingsService>());
    }

    internal static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
}

/// <summary>
/// What touches process-wide state - an environment variable, the NLog configuration, the default
/// culture - and so must not run beside anything else.
/// </summary>
[Collection(nameof(ProcessStateCollection))]
public sealed class MlqtMcpServerProcessStateTests
{
    private const string ToolLogVariable = "MLQT_MCP_TOOL_LOG";

    [Fact]
    public async Task WithoutALoggerNothingIsRecorded()
    {
        var log = Path.Combine(Path.GetTempPath(), $"mlqt-usage-{Guid.NewGuid():N}.jsonl");
        var previous = Environment.GetEnvironmentVariable(ToolLogVariable);
        Environment.SetEnvironmentVariable(ToolLogVariable, log); // would force a constructed logger on
        try
        {
            await using (var server = await InProcessServer.StartAsync())
                await server.CallAsync("mlqt_server_info", []);
            Assert.False(File.Exists(log), "a ToolUsageLogger was constructed with null options");

            // The control: the same variable with a logger passed does record, so the absence above
            // is the null options and not a variable the logger ignores.
            await using (var server = await InProcessServer.StartAsync(new MlqtMcpServerOptions { ToolUsageLogger = new ToolUsageLogger() }))
                await server.CallAsync("mlqt_server_info", []);
            Assert.Contains("mlqt_server_info", File.ReadAllText(log));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ToolLogVariable, previous);
            File.Delete(log);
        }
    }

    [Fact]
    public async Task TheNLogFileLogIsNotStarted()
    {
        await using (var server = await InProcessServer.StartAsync())
            await server.CallAsync("mlqt_server_info", []);

        Assert.Empty(NLog.LogManager.Configuration?.AllTargets ?? []);
    }

    [Fact]
    public void SetsTheInvariantCulture()
    {
        var previous = (CultureInfo.DefaultThreadCurrentCulture, CultureInfo.DefaultThreadCurrentUICulture);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("de-DE");
        try
        {
            new ServiceCollection().AddMlqtMcpServer();

            Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.DefaultThreadCurrentCulture);
            Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.DefaultThreadCurrentUICulture);
        }
        finally
        {
            (CultureInfo.DefaultThreadCurrentCulture, CultureInfo.DefaultThreadCurrentUICulture) = previous;
        }
    }
}

[CollectionDefinition(nameof(ProcessStateCollection), DisableParallelization = true)]
public sealed class ProcessStateCollection;

/// <summary>A tool of a host's own, with the scalar parameters a client might quote.</summary>
[McpServerToolType]
public sealed class HostTools
{
    [McpServerTool(Name = "host_echo"), Description("Echoes its arguments.")]
    public static string Echo(bool flag, int count, string label) => $"{flag}:{count}:{label}";
}

/// <summary>
/// A host built with <see cref="MlqtMcpServerExtensions.AddMlqtMcpServer"/> and a client connected to
/// it over in-process pipes. Settings are in memory unless a test says otherwise, so nothing is
/// written under the user's own MLQT folder.
/// </summary>
internal sealed class InProcessServer : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly List<string> _temp = [];

    private InProcessServer(IHost host, McpClient client)
    {
        _host = host;
        Client = client;
    }

    public McpClient Client { get; }
    public IServiceProvider Services => _host.Services;

    public static async Task<InProcessServer> StartAsync(
        MlqtMcpServerOptions? options = null,
        Action<IMcpServerBuilder>? after = null,
        ISettingsService? settings = null)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSingleton(settings ?? new InMemorySettingsService());
        var mcp = builder.Services.AddMlqtMcpServer(options)
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
        after?.Invoke(mcp);

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
            cancellationToken: TestContext.Current.CancellationToken);
        return new InProcessServer(host, client);
    }

    public Task<CallToolResult> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        Client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken).AsTask();

    public string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mlqt-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _temp.Add(path);
        return path;
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
        foreach (var path in _temp)
            try { Directory.Delete(path, recursive: true); } catch (IOException) { }
    }
}
