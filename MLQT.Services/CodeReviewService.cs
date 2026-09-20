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

    // A style check delivers its findings a class at a time — 1,397 batches for the Modelica
    // Standard Library on five rules, with a median of two findings in each — and every one of them
    // used to raise this event. Each raise costs the UI thread a copy of the whole findings list, a
    // sort of it, a scan for misspellings and a full re-render of the page; the desktop host runs
    // its window message pump on that same thread, so during a check the window stopped following
    // the mouse and jumped to where it had been dropped (B190).
    //
    // So a burst coalesces. The first change is announced at once, because the common case is a
    // single edit the user is waiting to see; anything arriving inside the window is collapsed into
    // one trailing announcement after it. Nothing is lost by that — the list itself is always
    // current, and this only says "look again".
    private static readonly TimeSpan NotifyWindow = TimeSpan.FromMilliseconds(250);
    private long _lastNotifyTicks;
    private int _trailingQueued;

    /// <summary>Announces a change, at most once per <see cref="NotifyWindow"/> plus a trailing one.</summary>
    private void NotifyChanged()
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastNotifyTicks) >= NotifyWindow.TotalMilliseconds)
        {
            Interlocked.Exchange(ref _lastNotifyTicks, now);
            OnLogMessagesChanged?.Invoke();
            return;
        }

        if (Interlocked.CompareExchange(ref _trailingQueued, 1, 0) != 0)
            return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(NotifyWindow);
            Interlocked.Exchange(ref _trailingQueued, 0);
            Interlocked.Exchange(ref _lastNotifyTicks, Environment.TickCount64);
            OnLogMessagesChanged?.Invoke();
        });
    }

    /// <summary>
    /// Announces a change now, whatever the throttle would have said. For the places where the user
    /// is looking at the thing that changed and a quarter of a second of nothing reads as a failure.
    /// </summary>
    private void NotifyChangedNow()
    {
        Interlocked.Exchange(ref _lastNotifyTicks, Environment.TickCount64);
        OnLogMessagesChanged?.Invoke();
    }

    /// <inheritdoc/>
    public void AddLogMessage(LogMessage message)
    {
        lock (_lock)
        {
            _logMessages.Add(message);
        }
        NotifyChanged();
    }

    /// <inheritdoc/>
    public void AddLogMessages(IEnumerable<LogMessage> messages)
    {
        lock (_lock)
        {
            _logMessages.AddRange(messages);
        }
        NotifyChanged();
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
            // Coalesced like the adds: a re-check drops each model's findings before re-adding
            // them, so removals arrive in the same burst (B190).
            NotifyChanged();
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
            NotifyChanged();
        }
    }

    /// <inheritdoc/>
    public void ClearLogMessages()
    {
        lock (_lock)
        {
            _logMessages.Clear();
        }

        // Not coalesced: clearing is something the user did and is watching for.
        NotifyChangedNow();
    }
}
