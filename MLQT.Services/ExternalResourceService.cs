using System.Collections.Concurrent;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using MLQT.Services.DataTypes;
using MLQT.Services.Helpers;
using MLQT.Services.Interfaces;
using static MLQT.Services.LoggingService;

namespace MLQT.Services;

/// <summary>
/// Service for analyzing, validating, and monitoring external resource files
/// referenced by Modelica models.
/// </summary>
public class ExternalResourceService : IExternalResourceService, IDisposable
{
    private readonly Dictionary<string, List<ExternalResourceReference>> _modelResources = new();
    private readonly Dictionary<string, HashSet<string>> _reverseIndex = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly List<ResourceWarning> _warnings = new();
    private readonly object _lock = new();

    // Files contained within monitored directories (IncludeDirectory, etc.)
    // These files appear in the tree but don't have direct model references.
    // Their reference count is 0 unless there's a direct Include directive.
    private readonly List<ExternalResourceReference> _directoryContents = new();

    // Resource file monitoring
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, (DateTime Time, WatcherChangeTypes Type)> _lastChange = new();
    private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(500);
    private readonly List<ResourceFileChange> _pendingResourceChanges = new();

    public async Task AnalyzeResourcesAsync(DirectedGraph graph)
    {
        lock (_lock)
        {
            _modelResources.Clear();
            _reverseIndex.Clear();
            _warnings.Clear();
            _directoryContents.Clear();
        }

        // Read resource data from graph nodes and edges
        foreach (var edge in graph.ResourceEdges)
        {
            ProcessResourceEdge(graph, edge);
        }

        await Task.CompletedTask;
    }

    public async Task AnalyzeResourcesForModelsAsync(
        IEnumerable<string> modelIds,
        DirectedGraph graph)
    {
        var modelIdList = modelIds.ToList();

        // Clear old data for these models
        ClearDataForModels(modelIdList);

        // Clear and rebuild directory contents (simpler than tracking per-model)
        lock (_lock)
        {
            _directoryContents.Clear();
        }

        // Read resource edges for the specified models
        foreach (var modelId in modelIdList)
        {
            var edges = graph.GetResourceEdgesForModel(modelId);
            foreach (var edge in edges)
            {
                ProcessResourceEdge(graph, edge);
            }
        }

        // Also rebuild directory contents from all other models' directory edges
        foreach (var edge in graph.ResourceEdges)
        {
            if (modelIdList.Contains(edge.ModelId))
                continue; // Already processed above

            var node = graph.GetNode(edge.ResourceNodeId);
            if (node is ResourceDirectoryNode dirNode)
            {
                ProcessDirectoryContents(graph, dirNode, edge.ReferenceType);
            }
        }

        // Update watchers to reflect any new/removed resource paths
        UpdateWatchers();

        await Task.CompletedTask;
    }

    /// <summary>
    /// Processes a single resource edge from the graph, creating ExternalResourceReference
    /// and updating internal indexes.
    /// </summary>
    private void ProcessResourceEdge(DirectedGraph graph, ResourceEdge edge)
    {
        var resourceNode = graph.GetNode(edge.ResourceNodeId);
        if (resourceNode == null)
            return;

        string? resolvedPath = null;
        bool fileExists = false;
        bool isImageFile = false;
        bool isDirectory = false;

        if (resourceNode is ResourceFileNode fileNode)
        {
            resolvedPath = fileNode.ResolvedPath;
            fileExists = fileNode.FileExists;
            isImageFile = fileNode.IsImageFile;
        }
        else if (resourceNode is ResourceDirectoryNode dirNode)
        {
            resolvedPath = dirNode.ResolvedPath;
            fileExists = dirNode.DirectoryExists;
            isDirectory = true;

            // Also add entries for files contained within this directory.
            // These files appear in the tree but don't have direct model references.
            // Their reference count is 0 unless there's a separate Include directive.
            ProcessDirectoryContents(graph, dirNode, edge.ReferenceType);
        }

        var reference = new ExternalResourceReference
        {
            ModelId = edge.ModelId,
            RawPath = edge.RawPath,
            ResolvedPath = resolvedPath,
            ReferenceType = edge.ReferenceType,
            ParameterName = edge.ParameterName,
            IsAbsolutePath = edge.IsAbsolutePath,
            FileExists = fileExists,
            IsImageFile = isImageFile,
            IsDirectory = isDirectory
        };

        // Add to model resources
        lock (_lock)
        {
            if (!_modelResources.TryGetValue(edge.ModelId, out var resources))
            {
                resources = new List<ExternalResourceReference>();
                _modelResources[edge.ModelId] = resources;
            }
            resources.Add(reference);
        }

        // Update reverse index
        if (resolvedPath != null)
        {
            lock (_lock)
            {
                var key = NormalizePath(resolvedPath);
                if (!_reverseIndex.TryGetValue(key, out var modelSet))
                {
                    modelSet = new HashSet<string>();
                    _reverseIndex[key] = modelSet;
                }
                modelSet.Add(edge.ModelId);
            }
        }

        // Generate warnings (skip for directories and non-path reference types)
        if (!isDirectory)
        {
            GenerateWarnings(reference);
        }
    }

    public List<ExternalResourceReference> GetResourcesForModel(string modelId)
    {
        lock (_lock)
        {
            return _modelResources.TryGetValue(modelId, out var resources)
                ? resources.ToList()
                : new List<ExternalResourceReference>();
        }
    }

    public List<string> GetModelsReferencingResource(string resolvedFilePath)
    {
        lock (_lock)
        {
            var key = NormalizePath(resolvedFilePath);
            return _reverseIndex.TryGetValue(key, out var modelIds)
                ? modelIds.ToList()
                : new List<string>();
        }
    }

    public List<ResourceWarning> GetWarnings()
    {
        lock (_lock)
        {
            return _warnings.ToList();
        }
    }

    public List<ExternalResourceReference> GetAllResources()
    {
        lock (_lock)
        {
            // Include both directly referenced resources and files contained in directories
            var result = _modelResources.Values.SelectMany(r => r).ToList();
            result.AddRange(_directoryContents);
            return result;
        }
    }

    public void ClearDataForModels(IEnumerable<string> modelIds)
    {
        lock (_lock)
        {
            foreach (var modelId in modelIds)
            {
                // Remove model resources and update reverse index
                if (_modelResources.TryGetValue(modelId, out var resources))
                {
                    foreach (var resource in resources)
                    {
                        if (resource.ResolvedPath != null)
                        {
                            var key = NormalizePath(resource.ResolvedPath);
                            if (_reverseIndex.TryGetValue(key, out var modelSet))
                            {
                                modelSet.Remove(modelId);
                                if (modelSet.Count == 0)
                                    _reverseIndex.Remove(key);
                            }
                        }
                    }
                    _modelResources.Remove(modelId);
                }

                // Remove warnings for this model
                _warnings.RemoveAll(w => w.ModelId == modelId);
            }
        }
    }

    public void StartMonitoringResources()
    {
        UpdateWatchers();
        Info("ExternalResourceService", $"Started monitoring {_watchers.Count} resource directories");
    }

    public void StopMonitoringResources()
    {
        lock (_lock)
        {
            foreach (var watcher in _watchers.Values)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch (Exception ex)
                {
                    Warn("ExternalResourceService", $"Error disposing resource watcher: {ex.Message}");
                }
            }
            _watchers.Clear();
            _lastChange.Clear();
        }

        Info("ExternalResourceService", "Stopped all resource file monitoring");
    }

    /// <summary>
    /// Processes files contained within a directory node, adding them to the
    /// _directoryContents list. These files appear in the tree but don't have
    /// direct model references - their reference count reflects only direct
    /// Include directives, not directory membership.
    /// </summary>
    private void ProcessDirectoryContents(
        DirectedGraph graph,
        ResourceDirectoryNode dirNode,
        ResourceReferenceType dirReferenceType)
    {
        foreach (var fileId in dirNode.ContainedFileIds)
        {
            var fileNode = graph.GetNode<ResourceFileNode>(fileId);
            if (fileNode == null)
                continue;

            // Check if this file already has entries (from direct Include references)
            // If so, skip adding it to directory contents to avoid duplicates
            var normalizedPath = NormalizePath(fileNode.ResolvedPath);
            lock (_lock)
            {
                if (_reverseIndex.ContainsKey(normalizedPath))
                    continue; // File has direct references, don't add as directory content

                // Check if already in directory contents
                if (_directoryContents.Any(r =>
                    r.ResolvedPath != null &&
                    string.Equals(NormalizePath(r.ResolvedPath), normalizedPath, StringComparison.Ordinal)))
                    continue;

                _directoryContents.Add(new ExternalResourceReference
                {
                    ModelId = "", // Empty - not directly referenced by any model
                    RawPath = fileNode.ResolvedPath, // Use resolved path as raw path for display
                    ResolvedPath = fileNode.ResolvedPath,
                    ReferenceType = dirReferenceType, // Inherit from parent directory
                    ParameterName = null,
                    IsAbsolutePath = false,
                    FileExists = fileNode.FileExists,
                    IsImageFile = fileNode.IsImageFile,
                    IsDirectory = false
                });
            }
        }
    }

    /// <summary>
    /// Generates warnings for a resource reference.
    /// </summary>
    private void GenerateWarnings(ExternalResourceReference reference)
    {
        lock (_lock)
        {
            if (reference.IsAbsolutePath)
            {
                _warnings.Add(new ResourceWarning
                {
                    ModelId = reference.ModelId,
                    ResourcePath = reference.RawPath,
                    WarningType = ResourceWarningType.AbsolutePath,
                    Message = $"Absolute path is not portable: {reference.RawPath}"
                });
            }

            if (reference.ResolvedPath != null && !reference.FileExists)
            {
                _warnings.Add(new ResourceWarning
                {
                    ModelId = reference.ModelId,
                    ResourcePath = reference.ResolvedPath,
                    WarningType = ResourceWarningType.MissingFile,
                    Message = $"Resource file not found: {reference.ResolvedPath}"
                });
            }
        }
    }

    /// <summary>
    /// Updates FileSystemWatchers to match the current set of referenced resource directories.
    /// Adds watchers for new directories and removes watchers for directories no longer referenced.
    /// </summary>
    private void UpdateWatchers()
    {
        HashSet<string> neededDirectories;

        lock (_lock)
        {
            // Collect all unique directories containing referenced resource files
            neededDirectories = new HashSet<string>(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            foreach (var resolvedPath in _reverseIndex.Keys)
            {
                var dir = Path.GetDirectoryName(resolvedPath);
                if (dir != null && Directory.Exists(dir))
                {
                    neededDirectories.Add(dir);
                }
            }
        }

        lock (_lock)
        {
            // Remove watchers for directories no longer needed
            var toRemove = _watchers.Keys
                .Where(dir => !neededDirectories.Contains(dir))
                .ToList();

            foreach (var dir in toRemove)
            {
                try
                {
                    _watchers[dir].EnableRaisingEvents = false;
                    _watchers[dir].Dispose();
                }
                catch (Exception ex)
                {
                    Warn("ExternalResourceService", $"Error disposing watcher for {dir}: {ex.Message}");
                }
                _watchers.Remove(dir);
            }

            // Add watchers for new directories
            foreach (var dir in neededDirectories)
            {
                if (_watchers.ContainsKey(dir))
                    continue;

                try
                {
                    var watcher = new FileSystemWatcher(dir)
                    {
                        NotifyFilter = NotifyFilters.FileName
                                     | NotifyFilters.LastWrite
                                     | NotifyFilters.Size,
                        Filter = "*.*",
                        IncludeSubdirectories = false,
                        EnableRaisingEvents = true
                    };

                    var capturedDir = dir;
                    watcher.Created += (s, e) => OnResourceFileSystemEvent(capturedDir, e.FullPath, WatcherChangeTypes.Created);
                    watcher.Changed += (s, e) => OnResourceFileSystemEvent(capturedDir, e.FullPath, WatcherChangeTypes.Changed);
                    watcher.Deleted += (s, e) => OnResourceFileSystemEvent(capturedDir, e.FullPath, WatcherChangeTypes.Deleted);
                    watcher.Renamed += (s, e) =>
                    {
                        OnResourceFileSystemEvent(capturedDir, e.OldFullPath, WatcherChangeTypes.Deleted);
                        OnResourceFileSystemEvent(capturedDir, e.FullPath, WatcherChangeTypes.Created);
                    };
                    watcher.Error += (s, e) => OnWatcherError(capturedDir, e.GetException());

                    _watchers[dir] = watcher;
                    Debug("ExternalResourceService", $"Watching resource directory: {dir}");
                }
                catch (Exception ex)
                {
                    Error("ExternalResourceService", $"Failed to create watcher for {dir}", ex);
                }
            }
        }
    }

    /// <summary>
    /// Handles file system events for resource files.
    /// Uses type-aware debouncing (matching FileMonitoringService) to handle SVN revert correctly.
    /// </summary>
    internal void OnResourceFileSystemEvent(string directory, string fullPath, WatcherChangeTypes changeType)
    {
        // Skip hidden directories
        if (FileMonitoringServiceHelpers.IsInHiddenDirectory(fullPath))
            return;

        // Only track files that are in our reverse index (referenced by models)
        var normalizedPath = NormalizePath(fullPath);

        bool isTracked;
        lock (_lock)
        {
            isTracked = _reverseIndex.ContainsKey(normalizedPath);
        }

        if (!isTracked)
            return;

        // Type-aware debouncing: only suppress identical consecutive change types
        // Different change types (e.g., Delete then Add from SVN revert) pass through
        var now = DateTime.UtcNow;
        if (_lastChange.TryGetValue(normalizedPath, out var lastChange))
        {
            if (lastChange.Type == changeType && now - lastChange.Time < _debounceInterval)
                return;
        }
        _lastChange[normalizedPath] = (now, changeType);

        // Consolidate changes (matching FileMonitoringService pattern)
        lock (_lock)
        {
            var existingIndex = _pendingResourceChanges.FindIndex(c =>
                string.Equals(c.ResolvedPath, normalizedPath,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

            if (existingIndex >= 0)
            {
                var existing = _pendingResourceChanges[existingIndex];

                // Consolidation rules:
                if (existing.ChangeType == WatcherChangeTypes.Created &&
                    changeType == WatcherChangeTypes.Deleted)
                {
                    // Added + Deleted = no net change
                    _pendingResourceChanges.RemoveAt(existingIndex);
                    return;
                }
                else if (existing.ChangeType == WatcherChangeTypes.Created &&
                         changeType == WatcherChangeTypes.Changed)
                {
                    // Added + Modified = keep as Added
                    existing.DetectedAt = now;
                }
                else if (existing.ChangeType == WatcherChangeTypes.Changed &&
                         changeType == WatcherChangeTypes.Deleted)
                {
                    // Modified + Deleted = change to Deleted
                    existing.ChangeType = WatcherChangeTypes.Deleted;
                    existing.DetectedAt = now;
                }
                else if (existing.ChangeType == WatcherChangeTypes.Deleted &&
                         changeType == WatcherChangeTypes.Created)
                {
                    // Deleted + Added = treat as Modified (SVN revert scenario)
                    existing.ChangeType = WatcherChangeTypes.Changed;
                    existing.DetectedAt = now;
                }
                else
                {
                    // Replace with latest
                    _pendingResourceChanges[existingIndex] = new ResourceFileChange
                    {
                        ResolvedPath = normalizedPath,
                        ChangeType = changeType,
                        DetectedAt = now
                    };
                }
            }
            else
            {
                _pendingResourceChanges.Add(new ResourceFileChange
                {
                    ResolvedPath = normalizedPath,
                    ChangeType = changeType,
                    DetectedAt = now
                });
            }
        }

        // Look up affected models and fire event
        List<string> affectedModels;
        lock (_lock)
        {
            affectedModels = _reverseIndex.TryGetValue(normalizedPath, out var modelSet)
                ? modelSet.ToList()
                : new List<string>();
        }

        if (affectedModels.Count > 0)
        {
            Debug("ExternalResourceService",
                $"Resource file {changeType}: {fullPath} (affects {affectedModels.Count} model(s))");
        }
    }

    /// <summary>
    /// Handles watcher errors by recreating the watcher.
    /// </summary>
    internal void OnWatcherError(string directory, Exception ex)
    {
        Error("ExternalResourceService", $"Watcher error for directory {directory}", ex);

        lock (_lock)
        {
            if (_watchers.TryGetValue(directory, out var watcher))
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch { }
                _watchers.Remove(directory);
            }
        }

        // Recreate the watcher
        UpdateWatchers();
    }

    /// <summary>
    /// Normalizes a file path for consistent dictionary lookups.
    /// On Windows, converts to lowercase for case-insensitive matching.
    /// </summary>
    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return OperatingSystem.IsWindows() ? fullPath.ToLowerInvariant() : fullPath;
    }

    /// <summary>
    /// Gets the current pending resource changes (for testing).
    /// </summary>
    internal IReadOnlyList<ResourceFileChange> GetPendingChanges()
    {
        lock (_lock)
        {
            return _pendingResourceChanges.ToList().AsReadOnly();
        }
    }

    public void Dispose()
    {
        StopMonitoringResources();
    }

    /// <summary>
    /// Internal class for tracking pending resource file changes with consolidation.
    /// </summary>
    internal class ResourceFileChange
    {
        public string ResolvedPath { get; set; } = "";
        public WatcherChangeTypes ChangeType { get; set; }
        public DateTime DetectedAt { get; set; }
    }
}
