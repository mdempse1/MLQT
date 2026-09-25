using MLQT.Services.DataTypes;

namespace MLQT.Services.Helpers;

/// <summary>
/// What an external tool that is not there looks like to the user — one wording for both tools.
/// </summary>
/// <remarks>
/// <para>A tool that would not start used to end the run with nothing but a completion event, so the
/// dialog said "<i>Tool</i> checked nothing." in a success-coloured alert and the reason — a wrong
/// path, a start that timed out — was in the log file only (B332). Shared for the same reason as
/// <see cref="ToolTimeLimit"/>: the two services are siblings, and a message fixed in one has more
/// than once been left wrong in the other.</para>
/// </remarks>
public static class UnavailableTool
{
    /// <summary>Where the tool's path and port are set, as the settings dialog labels it.</summary>
    public const string Advice = "Check its path and port in Settings → External Tools.";

    /// <summary>The result for a run whose tool could not be started or reached.</summary>
    public static ModelCheckResult CouldNotStart(string tool, string modelId, string reason) => new()
    {
        ModelId = modelId,
        Success = false,
        ToolUnavailable = true,
        Summary = $"{tool} could not be started",
        ErrorMessage = $"{tool} could not be started, so nothing was checked: {reason} {Advice}",
    };

    /// <summary>
    /// The result for the class a tool was checking when it went away - its window closed, or its
    /// process exited. The run ends here: every class after it would be asked of a tool that is not
    /// there, and each used to come back as an empty "Check Failed" (B334).
    /// </summary>
    public static ModelCheckResult WentAway(string tool, string modelId) => new()
    {
        ModelId = modelId,
        Success = false,
        ToolUnavailable = true,
        Summary = $"{tool} stopped answering",
        ErrorMessage =
            $"{tool} stopped answering while checking {modelId} - its process has exited, or its window " +
            "was closed - so the classes after it were not checked. The next check starts a new session.",
    };
}
