using MLQT.Services.Interfaces;

namespace MLQT.Services.Tests;

/// <summary>
/// Simple in-memory settings service for testing.
/// </summary>
internal class InMemorySettingsService : ISettingsService
{
    private readonly Dictionary<string, object> _settings = new();

    public Task<T> GetAsync<T>(string key, T defaultValue)
    {
        if (_settings.TryGetValue(key, out var value) && value is T typedValue)
        {
            return Task.FromResult(typedValue);
        }
        return Task.FromResult(defaultValue);
    }

    public Task SetAsync<T>(string key, T value)
    {
        _settings[key] = value!;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _settings.Remove(key);
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        _settings.Clear();
        return Task.CompletedTask;
    }
}