namespace MLQT.Services.Helpers;

/// <summary>
/// Helper methods for building the external resource tree structure.
/// Extracted as a static class for testability.
/// </summary>
public static class ResourceTreeHelper
{
    /// <summary>
    /// Finds the longest common directory root among a list of absolute directory paths.
    /// Returns empty string if no common root exists or if the list is empty.
    /// </summary>
    public static string FindCommonDirectoryRoot(List<string> directories)
    {
        if (directories.Count == 0)
            return "";
        if (directories.Count == 1)
            return directories[0];

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var splitDirs = directories
            .Select(d => d.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToList();
        var minLength = splitDirs.Min(s => s.Length);

        var commonSegments = new List<string>();
        for (int i = 0; i < minLength; i++)
        {
            var segment = splitDirs[0][i];
            if (splitDirs.All(s => string.Equals(s[i], segment, comparison)))
                commonSegments.Add(segment);
            else
                break;
        }

        if (commonSegments.Count == 0)
            return "";

        var result = string.Join(Path.DirectorySeparatorChar, commonSegments);

        // On Windows, a single drive letter segment "C:" needs a trailing separator
        // to represent the root of the drive rather than a relative path
        if (result.Length == 2 && result[1] == ':')
            result += Path.DirectorySeparatorChar;

        return result;
    }
}
