namespace RevisionControl;

/// <summary>
/// Status of a file in the working copy.
/// </summary>
public enum VcsFileStatus
{
    /// <summary>File has been modified.</summary>
    Modified,
    /// <summary>File has been added (new, untracked then staged in Git).</summary>
    Added,
    /// <summary>File has been deleted.</summary>
    Deleted,
    /// <summary>File has been renamed.</summary>
    Renamed,
    /// <summary>File is untracked (new file not yet added).</summary>
    Untracked,
    /// <summary>File has conflicts.</summary>
    Conflicted
}
