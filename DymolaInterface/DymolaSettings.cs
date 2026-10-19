using System.Text.Json.Serialization;

namespace DymolaInterface;

public class DymolaSettings : IJsonOnDeserialized
{
    public string DymolaPath { get; set; } = string.Empty;
    public string HostAddress { get; } = "127.0.0.1";
    public int PortNumber { get; set; } = 8082;

    /// <summary>
    /// How long one command may take before MLQT stops waiting for it, in milliseconds; 0 for no
    /// limit. Five minutes by default, which is what the interface used before it could be changed.
    /// </summary>
    /// <remarks>
    /// <para>What a user raises for a model that takes longer to check than that (B263). Milliseconds,
    /// and named like <c>OpenModelicaSettings.CommandTimeoutMs</c>, so the two tools read the same way
    /// in the settings file and the dialog can treat them alike.</para>
    ///
    /// <para>An <c>int</c> of milliseconds stops at about 24.8 days, inside
    /// <see cref="DymolaInterface.MaxCommandTimeout"/> (≈49.7 days) — so no value this can hold is one
    /// the interface would refuse. That matters because a refused timeout throws, and the command
    /// wrapper that would catch it reads as every command failing.</para>
    /// </remarks>
    public int CommandTimeoutMs { get; set; } = 300_000;

    /// <summary>
    /// <see cref="CommandTimeoutMs"/> as the interface takes it: <see cref="Timeout.InfiniteTimeSpan"/>
    /// for 0 (no limit), and the five-minute default for a negative value, which the dialog does not
    /// allow and a hand-edited settings file might.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan CommandTimeout => CommandTimeoutMs switch
    {
        0 => Timeout.InfiniteTimeSpan,
        < 0 => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromMilliseconds(CommandTimeoutMs),
    };

    /// <summary>The oldest release auto-detection looks for, as MLQT's documented minimum.</summary>
    public const int OldestDetectedYear = 2021;

    /// <summary>
    /// Creates settings with default values, and the newest Dymola this machine has - see
    /// <see cref="FindInstalledDymola()"/>.
    /// </summary>
    public DymolaSettings()
    {
        if (string.IsNullOrEmpty(DymolaPath))
            DymolaPath = FindInstalledDymola();
    }

    /// <summary>
    /// A blank path read from saved settings is looked for again. The constructor's search runs
    /// before the saved values are applied, so a saved <c>""</c> overwrote whatever it found and
    /// blanking the field turned detection off for good - though a blank path can never work (B395).
    /// </summary>
    void IJsonOnDeserialized.OnDeserialized()
    {
        if (string.IsNullOrEmpty(DymolaPath))
            DymolaPath = FindInstalledDymola();
    }

    /// <summary>Dymola's file name on this platform: <c>dymola.exe</c> on Windows, <c>dymola</c> elsewhere.</summary>
    public static string DymolaExecutableName => ExecutableName(OperatingSystem.IsWindows());

    private static string ExecutableName(bool windows) => windows ? "dymola.exe" : "dymola";

    /// <summary>
    /// The Dymola a user means by choosing <paramref name="folder"/> in the settings dialog on
    /// Windows: the installation's <c>bin64\dymola.exe</c>, or <c>dymola.exe</c> in the folder itself
    /// when they chose <c>bin64</c>.
    /// </summary>
    public static string DymolaUnder(string folder)
    {
        var direct = Path.Combine(folder, "dymola.exe");
        return File.Exists(direct) ? direct : Path.Combine(folder, "bin64", "dymola.exe");
    }

    /// <summary>The newest Dymola installed on this machine, or empty when none is found.</summary>
    public static string FindInstalledDymola() => FindInstalledDymola(
        OperatingSystem.IsWindows(),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetEnvironmentVariable("PATH"),
        DateTime.Now.Year + 1);

    /// <summary>The first of <see cref="CandidateDymolaPaths"/> that exists, or empty.</summary>
    public static string FindInstalledDymola(bool windows, string programFiles, string? pathVariable, int newestYear,
        string optFolder = "/opt", string localBinFolder = "/usr/local/bin") =>
        CandidateDymolaPaths(windows, programFiles, pathVariable, newestYear, optFolder, localBinFolder)
            .FirstOrDefault(File.Exists) ?? string.Empty;

    /// <summary>
    /// Where auto-detection looks for Dymola, in order.
    /// </summary>
    /// <remarks>
    /// <para><b>Windows:</b> <c>bin64\dymola.exe</c> under each folder in Program Files named for a
    /// Dymola release, newest first in release order (<see cref="WindowsInstallsNewestFirst"/>); then
    /// <c>dymola.exe</c> in each folder on <c>PATH</c>. Only the names in
    /// <see cref="CandidateInstallNames"/> were probed, so a beta's folder was never found, and the
    /// MCP server's dependency search used an older release's libraries than the Dymola running
    /// (B504).</para>
    ///
    /// <para><b>Linux:</b> Dassault's installation guide puts Dymola in
    /// <c>/opt/dymola-&lt;version&gt;-x86_64</c> and installs a launcher script,
    /// <c>/usr/local/bin/dymola-&lt;version&gt;-x86_64</c>, that sets the environment the program
    /// needs to find its libraries - so launchers are looked at before the program itself:
    /// <c>/usr/local/bin/dymola</c> (the name the setup guides give a launcher), then the versioned
    /// launchers in <paramref name="localBinFolder"/>, then <c>bin64/dymola</c> in each versioned
    /// folder in <paramref name="optFolder"/>, both newest first; then <c>dymola</c> in each folder on
    /// <c>PATH</c>. Only Program Files was ever looked at, so nothing was found on the Linux host
    /// (B395).</para>
    /// </remarks>
    public static IEnumerable<string> CandidateDymolaPaths(bool windows, string programFiles, string? pathVariable,
        int newestYear, string optFolder = "/opt", string localBinFolder = "/usr/local/bin")
    {
        if (windows)
        {
            foreach (var install in WindowsInstallsNewestFirst(programFiles, newestYear))
                yield return Path.Combine(install, "bin64", "dymola.exe");
        }
        else
        {
            yield return Path.Combine(localBinFolder, "dymola");
            foreach (var launcher in NewestFirst(localBinFolder, file: true))
                yield return launcher;
            foreach (var install in NewestFirst(optFolder, file: false))
                yield return Path.Combine(install, "bin64", "dymola");
        }

        if (string.IsNullOrEmpty(pathVariable))
            yield break;

        foreach (var dir in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return Path.Combine(dir.Trim(), ExecutableName(windows));
    }

    /// <summary>
    /// A Linux release's name as its folder and launcher carry it: <c>dymola-2025x-x86_64</c>, with or
    /// without the first hyphen (the installation guide writes it both ways), and anything between the
    /// release and the architecture - a refresh - kept for ordering.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex LinuxReleaseName = new(
        @"^dymola-?(?<year>\d{4})(?<x>x?)(?<rest>.*)-x86_64$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The entries of <paramref name="folder"/> named for a Dymola release, newest first in release
    /// order: a year's spring release, then its <c>x</c> release, then a refresh of that. Empty when the
    /// folder cannot be read.
    /// </summary>
    private static IEnumerable<string> NewestFirst(string folder, bool file)
    {
        string[] entries;
        try
        {
            if (!Directory.Exists(folder))
                return [];
            entries = file ? Directory.GetFiles(folder, "dymola*") : Directory.GetDirectories(folder, "dymola*");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return entries
            .Select(path => (path, match: LinuxReleaseName.Match(Path.GetFileName(path))))
            .Where(e => e.match.Success)
            .OrderByDescending(e => int.Parse(e.match.Groups["year"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ThenByDescending(e => e.match.Groups["x"].Length)
            .ThenByDescending(e => e.match.Groups["rest"].Length)
            .ThenByDescending(e => e.path, StringComparer.Ordinal)
            .Select(e => e.path);
    }

    /// <summary>
    /// A Windows release's folder name: <c>Dymola 2026x</c>, <c>Dymola 2026x Refresh 1</c>, <c>Dymola
    /// 2030x Beta 2</c>, a refresh's beta (<c>Dymola 2026x Refresh 1 Beta 1</c>), or a spring release
    /// (<c>Dymola 2023</c>). Anything else in the name is ignored for ordering.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex WindowsReleaseName = new(
        @"^Dymola (?<year>\d{4})(?<x>x?)(?:\s+Refresh\s*(?<refresh>\d+))?(?:\s+Beta\s*(?<beta>\d*))?\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The folders of <paramref name="programFiles"/> named for a Dymola release from
    /// <see cref="OldestDetectedYear"/> to <paramref name="newestYear"/>, newest first in release
    /// order: by year, a year's <c>x</c> release after its spring one, a refresh after the release it
    /// refreshes, and a beta just before the release of its name - after everything older. Empty when
    /// the folder cannot be read.
    /// </summary>
    private static IEnumerable<string> WindowsInstallsNewestFirst(string programFiles, int newestYear)
    {
        string[] folders;
        try
        {
            if (!Directory.Exists(programFiles))
                return [];
            folders = Directory.GetDirectories(programFiles, "Dymola *");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        static int Number(System.Text.RegularExpressions.Group g) =>
            g.Success && g.Value.Length > 0 ? int.Parse(g.Value, System.Globalization.CultureInfo.InvariantCulture) : 0;

        return folders
            .Select(path => (path, match: WindowsReleaseName.Match(Path.GetFileName(path))))
            .Where(e => e.match.Success)
            .Select(e => (e.path, year: Number(e.match.Groups["year"]), x: e.match.Groups["x"].Length,
                refresh: Number(e.match.Groups["refresh"]), beta: e.match.Groups["beta"].Success,
                betaNumber: Number(e.match.Groups["beta"])))
            .Where(e => e.year >= OldestDetectedYear && e.year <= newestYear)
            .OrderByDescending(e => e.year)
            .ThenByDescending(e => e.x)
            .ThenByDescending(e => e.refresh)
            .ThenBy(e => e.beta)                    // the release before its betas
            .ThenByDescending(e => e.betaNumber)
            .ThenByDescending(e => e.path, StringComparer.Ordinal)
            .Select(e => e.path);
    }

    /// <summary>
    /// The install folders auto-detection looked for before it listed Program Files (B504), newest
    /// release first: for each year from
    /// <paramref name="newestYear"/> back to <see cref="OldestDetectedYear"/>, <c>Dymola {year}x
    /// Refresh 1</c>, <c>Dymola {year}x</c> and <c>Dymola {year}</c>.
    /// </summary>
    /// <remarks>
    /// <para>That is also the order they were released in: the spring release of a year (<c>Dymola
    /// 2023</c>) came out before its autumn one (<c>2023x</c>), which came before the next spring's
    /// refresh (<c>2023x Refresh 1</c>) - and the autumn release named for the next year
    /// (<c>2024x</c>) is newer than all three.</para>
    ///
    /// <para><b>The spring releases used to be missed</b> - only the two <c>x</c> names were probed,
    /// so a machine with <c>Dymola 2022</c> or <c>Dymola 2023</c> and nothing later was found to have
    /// none - and the loop decremented the year before building the refresh name, so it probed
    /// <c>Dymola 2020x Refresh 1</c> while the documentation said 2021 onwards (B337).</para>
    /// </remarks>
    public static IEnumerable<string> CandidateInstallNames(int newestYear)
    {
        for (var year = newestYear; year >= OldestDetectedYear; year--)
        {
            yield return $"Dymola {year}x Refresh 1";
            yield return $"Dymola {year}x";
            yield return $"Dymola {year}";
        }
    }

    /// <summary>
    /// The newest <c>bin64/dymola.exe</c> under <paramref name="programFiles"/>, among its folders
    /// named for a Dymola release; empty when there is none.
    /// </summary>
    public static string FindInstalledDymola(string programFiles, int newestYear) =>
        WindowsInstallsNewestFirst(programFiles, newestYear)
            .Select(install => Path.Combine(install, "bin64", "dymola.exe"))
            .FirstOrDefault(File.Exists) ?? string.Empty;
}
