
namespace ModelicaGraph.DataTypes;

/// <summary>
/// Represents a file node in the graph.
/// A file can contain multiple Modelica models.
/// </summary>
public class FileNode : GraphNode
{
    /// <summary>
    /// Full path to the file.
    /// </summary>
    public string FilePath { get; set; }

    /// <summary>
    /// File name without path.
    /// </summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// Raw content of the file.
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Models contained in this file (IDs of ModelNode instances).
    /// </summary>
    public HashSet<string> ContainedModelIds { get; }

    public FileNode(string id, string filePath) : base(id, NodeType.File, Path.GetFileName(filePath))
    {
        FilePath = filePath;
        ContainedModelIds = new HashSet<string>();
    }

    /// <summary>
    /// Adds a model to this file's list of contained models.
    /// </summary>
    public void AddContainedModel(string modelId) => ContainedModelIds.Add(modelId);

    public override string ToString()
    {
        return $"File: {FileName} ({ContainedModelIds.Count} models)";
    }
}
