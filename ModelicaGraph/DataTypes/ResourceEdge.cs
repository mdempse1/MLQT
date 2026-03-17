using ModelicaParser.DataTypes;

namespace ModelicaGraph.DataTypes;

/// <summary>
/// Represents an edge from a model to a resource (file or directory).
/// Contains metadata about the reference including the original raw path,
/// reference type, and optional parameter name.
/// </summary>
public class ResourceEdge
{
    /// <summary>
    /// The ID of the model that references the resource.
    /// </summary>
    public string ModelId { get; set; } = "";

    /// <summary>
    /// The ID of the resource node (ResourceFileNode or ResourceDirectoryNode).
    /// </summary>
    public string ResourceNodeId { get; set; } = "";

    /// <summary>
    /// The original raw path as specified in the Modelica code
    /// (e.g., "modelica://Modelica/Resources/Data/test.mat").
    /// </summary>
    public string RawPath { get; set; } = "";

    /// <summary>
    /// The type of resource reference (LoadResource, UriReference, ExternalInclude, etc.).
    /// </summary>
    public ResourceReferenceType ReferenceType { get; set; }

    /// <summary>
    /// The parameter name if the resource is referenced via a parameter declaration.
    /// Null if not applicable.
    /// </summary>
    public string? ParameterName { get; set; }

    /// <summary>
    /// Whether the original path was an absolute file system path rather than a modelica:// URI.
    /// </summary>
    public bool IsAbsolutePath { get; set; }

    /// <summary>
    /// Creates a unique key for this edge based on model ID, resource node ID, and reference type.
    /// Used for edge deduplication.
    /// </summary>
    public string GetEdgeKey()
    {
        return $"{ModelId}|{ResourceNodeId}|{ReferenceType}";
    }

    public override string ToString()
    {
        return $"{ModelId} -> {ResourceNodeId} ({ReferenceType})";
    }
}
