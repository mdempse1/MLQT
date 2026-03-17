namespace MLQT.Services.DataTypes;

/// <summary>
/// Represents a node in the dependency network visualization.
/// </summary>
public class NetworkNode
{
    public string Id { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string FullName { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public string Color { get; set; } = "var(--mud-palette-primary)";
    public string BorderColor { get; set; } = "var(--mud-palette-primary-darken)";
    public bool IsSelected { get; set; }
    public bool IsImpacted { get; set; }
}
