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
    /// Logs an informational message — used for per-operation timing summaries
    /// that the consuming application is expected to see in production logs.
    /// </summary>
    internal static void Info(string message) => Logger.Info(message);

    /// <summary>
    /// Logs an exception from a revision control operation, with its stack and inner exceptions.
    /// </summary>
    /// <remarks>
    /// At <c>Warn</c>, not <c>Debug</c>. Every caller has caught the exception and turned it into a
    /// false, a null or an empty list, so this line is the only place the exception survives - and a
    /// host that records Info and above, as a production host does, never saw it. An operation that
    /// fails this way is not routine, so the line is worth its space.
    /// </remarks>
    internal static void Error(string operation, Exception ex)
    {
        Logger.Warn(ex, $"Operation failed: {operation}");
    }

    /// <summary>
    /// Logs the exception an operation failed with (<see cref="Error"/>) and returns the message its
    /// result reports. The message is the exception's own followed by each inner exception's, because
    /// the outer one alone is often a wrapper that names no cause: a
    /// <see cref="TypeInitializationException"/> says only which type's initializer threw.
    /// </summary>
    internal static string Failed(string operation, Exception ex)
    {
        Error(operation, ex);
        return Describe(ex);
    }

    /// <summary>
    /// The exception's message, then each inner exception's after an arrow. An
    /// <see cref="AggregateException"/> - what a failure inside a parallel loop or a task arrives as - is
    /// described by every exception it holds rather than by its own wrapper's message, which names none
    /// of them usefully.
    /// </summary>
    internal static string Describe(Exception ex)
    {
        var parts = new List<string>();
        Collect(ex);
        return parts.Count == 0 ? ex.GetType().FullName ?? "Unknown error" : string.Join(" -> ", parts);

        void Collect(Exception e)
        {
            if (e is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
            {
                foreach (var inner in aggregate.InnerExceptions)
                    Collect(inner);
                return;
            }
            if (!string.IsNullOrWhiteSpace(e.Message) && !parts.Contains(e.Message))
                parts.Add(e.Message);
            if (e.InnerException is { } next)
                Collect(next);
        }
    }
}
