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

    /// <summary>
    /// Whether the operation rewrote files in the working copy, so anything loaded from them has to
    /// be read again. Set by an SVN switch, from what <c>svn switch</c> reports it changed: creating
    /// a branch from HEAD and switching to it changes nothing when the working copy is up to date,
    /// and brings in others' commits when it is not. False for an operation that cannot change files.
    /// </summary>
    public bool WorkingCopyChanged { get; set; }
}
