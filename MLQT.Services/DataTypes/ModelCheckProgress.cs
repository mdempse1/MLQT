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
    /// What the tool is doing, when that is not yet "checking class N of M".
    /// </summary>
    /// <remarks>
    /// A check does not begin with the first class: the tool has to be started and the library
    /// opened, and for a large library that is most of the wait. Reported separately from
    /// <see cref="CurrentModel"/> because it belongs to the run rather than to a class, and because
    /// the counts are both zero while it is happening - a progress dialog with nothing in it reads
    /// as a stuck application, which is what it was reported as (B259). Null once classes are being
    /// checked.
    /// </remarks>
    public string? Status { get; set; }

    /// <summary>
    /// Whether the checking operation is complete.
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Whether the checking operation was cancelled.
    /// </summary>
    public bool WasCancelled { get; set; }
}
