using DymolaInterface;
using DymolaInterface.Interfaces;

namespace MLQT.Services.Tests;

/// <summary>
/// B331 — the factory decides whether to reuse, wait for, start or drop a Dymola session, and it
/// must never end the Dymola behind one.
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> A cached session was asked <c>IsAliveAsync</c>, a two-second ping.
/// A Dymola still working on a check that timed out or was stopped answers nothing, exactly like one
/// whose window was closed, so the factory took it for dead and disposed it — and disposing a session
/// runs <c>Kill()</c> on the Dymola it started, with whatever the user had open in it. Against a
/// Dymola MLQT had only attached to, it built a new session instead, which found nothing answering
/// and started a second Dymola on the same port.</para>
///
/// <para><b>Here rather than in DymolaInterface.Tests</b> because that suite drives a live Dymola and
/// runs in no CI job; this one runs on every push, and the factory builds its sessions through a
/// function precisely so it can. The same decisions against a real socket are in
/// <c>DymolaInterface.Tests/CommandTimeoutTests</c>.</para>
/// </remarks>
public class DymolaInterfaceFactoryTests
{
    /// <summary>A session whose state a test sets, recording what the factory did to it.</summary>
    private sealed class FakeSession : IDymolaSession
    {
        public DymolaSessionState State = DymolaSessionState.Answering;
        public bool Offline;
        public bool Detached;
        public bool Disposed;
        public int Starts;
        public TimeSpan CommandTimeout { get; set; }

        /// <summary>Whether disposing this session would have ended the process behind it.</summary>
        public bool Killed => Disposed && !Detached;

        public Task<DymolaSessionState> GetSessionStateAsync() => Task.FromResult(State);
        public bool IsOfflineMode() => Offline;
        public void SetOfflineMode(bool enable) => Offline = enable;

        public Task StartDymolaProcessAsync(CancellationToken cancellationToken = default)
        {
            Starts++;
            State = DymolaSessionState.Answering;
            Offline = false;
            return Task.CompletedTask;
        }

        public void Detach() => Detached = true;
        public void Dispose() => Disposed = true;

        // The service's half, which the factory never calls.
        public CommandOutcome LastOutcome => CommandOutcome.Answered;
        public Task<bool> OpenModelAsync(string path, bool mustRead = true, bool changeDirectory = true,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CheckModelAsync(string problem, bool simulate = false, bool constraint = false,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ClearAsync(bool fast = false) => Task.FromResult(true);
        public Task<bool> ClearLogAsync() => Task.FromResult(true);
        public Task<string> GetLastErrorAsync() => Task.FromResult("");
    }

    /// <summary>A factory handing out the given sessions in turn, and how many it was asked for.</summary>
    private sealed class Sessions
    {
        private readonly Queue<FakeSession> _next;
        public readonly List<FakeSession> Created = [];

        public Sessions(params FakeSession[] sessions) => _next = new Queue<FakeSession>(sessions);

        public IDymolaSession Create(DymolaSettings _)
        {
            var session = _next.Count > 0 ? _next.Dequeue() : new FakeSession();
            Created.Add(session);
            return session;
        }
    }

    [Fact]
    public async Task ABusySessionIsHandedBackRatherThanReplaced()
    {
        // The reported defect: the check after a timeout or Stop. Dymola is still working on the one
        // it was given and does not answer.
        var first = new FakeSession();
        var sessions = new Sessions(first);
        var factory = new DymolaInterfaceFactory(sessions.Create);
        await factory.GetOrCreateAsync();

        first.State = DymolaSessionState.Busy;
        var second = await factory.GetOrCreateAsync();

        Assert.Same(first, second);
        Assert.False(first.Disposed, "a busy Dymola's session was disposed - which kills the Dymola MLQT started");
        Assert.Single(sessions.Created);
        Assert.Equal(0, first.Starts);
    }

    [Fact]
    public async Task ABusySessionThatWasOfflineIsLetThroughSoItsCommandsWait()
    {
        // Held back as offline, a command would probe once, find Dymola silent and give up at once -
        // the next check would fail rather than wait, which is what external-tools.md promises.
        var first = new FakeSession();
        var factory = new DymolaInterfaceFactory(new Sessions(first).Create);
        await factory.GetOrCreateAsync();

        first.Offline = true;
        first.State = DymolaSessionState.Busy;
        await factory.GetOrCreateAsync();

        Assert.False(first.Offline);
    }

    [Fact]
    public async Task AGoneSessionIsReplacedWithoutEndingAnyProcess()
    {
        // B171's case, which must keep working: the window was closed, so a new session is needed.
        var first = new FakeSession();
        var sessions = new Sessions(first);
        var factory = new DymolaInterfaceFactory(sessions.Create);
        await factory.GetOrCreateAsync();

        first.State = DymolaSessionState.Gone;
        var second = await factory.GetOrCreateAsync();

        Assert.NotSame(first, second);
        Assert.True(first.Disposed);
        Assert.False(first.Killed, "the session was disposed without being detached first");
    }

    [Fact]
    public async Task ADymolaStillStartingIsWaitedForRatherThanStartedAgain()
    {
        var first = new FakeSession();
        var sessions = new Sessions(first);
        var factory = new DymolaInterfaceFactory(sessions.Create);
        await factory.GetOrCreateAsync();

        first.State = DymolaSessionState.Starting;
        var second = await factory.GetOrCreateAsync();

        Assert.Same(first, second);
        Assert.Single(sessions.Created);
        Assert.Equal(1, first.Starts);   // the wait for the process it has, not a second launch
        Assert.False(first.Disposed);
    }

    [Fact]
    public async Task ANewSessionFindingABusyDymolaDoesNotStartASecondOne()
    {
        // The attach case: a Dymola is on the port and busy, so nothing answered in the connection
        // window. Starting one would put two Dymolas on one port.
        var busy = new FakeSession { Offline = true, State = DymolaSessionState.Busy };
        var factory = new DymolaInterfaceFactory(new Sessions(busy).Create);

        var session = await factory.GetOrCreateAsync();

        Assert.Same(busy, session);
        Assert.Equal(0, busy.Starts);
        Assert.False(busy.Offline, "its commands would be held back instead of waiting for Dymola");
    }

    [Fact]
    public async Task ANewSessionWithNothingThereStartsDymola()
    {
        var nothing = new FakeSession { Offline = true, State = DymolaSessionState.Gone };
        var factory = new DymolaInterfaceFactory(new Sessions(nothing).Create);

        await factory.GetOrCreateAsync();

        Assert.Equal(1, nothing.Starts);
    }

    [Fact]
    public async Task AnAnsweringSessionIsReusedAndGivenTheCurrentTimeLimit()
    {
        var first = new FakeSession();
        var sessions = new Sessions(first);
        var factory = new DymolaInterfaceFactory(sessions.Create);
        await factory.GetOrCreateAsync();

        factory.UpdateSettings(new DymolaSettings { CommandTimeoutMs = 42_000 });
        var second = await factory.GetOrCreateAsync();

        Assert.Same(first, second);
        Assert.Equal(TimeSpan.FromSeconds(42), first.CommandTimeout);
    }

    [Fact]
    public async Task ResettingForgetsTheSessionWithoutEndingDymola()
    {
        var first = new FakeSession();
        var sessions = new Sessions(first);
        var factory = new DymolaInterfaceFactory(sessions.Create);
        await factory.GetOrCreateAsync();

        await factory.ResetAsync();
        await factory.GetOrCreateAsync();

        Assert.True(first.Disposed);
        Assert.False(first.Killed, "resetting killed the Dymola window the session had started");
        Assert.Equal(2, sessions.Created.Count);
    }
}
