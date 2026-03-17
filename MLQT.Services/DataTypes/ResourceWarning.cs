namespace MLQT.Services.DataTypes;

/// <summary>
/// Represents a warning about an external resource reference in a Modelica model.
/// </summary>
public class ResourceWarning
{
    /// <summary>
    /// Fully qualified ID of the model containing the resource reference.
    /// </summary>
    public string ModelId { get; set; } = "";

    /// <summary>
    /// The resource path (raw or resolved) that triggered the warning.
    /// </summary>
    public string ResourcePath { get; set; } = "";

    /// <summary>
    /// Type of warning.
    /// </summary>
    public ResourceWarningType WarningType { get; set; }

    /// <summary>
    /// Human-readable warning message.
    /// </summary>
    public string Message { get; set; } = "";
}
