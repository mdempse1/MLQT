using ModelicaParser.DataTypes;

namespace MLQT.Services.Interfaces;

/// <summary>
/// Service for managing code review log messages across navigation.
/// Persists messages so they remain available when switching tabs.
/// </summary>
public interface ICodeReviewService
{
    /// <summary>
    /// Gets the current list of log messages.
    /// </summary>
    List<LogMessage> LogMessages { get; }

    /// <summary>
    /// Adds a single log message.
    /// </summary>
    void AddLogMessage(LogMessage message);

    /// <summary>
    /// Adds multiple log messages.
    /// </summary>
    void AddLogMessages(IEnumerable<LogMessage> messages);

    /// <summary>
    /// Clears all log messages.
    /// </summary>
    void ClearLogMessages();

    /// <summary>
    /// Removes a single log message.
    /// </summary>
    void RemoveLogMessage(LogMessage message);

    /// <summary>
    /// Removes all log messages associated with the specified models.
    /// Used when models are reloaded to clear old violations before re-checking.
    /// </summary>
    /// <param name="modelIds">The model IDs (full Modelica paths) to remove messages for.</param>
    void RemoveLogMessagesForModels(IEnumerable<string> modelIds);

    /// <summary>
    /// Removes all log messages matching a predicate.
    /// </summary>
    void RemoveLogMessagesByPredicate(Func<LogMessage, bool> predicate);

    /// <summary>
    /// Event fired when log messages are updated.
    /// </summary>
    event Action? OnLogMessagesChanged;
}
