namespace RevisionControl;

/// <summary>
/// Represents a branch in a repository.
/// </summary>
public class VcsBranchInfo
{
    /// <summary>
    /// The name of the branch.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Whether this is the currently checked out branch.
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// Whether this is a remote tracking branch.
    /// </summary>
    public bool IsRemote { get; set; }

    /// <summary>
    /// The last commit on this branch (may be null if not available).
    /// </summary>
    public string? LastCommit { get; set; }
}
