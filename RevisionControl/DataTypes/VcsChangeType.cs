namespace RevisionControl;

/// <summary>
/// Represents the type of change made to a file in a commit.
/// </summary>
public enum VcsChangeType
{
    /// <summary>File was added in this commit.</summary>
    Added,
    /// <summary>File was modified in this commit.</summary>
    Modified,
    /// <summary>File was deleted in this commit.</summary>
    Deleted,
    /// <summary>File was renamed in this commit.</summary>
    Renamed,
    /// <summary>File was copied in this commit.</summary>
    Copied
}
