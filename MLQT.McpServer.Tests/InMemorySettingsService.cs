using System.Text.Json;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tests;

/// <summary>In-memory <see cref="ISettingsService"/> for tests (JSON-serialized values).</summary>
public sealed class InMemorySettingsService : ISettingsService
{
    private readonly Dictionary<string, string> _values = new();

    public Task<T> GetAsync<T>(string key, T defaultValue)
    {
        if (!_values.TryGetValue(key, out var json))
            return Task.FromResult(defaultValue);
        return Task.FromResult(JsonSerializer.Deserialize<T>(json) ?? defaultValue);
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
}
