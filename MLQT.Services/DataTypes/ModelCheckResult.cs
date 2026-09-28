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

    /// <summary>
    /// What the tool logged while checking, whether or not it found a problem.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ErrorMessage"/> because <b>a successful check still has something to
    /// say</b>. Dymola's <c>checkModel</c> returns true for a model that is fine and for one that is
    /// fine apart from six warnings; <c>getLastError()</c> is where the difference is. Putting that
    /// in a field called ErrorMessage would make every warning look like a failure (B170).
    /// </remarks>
    public string? Log { get; set; }

    /// <summary>
    /// The tool did not finish within its time limit. Not a verdict on the model: a large one that
    /// is perfectly sound looks the same. A run stops at the first of these, because the next class
    /// would wait on a tool that is still busy or has been restarted (B263).
    /// </summary>
    public bool TimedOut { get; set; }

    /// <summary>
    /// The tool could not be started, or stopped answering partway through a run. Not a verdict on
    /// the model either, and a run ends at the first of these, because every class after it would be
    /// asked of a tool that is not there (B332, B334).
    /// </summary>
    public bool ToolUnavailable { get; set; }

    /// <summary>
    /// Whether this says something is wrong with the model: it failed, and not because the tool ran
    /// out of time or was not there to ask. Only these belong in the findings (B333).
    /// </summary>
    public bool IsModelFailure => !Success && !TimedOut && !ToolUnavailable;
}
