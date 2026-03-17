using ModelicaGraph.DataTypes;

namespace ModelicaGraph.Interfaces;

/// <summary>
/// Interface for nodes in the directed graph.
/// </summary>
public interface IGraphNode
{
    /// <summary>
    /// Unique identifier for the node.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Type of the node (File, Model, etc.).
    /// </summary>
    NodeType NodeType { get; }

    /// <summary>
    /// Display name for the node.
    /// </summary>
    string Name { get; }
}
