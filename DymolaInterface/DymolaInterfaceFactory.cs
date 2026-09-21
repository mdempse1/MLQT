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
                    return _instance;

                // Dropped and rebuilt rather than reconnected: the instance owns an HttpClient and a
                // process handle that both describe the session that has gone.
                try { _instance.Dispose(); } catch { /* the session is already gone */ }
                _instance = null;
            }

            // Create instance with settings
            _instance = new DymolaInterface(
                dymolaPath: _dymolaSettings.DymolaPath,
                portNumber: _dymolaSettings.PortNumber,
                hostname: _dymolaSettings.HostAddress
            );

            //Open Dymola
            if (_instance.IsOfflineMode())
            {
                await _instance.StartDymolaProcessAsync();
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
