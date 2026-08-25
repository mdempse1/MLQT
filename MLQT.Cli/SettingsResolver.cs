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
    /// Resolves the style-checking settings: an explicit <c>--config</c> path, else
    /// <c>&lt;libraryPath&gt;/.mlqt/settings.json</c> if present, else built-in defaults.
    ///
    /// <para>The returned <c>DictionaryRoot</c> is the directory the settings were found under, so a
    /// run told to use a repository's settings reads that repository's accepted spellings too. When
    /// the two were resolved separately, <c>--config &lt;repo&gt;/.mlqt/settings.json</c> checked a
    /// sub-library against the repository's rules but none of its words, and CI then reported every
    /// term the team had accepted in the app.</para>
    /// </summary>
    public static ResolvedSettings Resolve(string libraryPath, string? configPath)
    {
        var path = configPath is not null
            ? RepoPath.Resolve(libraryPath, configPath)
            : Path.Combine(libraryPath, ".mlqt", "settings.json");

        if (File.Exists(path))
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
