using MLQT.Services.DataTypes;

namespace MLQT.Services.Helpers;

/// <summary>
/// How opening the library in an external tool went — including whether it ran out of time, which
/// the public <c>(Success, ErrorMessage)</c> pair cannot say.
/// </summary>
/// <remarks>
/// A library that ran out of time opening was reported by both services as "Failed to load library"
/// with <see cref="ModelCheckResult.TimedOut"/> false, so the one timeout a user was most likely to
/// meet - a large library, on the first check - was the one reported as a problem with their code
/// (B333). Shared because the two services are siblings.
/// </remarks>
internal readonly record struct LibraryLoad(bool Success, string? ErrorMessage, bool TimedOut = false,
    bool ToolGone = false)
{
    public static LibraryLoad Loaded => new(true, null);

    /// <summary>
    /// The message for a library the tool refused twice, with the tool's own reason when it gave one:
    /// "could not open the file" sends the user to the tool to find out why.
    /// </summary>
    public static string WouldNotOpen(string tool, string? reason) =>
        $"Could not get {tool} to open the file for this Modelica model" +
        (string.IsNullOrWhiteSpace(reason) ? "" : $": {reason.Trim()}");

    public static LibraryLoad Failed(string message) => new(false, message);

    public static LibraryLoad RanOutOfTime(string message) => new(false, message, TimedOut: true);

    /// <summary>The tool went away while opening the library (B334).</summary>
    public static LibraryLoad Gone(string message) => new(false, message, ToolGone: true);

    /// <summary>The result a run ends with when the library did not open.</summary>
    public ModelCheckResult FailureFor(string modelId, string tool) => new()
    {
        ModelId = modelId,
        Success = false,
        TimedOut = TimedOut,
        ToolUnavailable = ToolGone,
        Summary = TimedOut ? $"{tool} ran out of time"
            : ToolGone ? $"{tool} stopped answering"
            : "Failed to load library",
        ErrorMessage = ErrorMessage,
    };
}
