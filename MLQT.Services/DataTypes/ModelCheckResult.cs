namespace MLQT.Services.DataTypes;

/// <summary>
/// Result of a model checking operation.
/// </summary>
public class ModelCheckResult
{
    /// <summary>
    /// Whether the model check passed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The model ID that was checked.
    /// </summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// Error message if the check failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Summary of the result for display.
    /// </summary>
    public string? Summary { get; set; }
}
