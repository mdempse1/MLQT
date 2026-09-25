namespace RevisionControl;

/// <summary>
/// Represents a file with uncommitted changes in the working copy.
/// </summary>
public class VcsWorkingCopyFile
{
    /// <summary>
    /// The path of the file relative to the repository root.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// The status of the file in the working copy.
    /// </summary>
    public VcsFileStatus Status { get; set; }

    /// <summary>
    /// For a renamed file, the path it had in the committed version, relative to the repository root;
    /// otherwise null. Where its committed content is to be found (B350).
    /// </summary>
    public string? OldPath { get; set; }

    /// <summary>
    /// Whether the file is staged for commit (Git only).
    /// </summary>
    public bool IsStaged { get; set; }
}
