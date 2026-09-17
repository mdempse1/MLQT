using System.Collections.Concurrent;
using MLQT.Services.Helpers;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using static MLQT.Services.LoggingService;

namespace MLQT.Services;

/// <summary>
/// Service implementation for monitoring file system changes in repository directories.
/// Uses FileSystemWatcher to detect changes and accumulates them for batch processing.
/// </summary>
public class FileMonitoringService : IFileMonitoringService, IDisposable
{
    // Keyed by watched path, not by repository (B168). Repositories share a path more often than it
    // looks: every repository monitors its VcsRootPath rather than its own folder, deliberately, so
    // two libraries checked out of one working copy are two repositories watching one directory — and
    // the same library reached both as a project repository and through the reference paths is the
    // same thing again. Keyed by repository that produced one OS watcher per repository over the same
    // tree, which wastes handles (inotify instances are scarce enough on Linux that this project has
    // already exhausted them once) and, worse, made the two interfere: _lastChange is keyed by path
    // alone, so the second watcher's event for the same file looked like a duplicate of the first
    // and was debounced away, leaving one of the two repositories never told its file had changed.
    //
    // One watcher per path now, fanning out to every repository subscribed to it, so the debounce is
    // applied once per edit and every subscriber still hears about it.
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(PathComparer);
    private readonly Dictionary<string, HashSet<string>> _subscribers = new(PathComparer); // path -> repositoryIds
    private readonly Dictionary<string, string> _repositoryPaths = new(); // repositoryId -> watched path

    private readonly List<FileChangeInfo> _pendingChanges = new();
    private readonly object _lock = new();

    // Debouncing: track last change time and type per file to avoid duplicate events
    // We only debounce identical consecutive events (e.g., multiple Modified events)
    // but NOT different event types (e.g., Delete followed by Add from SVN revert)
    private readonly ConcurrentDictionary<string, (DateTime Time, FileChangeType Type)> _lastChange = new();
    private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How two watched paths are told apart. Case-insensitive on Windows and macOS, case-sensitive on
    /// Linux — the same rule the filesystem itself applies, so that two spellings of one directory do
    /// not become two watchers on it.
    /// </summary>
    private static StringComparer PathComparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// The key a path is registered under: absolute, with any trailing separator removed, so that
    /// <c>C:\Repo</c> and <c>C:\Repo\</c> are one watcher rather than two.
    /// </summary>
    private static string NormalizePath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // An unusable path is not a reason to throw from here: StartMonitoring already declines
            // a directory that is not there, and this keeps that the only outcome.
            return path;
        }
    }

    public bool IsMonitoring => _watchers.Count > 0;

    /// <summary>
    /// How many <see cref="FileSystemWatcher"/> instances are open — directories, not repositories,
    /// so several repositories sharing a working copy cost one between them (B168).
    ///
    /// <para>Here because the number is an operational fact rather than a detail: each watcher is an
    /// inotify instance on Linux, the per-process limit is small, and this project has already
    /// exhausted it once — 85 of 128 taken by one watcher per resource directory. A count that grows
    /// with repositories rather than with directories is the shape of that failure returning.</para>
    /// </summary>
    public int WatchedPathCount
    {
        get
        {
            lock (_lock)
            {
                return _watchers.Count;
            }
        }
    }

    /// <inheritdoc/>
    // Asked of the repository registry, not the watcher registry — the watchers are keyed by path now
    // and several repositories can share one.
    public bool IsMonitoringRepository(string repositoryId) => _repositoryPaths.ContainsKey(repositoryId);

    public IReadOnlyList<FileChangeInfo> PendingChanges
    {
        get
        {
            lock (_lock)
            {
                return _pendingChanges.ToList().AsReadOnly();
            }
        }
    }

    public event Action<FileChangeInfo>? OnFileChanged;
    public event Action? OnPendingChangesUpdated;
    public event Action<string>? OnRepositoryFileActivity;

    public PendingChangesSummary GetPendingChangesSummary()
    {
        lock (_lock)
        {
            return new PendingChangesSummary
            {
                AddedFiles = _pendingChanges.Count(c => c.ChangeType == FileChangeType.Added && !c.IsDirectory),
                ModifiedFiles = _pendingChanges.Count(c => c.ChangeType == FileChangeType.Modified && !c.IsDirectory),
                DeletedFiles = _pendingChanges.Count(c => c.ChangeType == FileChangeType.Deleted && !c.IsDirectory),
                RenamedFiles = _pendingChanges.Count(c => c.ChangeType == FileChangeType.Renamed && !c.IsDirectory),
                AddedDirectories = _pendingChanges.Count(c => c.ChangeType == FileChangeType.Added && c.IsDirectory),
                DeletedDirectories = _pendingChanges.Count(c => c.ChangeType == FileChangeType.Deleted && c.IsDirectory)
            };
        }
    }

    public void StartMonitoring(string repositoryId, string localPath)
    {
        if (!Directory.Exists(localPath))
        {
            Warn("FileMonitoringService", $"Cannot monitor non-existent directory: {localPath}");
            return;
        }

        var watchedPath = NormalizePath(localPath);

        lock (_lock)
        {
            // Whatever this repository was watching before, it is not watching it now. This also
            // releases the old path's watcher when it was the last subscriber to it.
            if (_repositoryPaths.ContainsKey(repositoryId))
            {
                StopMonitoringInternal(repositoryId);
            }

            _repositoryPaths[repositoryId] = watchedPath;

            if (!_subscribers.TryGetValue(watchedPath, out var subscribers))
            {
                subscribers = new HashSet<string>(StringComparer.Ordinal);
                _subscribers[watchedPath] = subscribers;
            }
            subscribers.Add(repositoryId);

            // Another repository is already watching this directory; join it rather than opening a
            // second watcher over the same tree.
            if (_watchers.ContainsKey(watchedPath))
            {
                Info("FileMonitoringService",
                    $"Repository {repositoryId} joined the existing watcher at {watchedPath} " +
                    $"({subscribers.Count} repositories)");
                return;
            }

            try
            {
                var watcher = new FileSystemWatcher(watchedPath)
                {
                    NotifyFilter = NotifyFilters.FileName
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Size,
                    Filter = "*.*",
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                // The handlers close over the *path*, not over the repository that happened to ask
                // first: which repositories care is looked up when the event arrives, so one joining
                // or leaving later does not need the watcher rebuilt.
                watcher.Created += (s, e) => OnFileSystemEvent(watchedPath, e.FullPath, FileChangeType.Added);
                watcher.Changed += (s, e) => OnFileSystemEvent(watchedPath, e.FullPath, FileChangeType.Modified);
                watcher.Deleted += (s, e) => OnFileSystemEvent(watchedPath, e.FullPath, FileChangeType.Deleted);
                watcher.Renamed += (s, e) => OnFileSystemRenamedEvent(watchedPath, e.OldFullPath, e.FullPath);
                watcher.Error += (s, e) => OnWatcherError(watchedPath, e.GetException());

                _watchers[watchedPath] = watcher;

                Info("FileMonitoringService", $"Started monitoring repository {repositoryId} at {watchedPath}");
            }
            catch (Exception ex)
            {
                // The subscription is rolled back, or the repository would read as monitored while
                // nothing was watching for it.
                subscribers.Remove(repositoryId);
                if (subscribers.Count == 0)
                    _subscribers.Remove(watchedPath);
                _repositoryPaths.Remove(repositoryId);

                Error("FileMonitoringService", $"Failed to start monitoring {watchedPath}", ex);
            }
        }
    }

    public void StopMonitoring(string repositoryId)
    {
        lock (_lock)
        {
            StopMonitoringInternal(repositoryId);
        }
    }

    private void StopMonitoringInternal(string repositoryId)
    {
        if (!_repositoryPaths.TryGetValue(repositoryId, out var watchedPath))
            return;

        _repositoryPaths.Remove(repositoryId);

        if (_subscribers.TryGetValue(watchedPath, out var subscribers))
        {
            subscribers.Remove(repositoryId);

            // Another repository is still watching this directory, so the watcher stays. This is what
            // makes StopMonitoring safe for one library of a working copy that holds several: it used
            // to be the only subscriber by construction, because every repository had its own watcher.
            if (subscribers.Count > 0)
            {
                Info("FileMonitoringService",
                    $"Stopped monitoring repository {repositoryId}; {subscribers.Count} still watching {watchedPath}");
                return;
            }

            _subscribers.Remove(watchedPath);
        }

        if (_watchers.TryGetValue(watchedPath, out var watcher))
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                Warn("FileMonitoringService", $"Error disposing watcher for repository {repositoryId}: {ex.Message}");
            }

            _watchers.Remove(watchedPath);
        }

        Info("FileMonitoringService", $"Stopped monitoring repository {repositoryId}");
    }

    public void NotifyFileActivity(string repositoryId)
    {
        OnRepositoryFileActivity?.Invoke(repositoryId);
    }

    public void StopAllMonitoring()
    {
        lock (_lock)
        {
            // Over the repositories, not the watchers: the watchers are keyed by path now, and one
            // path can have several repositories on it.
            foreach (var repositoryId in _repositoryPaths.Keys.ToList())
            {
                StopMonitoringInternal(repositoryId);
            }

            Info("FileMonitoringService", "Stopped all file monitoring");
        }
    }

    public IReadOnlyList<FileChangeInfo> GetPendingChangesForRepository(string repositoryId)
    {
        lock (_lock)
        {
            return _pendingChanges
                .Where(c => c.RepositoryId == repositoryId)
                .ToList()
                .AsReadOnly();
        }
    }

    public void ClearPendingChanges()
    {
        lock (_lock)
        {
            _pendingChanges.Clear();
            _lastChange.Clear();
        }
        OnPendingChangesUpdated?.Invoke();
    }

    public void ClearPendingChanges(string repositoryId)
    {
        lock (_lock)
        {
            _pendingChanges.RemoveAll(c => c.RepositoryId == repositoryId);
        }
        OnPendingChangesUpdated?.Invoke();
    }

    /// <summary>
    /// The repositories watching a path, as a snapshot. Taken under the lock and then used outside it,
    /// because raising the events below with the lock held is how a handler that calls back in
    /// deadlocks.
    /// </summary>
    private List<string> SubscribersOf(string watchedPath)
    {
        lock (_lock)
        {
            return _subscribers.TryGetValue(watchedPath, out var subscribers)
                ? subscribers.ToList()
                : [];
        }
    }

    private void OnFileSystemEvent(string watchedPath, string fullPath, FileChangeType changeType)
    {
        var repositories = SubscribersOf(watchedPath);

        // Always signal broad file activity (used to refresh VCS status indicators for
        // non-Modelica files), but skip hidden VCS directories to avoid noise from git/svn internals.
        if (!FileMonitoringServiceHelpers.IsInHiddenDirectory(fullPath))
        {
            foreach (var repositoryId in repositories)
                OnRepositoryFileActivity?.Invoke(repositoryId);
        }

        // Filter: only track .mo files, package.order files, and directory changes
        if (!ShouldTrackPath(fullPath, changeType))
            return;

        // Debounce: ignore if we just saw the SAME change type for this file recently
        // Different change types (e.g., Delete followed by Add) should NOT be debounced
        // because this is a valid scenario (e.g., SVN revert deletes then recreates the file)
        var now = DateTime.UtcNow;
        if (_lastChange.TryGetValue(fullPath, out var lastChange))
        {
            if (lastChange.Type == changeType && now - lastChange.Time < _debounceInterval)
                return;
        }
        _lastChange[fullPath] = (now, changeType);

        var isDirectory = changeType != FileChangeType.Deleted
            ? Directory.Exists(fullPath)
            : !Path.HasExtension(fullPath);

        // One debounce decision per edit, then one change per subscribed repository. The debounce
        // above is keyed by path alone, so doing it per repository - which is what two watchers over
        // one tree amounted to - meant the first repository's event consumed the entry and the
        // second repository's identical event was discarded as a duplicate of it (B168).
        foreach (var repositoryId in repositories)
        {
            AddOrUpdateChange(new FileChangeInfo
            {
                ChangeType = changeType,
                FilePath = fullPath,
                RepositoryId = repositoryId,
                IsDirectory = isDirectory
            });
        }
    }

    private void OnFileSystemRenamedEvent(string watchedPath, string oldPath, string newPath)
    {
        if (!ShouldTrackPath(newPath, FileChangeType.Renamed) && !ShouldTrackPath(oldPath, FileChangeType.Renamed))
            return;

        foreach (var repositoryId in SubscribersOf(watchedPath))
        {
            AddOrUpdateChange(new FileChangeInfo
            {
                ChangeType = FileChangeType.Renamed,
                FilePath = newPath,
                OldFilePath = oldPath,
                RepositoryId = repositoryId,
                IsDirectory = Directory.Exists(newPath)
            });
        }
    }

    private void OnWatcherError(string repositoryId, Exception ex)
    {
        Error("FileMonitoringService", $"Watcher error for repository {repositoryId}", ex);

        // Try to restart the watcher
        string? path;
        lock (_lock)
        {
            _repositoryPaths.TryGetValue(repositoryId, out path);
        }

        if (path != null)
        {
            StopMonitoring(repositoryId);
            StartMonitoring(repositoryId, path);
        }
    }

    private bool ShouldTrackPath(string path, FileChangeType changeType)
    {
        // Skip hidden directories and files (e.g., .git, .svn)
        if (FileMonitoringServiceHelpers.IsInHiddenDirectory(path))
            return false;

        var fileName = Path.GetFileName(path);

        // Track .mo files
        if (fileName.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
            return true;

        // Track package.order files
        if (fileName.Equals("package.order", StringComparison.OrdinalIgnoreCase))
            return true;

        // Track directories (potential packages) - but only for created/deleted events
        if (changeType == FileChangeType.Added || changeType == FileChangeType.Deleted || changeType == FileChangeType.Renamed)
        {
            // For deleted items, we can't check if it was a directory, so check if it has no extension
            if (changeType == FileChangeType.Deleted && !Path.HasExtension(path))
                return true;

            // For existing items, check if it's a directory
            if (Directory.Exists(path))
                return true;
        }

        return false;
    }

    private void AddOrUpdateChange(FileChangeInfo change)
    {
        lock (_lock)
        {
            // Look for existing change for this file path
            var existingIndex = _pendingChanges.FindIndex(c =>
                c.FilePath.Equals(change.FilePath, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                var existing = _pendingChanges[existingIndex];

                // Consolidate changes:
                // Added + Deleted = Remove from list (no net change)
                // Added + Modified = Keep as Added
                // Modified + Deleted = Change to Deleted
                // Deleted + Added = Change to Modified

                if (existing.ChangeType == FileChangeType.Added &&
                    change.ChangeType == FileChangeType.Deleted)
                {
                    _pendingChanges.RemoveAt(existingIndex);
                }
                else if (existing.ChangeType == FileChangeType.Added &&
                         change.ChangeType == FileChangeType.Modified)
                {
                    // Keep as Added (file was created and then modified)
                    existing.DetectedAt = change.DetectedAt;
                }
                else if (existing.ChangeType == FileChangeType.Modified &&
                         change.ChangeType == FileChangeType.Deleted)
                {
                    existing.ChangeType = FileChangeType.Deleted;
                    existing.DetectedAt = change.DetectedAt;
                }
                else if (existing.ChangeType == FileChangeType.Deleted &&
                         change.ChangeType == FileChangeType.Added)
                {
                    existing.ChangeType = FileChangeType.Modified;
                    existing.DetectedAt = change.DetectedAt;
                }
                else
                {
                    // Replace with latest change
                    _pendingChanges[existingIndex] = change;
                }
            }
            else
            {
                _pendingChanges.Add(change);
            }
        }

        Debug("FileMonitoringService", $"Change detected: {change.ChangeType} - {change.FilePath}");
        OnFileChanged?.Invoke(change);
        OnPendingChangesUpdated?.Invoke();
    }

    public void Dispose()
    {
        StopAllMonitoring();
    }
}
