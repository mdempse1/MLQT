using MLQT.McpServer.Services;
using MLQT.Services.Interfaces;
using MLQT.TestSupport;

namespace MLQT.McpServer.Tests;

/// <summary>
/// The MCP server's own settings store, held to the same contract as the desktop one and the double
/// (B205). It is a separate implementation reading the same file the app writes, so the two agreeing
/// is not something to take on trust.
/// </summary>
public sealed class HeadlessSettingsServiceContractTests : SettingsServiceContract, IDisposable
{
    private readonly List<string> _files = [];

    protected override ISettingsService CreateStore()
    {
        var path = Path.Combine(Path.GetTempPath(), "mlqt-contract-" + Guid.NewGuid().ToString("N") + ".json");
        _files.Add(path);
        return new HeadlessSettingsService(path);
    }

    public void Dispose()
    {
        foreach (var file in _files)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { /* best effort */ }
        }
    }
}

/// <summary>The shared double, from this suite as well, since it is used here too.</summary>
public sealed class InMemorySettingsServiceContractTests : SettingsServiceContract
{
    protected override ISettingsService CreateStore() => new InMemorySettingsService();
}
