using System.Text.Json;
using ModelicaGraph;

namespace MLQT.Cli;

internal static class SettingsResolver
{
    /// <summary>
    /// Resolves the style-checking settings: an explicit <c>--config</c> path, else
    /// <c>&lt;libraryPath&gt;/.mlqt/settings.json</c> if present, else built-in defaults.
    /// </summary>
    public static StyleCheckingSettings Resolve(string libraryPath, string? configPath, out string source)
    {
        var path = configPath ?? Path.Combine(libraryPath, ".mlqt", "settings.json");

        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<StyleCheckingSettings>(json)
                ?? throw new InvalidOperationException($"could not parse settings from '{path}'");
            source = path;
            return settings;
        }

        if (configPath is not null)
            throw new FileNotFoundException($"config file not found: '{configPath}'");

        source = "built-in defaults";
        return new StyleCheckingSettings();
    }
}
