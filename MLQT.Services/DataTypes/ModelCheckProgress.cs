namespace MLQT.Services.DataTypes;

/// <summary>
/// Progress information for model checking operations.
/// </summary>
public class ModelCheckProgress
{
    /// <summary>
    /// Total number of models to check.
    /// </summary>
    public int TotalModels { get; set; }

    /// <summary>
    /// Number of models checked so far.
    /// </summary>
    public int ModelsChecked { get; set; }

    /// <summary>
    /// The model currently being checked.
    /// </summary>
    public string CurrentModel { get; set; } = string.Empty;

    /// <summary>
    /// Whether the checking operation is complete.
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Whether the checking operation was cancelled.
    /// </summary>
    public bool WasCancelled { get; set; }
}
