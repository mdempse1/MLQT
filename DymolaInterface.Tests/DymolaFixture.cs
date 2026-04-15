using Xunit;

namespace DymolaInterface.Tests;

/// <summary>
/// Shared fixture for all Dymola tests. Ensures a single Dymola instance is used across all tests.
/// </summary>
public class DymolaFixture : IDisposable
{
    private const int Port = 8082;
    private const string Hostname = "127.0.0.1";
    private readonly SemaphoreSlim _dymolaLock = new(1, 1);

    /// <summary>
    /// Resolve the most recently installed Dymola executable under %ProgramFiles%.
    /// Mirrors the logic in <see cref="DymolaSettings"/>, so the fixture tracks
    /// whichever version is present on the current machine without hard-coding
    /// a year.
    /// </summary>
    public static string ResolveDymolaPath()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var year = DateTime.Now.Year + 1;
        var refreshVersionNext = false;
        while (year > 2020)
        {
            var versionName = refreshVersionNext
                ? $"Dymola {year}x Refresh 1"
                : $"Dymola {year}x";
            var path = Path.Combine(programFiles, versionName, "bin64", "dymola.exe");
            if (File.Exists(path)) return path;
            if (refreshVersionNext) year--;
            refreshVersionNext = !refreshVersionNext;
        }
        return string.Empty;
    }

    public string DymolaPath { get; }
    public DymolaInterface Dymola { get; private set; }
    public bool IsInitialized { get; private set; }

    public DymolaFixture()
    {
        // Constructor runs once before any tests
        DymolaPath = ResolveDymolaPath();
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
