using Xunit;

namespace OpenModelicaInterface.Tests;

/// <summary>
/// Shared fixture for all OpenModelica tests. Ensures a single OMC instance is used across all tests.
/// </summary>
public class OpenModelicaFixture : IDisposable
{
    private const string OmcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe";
    private readonly SemaphoreSlim _omcLock = new(1, 1);

    public OpenModelicaInterface Omc { get; private set; }
    public bool IsInitialized { get; private set; }

    public OpenModelicaFixture()
    {
        // Constructor runs once before any tests
        Omc = new OpenModelicaInterface(OmcPath);
        IsInitialized = false;
    }

    /// <summary>
    /// Ensures OMC is started and ready. Call this at the beginning of each test.
    /// </summary>
    public async Task EnsureOmcStartedAsync()
    {
        await _omcLock.WaitAsync();
        try
        {
            if (!IsInitialized)
            {
                if (!Omc.IsConnected)
                {
                    await Omc.StartAsync();
                    // Wait a bit for OMC to fully initialize (ZMQ server takes ~2 seconds)
                    await Task.Delay(3000);
                }
                IsInitialized = true;
            }
        }
        finally
        {
            _omcLock.Release();
        }
    }

    public void Dispose()
    {
        // Cleanup runs once after all tests
        if (IsInitialized)
        {
            try
            {
                Omc.ExitAsync().Wait();
            }
            catch
            {
                // Ignore errors on cleanup
            }
        }
        Omc?.Dispose();
    }
}
