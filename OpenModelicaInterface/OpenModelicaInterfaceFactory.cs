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

    public void UpdateSettings(OpenModelicaSettings settings)
    {
        _omcSettings = settings;
    }

    public bool IsConnected => _instance?.IsConnected ?? false;

    public async Task<IOpenModelicaInterface> GetOrCreateAsync()
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
