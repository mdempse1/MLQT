namespace MLQT.Services.DataTypes;

/// <summary>
/// Represents an edge in the dependency network visualization.
/// </summary>
public class NetworkEdge
{
    public string FromId { get; set; } = "";
    public string ToId { get; set; } = "";
}
