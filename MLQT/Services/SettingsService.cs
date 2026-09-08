using MLQT.Services.Interfaces;

namespace MLQT.Services;

/// <summary>
/// The MAUI host's settings, which are now the same settings the Photino host reads.
/// </summary>
/// <remarks>
/// <para>Phase 7b-3. This used to be MAUI's <c>Preferences</c> API directly. It now delegates to
/// <see cref="JsonSettingsService"/> — the store both hosts share — and <b>seeds that store once from
/// <c>Preferences</c></b> so that an existing user's project list, repository settings, themes and
/// external-tool paths are already there the first time they run the new host.</para>
///
/// <para><b>Why the migration lives here rather than in the Photino host.</b> <c>Preferences</c> is a
/// MAUI API, and its store is not somewhere another process can reliably find: on the machine this
/// was written on it is in neither the registry, nor a WinRT settings container, nor the
/// application's own data folder. Rather than reverse engineer that and depend on the answer, the app
/// that owns the data hands it over. The Photino host contains no migration code at all, which is the
/// best kind: the one that has already happened by the time it is needed.</para>
///
/// <para><b>Nothing is deleted.</b> <c>Preferences</c> is left exactly as it was, so a user who goes
/// back to an earlier MLQT release still has their settings. During a migration the rollback has to
/// work.</para>
///
/// <para>The consequence worth stating plainly: once this ships, <b>the MAUI app is writing to the
/// new store</b>. That is deliberate — it is what makes the cutover a non-event — but it means a
/// release carrying this change is the point of no return for the settings format, not 7b-8.</para>
/// </remarks>
public class SettingsService : ISettingsService
{
    private readonly JsonSettingsService _store = new();

    public SettingsService()
    {
        try
        {
            // Preferences answers with the default for a key it does not hold, so absent reads as "".
            var copied = _store.SeedFrom(key => Preferences.Get(key, string.Empty));

            if (copied.Count > 0)
                LoggingService.Info(nameof(SettingsService),
                    $"Migrated {copied.Count} setting(s) from MAUI Preferences: {string.Join(", ", copied)}");
        }
        catch (Exception ex)
        {
            // A failed migration must not stop the application starting. The user would see defaults,
            // which is recoverable; a host that will not open is not.
            LoggingService.Error(nameof(SettingsService), "Could not migrate settings from MAUI Preferences", ex);
        }
    }

    /// <inheritdoc />
    public string BackingStore => _store.BackingStore;

    public Task<T> GetAsync<T>(string key, T defaultValue) => _store.GetAsync(key, defaultValue);

    public Task SetAsync<T>(string key, T value) => _store.SetAsync(key, value);

    public Task RemoveAsync(string key) => _store.RemoveAsync(key);

    public Task ClearAsync() => _store.ClearAsync();
}
