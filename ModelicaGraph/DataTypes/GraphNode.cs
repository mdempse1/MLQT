using ModelicaGraph.Interfaces;

namespace ModelicaGraph.DataTypes;

/// <summary>
/// Base class for graph nodes.
/// </summary>
public abstract class GraphNode : IGraphNode
{
    /// <summary>
    /// Unique identifier for the node.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Type of the node.
    /// </summary>
    public NodeType NodeType { get; }

    /// <summary>
    /// Display name for the node.
    /// </summary>
    public string Name { get; set; }

    protected GraphNode(string id, NodeType nodeType, string name)
    {
        Id = id;
        NodeType = nodeType;
        Name = name;
    }

    public override string ToString()
    {
        return $"{NodeType}: {Name} (ID: {Id})";
    }

    public override bool Equals(object? obj)
    {
        return obj is GraphNode node && Id == node.Id;
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }
}
