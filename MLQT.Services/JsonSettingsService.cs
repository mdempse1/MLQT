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
/// <para><b>The Photino host's store, and where a MAUI user's settings end up.</b> On first run the
/// host reads whatever the MAUI build left in its own file and copies it in here — see
/// <see cref="MigrateFrom"/> and <see cref="MauiPreferencesFile"/> — so switching hosts keeps a
/// user's projects, repositories, themes and tool paths. The MAUI build is never modified and never
/// written to.</para>
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

    /// <summary>Marks the store as migrated, so a user's later edits are never overwritten.</summary>
    internal const string MigratedKey = "__MauiSettingsMigrated";

    /// <summary>
    /// Copies a user's settings in from the MAUI build, once.
    /// </summary>
    /// <param name="old">Everything <see cref="MauiPreferencesFile"/> found. May be empty.</param>
    /// <returns>The keys copied, for the host to log.</returns>
    /// <remarks>
    /// <para>Two guards, and they guard different things. The marker stops this running twice, so a
    /// setting the user <i>deleted</i> in the new host does not come back on the next launch. The
    /// per-key check stops it overwriting anything already here, so a setting changed in the new host
    /// survives — anything in this store is newer by definition, because the MAUI build cannot write
    /// to it.</para>
    ///
    /// <para>The marker is written even when nothing was copied. "There was nothing to bring across"
    /// is an answer, and re-reading the old file on every launch to reach it again is not free.</para>
    /// </remarks>
    public IReadOnlyList<string> MigrateFrom(IReadOnlyDictionary<string, string> old)
    {
        lock (_gate)
        {
            if (_values.ContainsKey(MigratedKey))
                return [];

            var copied = new List<string>();

            foreach (var (key, value) in old)
            {
                if (_values.ContainsKey(key) || string.IsNullOrEmpty(value))
                    continue;

                _values[key] = value;
                copied.Add(key);
            }

            _values[MigratedKey] = "true";
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
