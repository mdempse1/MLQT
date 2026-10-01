using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace OpenModelicaInterface.Tests;

/// <summary>
/// omc choosing its own port and MLQT finding it, against a real omc. The parsing half is
/// <see cref="OmcPortAnnouncementTests"/>.
/// </summary>
// Starts omc of its own rather than taking the fixture, so ToolTraitTests cannot see that it needs one.
[Trait("Requires", "OpenModelica")]
public class PortDiscoveryTests
{
    private static readonly string OmcPath = OpenModelicaSettings.FindInstalledOmc();
    private static CancellationToken Test => TestContext.Current.CancellationToken;

    private static OpenModelicaInterface Session(int port = OpenModelicaInterface.AnyPort) =>
        new(OmcPath, port) { StartupTimeout = TimeSpan.FromSeconds(30) };

    [Fact]
    public async Task AnyPort_IsFoundAndAnswers()
    {
        using var omc = Session();
        await omc.StartAsync(Test);

        Assert.True(omc.Port is > 0 and <= 65535, $"no port was found: {omc.Port}");
        Assert.False(string.IsNullOrEmpty(await omc.GetVersionAsync()));
    }

    /// <summary>The point of it: two sessions at once - the GUI's and the MCP server's - need no
    /// ports chosen for them to stay apart.</summary>
    [Fact]
    public async Task TwoSessionsAtOnce_TakeDifferentPorts()
    {
        using var first = Session();
        using var second = Session();
        await Task.WhenAll(first.StartAsync(Test), second.StartAsync(Test));

        Assert.NotEqual(first.Port, second.Port);
        Assert.False(string.IsNullOrEmpty(await first.GetVersionAsync()));
        Assert.False(string.IsNullOrEmpty(await second.GetVersionAsync()));
    }

    [Fact]
    public async Task AFixedPort_IsTheOneUsed()
    {
        var port = FreePort();
        using var omc = Session(port);
        await omc.StartAsync(Test);

        Assert.Equal(port, omc.Port);
    }

    /// <summary>
    /// omc says the port is taken and exits at once; that is reported then, in omc's words, rather
    /// than as a start that ran out of time.
    /// </summary>
    [Fact]
    public async Task AFixedPortAlreadyTaken_IsReportedAtOnce()
    {
        using var holder = new TcpListener(IPAddress.Loopback, 0);
        holder.Start();
        var taken = ((IPEndPoint)holder.LocalEndpoint).Port;

        using var omc = Session(taken);
        var clock = Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<OpenModelicaExitedException>(() => omc.StartAsync(Test));
        clock.Stop();

        Assert.Contains("address in use", failure.Message);
        Assert.False(omc.IsConnected);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"waited {clock.Elapsed} for an omc that had gone");
    }

    [Fact]
    public async Task ThePortFileIsNotLeftBehind()
    {
        var before = PortFiles();
        using (var omc = Session())
        {
            await omc.StartAsync(Test);
        }

        Assert.Empty(PortFiles().Except(before));
    }

    [Fact]
    public async Task AClosedSession_HasNoPort()
    {
        var omc = Session();
        await omc.StartAsync(Test);
        omc.Dispose();

        Assert.Null(omc.Port);
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    /// <summary>Port files of MLQT's sessions, wherever omc writes them on this platform.</summary>
    private static string[] PortFiles()
    {
        var folders = new[] { Path.GetTempPath(), "/tmp" }.Where(Directory.Exists).Distinct();
        return folders.SelectMany(f => Directory.GetFiles(f, "openmodelica.*mlqt-*")).ToArray();
    }
}
