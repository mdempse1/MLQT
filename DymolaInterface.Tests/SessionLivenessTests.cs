using Xunit;

namespace DymolaInterface.Tests;

/// <summary>
/// B171 — a session that has gone is not handed back.
///
/// <para><b>What was wrong.</b> The first check worked; closing Dymola's window and checking again
/// did not. Closing the window ends the process and its JSON-RPC server, but the object holding the
/// connection knew nothing about it — <c>IsOfflineMode</c> is a flag set at construction, not a
/// probe — and the factory returned the cached instance without asking.</para>
///
/// <para><b>These run without Dymola</b>, which is the point: they exercise the two answers that do
/// not need it — a port nothing is listening on, and a disposed session. The case that needs the
/// tool is the one the package says to verify by hand, because neither suite here runs in any CI
/// job.</para>
/// </summary>
public class SessionLivenessTests
{
    /// <summary>A port nothing is expected to be listening on, as the suite's other tests use.</summary>
    private const int DeadPort = 9999;

    [Fact]
    public async Task ASessionWithNothingListeningIsNotAlive()
    {
        // The shape of the reported defect: the server is gone and the object does not know.
        using var dymola = new DymolaInterface("", DeadPort, "127.0.0.1");

        Assert.False(await dymola.IsAliveAsync());
    }

    [Fact]
    public async Task ADisposedSessionIsNotAlive()
    {
        // Asked after the factory has dropped one, so it must answer rather than throw.
        var dymola = new DymolaInterface("", DeadPort, "127.0.0.1");
        dymola.Dispose();

        Assert.False(await dymola.IsAliveAsync());
    }

    [Fact]
    public async Task AnOfflineSessionIsNotAlive()
    {
        using var dymola = new DymolaInterface("", DeadPort, "127.0.0.1");
        dymola.SetOfflineMode(true);

        Assert.False(await dymola.IsAliveAsync());
    }

    [Fact]
    public async Task TheProbeIsQuick()
    {
        // The whole reason for a separate probe. The command client has a 300-second timeout, and
        // asking "are you there" through that would hang the check it was meant to rescue.
        using var dymola = new DymolaInterface("", DeadPort, "127.0.0.1");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await dymola.IsAliveAsync();
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10),
            $"the liveness probe took {clock.Elapsed.TotalSeconds:F1}s; it must not wait on the "
            + "command timeout");
    }

    /// <summary>
    /// A stand-in for Dymola's JSON-RPC server: answers 200 to anything until it is stopped.
    /// </summary>
    private sealed class StubServer : IDisposable
    {
        private readonly System.Net.HttpListener _listener = new();

        public StubServer(int port)
        {
            Port = port;
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    try
                    {
                        var context = await _listener.GetContextAsync();
                        context.Response.StatusCode = 200;
                        context.Response.Close();
                    }
                    catch { return; }   // stopped
                }
            });
        }

        public int Port { get; }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
        }
    }

    /// <summary>A port this machine is not using, found by asking the OS for one and letting it go.</summary>
    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Fact]
    public async Task ASessionThatWasAliveAndIsNotAnyMoreSaysSo()
    {
        // **The reported defect, as closely as it can be had without Dymola.** Connect to something
        // answering, then take it away: the object is not offline, it has no process handle to
        // check, and the only thing that can tell is asking over the wire. Without the ping this
        // passes for alive and every check after the window closed fails (B171).
        var port = FreePort();
        DymolaInterface dymola;

        using (var server = new StubServer(port))
        {
            dymola = new DymolaInterface("", port, "127.0.0.1");
            Assert.False(dymola.IsOfflineMode(), "the stub should have answered the constructor's ping");
            Assert.True(await dymola.IsAliveAsync());
        }

        // The window has been closed.
        Assert.False(await dymola.IsAliveAsync());
        dymola.Dispose();
    }
}
