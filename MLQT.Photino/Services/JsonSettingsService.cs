using System.Text.Json;
using MLQT.Services.Interfaces;

namespace MLQT.Photino.Services;

/// <summary>
/// Settings in a JSON file beside the logs.
/// </summary>
/// <remarks>
/// <para>Replaces MAUI's <c>Preferences</c>, which is a platform key/value store with no equivalent
/// off MAUI. The location follows <see cref="Environment.SpecialFolder.LocalApplicationData"/>, so it
/// is <c>%LocalAppData%/MLQT</c> on Windows and <c>~/.local/share/MLQT</c> on Linux — the same folder
/// the logs and dictionaries already use, which is why no XDG-specific code appears here.</para>
///
/// <para><b>This does not migrate anything, and that is 7b-3's job and the phase's highest silent
/// risk.</b> An existing user's project list, repository settings, theme and window state are in MAUI
/// <c>Preferences</c>, and this service starts empty. Cutting over without a migration resets every
/// one of them, and no probe can see it. Said here as well as in the plan because this is the file
/// somebody will be looking at when they write it.</para>
///
/// <para>Writes are whole-file and synchronous. The store is a few kilobytes and settings change at
/// human speed, so the simplest thing that cannot half-write is the right one.</para>
/// </remarks>
internal sealed class JsonSettingsService : ISettingsService
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private Dictionary<string, string> _values;

    public JsonSettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MLQT");

        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "settings.json");
        _values = Load(_path);
    }

    /// <inheritdoc />
    public string BackingStore => _path;

    public Task<T> GetAsync<T>(string key, T defaultValue)
    {
        lock (_gate)
        {
            if (!_values.TryGetValue(key, out var json))
                return Task.FromResult(defaultValue);

            try
            {
                return Task.FromResult(JsonSerializer.Deserialize<T>(json) ?? defaultValue);
            }
            catch (JsonException)
            {
                // A setting whose shape has changed since it was written is not a reason to fail
                // startup; the default is what a new user would get anyway.
                return Task.FromResult(defaultValue);
            }
        }
    }

    public Task SetAsync<T>(string key, T value)
    {
        lock (_gate)
        {
            _values[key] = JsonSerializer.Serialize(value);
            Flush();
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        lock (_gate)
        {
            _values.Remove(key);
            Flush();
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        lock (_gate)
        {
            _values.Clear();
            Flush();
        }

        return Task.CompletedTask;
    }

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch (Exception ex)
        {
            // A corrupt settings file must not stop the application opening. Starting from defaults
            // is recoverable; refusing to start is not.
            MLQT.Services.LoggingService.Error(nameof(JsonSettingsService), $"Could not read {path}", ex);
            return [];
        }
    }

    private void Flush()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            MLQT.Services.LoggingService.Error(nameof(JsonSettingsService), $"Could not write {_path}", ex);
        }
    }
}
