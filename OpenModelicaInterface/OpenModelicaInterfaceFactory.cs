using Microsoft.Extensions.DependencyInjection;
using OpenModelicaInterface.Interfaces;

namespace OpenModelicaInterface;

/// <summary>
/// Factory for creating and managing OpenModelica interface instances.
/// Implements singleton pattern with thread-safe initialization.
/// </summary>
public class OpenModelicaInterfaceFactory : IOpenModelicaInterfaceFactory
{
    private OpenModelicaInterface? _instance;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private OpenModelicaSettings _omcSettings = new();

    public void UpdateSettings(OpenModelicaSettings settings)
    {
        _omcSettings = settings;
    }

    public bool IsConnected => _instance?.IsConnected ?? false;

    public async Task<OpenModelicaInterface> GetOrCreateAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_instance != null)
            {
                return _instance;
            }

            // Create instance with settings
            _instance = new OpenModelicaInterface(
                omcPath: _omcSettings.OmcPath,
                port: _omcSettings.PortNumber
            );

            // Start OMC process
            if (!_instance.IsConnected)
            {
                await _instance.StartAsync();

                // Optionally load Modelica standard library
                if (_omcSettings.AutoLoadModelicaLibrary)
                {
                    await _instance.LoadModelAsync("Modelica");
                }
            }

            return _instance;
        }
        finally
        {
            _lock.Release();
        }
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
