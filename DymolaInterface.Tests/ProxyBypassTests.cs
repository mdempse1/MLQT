using System.Net;
using Xunit;

namespace DymolaInterface.Tests;

/// <summary>
/// Nothing between MLQT and Dymola is a proxy's business. On a customer machine a corporate proxy
/// was handed the requests for 127.0.0.1, treated it as an uncategorised website and refused it, so
/// Dymola looked absent though it was running. These replace <see cref="HttpClient.DefaultProxy"/>
/// with one that claims every address and leads nowhere, then ask a stand-in Dymola: it is reached
/// only if the client ignores the proxy.
///
/// <para>The default proxy is process-wide, hence a collection of its own that runs alone.</para>
/// </summary>
[Collection("Default proxy")]
public class ProxyBypassTests
{
    [Fact]
    public async Task APingIgnoresTheDefaultProxy()
    {
        using var server = new StubServer();
        using (new ProxyEverything())
        {
            Assert.True(await DymolaInterface.PingAsync(server.Port, "127.0.0.1"),
                "the ping went to the proxy instead of to Dymola");
        }
    }

    [Fact]
    public async Task ASessionIgnoresTheDefaultProxy()
    {
        using var server = new StubServer();
        using (new ProxyEverything())
        {
            // The constructor's probe and every command after it share one client.
            using var dymola = new DymolaInterface("", server.Port, "127.0.0.1", TimeSpan.Zero);
            Assert.False(dymola.IsOfflineMode(), "the constructor's probe went to the proxy");
            Assert.True(await dymola.IsAliveAsync());
        }
    }

    /// <summary>
    /// Installs a default proxy that bypasses nothing, not even loopback, and points at a port
    /// nothing listens on; restores the previous one when disposed.
    /// </summary>
    private sealed class ProxyEverything : IWebProxy, IDisposable
    {
        private readonly IWebProxy _previous = HttpClient.DefaultProxy;

        public ProxyEverything() => HttpClient.DefaultProxy = this;

        public ICredentials? Credentials { get; set; }

        public Uri GetProxy(Uri destination) => new("http://127.0.0.1:9/");

        public bool IsBypassed(Uri host) => false;

        public void Dispose() => HttpClient.DefaultProxy = _previous;
    }

    /// <summary>A stand-in for Dymola's JSON-RPC server: answers 200 to anything.</summary>
    private sealed class StubServer : IDisposable
    {
        private readonly HttpListener _listener = new();

        public StubServer()
        {
            Port = FreePort();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
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

        private static int FreePort()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }
    }
}

[CollectionDefinition("Default proxy", DisableParallelization = true)]
public class DefaultProxyCollection;
