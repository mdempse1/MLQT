using System.Text.Json;
using MLQT.Services.Interfaces;

namespace MLQT.TestHost.Services;

/// <summary>
/// Settings held in memory for the duration of a test run.
/// </summary>
/// <remarks>
/// <para>Values go through the same JSON round-trip the real implementations use rather than being
/// stashed as objects. That is the whole point of the fake: a type that does not survive
/// serialization fails here exactly as it would in the app, instead of passing in every journey and
/// failing the first time a user restarts.</para>
/// </remarks>
public sealed class InMemorySettingsService : ISettingsService
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>Where the real implementations would put the file. Reported, never written.</summary>
    public string BackingPath { get; } =
        Path.Combine(Path.GetTempPath(), "mlqt-testhost-settings");

    public Task<T> GetAsync<T>(string key, T defaultValue)
    {
        if (!_values.TryGetValue(key, out var json))
            return Task.FromResult(defaultValue);

        var value = JsonSerializer.Deserialize<T>(json);
        return Task.FromResult(value ?? defaultValue);
    }

    public Task SetAsync<T>(string key, T value)
    {
        _values[key] = JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _values.Remove(key);
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        _values.Clear();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Says "in memory" rather than reporting <see cref="BackingPath"/> as though it were real. The
    /// probe that reads this exists to make a store that does not persist visible, so a fake that
    /// claimed a path would defeat the one thing it is for.
    /// </remarks>
    public string BackingStore => $"in memory (would be {BackingPath})";
}
