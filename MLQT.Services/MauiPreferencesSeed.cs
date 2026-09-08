namespace MLQT.Services;

/// <summary>
/// Copies a user's settings out of MAUI's <c>Preferences</c> and into the JSON store both hosts use.
/// </summary>
/// <remarks>
/// <para>Phase 7b-3, and the answer to the phase's highest silent risk. An existing user's project
/// list, repository settings, themes and external-tool paths live in MAUI <c>Preferences</c>; the
/// Photino host reads a JSON file; and nothing connects the two. Cutting over without this resets
/// every one of them, on a path no probe can see and no test would have failed on.</para>
///
/// <para><b>The MAUI host does the seeding, not the Photino one</b>, and that is the whole design.
/// <c>Preferences</c> is a MAUI API that can only be read by a MAUI app, and its store is not
/// somewhere another process can reliably find — on this developer's own machine it is in neither the
/// registry, nor a WinRT settings container, nor the application's data folder. Rather than reverse
/// engineer that, the app that <i>owns</i> the data hands it over: MAUI seeds the shared file once,
/// so by the time anybody runs the Photino host the migration has already happened and the new host
/// contains no migration code at all.</para>
///
/// <para><b>It never deletes anything.</b> <c>Preferences</c> is left exactly as it was, so a user who
/// goes back to a previous MLQT release still has their settings. That matters more than tidiness
/// during a migration: the rollback has to work.</para>
/// </remarks>
public static class MauiPreferencesSeed
{
    /// <summary>Marks the store as seeded, so a user's later edits are never overwritten.</summary>
    public const string CompletedKey = "__MauiPreferencesSeeded";

    /// <summary>
    /// Copies each known key that the old store has and the new store does not.
    /// </summary>
    /// <param name="readOldValue">
    /// Reads a raw value from MAUI's <c>Preferences</c>, or returns null when it holds nothing for
    /// that key. A delegate rather than an interface because only the MAUI host can supply it, and
    /// this assembly must not depend on MAUI.
    /// </param>
    /// <param name="readNewValue">Reads the raw value already in the JSON store, if any.</param>
    /// <param name="writeNewValue">Writes a raw value into the JSON store.</param>
    /// <param name="alreadySeeded">Whether the marker is present.</param>
    /// <returns>The keys copied, for logging.</returns>
    /// <remarks>
    /// <para>Runs <b>once</b>, and skips any key the new store already has. Both guards matter and
    /// they guard different things: the marker stops a later run resurrecting a setting the user
    /// deliberately changed in the new host, and the per-key check stops the seed clobbering
    /// settings made in the new host before the old one was next opened. Users will run the two
    /// alternately during a migration, and the newer store must win.</para>
    /// </remarks>
    public static IReadOnlyList<string> Seed(
        Func<string, string?> readOldValue,
        Func<string, string?> readNewValue,
        Action<string, string> writeNewValue,
        bool alreadySeeded)
    {
        if (alreadySeeded)
            return [];

        var copied = new List<string>();

        foreach (var key in MlqtSettingsKeys.All)
        {
            // Anything already in the new store is newer than anything in the old one, by definition:
            // the old host cannot write there.
            if (!string.IsNullOrEmpty(readNewValue(key)))
                continue;

            var value = readOldValue(key);
            if (string.IsNullOrEmpty(value))
                continue;

            writeNewValue(key, value);
            copied.Add(key);
        }

        return copied;
    }
}
