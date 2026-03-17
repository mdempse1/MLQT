namespace RevisionControl;

/// <summary>
/// Result of a VCS update operation.
/// </summary>
public class VcsUpdateResult
{
    /// <summary>
    /// Whether the update was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the update failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The revision after the update (commit hash for Git, revision number for SVN).
    /// </summary>
    public string? NewRevision { get; set; }

    /// <summary>
    /// The revision before the update, if it changed.
    /// </summary>
    public string? OldRevision { get; set; }

    /// <summary>
    /// Whether any changes were pulled (false if already up to date).
    /// </summary>
    public bool HasChanges { get; set; }
}
