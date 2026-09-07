using System.Text.Json;
using ModelicaGraph;
using MLQT.Services;

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
    /// <para>The returned <c>DictionaryRoot</c> is where this run's accepted spellings live — the
    /// settings file's own <c>.mlqt</c> when it has a word list, else the nearest one at or above the
    /// library. See <see cref="DictionaryRootFor"/> for why it is not simply the settings
    /// directory.</para>
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
            return new ResolvedSettings(settings, path, DictionaryRootFor(path, libraryPath));
        }

        if (configPath is not null)
            throw new FileNotFoundException($"config file not found: '{path}'");

        return new ResolvedSettings(
            new StyleCheckingSettings(), "built-in defaults", NearestDictionaryRoot(libraryPath) ?? libraryPath);
    }

    /// <summary>
    /// The nearest <c>.mlqt/settings.json</c> at or above the library, or null if there is none.
    ///
    /// <para>The walk stops at a working-copy root — a directory holding <c>.git</c> or <c>.svn</c> —
    /// so a checkout can never pick up a settings file belonging to something outside it, such as one
    /// left in a shared parent folder or a home directory.</para>
    /// </summary>
    private static string? NearestSettingsFile(string libraryPath) =>
        NearestMlqtFile(libraryPath, "settings.json");

    /// <summary>
    /// The directory whose <c>.mlqt</c> this run belongs to — the repository, when the library sits
    /// inside one — falling back to the library itself when nothing above it has one.
    ///
    /// <para>Public because more than the settings live in that folder. The metrics history does too,
    /// and the desktop app keeps it per <em>repository</em>: composing it from the library path
    /// instead meant <c>mlqt check &lt;repo&gt;/MyLib --metrics</c> read the team's rules from
    /// <c>&lt;repo&gt;/.mlqt</c> and wrote the trend into <c>&lt;repo&gt;/MyLib/.mlqt</c>, a second
    /// file nothing else opens — so <c>--coverage-ratchet</c> also found nothing to compare against
    /// and said so, on a repository with a perfectly good history.</para>
    ///
    /// <para>Walking by the same rule as the settings lookup is the point: whatever <c>.mlqt</c> the
    /// rules came out of is the one the run's own numbers go back into.</para>
    /// </summary>
    public static string RepositoryRootFor(string libraryPath)
    {
        var directory = Directory.Exists(libraryPath)
            ? new DirectoryInfo(libraryPath)
            : new FileInfo(libraryPath).Directory;

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".mlqt")))
                return directory.FullName;

            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".svn")))
                return directory.FullName;

            directory = directory.Parent;
        }

        // No .mlqt and no working copy: a loose library, or a single .mo file. Its own directory is
        // the only sensible home, and is what the library path meant before any of this existed.
        return Directory.Exists(libraryPath)
            ? libraryPath
            : Path.GetDirectoryName(Path.GetFullPath(libraryPath)) ?? libraryPath;
    }

    /// <summary>
    /// The directory holding the nearest <c>.mlqt/dictionary.txt</c> at or above the library, or null
    /// if there is none. Walks by the same rule as the settings lookup.
    /// </summary>
    private static string? NearestDictionaryRoot(string libraryPath)
    {
        var file = NearestMlqtFile(libraryPath, CustomDictionaryService.DictionaryFileName);
        return file is null ? null : Path.GetDirectoryName(Path.GetDirectoryName(file));
    }

    /// <summary>
    /// The nearest <c>.mlqt/&lt;name&gt;</c> at or above the library, or null if there is none.
    ///
    /// <para>The walk stops at a working-copy root — a directory holding <c>.git</c> or <c>.svn</c> —
    /// so a checkout can never pick up a file belonging to something outside it, such as one left in
    /// a shared parent folder or a home directory.</para>
    /// </summary>
    private static string? NearestMlqtFile(string libraryPath, string fileName)
    {
        var directory = Directory.Exists(libraryPath)
            ? new DirectoryInfo(libraryPath)
            : new FileInfo(libraryPath).Directory;

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ".mlqt", fileName);
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
    /// Where this run's accepted spellings live: the directory the settings file's <c>.mlqt</c> sits
    /// in, when that directory has a word list of its own.
    ///
    /// <para>Otherwise the nearest <c>.mlqt/dictionary.txt</c> at or above the library. The settings
    /// and the words are two files that usually sit together but need not: a run pointed at a shared
    /// rules file with <c>--config</c>, or one falling back to built-in defaults, was reading the
    /// repository's code against nobody's word list, and reported every term the team had accepted
    /// with nothing in the output to say why. The rules being shared is no reason for the repository
    /// to lose its own vocabulary.</para>
    /// </summary>
    private static string DictionaryRootFor(string settingsPath, string libraryPath)
    {
        var beside = RootOf(settingsPath, libraryPath);
        if (File.Exists(Path.Combine(beside, ".mlqt", CustomDictionaryService.DictionaryFileName)))
            return beside;

        return NearestDictionaryRoot(libraryPath) ?? beside;
    }

    /// <summary>The directory a settings file's <c>.mlqt</c> sits in, or the library when the file is
    /// kept somewhere else entirely — a shared rules file outside any repository.</summary>
    private static string RootOf(string settingsPath, string libraryPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(settingsPath));
        if (directory is null || !string.Equals(
                Path.GetFileName(directory), ".mlqt", StringComparison.OrdinalIgnoreCase))
            return libraryPath;

        return Path.GetDirectoryName(directory) ?? libraryPath;
    }
}
