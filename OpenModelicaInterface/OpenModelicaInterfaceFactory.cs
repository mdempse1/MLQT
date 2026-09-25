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
            _instance = new OpenModelicaInterface(
                omcPath: _omcSettings.OmcPath,
                port: _omcSettings.PortNumber
            )
            {
                CommandTimeout = _omcSettings.CommandTimeout,
                StartupTimeout = _omcSettings.StartupTimeout,
            };
            _instanceBuiltFor = (_omcSettings.OmcPath, _omcSettings.PortNumber);

            // Start OMC process
            if (!_instance.IsConnected)
            {
                await _instance.StartAsync(cancellationToken);

                // Optionally load Modelica standard library
                if (_omcSettings.AutoLoadModelicaLibrary)
                {
                    await _instance.LoadModelAsync("Modelica", cancellationToken: cancellationToken);
                }
            }

            return _instance;
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

        _instance?.Dispose();
        _instance = null;
        _lock.Dispose();
    }

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
