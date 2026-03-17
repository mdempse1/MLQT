using MLQT.Services.DataTypes;
using ModelicaGraph;

namespace MLQT.Services.Interfaces;

/// <summary>
/// Service for analyzing the impact of changes to selected models.
/// Performs BFS traversal to find all transitively dependent models.
/// </summary>
public interface IImpactAnalysisService
{
    /// <summary>
    /// Analyzes the impact of changes to the selected models.
    /// </summary>
    /// <param name="graph">The dependency graph.</param>
    /// <param name="selectedModelIds">IDs of the selected models.</param>
    /// <param name="svgWidth">Width available for visualization.</param>
    /// <param name="svgHeight">Height available for visualization.</param>
    /// <returns>Impact analysis result with nodes, edges, and details.</returns>
    ImpactAnalysisResult AnalyzeImpact(
        DirectedGraph graph,
        IEnumerable<string> selectedModelIds,
        int svgWidth = 700,
        int svgHeight = 450);

    /// <summary>
    /// Gets the connected node IDs for a given node (nodes directly connected by edges).
    /// </summary>
    /// <param name="edges">The network edges.</param>
    /// <param name="nodeId">The node to find connections for.</param>
    /// <returns>Set of connected node IDs.</returns>
    HashSet<string> GetConnectedNodes(IEnumerable<NetworkEdge> edges, string nodeId);
}
