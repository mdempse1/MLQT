using System.ComponentModel;
using DymolaInterface.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DymolaInterface;

/// <summary>
/// Factory for creating and managing DymolaInterface singleton instances with configuration from settings.
/// </summary>
public class DymolaInterfaceFactory : IDymolaInterfaceFactory
{
    private IDymolaSession? _instance;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DymolaSettings _dymolaSettings = new();
    private readonly Func<DymolaSettings, IDymolaSession> _createSession;

    /// <summary>The path, port and host the cached session was built for - copied, because the
    /// settings object is edited in place.</summary>
    private (string Path, int Port, string Host) _instanceBuiltFor;

    /// <summary>
    /// A factory that connects to - or starts - a real Dymola.
    /// </summary>
    public DymolaInterfaceFactory()
        : this(settings => new DymolaInterface(
            dymolaPath: settings.DymolaPath,
            portNumber: settings.PortNumber,
            hostname: settings.HostAddress))
    {
    }

    /// <summary>
    /// A factory whose sessions come from <paramref name="createSession"/> - how the decisions below
    /// are tested without Dymola (B331). The function is called on the thread pool, because the real
    /// constructor waits out a busy Dymola.
    /// </summary>
    public DymolaInterfaceFactory(Func<DymolaSettings, IDymolaSession> createSession)
    {
        _createSession = createSession;
    }

    /// <summary>
    /// Update the settings used for Dymola instances
    /// </summary>
    public void UpdateSettings(DymolaSettings settings)
    {
        _dymolaSettings = settings;
    }

    /// <summary>
    /// Where each decision about a session is reported: what state a cached one was found in, and
    /// whether a Dymola was attached to, started or let go. The host points it at its log, which is
    /// the only record of which Dymola a check reached - and whose it was - once the window is gone.
    /// </summary>
    public Action<string>? Log { get; set; }

    private void Report(string message)
    {
        try { Log?.Invoke(message); } catch { /* logging must never decide a check */ }
    }

    private static string Whose(IDymolaSession session) =>
        session.ProcessId is { } pid ? $"the Dymola MLQT started (process {pid})" : "a Dymola MLQT did not start";

    /// <summary>
    /// Gets or creates the singleton DymolaInterface instance with settings from SettingsService.
    /// </summary>
    /// <remarks>
    /// <para><b>This never ends a Dymola process</b> (B331, and B260 before it). A cached session is
    /// asked what state it is in rather than whether it answers a ping: a Dymola still working on a
    /// check that timed out or was stopped answers nothing, exactly like one whose window was closed,
    /// and taking it for gone disposed the session - which killed the Dymola MLQT had started, with
    /// whatever the user had open in it - or, against a Dymola MLQT had only attached to, started a
    /// second one on a port that was taken. A busy session is handed back and its commands wait for
    /// Dymola; a gone one is detached before it is dropped, so even a process that is somehow still
    /// running is left to the user.</para>
    /// </remarks>
    public async Task<IDymolaInterface> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        // Stop reaches a check that is still starting Dymola, which can be a minute: the connection
        // window and the start each take up to thirty seconds, and a token that arrived only with the
        // first class left Stop doing nothing throughout (B335).
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var settings = _dymolaSettings;

            // A session built for another path or port is not the one asked for. Only the time limit
            // used to reach a running session, so a new Dymola version or port did nothing until the
            // session died or MLQT restarted (B336). Dropped, not ended: that Dymola is the user's.
            // A new path against a Dymola still serving the same port attaches to that one - two
            // cannot share a port - and takes effect once it is closed.
            if (_instance != null && _instanceBuiltFor != BuiltFor(settings))
                Drop();

            // A cached session is only worth having if it is still there. Closing Dymola's window
            // ends its process and its JSON-RPC server, and nothing told this object — so the first
            // check worked and every one after it failed against a session that had gone (B171).
            //
            // Asked here rather than at the call sites because this is the one place that decides
            // whether to reuse or create, and a liveness test anywhere else would be a second
            // answer to the same question.
            if (_instance != null)
            {
                var state = await _instance.GetSessionStateAsync();
                Report($"Cached session on port {settings.PortNumber}, {Whose(_instance)}: {state}");
                switch (state)
                {
                    case DymolaSessionState.Answering:
                        break;

                    case DymolaSessionState.Busy:
                        // Still on the command it was given. Its commands are sent rather than held
                        // back as offline, so they wait for Dymola to finish - up to the time limit -
                        // which is what "the next check waits" means (B331).
                        _instance.SetOfflineMode(false);
                        break;

                    case DymolaSessionState.Starting:
                        // The Dymola it launched has not come up yet: wait for that one rather than
                        // launching another beside it on the same port.
                        var starting = _instance;
                        await Task.Run(() => starting.StartDymolaProcessAsync(cancellationToken), cancellationToken);
                        break;

                    default:
                        Drop();
                        break;
                }

                if (_instance != null)
                {
                    // Applied on every hand-out rather than only at creation, so a time limit changed
                    // in the settings reaches the session already open.
                    _instance.CommandTimeout = settings.CommandTimeout;
                    return _instance;
                }
            }

            // On the thread pool, both of them (B262). The constructor waits out a Dymola that
            // accepts the connection but is too busy to answer - up to its 30-second connection
            // window, synchronously - and a caller on the UI thread arrives here holding it, because
            // an uncontended lock is taken without yielding. Starting Dymola is the same wait in
            // another form: its loop resumes on the caller's context after each delay and then
            // probes synchronously. Either one froze the window.
            // Abandoned rather than interrupted when cancelled: the constructor's wait is synchronous.
            // A session left behind that way holds a client and no process.
            var created = await Task.Run(() => _createSession(settings), cancellationToken)
                .WaitAsync(cancellationToken);
            _instance = created;
            _instanceBuiltFor = BuiltFor(settings);
            created.CommandTimeout = settings.CommandTimeout;

            if (created.IsOfflineMode())
            {
                // Offline says only that nothing answered in the connection window. A Dymola that
                // accepted the connection and stayed silent is there and busy - somebody else's, or
                // one a session before this started - and starting another would put two on one
                // port (B331).
                var state = await created.GetSessionStateAsync();
                Report($"No answer on port {settings.PortNumber} within the connection window: {state}");
                if (state is DymolaSessionState.Busy)
                    created.SetOfflineMode(false);
                else
                {
                    Report($"Starting {settings.DymolaPath} -serverport {settings.PortNumber}");
                    await Task.Run(() => created.StartDymolaProcessAsync(cancellationToken), cancellationToken);
                    Report($"Dymola is answering on port {settings.PortNumber}: {Whose(created)}");
                }
            }
            else
            {
                Report($"Attached to the Dymola answering on port {settings.PortNumber}");
            }

            return created;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static (string Path, int Port, string Host) BuiltFor(DymolaSettings settings) =>
        (settings.DymolaPath, settings.PortNumber, settings.HostAddress);

    /// <summary>
    /// Forgets the cached session without ending the Dymola behind it: detached first, so disposing
    /// the session cannot kill a process the user may still be working in.
    /// </summary>
    private void Drop()
    {
        var session = _instance;
        _instance = null;
        if (session == null)
            return;

        Report($"Letting go of the session with {Whose(session)}; that Dymola is left running");
        try { session.Detach(); } catch { /* nothing to let go of */ }
        try { session.Dispose(); } catch { /* the session is already gone */ }
    }

    /// <summary>
    /// Checks if an instance exists and is connected.
    /// </summary>
    public bool IsConnected
    {
        get
        {
            if (_instance == null)
                return false;

            return !_instance.IsOfflineMode();
        }
    }

    /// <summary>
    /// Forgets the current session, so the next call to <see cref="GetOrCreateAsync"/> connects
    /// afresh. The Dymola behind it is left running: its window is the user's (B260, B331).
    /// </summary>
    public async Task ResetAsync()
    {
        await _lock.WaitAsync();
        try
        {
            Drop();
        }
        finally
        {
            _lock.Release();
        }
    }
}
