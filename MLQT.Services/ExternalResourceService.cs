using System.Collections.Concurrent;
using ModelicaGraph;
using ModelicaGraph.Analysis;
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
    /// <summary>The directory name a Modelica library keeps its external resources under.</summary>
    private const string ResourcesDirectoryName = "Resources";

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

        // What is wrong with this reference, decided where MLQT.Resource.Missing and
        // MLQT.Resource.AbsolutePath decide it, so the tab and the findings cannot disagree. It also
        // reads existence from the disk now, rather than trusting the node's flag from whenever the
        // node was made (B521).
        // Whether it exists is asked of the resource itself: a reference a nested class makes is
        // that class's problem and not this one's, which says nothing about the file being there.
        var problems = ResourceProblems.Of(graph, edge).ToList();
        fileExists = ResourceProblems.Exists(resourceNode);

        if (resourceNode is ResourceFileNode fileNode)
        {
            resolvedPath = fileNode.ResolvedPath;
            isImageFile = fileNode.IsImageFile;
        }
        else if (resourceNode is ResourceDirectoryNode dirNode)
        {
            resolvedPath = dirNode.ResolvedPath;
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

        // Directories included: a directory annotation that names nothing is recorded since phase 2,
        // and the rule reports it, so the tab has to as well.
        GenerateWarnings(problems);
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
        var directories = UpdateWatchers();

        int watchers;
        lock (_lock)
        {
            watchers = _watchers.Count;
        }

        // Both numbers, because they are no longer the same one: the watchers are placed on collapsed
        // roots (see WatchRootFor), so "84 directories" used to mean "84 watchers, and the rest failed".
        Info("ExternalResourceService",
            $"Started monitoring {directories} resource directories through {watchers} watcher(s)");
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
                    // Read from disk now, as every other reference is (B521).
                    FileExists = ResourceProblems.Exists(fileNode),
                    IsImageFile = fileNode.IsImageFile,
                    IsDirectory = false
                });
            }
        }
    }

    /// <summary>
    /// The tab's warnings for one reference: the problems <see cref="ResourceProblems"/> found, each
    /// as the warning the page shows.
    /// </summary>
    private void GenerateWarnings(IReadOnlyList<ResourceProblem> problems)
    {
        lock (_lock)
        {
            foreach (var problem in problems)
            {
                _warnings.Add(problem.Kind == ResourceProblemKind.AbsolutePath
                    ? new ResourceWarning
                    {
                        ModelId = problem.ModelId,
                        ResourcePath = problem.RawPath,
                        WarningType = ResourceWarningType.AbsolutePath,
                        Message = $"Absolute path is not portable: {problem.RawPath}"
                    }
                    : new ResourceWarning
                    {
                        ModelId = problem.ModelId,
                        ResourcePath = problem.ResolvedPath,
                        WarningType = ResourceWarningType.MissingFile,
                        Message = problem.IsDirectory
                            ? $"Resource directory not found: {problem.ResolvedPath}"
                            : $"Resource file not found: {problem.ResolvedPath}"
                    });
            }
        }
    }

    /// <summary>
    /// Where to put the watcher that covers <paramref name="directory"/>, and whether it has to be
    /// recursive to do so.
    /// </summary>
    /// <remarks>
    /// <para>A library's resources live under <c>&lt;library&gt;/Resources/</c> — that is what a
    /// <c>modelica://Library/Resources/...</c> URI resolves to — so a referenced directory almost
    /// always has a <c>Resources</c> ancestor, and every resource directory of one library shares it.
    /// Collapsing to that ancestor and watching it recursively turns the Modelica Standard Library's
    /// ~190 resource directories into <b>two</b> watchers.</para>
    ///
    /// <para><b>This is a resource limit, not a tidiness exercise.</b> One
    /// <see cref="FileSystemWatcher"/> costs one inotify <i>instance</i> on Linux and the per-user limit
    /// is 128 by default, so the watcher-per-directory version could not monitor MSL at all: it logged
    /// 265 failures, settled for the 84 it got, and starved every other process of the user's — which is
    /// how it took the MCP server down (B163). Windows has no comparable limit, which is why the design
    /// survived until the host moved.</para>
    ///
    /// <para>The outermost <c>Resources</c> is taken rather than the nearest, so a nested one collapses
    /// too. A directory with no such ancestor — a resource referenced by absolute path from somewhere
    /// else entirely — keeps the old behaviour: itself, not recursive, since there is no bound on what
    /// its parent might contain.</para>
    /// </remarks>
    internal static (string Root, bool Recursive) WatchRootFor(string directory)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var full = Path.GetFullPath(directory);

        string? resourcesRoot = null;
        for (var dir = full; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (string.Equals(Path.GetFileName(dir), ResourcesDirectoryName, comparison))
                resourcesRoot = dir;   // keep going: the last match up the chain is the outermost
        }

        return resourcesRoot is null ? (full, false) : (resourcesRoot, true);
    }

    /// <summary>
    /// Updates FileSystemWatchers to match the current set of referenced resource directories.
    /// Adds watchers for newly covered roots and removes watchers no longer covering anything.
    /// </summary>
    /// <returns>The number of resource directories the watchers now cover.</returns>
    /// <remarks>
    /// Watchers are placed on the roots <see cref="WatchRootFor"/> chooses, not on the directories
    /// themselves — several hundred referenced directories normally collapse to one watcher per library.
    /// Nothing downstream depends on which directory an event arrived through:
    /// <see cref="OnResourceFileSystemEvent"/> filters on the reverse index, so a recursive watcher
    /// simply offers it more paths to reject.
    /// </remarks>
    private int UpdateWatchers()
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        // Each directory to watch, and whether what it must report is further down than its own
        // entries.
        Dictionary<string, bool> neededDirectories;

        lock (_lock)
        {
            neededDirectories = new Dictionary<string, bool>(comparer);

            foreach (var resolvedPath in _reverseIndex.Keys)
            {
                // The nearest directory that exists. A missing file in a missing Resources/Data is
                // heard coming back only by watching what is above it, recursively, since the
                // directory it will appear in does not exist to be watched.
                var parent = Path.GetDirectoryName(resolvedPath);
                var dir = parent;
                while (dir != null && !Directory.Exists(dir))
                    dir = Path.GetDirectoryName(dir);
                if (dir == null)
                    continue;

                var deeper = !comparer.Equals(dir, parent);
                neededDirectories[dir] = neededDirectories.GetValueOrDefault(dir) || deeper;
            }
        }

        // Collapse those directories to the roots actually watched; a root is recursive when its own
        // rule says so or when any directory it covers needs it.
        var neededRoots = new Dictionary<string, bool>(comparer);
        foreach (var (dir, deeper) in neededDirectories)
        {
            var (root, recursive) = WatchRootFor(dir);
            neededRoots[root] = neededRoots.GetValueOrDefault(root) || recursive || deeper;
        }

        lock (_lock)
        {
            // Remove watchers for roots no longer needed
            var toRemove = _watchers.Keys
                .Where(root => !neededRoots.ContainsKey(root))
                .ToList();

            foreach (var root in toRemove)
            {
                try
                {
                    _watchers[root].EnableRaisingEvents = false;
                    _watchers[root].Dispose();
                }
                catch (Exception ex)
                {
                    Warn("ExternalResourceService", $"Error disposing watcher for {root}: {ex.Message}");
                }
                _watchers.Remove(root);
            }

            // Add watchers for new roots
            foreach (var (root, recursive) in neededRoots)
            {
                if (_watchers.TryGetValue(root, out var existing))
                {
                    if (existing.IncludeSubdirectories != recursive)
                        existing.IncludeSubdirectories = recursive;
                    continue;
                }

                try
                {
                    var watcher = new FileSystemWatcher(root)
                    {
                        // DirectoryName: an IncludeDirectory, LibraryDirectory or SourceDirectory is
                        // a directory, and a folder of resources can come and go whole.
                        NotifyFilter = NotifyFilters.FileName
                                     | NotifyFilters.DirectoryName
                                     | NotifyFilters.LastWrite
                                     | NotifyFilters.Size,
                        Filter = "*.*",
                        IncludeSubdirectories = recursive,
                        EnableRaisingEvents = true
                    };

                    var capturedRoot = root;
                    watcher.Created += (s, e) => OnResourceFileSystemEvent(capturedRoot, e.FullPath, WatcherChangeTypes.Created);
                    watcher.Changed += (s, e) => OnResourceFileSystemEvent(capturedRoot, e.FullPath, WatcherChangeTypes.Changed);
                    watcher.Deleted += (s, e) => OnResourceFileSystemEvent(capturedRoot, e.FullPath, WatcherChangeTypes.Deleted);
                    watcher.Renamed += (s, e) =>
                    {
                        OnResourceFileSystemEvent(capturedRoot, e.OldFullPath, WatcherChangeTypes.Deleted);
                        OnResourceFileSystemEvent(capturedRoot, e.FullPath, WatcherChangeTypes.Created);
                    };
                    watcher.Error += (s, e) => OnWatcherError(capturedRoot, e.GetException());

                    _watchers[root] = watcher;
                    Debug("ExternalResourceService",
                        recursive
                            ? $"Watching resource tree: {root}"
                            : $"Watching resource directory: {root}");
                }
                catch (Exception ex)
                {
                    Error("ExternalResourceService", $"Failed to create watcher for {root}", ex);
                }
            }
        }

        return neededDirectories.Count;
    }

    /// <summary>
    /// Handles file system events for resource files.
    /// Uses type-aware debouncing (matching FileMonitoringService) to handle SVN revert correctly.
    /// </summary>
    internal void OnResourceFileSystemEvent(string directory, string fullPath, WatcherChangeTypes changeType)
    {
        // Skip hidden directories below the watched one
        if (FileMonitoringServiceHelpers.IsInHiddenDirectory(fullPath, directory))
            return;

        // Only track paths that are in our reverse index (referenced by models) - or, for a directory
        // created or deleted whole, the referenced paths inside it, which may raise no event of their
        // own when their folder goes or comes back with them in it.
        var normalizedPath = NormalizePath(fullPath);

        List<string> tracked;
        lock (_lock)
        {
            if (_reverseIndex.ContainsKey(normalizedPath))
                tracked = [normalizedPath];
            else if (changeType is WatcherChangeTypes.Created or WatcherChangeTypes.Deleted)
            {
                var inside = normalizedPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                tracked = _reverseIndex.Keys.Where(k => k.StartsWith(inside, StringComparison.Ordinal)).ToList();
            }
            else
                tracked = [];
        }

        var queued = 0;
        foreach (var path in tracked)
        {
            if (Queue(path, changeType))
                queued++;
        }

        if (queued == 0)
            return;

        Debug("ExternalResourceService", $"Resource {changeType}: {fullPath} ({queued} referenced path(s))");

        // Raised outside the lock: a subscriber marshals onto the UI thread, and under Photino that
        // waits for it (desktop-host skill), so raising while holding a lock invites a deadlock.
        // Until B521 this only logged, under a comment saying it fired an event, and nothing ever
        // read the queue.
        OnPendingResourceChangesUpdated?.Invoke();
    }

    /// <summary>
    /// Queues one referenced path's change, consolidated with any already waiting. False when the
    /// change was debounced away.
    /// </summary>
    private bool Queue(string normalizedPath, WatcherChangeTypes changeType)
    {
        // Type-aware debouncing: only suppress identical consecutive change types
        // Different change types (e.g., Delete then Add from SVN revert) pass through
        var now = DateTime.UtcNow;
        if (_lastChange.TryGetValue(normalizedPath, out var lastChange))
        {
            if (lastChange.Type == changeType && now - lastChange.Time < _debounceInterval)
                return false;
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

        return true;
    }

    /// <inheritdoc/>
    public event Action? OnPendingResourceChangesUpdated;

    /// <inheritdoc/>
    public int PendingResourceChangeCount
    {
        get
        {
            lock (_lock)
            {
                return _pendingResourceChanges.Count + (_requeuedModels.Count > 0 ? 1 : 0);
            }
        }
    }

    // Classes taken by a refresh that then failed, given back so the next one re-checks them.
    private readonly HashSet<string> _requeuedModels = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public IReadOnlyList<string> TakePendingResourceChanges()
    {
        lock (_lock)
        {
            var affected = _pendingResourceChanges
                .SelectMany(change => _reverseIndex.TryGetValue(change.ResolvedPath, out var models)
                    ? models
                    : Enumerable.Empty<string>())
                .Concat(_requeuedModels)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            _pendingResourceChanges.Clear();
            _requeuedModels.Clear();
            return affected;
        }
    }

    /// <inheritdoc/>
    public void RequeueResourceChanges(IEnumerable<string> modelIds)
    {
        lock (_lock)
        {
            _requeuedModels.UnionWith(modelIds);
        }
        OnPendingResourceChangesUpdated?.Invoke();
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
    /// The directories currently carrying a watcher (for testing). One per library's resource tree,
    /// not one per referenced directory — see <see cref="WatchRootFor"/>.
    /// </summary>
    internal IReadOnlyList<string> GetWatchedRoots()
    {
        lock (_lock)
        {
            return _watchers.Keys.ToList().AsReadOnly();
        }
    }

    /// <summary>Whether the watcher on <paramref name="root"/> reports below it (for testing).</summary>
    internal bool IsWatchedRecursively(string root)
    {
        lock (_lock)
        {
            return _watchers.TryGetValue(root, out var watcher) && watcher.IncludeSubdirectories;
        }
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
