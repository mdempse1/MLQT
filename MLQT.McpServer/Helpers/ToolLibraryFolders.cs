using DymolaInterface;
using MLQT.Services;
using OpenModelicaInterface;

namespace MLQT.McpServer.Helpers;

/// <summary>Where a simulation tool keeps the libraries it loads, which install that came from, and why
/// that install (<see cref="InstallReason"/>).</summary>
internal sealed record ToolFolders(IReadOnlyList<string> Folders, string? Install, string? Note, string? InstallReason = null);

/// <summary>
/// The library folders of the simulation tool a user works with, for the dependency search
/// (<c>Design/mcp-tool-consolidation.md</c>, *Finding dependencies*).
/// </summary>
/// <remarks>
/// <para><b>Which install.</b> A machine often has many Dymola releases, and the one in use is not
/// necessarily the newest. So the install is, in order: the one the caller names; the one set in the
/// desktop app's External Tools settings; the newest found by the detection the app already uses.</para>
/// <para><b>Dymola</b> keeps its libraries in <c>&lt;install&gt;/Modelica/Library</c>, which it puts first in
/// its MODELICAPATH. <b>OpenModelica</b> keeps them in the user's library folder
/// (<c>%AppData%/.openmodelica/libraries</c>, <c>~/.openmodelica/libraries</c>), or where
/// <c>OPENMODELICALIBRARY</c> says; since 1.19 its install ships none, so an older install's
/// <c>lib/omlibrary</c> is added only when it exists.</para>
/// <para><b>The desktop settings are read, never written</b>, and only when the file is there: the server
/// keeps its own settings so that an agent does not inherit a person's project, and a tool's path is not
/// a project.</para>
/// </remarks>
internal static class ToolLibraryFolders
{
    public static ToolFolders For(string? tool, string? toolInstall, string? desktopSettingsDirectory,
        Func<string, string?> environment)
    {
        switch (tool?.Trim().ToLowerInvariant())
        {
            case null or "" or "none":
                return new ToolFolders([], null, null);
            case "dymola":
            {
                var (install, from, reason) = Choose(toolInstall, () => DesktopDymola(desktopSettingsDirectory),
                    DymolaSettings.FindInstalledDymola);
                if (install is null)
                    return new ToolFolders([], null, "No Dymola install was found; pass toolInstall with its folder.");
                var root = InstallRoot(install);
                var library = Path.Combine(root, "Modelica", "Library");
                return new ToolFolders(Directory.Exists(library) ? [library] : [], root,
                    Directory.Exists(library) ? $"Dymola install {from}." : $"Dymola install {from} has no {library}.",
                    reason);
            }
            case "openmodelica":
            {
                var folders = new List<string>();
                if (environment("OPENMODELICALIBRARY") is { Length: > 0 } configured)
                    folders.AddRange(configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                else
                    folders.Add(OperatingSystem.IsWindows()
                        ? Path.Combine(environment("APPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".openmodelica", "libraries")
                        : Path.Combine(environment("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".openmodelica", "libraries"));
                var (install, from, reason) = Choose(toolInstall, () => DesktopOmc(desktopSettingsDirectory), OpenModelicaSettings.FindInstalledOmc);
                string? root = null;
                if (install is not null)
                {
                    root = InstallRoot(install);
                    var bundled = Path.Combine(root, "lib", "omlibrary");
                    if (Directory.Exists(bundled))
                        folders.Add(bundled);
                }
                return new ToolFolders(folders.Where(Directory.Exists).ToList(), root,
                    root is null ? "OpenModelica's user library folder." : $"OpenModelica's user library folder, and install {from}.",
                    reason);
            }
            default:
                return new ToolFolders([], null, $"tool must be 'dymola', 'openmodelica' or 'none', not '{tool}'.");
        }
    }

    /// <summary>What the answer says when the install was detected: the newest release on the machine
    /// need not be the one the simulator is running, and only the caller can know that (B504).</summary>
    internal const string DetectedReason =
        "the newest found on this machine; if the simulator is running another release, pass toolInstall";

    // The caller's choice, else the desktop settings, else detection, saying which it was.
    private static (string? Install, string From, string? Reason) Choose(string? given, Func<string?> desktop, Func<string> detect)
    {
        if (!string.IsNullOrWhiteSpace(given))
            return (given, $"'{given}', as given", "as given");
        if (desktop() is { Length: > 0 } fromSettings)
            return (fromSettings, $"'{fromSettings}', from the desktop app's External Tools settings",
                "from the desktop app's External Tools settings");
        var detected = detect();
        return string.IsNullOrEmpty(detected)
            ? (null, string.Empty, null)
            : (detected, $"'{detected}', the newest found", DetectedReason);
    }

    /// <summary>The install folder for an executable or a folder: <c>bin64\dymola.exe</c> and
    /// <c>bin\omc.exe</c> are two levels down, a <c>bin</c> folder one.</summary>
    internal static string InstallRoot(string path)
    {
        // An executable's path names its folder even when the file is gone: a settings file can outlive
        // the install it points at, and the folder is still the thing to report.
        var trimmed = path.TrimEnd('/', '\\');
        var isFile = File.Exists(trimmed) || (!Directory.Exists(trimmed) && Path.HasExtension(trimmed));
        var folder = isFile ? Path.GetDirectoryName(trimmed) ?? trimmed : trimmed;
        var leaf = Path.GetFileName(folder);
        return leaf is "bin64" or "bin" ? Path.GetDirectoryName(folder) ?? folder : folder;
    }

    private static string? DesktopDymola(string? directory) =>
        DesktopSettings(directory) is { } s ? s.GetAsync<DymolaSettings?>("Dymola", null).GetAwaiter().GetResult()?.DymolaPath : null;

    private static string? DesktopOmc(string? directory) =>
        DesktopSettings(directory) is { } s ? s.GetAsync<OpenModelicaSettings?>("OpenModelica", null).GetAwaiter().GetResult()?.OmcPath : null;

    // Only an existing file is opened, so that nothing is created where the desktop app keeps its data.
    private static JsonSettingsService? DesktopSettings(string? directory)
    {
        if (directory is null || !File.Exists(Path.Combine(directory, "settings.json")))
            return null;
        try
        {
            return new JsonSettingsService(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
