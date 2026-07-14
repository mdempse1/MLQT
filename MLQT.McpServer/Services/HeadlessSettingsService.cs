using System.Collections.Concurrent;
using System.Text.Json;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Services;

/// <summary>
/// Headless (non-MAUI) implementation of <see cref="ISettingsService"/> that persists settings
/// as a single JSON file under <c>%LocalAppData%/MLQT/mcp-settings.json</c>.
///
/// The MAUI application uses the platform <c>Preferences</c> API; the MCP server has no MAUI
/// runtime, so this provides equivalent key/value persistence. Every value is stored as a
/// JSON-serialized string, matching the complex-type behaviour the services rely on
/// (e.g. <c>RepositoryService</c> persisting repository/project settings objects).
/// </summary>
public sealed class HeadlessSettingsService : ISettingsService
{
    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, string> _values;
    private readonly object _saveLock = new();

    public HeadlessSettingsService(string? filePath = null)
    {
        if (filePath is null)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(appData, "MLQT");
            Directory.CreateDirectory(dir);
            filePath = Path.Combine(dir, "mcp-settings.json");
        }

        _filePath = filePath;
        _values = Load(_filePath);
    }

    /// <inheritdoc/>
    public Task<T> GetAsync<T>(string key, T defaultValue)
    {
        try
        {
            if (!_values.TryGetValue(key, out var json) || string.IsNullOrEmpty(json))
                return Task.FromResult(defaultValue);

            var result = JsonSerializer.Deserialize<T>(json);
            return Task.FromResult(result ?? defaultValue);
        }
        catch
        {
            // Corrupt or type-mismatched entry: fall back to the default, matching the MAUI behaviour.
            return Task.FromResult(defaultValue);
        }
    }

    /// <inheritdoc/>
    public Task SetAsync<T>(string key, T value)
    {
        _values[key] = JsonSerializer.Serialize(value);
        Save();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveAsync(string key)
    {
        _values.TryRemove(key, out _);
        Save();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ClearAsync()
    {
        _values.Clear();
        Save();
        return Task.CompletedTask;
    }

    private static ConcurrentDictionary<string, string> Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (data is not null)
                    return new ConcurrentDictionary<string, string>(data);
            }
        }
        catch
        {
            // Ignore a corrupt settings file; start from empty rather than crashing the server.
        }

        return new ConcurrentDictionary<string, string>();
    }

    private void Save()
    {
        lock (_saveLock)
        {
            try
            {
                var snapshot = new Dictionary<string, string>(_values);
                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Persistence is best-effort; an unwritable settings file must not take down the server.
            }
        }
    }
}
