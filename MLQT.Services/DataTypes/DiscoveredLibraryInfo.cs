namespace MLQT.Services.DataTypes;

/// <summary>
/// Information about a discovered Modelica library within a repository.
/// </summary>
public class DiscoveredLibraryInfo
{
    /// <summary>
    /// Relative path from repository root to the library directory.
    /// Empty string if library is at repository root.
    /// </summary>
    public string RelativePath { get; set; } = "";

    /// <summary>
    /// Name of the library (extracted from package.mo).
    /// </summary>
    public string LibraryName { get; set; } = "";

    /// <summary>
    /// Full absolute path to the library directory.
    /// </summary>
    public string FullPath { get; set; } = "";
}
