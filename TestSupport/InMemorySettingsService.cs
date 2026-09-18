using System.Text.Json;
using MLQT.Services.Interfaces;

namespace MLQT.TestSupport;

/// <summary>
/// An <see cref="ISettingsService"/> held in memory for the duration of a test.
/// </summary>
/// <remarks>
/// <para><b>Values go through the same JSON round-trip the real implementations use</b>, rather than
/// being stashed as objects. That is the whole point of the double: a type that does not survive
/// serialization fails here exactly as it would in the app, instead of passing in every test and
/// failing the first time a user restarts.</para>
///
/// <para><b>Why this file exists (B205).</b> There were three of these. Two serialised;
/// <c>MLQT.Services.Tests</c>' kept a <c>Dictionary&lt;string, object&gt;</c> and handed the same
/// instance back from <see cref="GetAsync"/> — and it was the one used most, 27 call sites across 13
/// files. Aliasing hides a whole class of defect: production code that mutates what
/// <c>GetAsync</c> returned and never saves it behaves correctly under test and wrongly in the app.
/// <c>RepositoryService.LoadRepositorySettingsAsync</c> does exactly that with its "Default" project.
/// It also cost one wrong assertion directly, when a "before" snapshot turned out to be the same
/// object as the value read back afterwards.</para>
///
/// <para><see cref="MLQT.TestSupport.SettingsServiceContract"/> is what holds this and every other
/// implementation to the same behaviour.</para>
/// </remarks>
public sealed class InMemorySettingsService : ISettingsService
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<T> GetAsync<T>(string key, T defaultValue)
    {
        if (!_values.TryGetValue(key, out var json))
            return Task.FromResult(defaultValue);

        return Task.FromResult(JsonSerializer.Deserialize<T>(json) ?? defaultValue);
    }

    /// <inheritdoc />
    public Task SetAsync<T>(string key, T value)
    {
        _values[key] = JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key)
    {
        _values.Remove(key);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync()
    {
        _values.Clear();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public string BackingStore => "in memory (test double)";
}
