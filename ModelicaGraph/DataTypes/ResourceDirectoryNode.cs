
namespace ModelicaGraph.DataTypes;

/// <summary>
/// Represents an external resource directory node in the graph.
/// This includes IncludeDirectory, LibraryDirectory, and SourceDirectory references.
/// Multiple models can reference the same resource directory via edges.
/// </summary>
public class ResourceDirectoryNode : GraphNode
{
    /// <summary>
    /// The resolved absolute directory path.
    /// </summary>
    public string ResolvedPath { get; }

    /// <summary>
    /// Whether the directory exists on disk.
    /// </summary>
    public bool DirectoryExists { get; set; }

    /// <summary>
    /// IDs of models that reference this resource directory.
    /// Maintained for efficient reverse lookups.
    /// </summary>
    public HashSet<string> ReferencedByModelIds { get; }

    /// <summary>
    /// IDs of ResourceFileNodes for files contained within this directory.
    /// These files are discovered during directory scanning but don't have
    /// direct model references - they inherit the directory's references.
    /// </summary>
    public HashSet<string> ContainedFileIds { get; } = new();

    /// <summary>
    /// Creates a new resource directory node.
    /// </summary>
    /// <param name="id">Unique identifier for the node (typically based on normalized path).</param>
    /// <param name="resolvedPath">The resolved absolute directory path.</param>
    public ResourceDirectoryNode(string id, string resolvedPath)
        : base(id, NodeType.ResourceDirectory, Path.GetFileName(resolvedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
    {
        ResolvedPath = resolvedPath;
        ReferencedByModelIds = new HashSet<string>();
    }

    /// <summary>
    /// Adds a model ID to the list of models referencing this resource.
    /// </summary>
    public void AddReferencingModel(string modelId) => ReferencedByModelIds.Add(modelId);

    /// <summary>
    /// Removes a model ID from the list of models referencing this resource.
    /// </summary>
    public void RemoveReferencingModel(string modelId) => ReferencedByModelIds.Remove(modelId);

    /// <summary>
    /// Adds a file ID to the list of files contained in this directory.
    /// </summary>
    public void AddContainedFile(string fileId) => ContainedFileIds.Add(fileId);

    public override string ToString()
    {
        return $"ResourceDirectory: {Name} ({ReferencedByModelIds.Count} references)";
    }
}
