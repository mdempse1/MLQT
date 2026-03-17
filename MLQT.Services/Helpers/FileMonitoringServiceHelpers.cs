namespace MLQT.Services.Helpers;

/// <summary>
/// Helper methods for the file monitoring service.
/// </summary>
public static class FileMonitoringServiceHelpers
{
    
    /// <summary>
    /// Checks if a path is inside a hidden directory (e.g., .git, .svn).
    /// This is a public static method so it can be used by other services as a safeguard.
    /// </summary>
    public static bool IsInHiddenDirectory(string path)
    {
        var pathParts = path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        return pathParts.Any(part => part.StartsWith("."));
    }
}