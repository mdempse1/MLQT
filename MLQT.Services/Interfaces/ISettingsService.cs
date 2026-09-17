namespace MLQT.Services.Interfaces;

/// <summary>
/// Service for managing application settings with platform-specific persistence
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Get a setting value by key, returning defaultValue if not found
    /// </summary>
    Task<T> GetAsync<T>(string key, T defaultValue);

    /// <summary>
    /// Set a setting value by key
    /// </summary>
    Task SetAsync<T>(string key, T value);

    /// <summary>
    /// Remove a setting by key
    /// </summary>
    Task RemoveAsync(string key);

    /// <summary>
    /// Clear all settings
    /// </summary>
    Task ClearAsync();

    /// <summary>
    /// Where this implementation keeps settings: a filesystem path when it uses one, otherwise a
    /// short description of the store.
    /// </summary>
    /// <remarks>
    /// <para>Diagnostic, and specifically for host conformance — it is what the <c>/selftest</c>
    /// route's <c>settings.location</c> probe records, so that the committed MAUI baseline says where
    /// settings lived under the host being replaced and a new host's answer can be compared with it
    /// rather than guessed at.</para>
    ///
    /// <para>It exists because a write-read round-trip cannot tell the difference between settings
    /// that persist and settings that only look like they do: an implementation holding values in a
    /// dictionary passes that test and loses everything on restart. This does not prove persistence
    /// either — it makes the store visible, which is what lets a diff show that one host writes to
    /// <c>%LocalAppData%</c> and another to a temporary directory.</para>
    /// </remarks>
    string BackingStore { get; }
}
