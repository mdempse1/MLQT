namespace RevisionControl;

/// <summary>
/// Result of a VCS merge operation.
/// </summary>
public class VcsMergeResult
{
    /// <summary>
    /// Whether the merge was successful (no conflicts).
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the merge failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether there were conflicts that need manual resolution.
    /// </summary>
    public bool HasConflicts { get; set; }

    /// <summary>
    /// List of files with text conflicts (content merges that need resolution).
    /// </summary>
    public List<string> ConflictedFiles { get; set; } = new();

    /// <summary>
    /// List of paths with tree conflicts (directory/file existence conflicts, e.g. same path
    /// added in both branches). These can only be resolved by marking as resolved after
    /// manually verifying the working copy is correct.
    /// </summary>
    public List<string> TreeConflictedFiles { get; set; } = new();

    /// <summary>
    /// List of files that were modified by the merge.
    /// </summary>
    public List<string> ModifiedFiles { get; set; } = new();

    /// <summary>
    /// Whether any changes were merged (false if already up to date).
    /// </summary>
    public bool HasChanges { get; set; }

    /// <summary>
    /// The source branch that was merged from.
    /// </summary>
    public string? SourceBranch { get; set; }

    /// <summary>
    /// The first revision merged (SVN only). Null for Git or when no revisions were merged.
    /// </summary>
    public long? StartRevision { get; set; }

    /// <summary>
    /// The last revision merged (SVN only). Null for Git or when no revisions were merged.
    /// </summary>
    public long? EndRevision { get; set; }
}
