using System.Text.Json;
using MLQT.Services.Interfaces;

namespace MLQT.Services;

/// <summary>
/// Settings in a JSON file beside the logs.
/// </summary>
/// <remarks>
/// <para>Replaces MAUI's <c>Preferences</c>, which is a platform key/value store with no equivalent
/// off MAUI. The location follows <see cref="Environment.SpecialFolder.LocalApplicationData"/>, so it
/// is <c>%LocalAppData%/MLQT</c> on Windows and <c>~/.local/share/MLQT</c> on Linux — the same folder
/// the logs and dictionaries already use, which is why no XDG-specific code appears here.</para>
///
/// <para><b>Shared by both hosts</b> since 7b-3, which is what makes the cutover a non-event: the
/// MAUI app writes here too, having seeded this file once from its own <c>Preferences</c> store (see
/// <see cref="MauiPreferencesSeed"/>). By the time anyone runs the Photino host their settings are
/// already in it, and there is no migration step in the new host at all — the best migration being
/// the one that has already happened.</para>
///
/// <para>Writes are whole-file and synchronous. The store is a few kilobytes and settings change at
/// human speed, so the simplest thing that cannot half-write is the right one.</para>
/// </remarks>
public sealed class JsonSettingsService : ISettingsService
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private Dictionary<string, string> _values;

    /// <summary>The real store, under the platform's local application data.</summary>
    public JsonSettingsService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MLQT"))
    {
    }

    /// <summary>A store in a named directory.</summary>
    /// <remarks>
    /// <para>Exists so a test can point at a temporary directory. That is a seam worth having rather
    /// than a concession: without it the only way to test this is against the developer's own
    /// settings file, and <c>Environment.GetFolderPath</c> reads the shell's known folder rather than
    /// the <c>LOCALAPPDATA</c> variable, so redirecting the environment does <b>not</b> redirect it.
    /// A test suite written that way passes, and quietly overwrites the real file - which is exactly
    /// what happened when these tests were first written.</para>
    /// </remarks>
    public JsonSettingsService(string directory)
    {
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

    /// <summary>
    /// Copies a user's settings in from MAUI's <c>Preferences</c>, once.
    /// </summary>
    /// <param name="readOldValue">
    /// Reads a raw value from the old store, or returns null or empty when it holds nothing. Only the
    /// MAUI host can supply this, which is why it is a delegate and why the seeding happens there.
    /// </param>
    /// <returns>The keys copied, for the host to log.</returns>
    /// <remarks>
    /// One method rather than raw get/set accessors, so the dictionary stays private and the rules
    /// about what may overwrite what stay in <see cref="MauiPreferencesSeed"/> where they are tested.
    /// </remarks>
    public IReadOnlyList<string> SeedFrom(Func<string, string?> readOldValue)
    {
        lock (_gate)
        {
            var copied = MauiPreferencesSeed.Seed(
                readOldValue,
                key => _values.TryGetValue(key, out var existing) ? existing : null,
                (key, value) => _values[key] = value,
                alreadySeeded: _values.ContainsKey(MauiPreferencesSeed.CompletedKey));

            // The marker is written whether or not anything was copied: "there was nothing to bring
            // across" is an answer, and asking again every time the app starts is not free.
            _values[MauiPreferencesSeed.CompletedKey] = "true";
            Flush();

            return copied;
        }
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
            LoggingService.Error(nameof(JsonSettingsService), $"Could not read {path}", ex);
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
            LoggingService.Error(nameof(JsonSettingsService), $"Could not write {_path}", ex);
        }
    }
}
