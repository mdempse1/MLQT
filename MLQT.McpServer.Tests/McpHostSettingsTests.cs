using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MLQT.McpServer.Services;

namespace MLQT.McpServer.Tests;

/// <summary>
/// The MCP host must not watch its configuration files.
/// </summary>
/// <remarks>
/// <para>Every <c>reloadOnChange</c> configuration source costs one inotify instance on Linux, taken
/// inside the host builder's constructor before any MLQT code runs. The per-user limit is 128, and on a
/// machine at that limit the constructor throws straight out of <c>Main</c>: the process dies before the
/// stdio transport exists, so the client reports nothing more useful than "Server disconnected" (B163).
/// The server has never read <c>appsettings.json</c>, let alone wanted it reloaded.</para>
///
/// <para>Asserted against <c>ReloadOnChange</c> on the sources the builder actually ends up with, rather
/// than against the key that sets it: the key is how it is turned off today, and the thing that must
/// stay true is that no file source arrives asking to be watched — including one somebody adds later.</para>
/// </remarks>
public class McpHostSettingsTests
{
    [Fact]
    public void NoConfigurationFileIsWatched()
    {
        var builder = new HostApplicationBuilder(McpHostSettings.Create([]));

        var watched = ((IConfigurationBuilder)builder.Configuration).Sources
            .OfType<FileConfigurationSource>()
            .Where(source => source.ReloadOnChange)
            .Select(source => source.Path ?? "(no path)")
            .ToList();

        Assert.Empty(watched);
    }

    [Fact]
    public void TheDefaultHostDoesWatchThem_WhichIsWhatThisIsFor()
    {
        // The other half of the claim. Without this, the test above passes just as well against a host
        // that stopped adding appsettings.json at all, and would go on passing if the default changed.
        var builder = Host.CreateApplicationBuilder([]);

        Assert.Contains(
            ((IConfigurationBuilder)builder.Configuration).Sources.OfType<FileConfigurationSource>(),
            source => source.ReloadOnChange);
    }

    [Fact]
    public void TheHostDefaultsAreOtherwiseIntact()
    {
        // DisableDefaults would also have fixed the crash, by dropping environment variables, command
        // line, logging and the content root along with it. This checks the narrower choice held: the
        // command line still reaches configuration.
        var builder = new HostApplicationBuilder(McpHostSettings.Create(["--mlqt-test-key=value"]));

        Assert.Equal("value", builder.Configuration["mlqt-test-key"]);
    }
}
