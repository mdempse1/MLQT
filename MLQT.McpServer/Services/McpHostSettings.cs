using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MLQT.McpServer.Services;

/// <summary>
/// The settings the MCP host is built with: the generic host's defaults, minus the file watcher it
/// would otherwise put on <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> <c>Host.CreateApplicationBuilder</c> adds <c>appsettings.json</c> and
/// <c>appsettings.{Environment}.json</c> with <c>reloadOnChange: true</c>, which allocates a
/// <see cref="FileSystemWatcher"/> on the content root before a line of MLQT's own code runs. On Linux
/// each watcher costs one inotify <i>instance</i>, and the per-user limit is 128 by default — so when
/// the machine is at that limit the constructor throws
/// <c>IOException: The configured user limit (128) on the number of inotify instances has been
/// reached</c> straight out of <c>Main</c>. The process then exits before the stdio transport is up and
/// the client reports only "Server disconnected", with the real cause in the client's own log rather
/// than MLQT's.</para>
///
/// <para>That happened against a released build on 2026-09-17 (B163), with the desktop host holding 85
/// of the 128 instances. <b>The server neither reads nor reloads <c>appsettings.json</c></b>, so the
/// watcher bought nothing and cost the whole process. Worse, the content root is the client's working
/// directory — the watcher was on the user's home directory.</para>
///
/// <para><b>Why a seeded configuration rather than <c>DisableDefaults</c>.</b> Seeding the key the host
/// itself reads turns off exactly one default; <c>DisableDefaults</c> would also drop environment-
/// variable and command-line configuration, the default logging providers and the content root, none of
/// which are the problem. The host adds environment variables and command line <i>after</i> this source,
/// so <c>DOTNET_hostBuilder__reloadConfigOnChange=true</c> still turns watching back on for anyone who
/// wants it.</para>
/// </remarks>
public static class McpHostSettings
{
    /// <summary>
    /// The configuration key the generic host reads to decide whether to watch its configuration files.
    /// </summary>
    internal const string ReloadConfigOnChangeKey = "hostBuilder:reloadConfigOnChange";

    /// <summary>
    /// Builds the host settings for the MCP server, with configuration file watching turned off.
    /// </summary>
    public static HostApplicationBuilderSettings Create(string[] args)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ReloadConfigOnChangeKey] = "false"
        });

        return new HostApplicationBuilderSettings
        {
            Args = args,
            Configuration = configuration
        };
    }
}
