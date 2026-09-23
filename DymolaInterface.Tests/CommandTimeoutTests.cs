using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DymolaInterface.Tests.Fakes;

namespace DymolaInterface.Tests;

/// <summary>
/// Tests for <see cref="DymolaInterface.CommandTimeout"/>, the per-call timeout and cancellation,
/// and the connection window: the limits that let one long simulation run for hours while
/// ordinary commands keep the five-minute default, and that keep a busy Dymola from being
/// mistaken for an absent one.
/// </summary>
public class CommandTimeoutTests
{
    private static CancellationToken Test => TestContext.Current.CancellationToken;

    private static HttpClient ClientOf(DymolaInterface dymola)
        => (HttpClient)typeof(DymolaInterface)
            .GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(dymola)!;

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

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
        Assert.True(await h.Dymola.ExecuteCommandAsync("command()", cancellationToken: Test));
    }

    [Fact]
    public async Task CommandTimeout_Infinite_IsAcceptedAndCommandsStillRun()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);

        h.Dymola.CommandTimeout = Timeout.InfiniteTimeSpan;

        Assert.Equal(Timeout.InfiniteTimeSpan, h.Dymola.CommandTimeout);
        Assert.True(await h.Dymola.ExecuteCommandAsync("command()", cancellationToken: Test));
    }

    [Fact]
    public async Task Command_SlowerThanTheCommandTimeout_GivesUpAtTheTimeout()
    {
        using var h = new DymolaTestHarness();
        h.Handler.ResponseDelay = TimeSpan.FromSeconds(20);
        h.Dymola.CommandTimeout = TimeSpan.FromMilliseconds(200);

        var elapsed = Stopwatch.StartNew();
        var ok = await h.Dymola.ExecuteCommandAsync("slowCommand()", cancellationToken: Test);
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

        Assert.True(await h.Dymola.ExecuteCommandAsync("quickCommand()", cancellationToken: Test));
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
        var ok = await h.Dymola.ExecuteCommandAsync("longCommand()", cancellationToken: Test);
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
        var first = await h.Dymola.ExecuteCommandAsync("first()", cancellationToken: Test);
        h.Dymola.CommandTimeout = TimeSpan.FromSeconds(10);
        var second = await h.Dymola.ExecuteCommandAsync("second()", cancellationToken: Test);

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
        var ok = await h.Dymola.ExecuteCommandAsync("slowCommand()", TimeSpan.FromMilliseconds(200), Test);
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

        Assert.True(await h.Dymola.ExecuteCommandAsync("longCommand()", TimeSpan.FromSeconds(10), Test));
    }

    [Fact]
    public async Task PerCallTimeout_DoesNotChangeTheProperty()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);

        await h.Dymola.ExecuteCommandAsync("command()", TimeSpan.FromSeconds(42), Test);

        Assert.Equal(DymolaInterface.DefaultCommandTimeout, h.Dymola.CommandTimeout);
    }

    [Fact]
    public async Task PerCallTimeout_AboveWhatACancellationTokenSourceAccepts_IsRejected()
    {
        using var h = new DymolaTestHarness();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => h.Dymola.ExecuteCommandAsync("command()", TimeSpan.FromDays(60), Test));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => h.Dymola.ExecuteCommandAsync("command()", TimeSpan.Zero, Test));
    }

    /// <summary>
    /// The argument check does not depend on whether a Dymola happens to be connected: an
    /// invalid timeout is the caller's mistake either way.
    /// </summary>
    [Fact]
    public async Task PerCallTimeout_OutOfRange_IsRejectedEvenWhileOffline()
    {
        using var h = new DymolaTestHarness();
        h.Dymola.SetOfflineMode(true);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => h.Dymola.ExecuteCommandAsync("command()", TimeSpan.FromDays(60), Test));
    }

    [Fact]
    public async Task PerCallTimeout_ReachesTheMultiRunSimulation()
    {
        using var h = new DymolaTestHarness();
        h.Handler.ResponseDelay = TimeSpan.FromSeconds(20);

        var elapsed = Stopwatch.StartNew();
        var result = await h.Dymola.SimulateMultiResultsModelAsync("M", 0.0, 1.0, 0, 0.0, "Dassl", 1e-4, 0.0,
            "dsres", new[] { "p" }, new[] { new[] { 1.0 } }, new[] { "r" }, new[] { "f1" }, true,
            TimeSpan.FromMilliseconds(200), Test);
        elapsed.Stop();

        Assert.Null(result);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"gave up only after {elapsed.Elapsed}");
    }

    [Fact]
    public async Task PerCallTimeout_ReachesOpenModel()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        h.Handler.ResponseDelay = TimeSpan.FromSeconds(20);

        var elapsed = Stopwatch.StartNew();
        var ok = await h.Dymola.OpenModelAsync("Large.mo", timeout: TimeSpan.FromMilliseconds(200), cancellationToken: Test);
        elapsed.Stop();

        Assert.False(ok);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"gave up only after {elapsed.Elapsed}");
    }

    [Fact]
    public async Task OpenModel_CancelledWhileInFlight_ReturnsFalsePromptly()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        h.Handler.ResponseDelay = TimeSpan.FromSeconds(20);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var elapsed = Stopwatch.StartNew();
        var ok = await h.Dymola.OpenModelAsync("Large.mo", cancellationToken: cancel.Token);
        elapsed.Stop();

        Assert.False(ok);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"gave up only after {elapsed.Elapsed}");
    }

    [Fact]
    public async Task PerCallTimeout_ReachesSaveTotalModel()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        h.Handler.ResponseDelay = TimeSpan.FromSeconds(20);

        var elapsed = Stopwatch.StartNew();
        var ok = await h.Dymola.SaveTotalModelAsync("Total.mo", "M", timeout: TimeSpan.FromMilliseconds(200), cancellationToken: Test);
        elapsed.Stop();

        Assert.False(ok);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"gave up only after {elapsed.Elapsed}");
    }

    [Fact]
    public async Task SaveTotalModel_CancelledWhileInFlight_ReturnsFalsePromptly()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        h.Handler.ResponseDelay = TimeSpan.FromSeconds(20);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var elapsed = Stopwatch.StartNew();
        var ok = await h.Dymola.SaveTotalModelAsync("Total.mo", "M", cancellationToken: cancel.Token);
        elapsed.Stop();

        Assert.False(ok);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"gave up only after {elapsed.Elapsed}");
    }

    [Fact]
    public async Task Command_CancelledWhileInFlight_ReturnsFalsePromptly()
    {
        using var h = new DymolaTestHarness();
        h.Handler.ResponseDelay = TimeSpan.FromSeconds(20);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var elapsed = Stopwatch.StartNew();
        var ok = await h.Dymola.ExecuteCommandAsync("slowCommand()", cancellationToken: cancel.Token);
        elapsed.Stop();

        Assert.False(ok);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"gave up only after {elapsed.Elapsed}");
    }

    /// <summary>
    /// Cancelled while still queued behind another command, the call returns false like any
    /// other abandoned command - and is never sent at all.
    /// </summary>
    [Fact]
    public async Task Command_CancelledWhileQueuedBehindAnother_ReturnsFalseRatherThanThrowing()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        h.Handler.ResponseDelay = TimeSpan.FromSeconds(2);

        var first = h.Dymola.ExecuteCommandAsync("first()", cancellationToken: Test);
        await Task.Delay(200, Test);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var queued = await h.Dymola.ExecuteCommandAsync("queued()", cancellationToken: cancel.Token);

        Assert.False(queued);
        Assert.True(await first);
        Assert.DoesNotContain(h.Handler.Requests, r => r.Method == "queued()");
    }

    [Fact]
    public async Task Command_WithAnAlreadyCancelledToken_ReturnsFalse()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.False(await h.Dymola.ExecuteCommandAsync("command()", cancellationToken: cancel.Token));
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

        Assert.False(await h.Dymola.ExecuteCommandAsync("command()", cancellationToken: Test));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5000)]
    public void Constructor_NegativeConnectionWindow_IsRejected(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DymolaInterface(string.Empty, 1, "127.0.0.1", TimeSpan.FromMilliseconds(milliseconds)));
    }

    /// <summary>
    /// A refused connection means nothing is listening, so there is nothing to wait for. On
    /// Windows it takes about two seconds to fail - as long as the answer budget - which is
    /// why the probe makes the TCP connect on its own first: over HTTP alone it would read as
    /// a busy Dymola, and construction would sit out the whole window before every start.
    /// </summary>
    [Fact]
    public void Constructor_WhenNothingIsListening_DoesNotWaitOutTheWindow()
    {
        var port = FreePort();

        var elapsed = Stopwatch.StartNew();
        using var dymola = new DymolaInterface(string.Empty, port, "127.0.0.1", TimeSpan.FromSeconds(30));
        elapsed.Stop();

        Assert.True(dymola.IsOfflineMode());
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), $"construction took {elapsed.Elapsed}");
    }

    /// <summary>
    /// A busy Dymola answers nothing until its command finishes. Construction keeps asking until
    /// the window closes rather than giving up at the first silent probe - and gives up then,
    /// bounded, whatever CommandTimeout is.
    /// </summary>
    [Fact]
    public void Constructor_AgainstADymolaThatNeverAnswers_GivesUpWhenTheWindowCloses()
    {
        using var server = new BusyServer(TimeSpan.MaxValue);

        var elapsed = Stopwatch.StartNew();
        using var dymola = new DymolaInterface(string.Empty, server.Port, "127.0.0.1", TimeSpan.FromSeconds(3));
        elapsed.Stop();

        Assert.True(dymola.IsOfflineMode());
        Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(3), $"gave up after only {elapsed.Elapsed}");
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(15), $"the probe took {elapsed.Elapsed}");
    }

    [Fact]
    public void Constructor_AgainstADymolaBusyForLessThanTheWindow_ComesUpOnline()
    {
        using var server = new BusyServer(TimeSpan.FromSeconds(3));

        var elapsed = Stopwatch.StartNew();
        using var dymola = new DymolaInterface(string.Empty, server.Port, "127.0.0.1", TimeSpan.FromSeconds(20));
        elapsed.Stop();

        Assert.False(dymola.IsOfflineMode(), $"still offline after {elapsed.Elapsed}");
        Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(2), $"answered too early, after {elapsed.Elapsed}");
    }

    [Fact]
    public void Constructor_WithAZeroWindow_ProbesOnce()
    {
        using var server = new BusyServer(TimeSpan.MaxValue);

        var elapsed = Stopwatch.StartNew();
        using var dymola = new DymolaInterface(string.Empty, server.Port, "127.0.0.1", TimeSpan.Zero);
        elapsed.Stop();

        Assert.True(dymola.IsOfflineMode());
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(4), $"the probe took {elapsed.Elapsed}");
    }

    /// <summary>
    /// An interface that started offline because Dymola was busy is not written off: its next
    /// command asks again, and once Dymola answers, the command goes through.
    /// </summary>
    [Fact]
    public async Task Command_OnceADymolaBusyAtConstructionAnswers_ComesOnlineAndRuns()
    {
        using var server = new BusyServer(TimeSpan.FromSeconds(3));
        using var dymola = new DymolaInterface(string.Empty, server.Port, "127.0.0.1", TimeSpan.Zero);
        await Task.Delay(TimeSpan.FromSeconds(2), Test);

        var ok = await dymola.ExecuteCommandAsync("command()", cancellationToken: Test);

        Assert.True(ok);
        Assert.False(dymola.IsOfflineMode());
    }

    [Fact]
    public async Task Command_WhileDymolaIsStillBusy_GivesUpAfterOneProbe()
    {
        using var server = new BusyServer(TimeSpan.MaxValue);
        using var dymola = new DymolaInterface(string.Empty, server.Port, "127.0.0.1", TimeSpan.Zero);

        var elapsed = Stopwatch.StartNew();
        var ok = await dymola.ExecuteCommandAsync("command()", cancellationToken: Test);
        elapsed.Stop();

        Assert.False(ok);
        Assert.True(dymola.IsOfflineMode());
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(6), $"took {elapsed.Elapsed}");
    }

    /// <summary>
    /// Stands in for Dymola's single-threaded JSON-RPC server: while it is busy, every
    /// connection is accepted - the listening socket completes the handshake - and never
    /// answered; after that, each request gets a JSON-RPC success.
    /// </summary>
    private sealed class BusyServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private readonly List<TcpClient> _held = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly TimeSpan _busyFor;

        public int Port { get; }

        public BusyServer(TimeSpan busyFor)
        {
            _busyFor = busyFor;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptAsync);
        }

        private async Task AcceptAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                }
                catch
                {
                    return;
                }

                if (_clock.Elapsed < _busyFor)
                {
                    lock (_held)
                        _held.Add(client);
                }
                else
                {
                    _ = Task.Run(() => AnswerAsync(client, _stopping.Token));
                }
            }
        }

        private static async Task AnswerAsync(TcpClient client, CancellationToken stopping)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var buffer = new byte[8192];
                    var read = await stream.ReadAsync(buffer, stopping);
                    if (read == 0)
                        return;

                    var id = Regex.Match(Encoding.UTF8.GetString(buffer, 0, read), "\"id\"\\s*:\\s*(\\d+)");
                    var body = $"{{\"result\":true,\"error\":null,\"id\":{(id.Success ? id.Groups[1].Value : "0")}}}";
                    var response = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
                        + $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), stopping);
                }
                catch
                {
                    // A probe that hung up first is nothing the test needs to see.
                }
            }
        }

        public void Dispose()
        {
            _stopping.Cancel();
            _listener.Stop();
            lock (_held)
                foreach (var client in _held)
                    client.Dispose();
            _stopping.Dispose();
        }
    }
}
