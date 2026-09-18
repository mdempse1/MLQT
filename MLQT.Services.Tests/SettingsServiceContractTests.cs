using MLQT.Services;
using MLQT.Services.Interfaces;
using MLQT.TestSupport;

namespace MLQT.Services.Tests;

/// <summary>
/// The in-memory double, held to the same behaviour as the real service (B205).
/// </summary>
public sealed class InMemorySettingsServiceContractTests : SettingsServiceContract
{
    protected override ISettingsService CreateStore() => new InMemorySettingsService();
}

/// <summary>
/// The real file-backed service, run against the same contract — which is what makes the contract
/// worth anything. A double that passes a contract the real thing has never been held to only proves
/// the two agree with the contract's author.
/// </summary>
public sealed class JsonSettingsServiceContractTests : SettingsServiceContract, IDisposable
{
    private readonly List<string> _directories = [];

    protected override ISettingsService CreateStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mlqt-contract-" + Guid.NewGuid().ToString("N"));
        _directories.Add(directory);
        return new JsonSettingsService(directory);
    }

    public void Dispose()
    {
        foreach (var directory in _directories)
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }
}
