using System.Text.Json.Serialization;

namespace OpenModelicaInterface;

/// <summary>
/// Configuration settings for OpenModelica interface.
/// </summary>
public class OpenModelicaSettings : IJsonOnDeserialized
{
    /// <summary>
    /// Path to the OMC executable.
    /// Default: "C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe" on Windows
    /// </summary>
    public string OmcPath { get; set; } = string.Empty;

    /// <summary>
    /// Port number used to communicate with OpenModelica
    /// Default: 13027
    /// </summary>
    public int PortNumber { get; set; } = 13027;

    /// <summary>
    /// Default integration method for simulations.
    /// Common values: "dassl", "euler", "rungekutta", "impeuler"
    /// </summary>
    public string DefaultIntegrationMethod { get; set; } = "dassl";

    /// <summary>
    /// Default tolerance for numerical integration.
    /// </summary>
    public double DefaultTolerance { get; set; } = 1e-6;

    /// <summary>
    /// Default number of output intervals for simulations.
    /// </summary>
    public int DefaultNumberOfIntervals { get; set; } = 500;

    /// <summary>
    /// How long omc may take to start and answer its first command (milliseconds); 0 for no limit.
    /// </summary>
    public int StartupTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// How long one command may take before the session is given up on (milliseconds); 0 for no
    /// limit. A command that runs out of time closes the session, because omc cannot be interrupted
    /// and its socket cannot be reused; the next command starts a fresh one.
    /// </summary>
    /// <remarks>
    /// Carried since this class was written and read by nothing until B263, so until then an omc
    /// check had no limit at all whatever this said.
    /// </remarks>
    public int CommandTimeoutMs { get; set; } = 60000;

    /// <summary><see cref="CommandTimeoutMs"/> as a span: infinite for 0, the default for a negative
    /// value, which the dialog does not allow and a hand-edited settings file might.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan CommandTimeout => ToSpan(CommandTimeoutMs, 60000);

    /// <summary><see cref="StartupTimeoutMs"/> as a span, on the same terms.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan StartupTimeout => ToSpan(StartupTimeoutMs, 5000);

    private static TimeSpan ToSpan(int milliseconds, int fallback) => milliseconds switch
    {
        0 => Timeout.InfiniteTimeSpan,
        < 0 => TimeSpan.FromMilliseconds(fallback),
        _ => TimeSpan.FromMilliseconds(milliseconds),
    };

    /// <summary>
    /// Whether to automatically load Modelica Standard Library on startup.
    /// </summary>
    public bool AutoLoadModelicaLibrary { get; set; } = false;

    /// <summary>
    /// Creates settings with default values, and the newest omc this machine has - see
    /// <see cref="FindInstalledOmc()"/>.
    /// </summary>
    public OpenModelicaSettings()
    {
        if (string.IsNullOrEmpty(OmcPath))
            OmcPath = FindInstalledOmc();
    }

    /// <summary>
    /// A blank path read from saved settings is looked for again. The constructor's search runs
    /// before the saved values are applied, so a saved <c>""</c> overwrote whatever it found and
    /// blanking the field turned detection off for good - though a blank path can never work.
    /// </summary>
    void IJsonOnDeserialized.OnDeserialized()
    {
        if (string.IsNullOrEmpty(OmcPath))
            OmcPath = FindInstalledOmc();
    }

    /// <summary>The compiler's file name on this platform: <c>omc.exe</c> on Windows, <c>omc</c> elsewhere.</summary>
    public static string OmcExecutableName => ExecutableName(OperatingSystem.IsWindows());

    private static string ExecutableName(bool windows) => windows ? "omc.exe" : "omc";

    /// <summary>
    /// The omc a user means by choosing <paramref name="folder"/> in the settings dialog: the
    /// installation's <c>bin/omc</c>, or <c>omc</c> in the folder itself when they chose <c>bin</c>.
    /// </summary>
    /// <remarks>
    /// Browsing always wrote <c>bin/omc.exe</c>, so on Linux it produced a path that could not
    /// exist (B338).
    /// </remarks>
    public static string OmcUnder(string folder)
    {
        var direct = Path.Combine(folder, OmcExecutableName);
        return File.Exists(direct) ? direct : Path.Combine(folder, "bin", OmcExecutableName);
    }

    /// <summary>The newest omc installed on this machine, or empty when none is found.</summary>
    public static string FindInstalledOmc() => FindInstalledOmc(
        OperatingSystem.IsWindows(),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetEnvironmentVariable("PATH"),
        DateTime.Now.Year + 1 - 2000);

    /// <summary>The first of <see cref="CandidateOmcPaths"/> that exists, or empty.</summary>
    public static string FindInstalledOmc(bool windows, string programFiles, string? pathVariable, int newestMinor) =>
        CandidateOmcPaths(windows, programFiles, pathVariable, newestMinor).FirstOrDefault(File.Exists) ?? string.Empty;

    /// <summary>
    /// Where auto-detection looks for omc, in order.
    /// </summary>
    /// <remarks>
    /// <para><b>Windows:</b> the installer's versioned folders under Program Files,
    /// <c>OpenModelica1.{minor}.{patch}-64bit\bin\omc.exe</c>, newest first from
    /// <c>1.{newestMinor}.10</c> down to <c>1.21.0</c>; then <c>omc.exe</c> in each folder on
    /// <c>PATH</c>.</para>
    ///
    /// <para><b>Linux:</b> <c>/usr/bin/omc</c>, where OpenModelica's own apt repository puts it,
    /// <c>/usr/local/bin/omc</c> and <c>/opt/openmodelica/bin/omc</c>; then <c>omc</c> in each
    /// folder on <c>PATH</c>. Only the Windows folders and <c>omc.exe</c> on PATH used to be probed,
    /// so nothing was ever found on the Linux host (B338).</para>
    /// </remarks>
    public static IEnumerable<string> CandidateOmcPaths(
        bool windows, string programFiles, string? pathVariable, int newestMinor)
    {
        if (windows)
        {
            for (var minor = newestMinor; minor > 20; minor--)
                for (var patch = 10; patch >= 0; patch--)
                    yield return Path.Combine(programFiles, $"OpenModelica1.{minor}.{patch}-64bit", "bin", "omc.exe");
        }
        else
        {
            // Written out rather than combined: these are Linux paths whatever machine asks.
            yield return "/usr/bin/omc";
            yield return "/usr/local/bin/omc";
            yield return "/opt/openmodelica/bin/omc";
        }

        if (string.IsNullOrEmpty(pathVariable))
            yield break;

        foreach (var dir in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return Path.Combine(dir.Trim(), ExecutableName(windows));
    }

    /// <summary>
    /// Creates settings with custom OMC path.
    /// </summary>
    public OpenModelicaSettings(string omcPath)
    {
        OmcPath = omcPath;
    }

    /// <summary>
    /// Validates the settings.
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrEmpty(OmcPath) && File.Exists(OmcPath);
    }

    /// <summary>
    /// Gets the OMC installation directory.
    /// </summary>
    public string GetInstallationDirectory()
    {
        return Path.GetDirectoryName(Path.GetDirectoryName(OmcPath)) ?? "";
    }

    /// <summary>
    /// Common installation paths for OpenModelica on Windows.
    /// </summary>
    public static string[] CommonWindowsPaths => new[]
    {
        @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe",
        @"C:\Program Files\OpenModelica1.25.0-64bit\bin\omc.exe",
        @"C:\Program Files\OpenModelica1.24.0-64bit\bin\omc.exe",
        @"C:\Program Files (x86)\OpenModelica1.26.0-64bit\bin\omc.exe",
        @"C:\Program Files (x86)\OpenModelica1.25.0-64bit\bin\omc.exe"
    };

    /// <summary>
    /// Tries to auto-detect OpenModelica installation - the same search as the default constructor.
    /// </summary>
    public static OpenModelicaSettings? TryAutoDetect()
    {
        var path = FindInstalledOmc();
        return path.Length > 0 ? new OpenModelicaSettings(path) : null;
    }
}
