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
    /// Timeout for starting OMC process (milliseconds).
    /// </summary>
    public int StartupTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Timeout for individual commands (milliseconds).
    /// Set to 0 for no timeout.
    /// </summary>
    public int CommandTimeoutMs { get; set; } = 60000;

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
