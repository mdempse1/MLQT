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
    public async Task<IDymolaInterface> GetOrCreateAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var settings = _dymolaSettings;

            // A cached session is only worth having if it is still there. Closing Dymola's window
            // ends its process and its JSON-RPC server, and nothing told this object — so the first
            // check worked and every one after it failed against a session that had gone (B171).
            //
            // Asked here rather than at the call sites because this is the one place that decides
            // whether to reuse or create, and a liveness test anywhere else would be a second
            // answer to the same question.
            if (_instance != null)
            {
                switch (await _instance.GetSessionStateAsync())
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
                        await Task.Run(() => starting.StartDymolaProcessAsync());
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
            var created = await Task.Run(() => _createSession(settings));
            _instance = created;
            created.CommandTimeout = settings.CommandTimeout;

            if (created.IsOfflineMode())
            {
                // Offline says only that nothing answered in the connection window. A Dymola that
                // accepted the connection and stayed silent is there and busy - somebody else's, or
                // one a session before this started - and starting another would put two on one
                // port (B331).
                if (await created.GetSessionStateAsync() is DymolaSessionState.Busy)
                    created.SetOfflineMode(false);
                else
                    await Task.Run(() => created.StartDymolaProcessAsync());
            }

            return created;
        }
        finally
        {
            _lock.Release();
        }
    }

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
