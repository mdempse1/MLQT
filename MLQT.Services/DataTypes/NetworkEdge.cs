namespace MLQT.Services.DataTypes;

/// <summary>
/// Represents an edge in the dependency network visualization.
/// </summary>
public class NetworkEdge
{
    public string FromId { get; set; } = "";
    public string ToId { get; set; } = "";
    public int X1 { get; set; }
    public int Y1 { get; set; }
    public int X2 { get; set; }
    public int Y2 { get; set; }
}
