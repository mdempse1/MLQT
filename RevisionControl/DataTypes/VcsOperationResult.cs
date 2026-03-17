namespace RevisionControl;

/// <summary>
/// Result of a VCS operation (generic).
/// </summary>
public class VcsOperationResult
{
    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
