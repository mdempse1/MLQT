namespace MLQT.Services.DataTypes;

/// <summary>
/// Represents a loaded Modelica library with its metadata and graph data.
/// </summary>
public class LoadedLibrary
{
    /// <summary>
    /// Unique identifier for this library instance.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name of the library (typically the top-level package name).
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Source path or identifier (file path, directory path, or revision control URL).
    /// </summary>
    public string SourcePath { get; set; } = "";

    /// <summary>
    /// Type of source (File, Directory, Zip, Git, SVN).
    /// </summary>
    public LibrarySourceType SourceType { get; set; }

    /// <summary>
    /// Revision identifier for version-controlled libraries.
    /// </summary>
    public string? Revision { get; set; }

    /// <summary>
    /// Set of model IDs that belong to this library.
    /// The actual ModelNode objects are stored in the CombinedGraph.
    /// </summary>
    public HashSet<string> ModelIds { get; set; } = new();

    /// <summary>
    /// Dictionary tracking parent-child relationships for models in this library.
    /// Key is parent model ID, value is list of child model IDs.
    /// </summary>
    public Dictionary<string, List<string>> ChildrenByParent { get; set; } = new();

    /// <summary>
    /// List of top-level model IDs (models without parents).
    /// </summary>
    public List<string> TopLevelModelIds { get; set; } = new();

    /// <summary>
    /// ID of the repository this library belongs to, if any.
    /// Null for libraries loaded directly (not from a repository).
    /// </summary>
    public string? RepositoryId { get; set; }

    /// <summary>
    /// Relative path within the repository where this library is located.
    /// Empty string if library is at repository root.
    /// </summary>
    public string? RelativePathInRepository { get; set; }
}
