using System.ComponentModel;
using DymolaInterface.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DymolaInterface;

/// <summary>
/// Factory for creating and managing DymolaInterface singleton instances with configuration from settings.
/// </summary>
public class DymolaInterfaceFactory : IDymolaInterfaceFactory
{
    private DymolaInterface? _instance;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DymolaSettings _dymolaSettings = new();

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
    public async Task<IDymolaInterface> GetOrCreateAsync()
    {
        await _lock.WaitAsync();
        try
        {
            // A cached session is only worth having if it is still there. Closing Dymola's window
            // ends its process and its JSON-RPC server, and nothing told this object — so the first
            // check worked and every one after it failed against a session that had gone (B171).
            //
            // Asked here rather than at the call sites because this is the one place that decides
            // whether to reuse or create, and a liveness test anywhere else would be a second
            // answer to the same question.
            if (_instance != null)
            {
                if (await _instance.IsAliveAsync())
                {
                    // Applied on every hand-out rather than only at creation, so a time limit changed
                    // in the settings reaches the session already open.
                    _instance.CommandTimeout = _dymolaSettings.CommandTimeout;
                    return _instance;
                }

                // Dropped and rebuilt rather than reconnected: the instance owns an HttpClient and a
                // process handle that both describe the session that has gone.
                try { _instance.Dispose(); } catch { /* the session is already gone */ }
                _instance = null;
            }

            // On the thread pool, both of them (B262). The constructor waits out a Dymola that
            // accepts the connection but is too busy to answer - up to its 30-second connection
            // window, synchronously - and a caller on the UI thread arrives here holding it, because
            // an uncontended lock is taken without yielding. Starting Dymola is the same wait in
            // another form: its loop resumes on the caller's context after each delay and then
            // probes synchronously. Either one froze the window.
            var settings = _dymolaSettings;
            _instance = await Task.Run(() => new DymolaInterface(
                dymolaPath: settings.DymolaPath,
                portNumber: settings.PortNumber,
                hostname: settings.HostAddress));
            _instance.CommandTimeout = settings.CommandTimeout;

            if (_instance.IsOfflineMode())
            {
                var starting = _instance;
                await Task.Run(starting.StartDymolaProcessAsync);
            }

            return _instance;
        }
        finally
        {
            _lock.Release();
        }
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
    /// Disposes the current instance if it exists.
    /// Call this when settings change to force recreation with new settings.
    /// </summary>
    public async Task ResetAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_instance != null)
            {
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
