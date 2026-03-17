namespace MLQT.Services.DataTypes;

/// <summary>
/// Result from picking a file, including path information for package handling.
/// </summary>
public class FilePickerResult
{
    /// <summary>
    /// The file path.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// The content of the selected file.
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// Whether this is a package.mo file.
    /// </summary>
    public bool IsPackageFile { get; set; }

    /// <summary>
    /// The directory path for package.mo files.
    /// </summary>
    public string? DirectoryPath { get; set; }
}
