using DymolaInterface;
using MLQT.Services;
using OpenModelicaInterface;

namespace MLQT.McpServer.Helpers;

/// <summary>Where a simulation tool keeps the libraries it loads, which install that came from, and why
/// that install (<see cref="InstallReason"/>).</summary>
internal sealed record ToolFolders(IReadOnlyList<string> Folders, string? Install, string? Note, string? InstallReason = null);

/// <summary>
/// The library folders of the simulation tool a user works with, for the dependency search
/// (<c>.claude/skills/mcp-library-loading/SKILL.md</c>, *Finding dependencies*).
/// </summary>
/// <remarks>
/// <para><b>Which install.</b> A machine often has many Dymola releases, and the one in use is not
/// necessarily the newest. So the install is, in order: the one the caller names; for Dymola, the one
/// running now; the one set in the desktop app's External Tools settings; the newest found by the
/// detection the app already uses, with the others installed named beside it.</para>
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
    /// <param name="findDymola">Detection of the newest Dymola, <see cref="DymolaSettings.FindInstalledDymola"/>
    /// unless a test says otherwise: what it finds is the machine's, so a test on a machine with Dymola and
    /// one on a build agent without it would otherwise run different code.</param>
    /// <param name="findOmc">The same for OpenModelica, <see cref="OpenModelicaSettings.FindInstalledOmc"/>.</param>
    /// <param name="runningDymola">The Dymola running now, asked after the caller's choice and before the
    /// desktop settings; null asks nothing. <c>mlqt_load</c> passes <see cref="RunningDymola"/>.</param>
    /// <param name="allDymola">Every Dymola installed, for naming the others when the newest is taken;
    /// by default the machine's, unless a test gave <paramref name="findDymola"/>.</param>
    public static ToolFolders For(string? tool, string? toolInstall, string? desktopSettingsDirectory,
        Func<string, string?> environment, Func<string>? findDymola = null, Func<string>? findOmc = null,
        Func<string?>? runningDymola = null, Func<IReadOnlyList<string>>? allDymola = null)
    {
        switch (tool?.Trim().ToLowerInvariant())
        {
            case null or "" or "none":
                return new ToolFolders([], null, null);
            case "dymola":
            {
                var (install, from, reason) = Choose(toolInstall, runningDymola ?? (() => null),
                    () => DesktopDymola(desktopSettingsDirectory), findDymola ?? DymolaSettings.FindInstalledDymola);
                if (reason == DetectedReason && install is not null)
                    reason += OtherInstalls(install, (allDymola ?? (findDymola is null ? InstalledDymolas : () => []))());
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
                var (install, from, reason) = Choose(toolInstall, () => null, () => DesktopOmc(desktopSettingsDirectory),
                    findOmc ?? OpenModelicaSettings.FindInstalledOmc);
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

    /// <summary>What the answer says when the install is the one running.</summary>
    internal const string RunningReason = "the one running now";

    /// <summary>
    /// The Dymola running on this machine, or null when none is, or more than one release is, or its path
    /// cannot be read. The release running is the one the user simulates with, which the newest installed
    /// need not be: with a 2027x beta installed beside the 2026x Refresh 1 a project used, the search took
    /// the beta's libraries, and MLQT checked against other MSL and DymolaCommands versions than Dymola
    /// simulated with (a 2026-11 session).
    /// </summary>
    internal static string? RunningDymola() => TheOneRunning(RunningDymolaPaths());

    /// <summary>
    /// The one executable <paramref name="paths"/> name, or null when they name none or more than one:
    /// with two releases running, which one the user means is theirs to say. Unreadable paths are null.
    /// </summary>
    internal static string? TheOneRunning(IEnumerable<string?> paths)
    {
        var distinct = paths.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    // The executable of each process named dymola; null for one this process may not read - another
    // user's, or one that has just exited.
    private static IEnumerable<string?> RunningDymolaPaths()
    {
        System.Diagnostics.Process[] processes;
        try
        {
            processes = System.Diagnostics.Process.GetProcessesByName("dymola");
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
        {
            yield break;
        }
        foreach (var process in processes)
        {
            using (process)
                yield return MainModuleOf(process);
        }
    }

    private static string? MainModuleOf(System.Diagnostics.Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    // Every Dymola installed on this machine, as the desktop app's detection orders them.
    private static IReadOnlyList<string> InstalledDymolas() =>
        InstalledDymolas(OperatingSystem.IsWindows(), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetEnvironmentVariable("PATH"));

    /// <summary>The Dymola executables that exist among the places detection looks, in its order.</summary>
    internal static IReadOnlyList<string> InstalledDymolas(bool windows, string programFiles, string? pathVariable) =>
        DymolaSettings.CandidateDymolaPaths(windows, programFiles, pathVariable, DateTime.Now.Year + 1)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The installs not taken, when the newest was: a machine with several releases is the case where the
    /// newest is most likely the wrong one, and a beta most of all, so the answer names them and says so.
    /// </summary>
    internal static string OtherInstalls(string taken, IReadOnlyList<string> all)
    {
        var root = InstallRoot(taken);
        var others = all.Select(InstallRoot)
            .Where(r => !string.Equals(r, root, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var beta = Path.GetFileName(root).Contains("beta", StringComparison.OrdinalIgnoreCase)
            ? " It is a beta release; a project is more often simulated with a released one."
            : string.Empty;
        return others.Count == 0
            ? (beta.Length > 0 ? "." + beta : string.Empty)
            : $". Also installed: {string.Join(", ", others.Select(o => $"'{o}'"))}.{beta}";
    }

    // The caller's choice, else the Dymola running, else the desktop settings, else detection, saying which.
    private static (string? Install, string From, string? Reason) Choose(string? given, Func<string?> running,
        Func<string?> desktop, Func<string> detect)
    {
        if (!string.IsNullOrWhiteSpace(given))
            return (given, $"'{given}', as given", "as given");
        if (running() is { Length: > 0 } now)
            return (now, $"'{now}', the one running now", RunningReason);
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
