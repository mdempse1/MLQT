using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using DymolaInterface.Tests.Fakes;

namespace DymolaInterface.Tests;

/// <summary>
/// Tests for <see cref="DymolaInterface.CommandTimeout"/> and the per-call timeout: the limits
/// that let one long simulation run for hours while ordinary commands keep the five-minute
/// default, and that keep the connection probe out of that budget.
/// </summary>
public class CommandTimeoutTests
{
    private static HttpClient ClientOf(DymolaInterface dymola)
        => (HttpClient)typeof(DymolaInterface)
            .GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(dymola)!;

    [Fact]
    public void Constructor_BuildsAClientWithNoOverallTimeout()
    {
        using var dymola = new DymolaInterface(dymolaPath: string.Empty, portNumber: 1, hostname: "127.0.0.1");

        Assert.Equal(Timeout.InfiniteTimeSpan, ClientOf(dymola).Timeout);
    }

    [Fact]
    public void CommandTimeout_ByDefault_IsFiveMinutes()
    {
        using var h = new DymolaTestHarness();

        Assert.Equal(TimeSpan.FromMinutes(5), h.Dymola.CommandTimeout);
        Assert.Equal(DymolaInterface.DefaultCommandTimeout, h.Dymola.CommandTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void CommandTimeout_NotPositive_IsRejected(int seconds)
    {
        using var h = new DymolaTestHarness();

        Assert.Throws<ArgumentOutOfRangeException>(() => h.Dymola.CommandTimeout = TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void CommandTimeout_AboveWhatACancellationTokenSourceAccepts_IsRejected()
    {
        using var h = new DymolaTestHarness();

        Assert.Throws<ArgumentOutOfRangeException>(() => h.Dymola.CommandTimeout = TimeSpan.MaxValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => h.Dymola.CommandTimeout = TimeSpan.FromDays(60));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            h.Dymola.CommandTimeout = DymolaInterface.MaxCommandTimeout + TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task CommandTimeout_AtTheMaximum_IsAcceptedAndCommandsStillRun()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);

        h.Dymola.CommandTimeout = DymolaInterface.MaxCommandTimeout;

        Assert.Equal(DymolaInterface.MaxCommandTimeout, h.Dymola.CommandTimeout);
        Assert.True(await h.Dymola.ExecuteCommandAsync("command()"));
    }

    [Fact]
    public async Task CommandTimeout_Infinite_IsAcceptedAndCommandsStillRun()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);

        h.Dymola.CommandTimeout = Timeout.InfiniteTimeSpan;

        Assert.Equal(Timeout.InfiniteTimeSpan, h.Dymola.CommandTimeout);
        Assert.True(await h.Dymola.ExecuteCommandAsync("command()"));
    }

    [Fact]
    public async Task Command_SlowerThanTheCommandTimeout_GivesUpAtTheTimeout()
    {
        using var h = new DymolaTestHarness();
        h.Handler.ResponseDelay = TimeSpan.FromSeconds(20);
        h.Dymola.CommandTimeout = TimeSpan.FromMilliseconds(200);

        var elapsed = Stopwatch.StartNew();
        var ok = await h.Dymola.ExecuteCommandAsync("slowCommand()");
        elapsed.Stop();

        Assert.False(ok);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"gave up only after {elapsed.Elapsed}");
    }

    [Fact]
    public async Task Command_FasterThanTheCommandTimeout_Completes()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        h.Handler.ResponseDelay = TimeSpan.FromMilliseconds(100);
        h.Dymola.CommandTimeout = TimeSpan.FromSeconds(10);

        Assert.True(await h.Dymola.ExecuteCommandAsync("quickCommand()"));
    }

    /// <summary>
    /// The case a client-wide cap breaks: a command that outlives any such cap but stays inside
    /// its own budget. Fails if the client the interface builds - or the one the harness swaps
    /// in - ever regains an overall limit shorter than CommandTimeout.
    /// </summary>
    [Fact]
    public async Task Command_LongerThanAShortClientWideCapWouldAllow_Completes()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        h.Handler.ResponseDelay = TimeSpan.FromSeconds(3);
        h.Dymola.CommandTimeout = TimeSpan.FromSeconds(30);

        var elapsed = Stopwatch.StartNew();
        var ok = await h.Dymola.ExecuteCommandAsync("longCommand()");
        elapsed.Stop();

        Assert.True(ok, $"gave up after {elapsed.Elapsed}");
        Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(3), $"answered too early, after {elapsed.Elapsed}");
    }

    [Fact]
    public async Task CommandTimeout_ChangedBetweenCommands_AppliesToTheNextCommand()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        h.Handler.ResponseDelay = TimeSpan.FromMilliseconds(500);

        h.Dymola.CommandTimeout = TimeSpan.FromMilliseconds(100);
        var first = await h.Dymola.ExecuteCommandAsync("first()");
        h.Dymola.CommandTimeout = TimeSpan.FromSeconds(10);
        var second = await h.Dymola.ExecuteCommandAsync("second()");

        Assert.False(first);
        Assert.True(second);
    }

    [Fact]
    public async Task PerCallTimeout_ShorterThanTheProperty_BoundsThatCallAlone()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        h.Handler.ResponseDelay = TimeSpan.FromSeconds(20);
        h.Dymola.CommandTimeout = TimeSpan.FromSeconds(30);

        var elapsed = Stopwatch.StartNew();
        var ok = await h.Dymola.ExecuteCommandAsync("slowCommand()", TimeSpan.FromMilliseconds(200));
        elapsed.Stop();

        Assert.False(ok);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"gave up only after {elapsed.Elapsed}");
    }

    [Fact]
    public async Task PerCallTimeout_LongerThanTheProperty_LetsTheCallComplete()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        h.Handler.ResponseDelay = TimeSpan.FromMilliseconds(500);
        h.Dymola.CommandTimeout = TimeSpan.FromMilliseconds(100);

        Assert.True(await h.Dymola.ExecuteCommandAsync("longCommand()", TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task PerCallTimeout_DoesNotChangeTheProperty()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);

        await h.Dymola.ExecuteCommandAsync("command()", TimeSpan.FromSeconds(42));

        Assert.Equal(DymolaInterface.DefaultCommandTimeout, h.Dymola.CommandTimeout);
    }

    [Fact]
    public async Task PerCallTimeout_AboveWhatACancellationTokenSourceAccepts_IsRejected()
    {
        using var h = new DymolaTestHarness();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => h.Dymola.ExecuteCommandAsync("command()", TimeSpan.FromDays(60)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => h.Dymola.ExecuteCommandAsync("command()", TimeSpan.Zero));
    }

    /// <summary>
    /// Dymola carries on with a command whose call gave up, so its answer can arrive while a
    /// later command is waiting. Only the reply carrying this request's id is its result.
    /// </summary>
    [Fact]
    public async Task Command_AnsweredWithAnotherRequestsId_IsDiscarded()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        h.Handler.ResponseIdOverride = 9999;

        Assert.False(await h.Dymola.ExecuteCommandAsync("command()"));
    }

    /// <summary>
    /// The connection probe must not inherit the command budget: StartDymolaProcessAsync polls
    /// it up to thirty times while holding the command lock, so a long - or infinite - command
    /// budget would wedge the interface. Against a server that accepts the connection and never
    /// answers, construction has to give up in seconds.
    /// </summary>
    [Fact]
    public void Constructor_AgainstAServerThatAcceptsAndNeverAnswers_GivesUpQuickly()
    {
        using var server = new SilentTcpServer();

        var elapsed = Stopwatch.StartNew();
        using var dymola = new DymolaInterface(dymolaPath: string.Empty, portNumber: server.Port, hostname: "127.0.0.1");
        elapsed.Stop();

        Assert.True(dymola.IsOfflineMode());
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(15), $"the probe took {elapsed.Elapsed}");
    }

    /// <summary>Accepts connections and never sends a byte back.</summary>
    private sealed class SilentTcpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private readonly List<TcpClient> _accepted = new();

        public int Port { get; }

        public SilentTcpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptAsync);
        }

        private async Task AcceptAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                    lock (_accepted)
                        _accepted.Add(client);
                }
                catch
                {
                    return;
                }
            }
        }

        public void Dispose()
        {
            _stopping.Cancel();
            _listener.Stop();
            lock (_accepted)
                foreach (var client in _accepted)
                    client.Dispose();
            _stopping.Dispose();
        }
    }
}
