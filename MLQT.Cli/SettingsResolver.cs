using System.Text.Json;
using ModelicaGraph;

namespace MLQT.Cli;

/// <summary>What the settings lookup found: the settings, where they came from, and the directory
/// whose <c>.mlqt</c> holds them — which is also where the accepted spellings live.</summary>
internal sealed record ResolvedSettings(
    StyleCheckingSettings Settings, string Source, string DictionaryRoot);

internal static class SettingsResolver
{
    /// <summary>
    /// Resolves the style-checking settings: an explicit <c>--config</c> path, else the nearest
    /// <c>.mlqt/settings.json</c> at or above the library, else built-in defaults.
    ///
    /// <para>Searching upwards is what the desktop app does — settings belong to a repository, and a
    /// repository usually holds several libraries with one <c>.mlqt</c> at its root. Looking only in
    /// the library meant <c>mlqt check &lt;repo&gt;/MyLib</c> silently fell back to built-in defaults
    /// while the app checked the same library against the team's rules.</para>
    ///
    /// <para>The returned <c>DictionaryRoot</c> is the directory the settings were found under, so a
    /// run reads the accepted spellings that belong with the rules it is using. Resolved separately,
    /// <c>--config &lt;repo&gt;/.mlqt/settings.json</c> checked a sub-library against the repository's
    /// rules but none of its words, and CI then reported every term the team had accepted.</para>
    /// </summary>
    public static ResolvedSettings Resolve(string libraryPath, string? configPath)
    {
        var path = configPath is not null
            ? RepoPath.Resolve(libraryPath, configPath)
            : NearestSettingsFile(libraryPath);

        if (path is not null && File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<StyleCheckingSettings>(json)
                ?? throw new InvalidOperationException($"could not parse settings from '{path}'");
            return new ResolvedSettings(settings, path, RootOf(path, libraryPath));
        }

        if (configPath is not null)
            throw new FileNotFoundException($"config file not found: '{path}'");

        return new ResolvedSettings(new StyleCheckingSettings(), "built-in defaults", libraryPath);
    }

    /// <summary>
    /// The nearest <c>.mlqt/settings.json</c> at or above the library, or null if there is none.
    ///
    /// <para>The walk stops at a working-copy root — a directory holding <c>.git</c> or <c>.svn</c> —
    /// so a checkout can never pick up a settings file belonging to something outside it, such as one
    /// left in a shared parent folder or a home directory.</para>
    /// </summary>
    private static string? NearestSettingsFile(string libraryPath)
    {
        var directory = Directory.Exists(libraryPath)
            ? new DirectoryInfo(libraryPath)
            : new FileInfo(libraryPath).Directory;

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ".mlqt", "settings.json");
            if (File.Exists(candidate))
                return candidate;

            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".svn")))
                return null;

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>
    /// The directory a settings file's <c>.mlqt</c> sits in. A config kept somewhere else entirely —
    /// a shared rules file outside any repository — has no accepted spellings of its own, so the
    /// library being checked stays the place to look.
    /// </summary>
    private static string RootOf(string settingsPath, string libraryPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(settingsPath));
        if (directory is null || !string.Equals(
                Path.GetFileName(directory), ".mlqt", StringComparison.OrdinalIgnoreCase))
            return libraryPath;

        return Path.GetDirectoryName(directory) ?? libraryPath;
    }
}
