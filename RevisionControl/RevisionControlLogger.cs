using NLog;

namespace RevisionControl;

/// <summary>
/// Simple logging utility for the RevisionControl library.
/// Logs exceptions from revision control operations for debugging purposes.
/// Uses the global NLog configuration set by the consuming application.
/// </summary>
internal static class RevisionControlLogger
{
    private static readonly Logger Logger = LogManager.GetLogger("RevisionControl");

    /// <summary>
    /// Logs a debug message.
    /// </summary>
    internal static void Debug(string message) => Logger.Debug(message);

    /// <summary>
    /// Logs an exception from a revision control operation.
    /// </summary>
    internal static void Error(string operation, Exception ex)
    {
        Logger.Debug(ex, $"Operation failed: {operation}");
    }
}
