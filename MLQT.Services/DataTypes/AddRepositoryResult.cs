namespace MLQT.Services.DataTypes;

/// <summary>
/// Result of adding a repository operation.
/// </summary>
public class AddRepositoryResult
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The repository that was added, if successful.
    /// </summary>
    public Repository? Repository { get; set; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Libraries discovered within the repository.
    /// </summary>
    public List<DiscoveredLibraryInfo> DiscoveredLibraries { get; set; } = new();
}
