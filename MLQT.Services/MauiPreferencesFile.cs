using System.Text.Json;

namespace MLQT.Services;

/// <summary>
/// Reads the settings the MAUI build left behind, so the Photino host can adopt them.
/// </summary>
/// <remarks>
/// <para>Phase 7b-3. MAUI stored settings through its <c>Preferences</c> API, which the Photino host
/// cannot call. For an <b>unpackaged</b> Windows app that API turns out to keep them in plain JSON at
/// <c>%LocalAppData%/&lt;publisher&gt;/&lt;application id&gt;/Settings/preferences.dat</c> — not in
/// the registry and not in a WinRT settings container, which is where it was looked for first
/// (B122).</para>
///
/// <para><b>Reading the file rather than the API is what makes this work at all.</b> The first design
/// had the MAUI app copy its own settings over, because <c>Preferences</c> cannot be enumerated and
/// only MAUI can call it — but that needs a MAUI release to ship and be run before the Photino one,
/// which is not acceptable. A file can be enumerated. So the Photino host reads it directly, copies
/// <b>every</b> key it finds, and the MAUI app never changes.</para>
///
/// <para>Copying everything rather than a known list matters more than it sounds. The list this was
/// first written against had six names on it, and the real file has six that are not the same six:
/// it carries a <c>StyleChecking</c> key from a version of MLQT that no longer reads it, and no
/// <c>ReferenceLibraries</c>. A migration that moves what it finds cannot be wrong about what to
/// look for.</para>
///
/// <para>The file is read and never written. A user who goes back to an older MLQT release still has
/// everything, which during a migration matters more than tidiness.</para>
/// </remarks>
public static class MauiPreferencesFile
{
    /// <summary>The application id, which is the folder MAUI files its settings under.</summary>
    /// <remarks>From <c>ApplicationId</c> in <c>MLQT.csproj</c>; MAUI derives the path from it.</remarks>
    public const string ApplicationId = "com.mlqtproject.MLQT";

    /// <summary>
    /// Finds the MAUI settings file, or returns null when there is nothing to migrate.
    /// </summary>
    /// <remarks>
    /// The publisher segment is searched for rather than hard-coded. It comes from the
    /// <c>Publisher</c> in the Windows app manifest — currently the MAUI template's default,
    /// <c>CN=User Name</c>, so the folder is literally "User Name" — and a build that set a real
    /// publisher would file its settings somewhere else. One level of wildcard costs nothing and
    /// removes a way for this to quietly find nothing.
    /// </remarks>
    public static string? Locate(string? localApplicationData = null)
    {
        var root = localApplicationData
                   ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (!Directory.Exists(root))
            return null;

        try
        {
            return Directory.EnumerateDirectories(root)
                .Select(publisher => Path.Combine(publisher, ApplicationId, "Settings", "preferences.dat"))
                .Where(File.Exists)
                // Newest wins if a machine somehow has two, which is better than an arbitrary one.
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            LoggingService.Error(nameof(MauiPreferencesFile), $"Could not search {root} for MAUI settings", ex);
            return null;
        }
    }

    /// <summary>
    /// Reads every setting in the file, or an empty set when it cannot be read.
    /// </summary>
    /// <remarks>
    /// <para>The shape is <c>{"&lt;container&gt;": {"&lt;key&gt;": "&lt;value&gt;"}}</c>. MAUI supports
    /// named containers and MLQT only ever used the default one, whose name is the empty string —
    /// but every container is read rather than just that one, because a key MLQT does not recognise
    /// costs nothing and a key it silently drops costs a user their settings.</para>
    ///
    /// <para>Values are already the JSON strings MLQT stored, which is exactly what
    /// <see cref="JsonSettingsService"/> holds, so this is a copy rather than a conversion.</para>
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));

            foreach (var container in document.RootElement.EnumerateObject())
            {
                if (container.Value.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var setting in container.Value.EnumerateObject())
                {
                    if (setting.Value.ValueKind == JsonValueKind.String &&
                        setting.Value.GetString() is { Length: > 0 } value)
                    {
                        settings[setting.Name] = value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // A settings file that cannot be parsed is a migration that does not happen, not a host
            // that will not start. The user sees defaults and their old install is untouched.
            LoggingService.Error(nameof(MauiPreferencesFile), $"Could not read {path}", ex);
        }

        return settings;
    }
}
