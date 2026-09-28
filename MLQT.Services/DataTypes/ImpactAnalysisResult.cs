namespace MLQT.Services.DataTypes;

/// <summary>
/// Result of an impact analysis operation.
/// </summary>
public class ImpactAnalysisResult
{
    /// <summary>
    /// Network nodes for visualization.
    /// </summary>
    public List<NetworkNode> Nodes { get; set; } = new();

    /// <summary>
    /// Network edges for visualization.
    /// </summary>
    public List<NetworkEdge> Edges { get; set; } = new();

    /// <summary>
    /// Detailed impact information for each impacted model.
    /// </summary>
    public List<ImpactDetail> ImpactDetails { get; set; } = new();

    /// <summary>
    /// Count of models impacted (excluding selected models).
    /// </summary>
    public int ImpactedModelsCount { get; set; }
}
