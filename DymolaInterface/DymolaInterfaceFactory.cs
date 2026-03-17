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
    public async Task<DymolaInterface> GetOrCreateAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_instance != null)
            {
                return _instance;
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
