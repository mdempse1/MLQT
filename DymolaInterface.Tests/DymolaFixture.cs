using Xunit;

namespace DymolaInterface.Tests;

/// <summary>
/// Shared fixture for all Dymola tests. Ensures a single Dymola instance is used across all tests.
/// </summary>
public class DymolaFixture : IDisposable
{
    private const string DymolaPath = @"C:\Program Files\Dymola 2025x Refresh 1\bin64\Dymola.exe";
    private const int Port = 8082;
    private const string Hostname = "127.0.0.1";
    private readonly SemaphoreSlim _dymolaLock = new(1, 1);

    public DymolaInterface Dymola { get; private set; }
    public bool IsInitialized { get; private set; }

    public DymolaFixture()
    {
        // Constructor runs once before any tests
        Dymola = new DymolaInterface(DymolaPath, Port, Hostname);
        IsInitialized = false;
    }

    /// <summary>
    /// Ensures Dymola is started and ready. Call this at the beginning of each test.
    /// </summary>
    public async Task EnsureDymolaStartedAsync()
    {   
        await _dymolaLock.WaitAsync();
        try
        {
            if (!IsInitialized)
            {
                if (Dymola.IsOfflineMode())
                {
                    await Dymola.StartDymolaProcessAsync();
                    // Wait longer for Dymola to fully initialize and load Modelica Standard Library
                    await Task.Delay(15000);
                }
                IsInitialized = true;
            }
        }
        finally
        {
            _dymolaLock.Release();
        }
    }

    public void Dispose()
    {
        // Cleanup runs once after all tests
        if (IsInitialized)
        {
            try
            {
                Dymola.ExitAsync().Wait();
            }
            catch
            {
                // Ignore errors on cleanup
            }
        }
        Dymola?.Dispose();
    }
}
