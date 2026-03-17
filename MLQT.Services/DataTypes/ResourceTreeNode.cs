namespace MLQT.Services.DataTypes;

/// <summary>
/// Data model for resource tree nodes displayed in the External Resources page.
/// Represents either a directory or a file in the resource file system hierarchy.
/// </summary>
public class ResourceTreeNode
{
    /// <summary>
    /// Display name (filename or directory name).
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Full resolved path (used as unique ID for the node).
    /// </summary>
    public string FullPath { get; set; } = "";

    /// <summary>
    /// True for folder nodes, false for file nodes.
    /// </summary>
    public bool IsDirectory { get; set; }

    /// <summary>
    /// For directory nodes, indicates the annotation type that specified this directory.
    /// None for regular intermediate directories in the path hierarchy.
    /// </summary>
    public DirectoryAnnotationType AnnotationType { get; set; } = DirectoryAnnotationType.None;

    /// <summary>
    /// List of model IDs that reference this directory (for annotated directories).
    /// </summary>
    public List<string> ReferencingModelIds { get; set; } = new();

    /// <summary>
    /// Whether this resource has a validation warning (missing file or absolute path).
    /// </summary>
    public bool HasWarning { get; set; }

    /// <summary>
    /// True when the referenced file or directory does not exist on disk.
    /// </summary>
    public bool IsMissing { get; set; }

    /// <summary>
    /// True when the resource uses a non-portable absolute path.
    /// </summary>
    public bool IsAbsolutePath { get; set; }

    /// <summary>
    /// Warning details for tooltip display.
    /// </summary>
    public string? WarningMessage { get; set; }

    /// <summary>
    /// File extension including the dot (e.g., ".mat", ".png").
    /// Empty for directories.
    /// </summary>
    public string FileExtension { get; set; } = "";

    /// <summary>
    /// Whether the file is an image file.
    /// </summary>
    public bool IsImageFile { get; set; }

    /// <summary>
    /// Number of models referencing this resource file.
    /// </summary>
    public int ReferencingModelCount { get; set; }
}
