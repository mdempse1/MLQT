namespace RevisionControl;

/// <summary>
/// Result of a VCS commit operation.
/// </summary>
public class VcsCommitResult
{
    /// <summary>
    /// Whether the commit was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the commit failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The revision identifier of the new commit.
    /// </summary>
    public string? NewRevision { get; set; }

    /// <summary>
    /// Whether the commit failed because the working copy is out of date and needs to be updated first.
    /// </summary>
    public bool IsOutOfDate { get; set; }

    /// <summary>
    /// Files that were excluded from this commit because they are new (unversioned) files inside a
    /// directory that was itself added via SVN merge. SVN does not allow adding new files to such
    /// directories in the same transaction. These files must be committed in a separate commit.
    /// </summary>
    public List<string> SkippedFiles { get; set; } = new();
}
