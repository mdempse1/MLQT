namespace MLQT.Services.DataTypes;

/// <summary>
/// Represents detailed information about an impacted model.
/// </summary>
public class ImpactDetail
{
    public string ModelId { get; set; } = "";
    public string ClassType { get; set; } = "";
    public List<string> ImpactedBy { get; set; } = new();
}
