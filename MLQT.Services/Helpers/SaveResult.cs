namespace MLQT.Services.Helpers;

/// <summary>
/// Result of a save operation containing information about written files.
/// </summary>
public class SaveResult
{
    /// <summary>
    /// Dictionary mapping model IDs to their new file paths.
    /// </summary>
    public Dictionary<string, string> ModelIdToFilePath { get; } = new();

    /// <summary>
    /// Set of all files written during the save operation.
    /// </summary>
    public HashSet<string> WrittenFiles { get; } = new();

    /// <summary>
    /// Set of all directories created during the save operation.
    /// </summary>
    public HashSet<string> CreatedDirectories { get; } = new();
}
