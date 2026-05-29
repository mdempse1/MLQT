using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RevisionControl;

/// <summary>
/// Resolves the path to an <c>svn</c> command-line executable for the CLI-based SVN
/// operations. The application bundles a private copy of the SlikSVN command-line
/// client under a <c>svn/</c> folder next to the executable; this locator prefers that
/// bundled copy so behaviour is identical regardless of what (if anything) the user has
/// installed system-wide.
///
/// Resolution order:
/// 1. The <c>MLQT_SVN_PATH</c> environment variable, if it points at an existing file
///    (lets CI and developers pin a specific client without rebuilding).
/// 2. The bundled client at <c>{AppContext.BaseDirectory}/svn/svn[.exe]</c>.
/// 3. Bare <c>svn</c> resolved via the OS PATH.
///
/// The resolved value is cached for the process lifetime. Returns null only when no
/// candidate exists at all, in which case <see cref="SvnCli"/> raises an error explaining
/// how to provide an svn client (there is no managed fallback any more).
/// </summary>
public static class SvnToolLocator
{
    /// <summary>Environment variable that overrides svn executable discovery.</summary>
    public const string OverrideEnvVar = "MLQT_SVN_PATH";

    /// <summary>Subdirectory (relative to the app base directory) holding the bundled client.</summary>
    public const string BundledSubdirectory = "svn";

    private static readonly Lazy<string?> Resolved = new(Resolve);

    /// <summary>
    /// The full path to the svn executable to invoke, or null if none could be found.
    /// When the result is bare "svn" the OS PATH is relied upon at process-start time.
    /// </summary>
    public static string? SvnExecutablePath => Resolved.Value;

    /// <summary>
    /// The directory containing the bundled svn client (and its sibling DLLs), or null if
    /// the bundled client is not present. Useful for tests and diagnostics.
    /// </summary>
    public static string? BundledDirectory
    {
        get
        {
            var dir = Path.Combine(AppContext.BaseDirectory, BundledSubdirectory);
            return File.Exists(Path.Combine(dir, ExecutableFileName)) ? dir : null;
        }
    }

    private static string ExecutableFileName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "svn.exe" : "svn";

    private static string? Resolve()
    {
        var overridePath = Environment.GetEnvironmentVariable(OverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            RevisionControlLogger.Debug($"SvnToolLocator: using {OverrideEnvVar}={overridePath}");
            return overridePath;
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, BundledSubdirectory, ExecutableFileName);
        if (File.Exists(bundled))
        {
            RevisionControlLogger.Debug($"SvnToolLocator: using bundled client at {bundled}");
            return bundled;
        }

        if (IsOnPath())
        {
            RevisionControlLogger.Debug("SvnToolLocator: using svn from PATH");
            return ExecutableFileName;
        }

        RevisionControlLogger.Debug("SvnToolLocator: no svn executable found");
        return null;
    }

    /// <summary>
    /// Probes whether a bare <c>svn</c> can be started from the PATH. Runs <c>svn --version --quiet</c>
    /// once; a successful start (regardless of exit code) means the executable resolves.
    /// </summary>
    private static bool IsOnPath()
    {
        try
        {
            var psi = new ProcessStartInfo(ExecutableFileName, "--version --quiet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            process.WaitForExit(5000);
            return true;
        }
        catch (Exception)
        {
            // Win32Exception (file not found) or any start failure means svn isn't usable from PATH.
            return false;
        }
    }
}
