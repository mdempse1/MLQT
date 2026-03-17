
namespace ModelicaGraph.DataTypes;

/// <summary>
/// Represents an external resource file node in the graph.
/// This includes data files, header files, library files, images, etc.
/// Multiple models can reference the same resource file via edges.
/// </summary>
public class ResourceFileNode : GraphNode
{
    /// <summary>
    /// The resolved absolute file system path.
    /// </summary>
    public string ResolvedPath { get; }

    /// <summary>
    /// Whether the file exists on disk.
    /// </summary>
    public bool FileExists { get; set; }

    /// <summary>
    /// Whether this is an image file (images don't affect simulation results).
    /// </summary>
    public bool IsImageFile { get; set; }

    /// <summary>
    /// IDs of models that reference this resource file.
    /// Maintained for efficient reverse lookups.
    /// </summary>
    public HashSet<string> ReferencedByModelIds { get; }

    /// <summary>
    /// Creates a new resource file node.
    /// </summary>
    /// <param name="id">Unique identifier for the node (typically based on normalized path).</param>
    /// <param name="resolvedPath">The resolved absolute file system path.</param>
    public ResourceFileNode(string id, string resolvedPath)
        : base(id, NodeType.ResourceFile, Path.GetFileName(resolvedPath))
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

    public override string ToString()
    {
        return $"ResourceFile: {Name} ({ReferencedByModelIds.Count} references)";
    }
}
