using ModelicaParser.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.Services;

/// <summary>
/// Singleton service that manages code review log messages.
/// Persists messages across component navigation and tab switches.
/// </summary>
public class CodeReviewService : ICodeReviewService
{
    private readonly List<LogMessage> _logMessages = new();
    private readonly object _lock = new();

    /// <inheritdoc/>
    public List<LogMessage> LogMessages
    {
        get
        {
            lock (_lock)
            {
                return new List<LogMessage>(_logMessages);
            }
        }
    }

    /// <inheritdoc/>
    public event Action? OnLogMessagesChanged;

    /// <inheritdoc/>
    public void AddLogMessage(LogMessage message)
    {
        lock (_lock)
        {
            _logMessages.Add(message);
        }
        OnLogMessagesChanged?.Invoke();
    }

    /// <inheritdoc/>
    public void AddLogMessages(IEnumerable<LogMessage> messages)
    {
        lock (_lock)
        {
            _logMessages.AddRange(messages);
        }
        OnLogMessagesChanged?.Invoke();
    }

    /// <summary>
    /// Removes a single log message.
    /// </summary>
    public void RemoveLogMessage(LogMessage message)
    {
        if (message == null)
            return;
        lock(_lock)
        {
            _logMessages.Remove(message);
        }
    }

    /// <inheritdoc/>
    public void RemoveLogMessagesForModels(IEnumerable<string> modelIds)
    {
        var modelIdSet = new HashSet<string>(modelIds);
        if (modelIdSet.Count == 0)
            return;

        int removedCount;
        lock (_lock)
        {
            removedCount = _logMessages.RemoveAll(m => modelIdSet.Contains(m.ModelName));
        }

        if (removedCount > 0)
        {
            OnLogMessagesChanged?.Invoke();
        }
    }

    /// <inheritdoc/>
    public void RemoveLogMessagesByPredicate(Func<LogMessage, bool> predicate)
    {
        int removedCount;
        lock (_lock)
        {
            removedCount = _logMessages.RemoveAll(m => predicate(m));
        }

        if (removedCount > 0)
        {
            OnLogMessagesChanged?.Invoke();
        }
    }

    /// <inheritdoc/>
    public void ClearLogMessages()
    {
        lock (_lock)
        {
            _logMessages.Clear();
        }
        OnLogMessagesChanged?.Invoke();
    }
}
