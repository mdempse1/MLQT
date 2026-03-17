namespace MLQT.Services.DataTypes;

/// <summary>
/// Represents a single file system change.
/// </summary>
public class FileChangeInfo
{
    /// <summary>
    /// Unique identifier for this change (GUID).
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Type of change that occurred.
    /// </summary>
    public FileChangeType ChangeType { get; set; }

    /// <summary>
    /// Full path to the affected file.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// For renamed files, the original path before rename.
    /// </summary>
    public string? OldFilePath { get; set; }

    /// <summary>
    /// ID of the repository this file belongs to.
    /// </summary>
    public string RepositoryId { get; set; } = "";

    /// <summary>
    /// ID of the library this file belongs to (if known).
    /// </summary>
    public string? LibraryId { get; set; }

    /// <summary>
    /// Timestamp when the change was detected.
    /// </summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this is a Modelica file (.mo).
    /// </summary>
    public bool IsModelicaFile => FilePath.EndsWith(".mo", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this is a package.order file.
    /// </summary>
    public bool IsPackageOrderFile => Path.GetFileName(FilePath)
        .Equals("package.order", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this change represents a directory (new package or deleted package).
    /// </summary>
    public bool IsDirectory { get; set; }

}
