
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
    /// The file's last-write time when its classes were read into the graph, or null when that is not
    /// known - a file not on disk, or a node made some other way than by loading the file.
    /// </summary>
    /// <remarks>
    /// What lets the startup format tell "already formatted and as loaded" from "already formatted but
    /// edited since": only a file whose write time still matches can skip having its classes read
    /// back from it. Taken just after the text was read, so an edit landing between the two shows as
    /// a newer time and the refresh happens.
    /// </remarks>
    public DateTime? LoadedWriteTimeUtc { get; set; }

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
