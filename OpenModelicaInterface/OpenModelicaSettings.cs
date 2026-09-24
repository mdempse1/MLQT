namespace OpenModelicaInterface;

/// <summary>
/// Configuration settings for OpenModelica interface.
/// </summary>
public class OpenModelicaSettings
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
    /// Creates settings with default values.
    /// </summary>
    public OpenModelicaSettings()
    {
        if (string.IsNullOrEmpty(OmcPath)) {
            //Search for the most recent OpenModelica version
            int minor = DateTime.Now.Year + 1 - 2000;
            int patch = 10;
            while (minor > 20)
            {
                var versionName = $"OpenModelica1.{minor}.{patch}-64bit";
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), versionName, "bin", "omc.exe");                
                if (File.Exists(path)) 
                {
                    OmcPath = path;
                    break;
                }
                if (patch <= 0)
                {
                    minor--;
                    patch = 10;                    
                }
                else
                    patch--;
            }
        }
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
    /// Tries to auto-detect OpenModelica installation.
    /// </summary>
    public static OpenModelicaSettings? TryAutoDetect()
    {
        // Try common paths
        foreach (var path in CommonWindowsPaths)
        {
            if (File.Exists(path))
            {
                return new OpenModelicaSettings(path);
            }
        }

        // Try to find in PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            var paths = pathEnv.Split(Path.PathSeparator);
            foreach (var dir in paths)
            {
                var omcPath = Path.Combine(dir, "omc.exe");
                if (File.Exists(omcPath))
                {
                    return new OpenModelicaSettings(omcPath);
                }
            }
        }

        return null;
    }
}
