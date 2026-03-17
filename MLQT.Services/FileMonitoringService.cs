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
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, string> _repositoryPaths = new(); // repositoryId -> localPath
    private readonly List<FileChangeInfo> _pendingChanges = new();
    private readonly object _lock = new();

    // Debouncing: track last change time and type per file to avoid duplicate events
    // We only debounce identical consecutive events (e.g., multiple Modified events)
    // but NOT different event types (e.g., Delete followed by Add from SVN revert)
    private readonly ConcurrentDictionary<string, (DateTime Time, FileChangeType Type)> _lastChange = new();
    private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(500);

    public bool IsMonitoring => _watchers.Count > 0;

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

        lock (_lock)
        {
            // Stop existing watcher if any
            if (_watchers.ContainsKey(repositoryId))
            {
                StopMonitoringInternal(repositoryId);
            }

            try
            {
                var watcher = new FileSystemWatcher(localPath)
                {
                    NotifyFilter = NotifyFilters.FileName
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Size,
                    Filter = "*.*",
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                watcher.Created += (s, e) => OnFileSystemEvent(repositoryId, e.FullPath, FileChangeType.Added);
                watcher.Changed += (s, e) => OnFileSystemEvent(repositoryId, e.FullPath, FileChangeType.Modified);
                watcher.Deleted += (s, e) => OnFileSystemEvent(repositoryId, e.FullPath, FileChangeType.Deleted);
                watcher.Renamed += (s, e) => OnFileSystemRenamedEvent(repositoryId, e.OldFullPath, e.FullPath);
                watcher.Error += (s, e) => OnWatcherError(repositoryId, e.GetException());

                _watchers[repositoryId] = watcher;
                _repositoryPaths[repositoryId] = localPath;

                Info("FileMonitoringService", $"Started monitoring repository {repositoryId} at {localPath}");
            }
            catch (Exception ex)
            {
                Error("FileMonitoringService", $"Failed to start monitoring {localPath}", ex);
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
        if (_watchers.TryGetValue(repositoryId, out var watcher))
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

            _watchers.Remove(repositoryId);
            _repositoryPaths.Remove(repositoryId);

            Info("FileMonitoringService", $"Stopped monitoring repository {repositoryId}");
        }
    }

    public void NotifyFileActivity(string repositoryId)
    {
        OnRepositoryFileActivity?.Invoke(repositoryId);
    }

    public void StopAllMonitoring()
    {
        lock (_lock)
        {
            foreach (var repositoryId in _watchers.Keys.ToList())
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

    private void OnFileSystemEvent(string repositoryId, string fullPath, FileChangeType changeType)
    {
        // Always signal broad file activity (used to refresh VCS status indicators for
        // non-Modelica files), but skip hidden VCS directories to avoid noise from git/svn internals.
        if (!FileMonitoringServiceHelpers.IsInHiddenDirectory(fullPath))
            OnRepositoryFileActivity?.Invoke(repositoryId);

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

        var changeInfo = new FileChangeInfo
        {
            ChangeType = changeType,
            FilePath = fullPath,
            RepositoryId = repositoryId,
            IsDirectory = isDirectory
        };

        AddOrUpdateChange(changeInfo);
    }

    private void OnFileSystemRenamedEvent(string repositoryId, string oldPath, string newPath)
    {
        if (!ShouldTrackPath(newPath, FileChangeType.Renamed) && !ShouldTrackPath(oldPath, FileChangeType.Renamed))
            return;

        var changeInfo = new FileChangeInfo
        {
            ChangeType = FileChangeType.Renamed,
            FilePath = newPath,
            OldFilePath = oldPath,
            RepositoryId = repositoryId,
            IsDirectory = Directory.Exists(newPath)
        };

        AddOrUpdateChange(changeInfo);
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
