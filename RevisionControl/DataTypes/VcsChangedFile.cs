namespace RevisionControl;

/// <summary>
/// Represents a file that was changed in a commit.
/// </summary>
public class VcsChangedFile
{
    /// <summary>
    /// The path of the file relative to the repository root.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// The type of change made to the file.
    /// </summary>
    public VcsChangeType ChangeType { get; set; }

    /// <summary>
    /// For renamed or copied files, the original path before the rename/copy.
    /// </summary>
    public string? OldPath { get; set; }
}
