using DymolaInterface;
using MLQT.Services;
using OpenModelicaInterface;

namespace MLQT.McpServer.Helpers;

/// <summary>Where a simulation tool keeps the libraries it loads, which install that came from, and why
/// that install (<see cref="InstallReason"/>).</summary>
internal sealed record ToolFolders(IReadOnlyList<string> Folders, string? Install, string? Note, string? InstallReason = null,
    string? Error = null);

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
    /// <param name="allOmc">Every omc installed, for finding the release <paramref name="tool"/> names; by
    /// default the machine's, unless a test gave <paramref name="findOmc"/>.</param>
    public static ToolFolders For(string? tool, string? toolInstall, string? desktopSettingsDirectory,
        Func<string, string?> environment, Func<string>? findDymola = null, Func<string>? findOmc = null,
        Func<string?>? runningDymola = null, Func<IReadOnlyList<string>>? allDymola = null,
        Func<IReadOnlyList<string>>? allOmc = null)
    {
        if (ParseTool(tool) is not { } parsed)
            return new ToolFolders([], null, null, Error: UnknownTool(tool));
        var (kind, release) = parsed;
        switch (kind)
        {
            case null:
                return new ToolFolders([], null, null);
            case "dymola":
            {
                var choice = release is null
                    ? Choose(toolInstall, runningDymola ?? (() => null),
                        () => DesktopDymola(desktopSettingsDirectory), findDymola ?? DymolaSettings.FindInstalledDymola)
                    : ChooseDymolaRelease(release, toolInstall, runningDymola ?? (() => null),
                        () => DesktopDymola(desktopSettingsDirectory),
                        allDymola ?? (findDymola is null ? InstalledDymolas : () => [findDymola()]));
                if (choice.Error is { } error)
                    return new ToolFolders([], null, null, Error: error);
                var (install, from, reason) = (choice.Install, choice.From, choice.Reason);
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
                var choice = release is null
                    ? Choose(toolInstall, () => null, () => DesktopOmc(desktopSettingsDirectory),
                        findOmc ?? OpenModelicaSettings.FindInstalledOmc)
                    : ChooseOmcRelease(release, toolInstall, () => DesktopOmc(desktopSettingsDirectory),
                        allOmc ?? (findOmc is null ? InstalledOmcs : () => [findOmc()]));
                if (choice.Error is { } error)
                    return new ToolFolders([], null, null, Error: error);
                var (install, from, reason) = (choice.Install, choice.From, choice.Reason);
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
                return new ToolFolders([], null, null, Error: UnknownTool(tool));
        }
    }

    internal static string UnknownTool(string? tool) =>
        $"tool must be the simulator and its release - 'Dymola 2026x Refresh 1', 'OpenModelica 1.27.0' - or 'none', not '{tool}'.";

    /// <summary>
    /// The simulator <paramref name="tool"/> names and the release it gives, if any: <c>Dymola 2026x Refresh 1</c>
    /// is <c>("dymola", "2026x Refresh 1")</c>, a bare <c>dymola</c> is <c>("dymola", null)</c>, and <c>none</c> or
    /// nothing is <c>(null, null)</c>. Null when it names no simulator MLQT knows.
    /// </summary>
    /// <remarks>The release is asked for because the newest install need not be the one the simulator was told
    /// to open: a Dymola server is started with a release, and a bare <c>dymola</c> searched another's library
    /// folder, so MLQT checked against other MSL and DymolaCommands versions than Dymola simulated with.</remarks>
    internal static (string? Kind, string? Release)? ParseTool(string? tool)
    {
        var text = tool?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase))
            return (null, null);
        foreach (var kind in new[] { "openmodelica", "dymola" })
        {
            if (!text.StartsWith(kind, StringComparison.OrdinalIgnoreCase))
                continue;
            var rest = text[kind.Length..].Trim(' ', '-', '_', '\t');
            // 'dymolax' is not Dymola: the release has to be set off, or start with its year.
            if (rest.Length > 0 && text.Length > kind.Length && char.IsLetter(text[kind.Length]))
                return null;
            return (kind, rest.Length == 0 ? null : rest);
        }
        return null;
    }

    /// <summary>A Dymola release in any text that carries one: its year, the <c>x</c> of an autumn release, a
    /// refresh and a beta, as <c>DymolaSettings</c> reads install folders. The first such in the text is taken,
    /// so the date after it in what <c>DymolaVersion()</c> reports is not.</summary>
    private static readonly System.Text.RegularExpressions.Regex DymolaRelease = new(
        @"(?<!\d)(?<year>\d{4})(?<x>x?)(?![\d])(?:[\s_-]*Refresh[\s_-]*(?<refresh>\d+))?(?:[\s_-]*Beta[\s_-]*(?<beta>\d*))?",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The Dymola release <paramref name="text"/> names, as a key to compare, or null when it names none:
    /// <c>Dymola 2026x Refresh 1</c>, <c>2026x refresh1</c>, <c>dymola-2026xRefresh1-x86_64</c> and
    /// <c>Dymola Version 2026x Refresh 1 (64-bit), 2025-10-09</c> are all <c>2026x r1</c>, and a beta keeps its
    /// beta, so <c>2027x Beta 2</c> is never the release it precedes.
    /// </summary>
    internal static string? DymolaReleaseKey(string text)
    {
        var m = DymolaRelease.Match(text);
        if (!m.Success)
            return null;
        var key = m.Groups["year"].Value + m.Groups["x"].Value.ToLowerInvariant();
        if (m.Groups["refresh"].Success)
            key += " r" + int.Parse(m.Groups["refresh"].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (m.Groups["beta"].Success)
            key += " b" + m.Groups["beta"].Value;
        return key;
    }

    /// <summary>
    /// The release an install path names, or null when it names none: the folder of
    /// <c>Dymola 2026x Refresh 1\bin64\dymola.exe</c>, <c>/opt/dymola-2026x-x86_64/bin64/dymola</c>, or a Linux
    /// launcher, <c>/usr/local/bin/dymola-2026x-x86_64</c>, whose own name carries it.
    /// </summary>
    internal static string? DymolaReleaseOf(string path)
    {
        var file = Path.GetFileNameWithoutExtension(path.TrimEnd('/', '\\'));
        foreach (var name in new[] { file, Path.GetFileName(InstallRoot(path)) })
        {
            if (name.StartsWith("dymola", StringComparison.OrdinalIgnoreCase) && DymolaReleaseKey(name) is { } key)
                return key;
        }
        return null;
    }

    /// <summary>The install a search takes, how it is described (<see cref="From"/> in the note,
    /// <see cref="Reason"/> as <c>toolInstallChosen</c>), or why none can be: a release named that is not
    /// installed is an <see cref="Error"/>, never a fall back to another release.</summary>
    private sealed record InstallChoice(string? Install, string From, string? Reason, string? Error = null)
    {
        public static InstallChoice Refused(string error) => new(null, string.Empty, null, error);
    }

    /// <summary>
    /// The Dymola install of the release named: <paramref name="given"/> when it is that release or its path
    /// names none, else the first of the one running, the desktop settings' and those installed that is.
    /// None is an error, naming what is installed, so a wrong release is never searched in its place.
    /// </summary>
    private static InstallChoice ChooseDymolaRelease(string release,
        string? given, Func<string?> running, Func<string?> desktop, Func<IReadOnlyList<string>> installed)
    {
        var match = DymolaRelease.Match(release);
        if (!match.Success)
            return InstallChoice.Refused($"'Dymola {release}' names no Dymola release; give it as 'Dymola 2026x Refresh 1'.");
        var wanted = DymolaReleaseKey(release);
        var label = $"Dymola {match.Value.Trim()}";
        if (!string.IsNullOrWhiteSpace(given))
        {
            var givenRelease = DymolaReleaseOf(given);
            return givenRelease is null || givenRelease == wanted
                ? new InstallChoice(given, $"'{given}', as given", "as given")
                : InstallChoice.Refused($"toolInstall '{given}' is not {label}, which tool names; pass the install of " +
                                        "the release the simulator runs, or name its release in tool.");
        }

        var all = new[] { running(), desktop() }.Concat(installed())
            .Where(p => !string.IsNullOrEmpty(p)).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // A Linux launcher names the release but is not the install, so a folder with the library goes first.
        var install = all.Where(p => DymolaReleaseOf(p) == wanted)
            .OrderByDescending(p => Directory.Exists(Path.Combine(InstallRoot(p), "Modelica", "Library")))
            .FirstOrDefault();
        if (install is not null)
            return new InstallChoice(install, $"'{install}', the {label} named", $"the {label} named in tool");

        var found = all.Select(InstallRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return InstallChoice.Refused($"No {label} was found on this machine" +
            (found.Count == 0 ? "" : $"; installed: {string.Join(", ", found.Select(f => $"'{f}'"))}") +
            ". Name one of those in tool, or pass toolInstall with the install's folder.");
    }

    private static readonly System.Text.RegularExpressions.Regex OmcVersion = new(@"\d+\.\d+(?:\.\d+)?",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The OpenModelica version an install path names, as <c>OpenModelica1.27.0-64bit</c> does on
    /// Windows; null for one that names none, as <c>/usr/bin/omc</c> on Linux does.</summary>
    internal static string? OmcVersionOf(string path) =>
        OmcVersion.Match(Path.GetFileName(InstallRoot(path))) is { Success: true } m ? m.Value : null;

    // 1.27 is any 1.27.x; 1.27.0 is only itself.
    private static bool OmcVersionMatches(string installed, string wanted) =>
        installed == wanted || installed.StartsWith(wanted + ".", StringComparison.Ordinal);

    /// <summary>
    /// The omc install of the version named, as <see cref="ChooseDymolaRelease"/> does for Dymola, except that an
    /// install whose path names no version - Linux's <c>/usr/bin/omc</c> - is taken when none names it, saying
    /// so: its libraries are in the user's library folder whatever the release.
    /// </summary>
    private static InstallChoice ChooseOmcRelease(string release,
        string? given, Func<string?> desktop, Func<IReadOnlyList<string>> installed)
    {
        if (OmcVersion.Match(release) is not { Success: true } version)
            return InstallChoice.Refused($"'OpenModelica {release}' names no OpenModelica version; give it as 'OpenModelica 1.27.0'.");
        var wanted = version.Value;
        var label = $"OpenModelica {wanted}";
        if (!string.IsNullOrWhiteSpace(given))
        {
            var givenVersion = OmcVersionOf(given);
            return givenVersion is null || OmcVersionMatches(givenVersion, wanted)
                ? new InstallChoice(given, $"'{given}', as given", "as given")
                : InstallChoice.Refused($"toolInstall '{given}' is not {label}, which tool names.");
        }

        var all = new[] { desktop() }.Concat(installed())
            .Where(p => !string.IsNullOrEmpty(p)).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (all.FirstOrDefault(p => OmcVersionOf(p) is { } v && OmcVersionMatches(v, wanted)) is { } match)
            return new InstallChoice(match, $"'{match}', the {label} named", $"the {label} named in tool");
        if (all.FirstOrDefault(p => OmcVersionOf(p) is null) is { } unversioned)
            return new InstallChoice(unversioned, $"'{unversioned}', whose path names no version",
                $"its path names no version, so it could not be checked against {label}");
        if (all.Count == 0)
            return new InstallChoice(null, string.Empty, null);
        return InstallChoice.Refused($"No {label} was found on this machine; installed: " +
            string.Join(", ", all.Select(p => $"'{InstallRoot(p)}'")) +
            ". Name one of those in tool, or pass toolInstall with the install's folder.");
    }

    // Every omc installed on this machine, as the desktop app's detection orders them.
    private static IReadOnlyList<string> InstalledOmcs() =>
        OpenModelicaSettings.CandidateOmcPaths(OperatingSystem.IsWindows(),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetEnvironmentVariable("PATH"), DateTime.Now.Year + 1 - 2000)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>What the answer says when the install was detected: the newest release on the machine
    /// need not be the one the simulator is running, and only the caller can know that (B504).</summary>
    internal const string DetectedReason =
        "the newest found on this machine, because tool named no release; if the simulator runs another, " +
        "name it in tool, as 'Dymola 2026x Refresh 1' or 'OpenModelica 1.27.0'";

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
    private static InstallChoice Choose(string? given, Func<string?> running,
        Func<string?> desktop, Func<string> detect)
    {
        if (!string.IsNullOrWhiteSpace(given))
            return new InstallChoice(given, $"'{given}', as given", "as given");
        if (running() is { Length: > 0 } now)
            return new InstallChoice(now, $"'{now}', the one running now", RunningReason + NoReleaseNamed);
        if (desktop() is { Length: > 0 } fromSettings)
            return new InstallChoice(fromSettings, $"'{fromSettings}', from the desktop app's External Tools settings",
                "from the desktop app's External Tools settings" + NoReleaseNamed);
        var detected = detect();
        return string.IsNullOrEmpty(detected)
            ? new InstallChoice(null, string.Empty, null)
            : new InstallChoice(detected, $"'{detected}', the newest found", DetectedReason);
    }

    /// <summary>What a guessed install's reason ends with: the running one or the desktop settings' is a good
    /// guess, but only the release the simulator's server was told to open is sure.</summary>
    internal const string NoReleaseNamed = ", because tool named no release; name the one the simulator runs in tool to be sure";

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
