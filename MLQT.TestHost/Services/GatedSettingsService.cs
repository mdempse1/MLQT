using MLQT.Services.Interfaces;
using MLQT.TestSupport;

namespace MLQT.TestHost.Services;

/// <summary>
/// The in-memory settings double, with a door a journey can close in front of one write.
/// </summary>
/// <remarks>
/// <para>A project switch's step 1 ends with the new project's settings being saved, and
/// <c>OnProjectChanged</c> is raised only after that - so holding that one write is how a journey
/// reloads the window while the switch is still loading and the new window has not been told it
/// happened (B423). Every other read and write goes straight to <see cref="InMemorySettingsService"/>,
/// which stays the one settings double (B365). Test host only.</para>
/// </remarks>
public sealed class GatedSettingsService(InMemorySettingsService inner) : ISettingsService
{
    private (HostGate Gate, Func<string, object?, bool> When)? _hold;

    /// <summary>Holds the next write for which <paramref name="when"/> is true.</summary>
    public HostGate HoldWrite(Func<string, object?, bool> when)
    {
        var gate = new HostGate();
        _hold = (gate, when);
        return gate;
    }

    public Task<T> GetAsync<T>(string key, T defaultValue) => inner.GetAsync(key, defaultValue);

    public async Task SetAsync<T>(string key, T value)
    {
        if (_hold is { } hold && hold.When(key, value))
        {
            _hold = null;
            await hold.Gate.PassAsync();
        }
        await inner.SetAsync(key, value);
    }

    public Task RemoveAsync(string key) => inner.RemoveAsync(key);

    public Task ClearAsync() => inner.ClearAsync();

    public string BackingStore => inner.BackingStore;
}
