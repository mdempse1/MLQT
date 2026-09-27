namespace MLQT.Services.Helpers;

/// <summary>
/// Result of a save operation containing information about written files.
/// </summary>
public class SaveResult
{
    /// <summary>
    /// Dictionary mapping model IDs to their new file paths.
    /// </summary>
    public Dictionary<string, string> ModelIdToFilePath { get; } = new();

    /// <summary>
    /// Set of all files written during the save operation.
    /// </summary>
    public HashSet<string> WrittenFiles { get; } = new();

    /// <summary>
    /// Set of all directories created during the save operation.
    /// </summary>
    public HashSet<string> CreatedDirectories { get; } = new();

    /// <summary>
    /// Files the save tried to write and could not. Each failure is logged and the save carries on,
    /// so a caller about to act on the result — deleting the file the classes came from, say — has
    /// to ask this rather than assume that returning means everything was written (B303). A class
    /// stored in one of these files is not in <see cref="ModelIdToFilePath"/>.
    /// </summary>
    public HashSet<string> FailedFiles { get; } = new();

    /// <summary>
    /// Classes the save was asked to write and put in no file it wrote — one whose file failed, one
    /// that could not be rendered, or one the layout found nowhere to put (B441). The files such a
    /// class came from may be the only copy of it, so a caller must delete nothing of the library
    /// when this is not empty. Classes left untouched in their files (B414) are never listed.
    /// </summary>
    public List<string> UnplacedModelIds { get; } = new();
}
