using Microsoft.Extensions.DependencyInjection;
using OpenModelicaInterface.Interfaces;

namespace OpenModelicaInterface;

/// <summary>
/// Factory for creating and managing OpenModelica interface instances.
/// Implements singleton pattern with thread-safe initialization.
/// </summary>
public class OpenModelicaInterfaceFactory : IOpenModelicaInterfaceFactory, IDisposable
{
    private OpenModelicaInterface? _instance;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private OpenModelicaSettings _omcSettings = new();

    /// <summary>The path and port the cached session was started with - copied, because the settings
    /// object is edited in place.</summary>
    private (string Path, int Port) _instanceBuiltFor;

    /// <summary>Set by <see cref="Shutdown"/>: MLQT is exiting, and no omc is started after it.</summary>
    private volatile bool _shutDown;

    public void UpdateSettings(OpenModelicaSettings settings)
    {
        _omcSettings = settings;
    }

    public bool IsConnected => _instance?.IsConnected ?? false;

    public async Task<IOpenModelicaInterface> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        // Stop reaches a check that is still starting omc and loading the standard library (B335).
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // A check still running as MLQT exits would otherwise start an omc after the one session
            // was ended, and leave it behind with nothing left to end it (B493).
            ObjectDisposedException.ThrowIf(_shutDown, this);

            // A session started from another omc or on another port is not the one asked for. Only the
            // time limit used to reach a running session, so B263's "the path and port set in the tab
            // never reached omc" stayed half true until the session died or MLQT restarted (B336).
            if (_instance != null && _instanceBuiltFor != (_omcSettings.OmcPath, _omcSettings.PortNumber))
            {
                try { await _instance.ExitAsync(); } catch { /* ending it regardless */ }
                _instance.Dispose();
                _instance = null;
            }

            if (_instance != null)
            {
                // A session a timed-out command closed is not handed back: its socket is gone and omc
                // with it (see OpenModelicaInterface.CommandTimeout). Replaced, not reconnected.
                if (_instance.IsConnected)
                {
                    // Applied on every hand-out, so a limit changed in the settings reaches the
                    // session already running.
                    _instance.CommandTimeout = _omcSettings.CommandTimeout;
                    return _instance;
                }

                _instance.Dispose();
                _instance = null;
            }

            // Create instance with settings. The two limits are the ones OpenModelicaSettings has
            // carried since it was written and nothing read until B263.
            // Held in a local as well: Shutdown may take the session from under a start that has
            // kept the lock past its short wait, and what this returns is the one it started.
            var created = new OpenModelicaInterface(
                omcPath: _omcSettings.OmcPath,
                port: _omcSettings.PortNumber
            )
            {
                CommandTimeout = _omcSettings.CommandTimeout,
                StartupTimeout = _omcSettings.StartupTimeout,
            };
            _instance = created;
            _instanceBuiltFor = (_omcSettings.OmcPath, _omcSettings.PortNumber);

            // Start OMC process
            if (!created.IsConnected)
            {
                await created.StartAsync(cancellationToken);

                // Optionally load Modelica standard library
                if (_omcSettings.AutoLoadModelicaLibrary)
                {
                    await created.LoadModelAsync("Modelica", cancellationToken: cancellationToken);
                }
            }

            return created;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Ends the OpenModelica session this factory started, if there is one.
    /// </summary>
    /// <remarks>
    /// <para><b>omc is headless</b>, so an `omc.exe` left behind when MLQT closes is a process with
    /// no window, no owner and nothing to say it should be ended - it sits there until somebody
    /// notices it in a task manager and wonders what it is (B260). Dymola is deliberately *not*
    /// treated this way: its window is visible, the user may well have carried on working in the
    /// session MLQT started, and closing it from underneath them would lose that work.</para>
    ///
    /// <para>Synchronous because it runs on the way out, after the window has gone. The instance's
    /// own Dispose sends `quit()` with a five-second bound and kills the process if that does not
    /// land, so this cannot hang the exit.</para>
    /// </remarks>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Shutdown();

        // The lock is not disposed: Shutdown may be called again after this - the host's exit hooks
        // can each reach it - and a check still on its way through GetOrCreateAsync must be able to
        // take and release it and be refused. It allocates nothing needing disposal unless its wait
        // handle is asked for, which nothing here does.
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>The lock is waited for briefly, not indefinitely: a check starting omc holds it for up
    /// to the start-up limit, and the session it is starting is already <see cref="_instance"/>, so
    /// ending that one is exactly what is wanted. Disposing the session sends <c>quit()</c> within
    /// five seconds and then ends omc's process tree if it has not gone.</para>
    /// </remarks>
    public void Shutdown()
    {
        _shutDown = true;

        var held = _lock.Wait(TimeSpan.FromSeconds(1));
        try
        {
            Interlocked.Exchange(ref _instance, null)?.Dispose();
        }
        finally
        {
            if (held)
                _lock.Release();
        }
    }

    /// <summary>Holds <paramref name="session"/> as though this factory had started it - so what
    /// <see cref="Shutdown"/> does to a session can be tested without an omc (B493).</summary>
    internal void Adopt(OpenModelicaInterface session) => _instance = session;

    public async Task ResetAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_instance != null)
            {
                try
                {
                    await _instance.ExitAsync();
                }
                catch
                {
                    // Ignore errors during shutdown
                }

                _instance.Dispose();
                _instance = null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
