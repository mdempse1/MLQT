using System.Collections.Concurrent;
using ModelicaGraph.Interfaces;
using ModelicaGraph.DataTypes;

namespace ModelicaGraph;

/// <summary>
/// Represents a directed graph of files and Modelica models.
/// </summary>
public class DirectedGraph
{
    private readonly ConcurrentDictionary<string, IGraphNode> _nodes;
    private readonly ConcurrentDictionary<string, HashSet<string>> _edges;
    private readonly ConcurrentDictionary<string, ResourceEdge> _resourceEdges;
    private Lock _lock = new();

    public DirectedGraph()
    {
        _nodes = new ConcurrentDictionary<string, IGraphNode>();
        _edges = new ConcurrentDictionary<string, HashSet<string>>();
        _resourceEdges = new ConcurrentDictionary<string, ResourceEdge>();
    }

    /// <summary>
    /// Gets all file nodes.
    /// </summary>
    public IEnumerable<FileNode> FileNodes => _nodes.Values.OfType<FileNode>();

    /// <summary>
    /// Gets all model nodes.
    /// </summary>
    public IEnumerable<ModelNode> ModelNodes => _nodes.Values.OfType<ModelNode>();

    /// <summary>
    /// Gets all resource file nodes.
    /// </summary>
    public IEnumerable<ResourceFileNode> ResourceFileNodes => _nodes.Values.OfType<ResourceFileNode>();

    /// <summary>
    /// Gets all resource directory nodes.
    /// </summary>
    public IEnumerable<ResourceDirectoryNode> ResourceDirectoryNodes => _nodes.Values.OfType<ResourceDirectoryNode>();

    /// <summary>
    /// Gets all resource edges.
    /// </summary>
    public IEnumerable<ResourceEdge> ResourceEdges => _resourceEdges.Values;

    /// <summary>
    /// Gets the total number of nodes in the graph.
    /// </summary>
    public int NodeCount => _nodes.Count;

    /// <summary>
    /// True once <see cref="GraphBuilder.AnalyzeDependenciesAsync"/> has completed over this graph,
    /// so <c>UsedModelIds</c>/<c>UsedByModelIds</c> are populated.
    ///
    /// This is the single source of truth for that question. Consumers must never infer it by
    /// sniffing whether any model happens to have edges — during a run in progress that sniff is
    /// true for a partly-built graph, which silently changes how many findings the dependency-based
    /// analyzers (unused class, uses hygiene) produce.
    /// </summary>
    public bool DependenciesAnalyzed { get; private set; }

    /// <summary>Records that full dependency analysis has completed over this graph.</summary>
    public void MarkDependenciesAnalyzed() => DependenciesAnalyzed = true;

    /// <summary>
    /// Records that the graph has gained content that has never been through dependency analysis
    /// (a newly loaded library), so the next consumer that needs the edges re-runs it. Incremental
    /// re-analysis of already-loaded models (<see cref="GraphBuilder.AnalyzeDependenciesForModelsAsync"/>)
    /// maintains the edges itself and must not call this.
    /// </summary>
    public void InvalidateDependencyAnalysis() => DependenciesAnalyzed = false;

    /// <summary>
    /// Adds a node to the graph.
    /// </summary>
    public void AddNode(IGraphNode node)
    {
        lock (_lock) {
            if (!_nodes.ContainsKey(node.Id))
            {
                _nodes[node.Id] = node;
                _edges[node.Id] = new HashSet<string>();
            }
            else if (node is ModelNode newModel && _nodes[node.Id] is ModelNode existingModel)
            {
                // Readable source always beats a class reconstructed from vendor documentation, in
                // whichever order the two arrive. The same library really does turn up twice: a tool's
                // library folder ships the encrypted build of a library the user also has checked out
                // as source, and both land in the one graph.
                //
                // This has to be decided before the standalone rule below, because that rule cannot
                // see the difference. A stub is never standalone, so a stub colliding with a nested
                // `redeclare` class — which is not standalone either — matched none of its cases and
                // left the stub in place. The class then had no source to check, so every rule went
                // quiet on it while the standalone classes beside it were checked normally.
                if (existingModel.IsExternalStub != newModel.IsExternalStub)
                {
                    if (existingModel.IsExternalStub)
                    {
                        DetachFromFile(existingModel);
                        _nodes[node.Id] = node;
                    }

                    // Otherwise the existing node is the real source: keep it.
                }
                // When a standalone model collides with a non-standalone (prefixed) model,
                // prefer the standalone version — it has the full class definition and can
                // be saved as a separate file. The non-standalone version (e.g., redeclare
                // function extends X) is just a modification embedded in the parent.
                else if (!existingModel.CanBeStoredStandalone && newModel.CanBeStoredStandalone)
                {
                    DetachFromFile(existingModel);
                    _nodes[node.Id] = node;
                }
                else if (existingModel.CanBeStoredStandalone && !newModel.CanBeStoredStandalone
                         && string.IsNullOrEmpty(existingModel.ElementPrefix))
                {
                    // Keep the standalone version but copy the element prefix for display
                    existingModel.ElementPrefix = newModel.ElementPrefix;
                }
            }
        }
    }

    /// <summary>
    /// Removes a node from the graph.
    /// </summary>
    public bool RemoveNode(string nodeId)
    {
        if (!_nodes.ContainsKey(nodeId))
            return false;

        lock (_lock) {
            // Remove all edges to this node
            foreach (var edges in _edges.Values)
            {
                edges.Remove(nodeId);
            }

            // Remove the node's edges
            _edges.Remove(nodeId, out _);

            // Remove the node
            return _nodes.Remove(nodeId, out _);
        }
    }

    /// <summary>
    /// Gets a node by its ID.
    /// </summary>
    public IGraphNode? GetNode(string nodeId)
    {
        return _nodes.TryGetValue(nodeId, out var node) ? node : null;
    }

    /// <summary>
    /// Gets a node by its ID with a specific type.
    /// </summary>
    public T? GetNode<T>(string nodeId) where T : IGraphNode
    {
        return _nodes.TryGetValue(nodeId, out var node) && node is T typedNode ? typedNode : default;
    }

    /// <summary>
    /// Checks if a node exists in the graph.
    /// </summary>
    public bool HasNode(string nodeId)
    {
        return _nodes.ContainsKey(nodeId);
    }

    /// <summary>
    /// Adds a directed edge from one node to another.
    /// </summary>
    public void AddEdge(string fromNodeId, string toNodeId)
    {
        lock (_lock) {
            if (!_nodes.ContainsKey(fromNodeId) || !_nodes.ContainsKey(toNodeId))
                throw new ArgumentException("Both nodes must exist in the graph before adding an edge.");

            _edges[fromNodeId].Add(toNodeId);
        }
    }

    /// <summary>
    /// Removes a directed edge from one node to another.
    /// </summary>
    public bool RemoveEdge(string fromNodeId, string toNodeId)
    {
        if (!_edges.ContainsKey(fromNodeId))
            return false;
        lock (_lock) {
            return _edges[fromNodeId].Remove(toNodeId);
        }
    }

    /// <summary>
    /// Gets all nodes that are directly connected from the specified node.
    /// </summary>
    public IEnumerable<IGraphNode> GetOutgoingNodes(string nodeId)
    {
        if (!_edges.ContainsKey(nodeId))
            return Enumerable.Empty<IGraphNode>();

        return _edges[nodeId].Select(id => _nodes[id]);
    }

    /// <summary>
    /// Gets all nodes that have a direct connection to the specified node.
    /// </summary>
    public IEnumerable<IGraphNode> GetIncomingNodes(string nodeId)
    {
        return _edges
            .Where(kvp => kvp.Value.Contains(nodeId))
            .Select(kvp => _nodes[kvp.Key]);
    }

    /// <summary>
    /// Creates a relationship where a file contains a model.
    /// </summary>
    public void AddFileContainsModel(string fileNodeId, string modelNodeId)
    {
        // Under the graph lock, all of it. Libraries load in parallel, and a class that moves between
        // files now touches the set belonging to the file it is leaving — a set another thread may be
        // adding to for a file of its own. Two threads inside one HashSet do not merely race for an
        // outcome: they corrupt it, and a corrupted set can spin forever on the next lookup, which
        // presents as a load that never finishes rather than as an error.
        lock (_lock)
        {
            AddFileContainsModelLocked(fileNodeId, modelNodeId);
        }
    }

    /// <summary>Caller must hold the lock.</summary>
    private void AddFileContainsModelLocked(string fileNodeId, string modelNodeId)
    {
        var fileNode = GetNode<FileNode>(fileNodeId);
        var modelNode = GetNode<ModelNode>(modelNodeId);

        if (fileNode == null || modelNode == null)
            throw new ArgumentException("Both file and model nodes must exist.");

        // A class whose source is loaded keeps the file its source is in. The only thing that ever
        // asks otherwise is a library recovered from a vendor's documentation being loaded over a
        // checked-out copy of the same library, and letting it win pointed the real class at an
        // encrypted package — which then got read, parsed and, in the worst case, written.
        if (!modelNode.IsExternalStub && IsExternalStubFile(fileNode))
            return;

        // A class lives in one file. Leaving it listed in the file it came from made it a member of
        // two: after a class recovered from an encrypted package was replaced by its real source, the
        // package still claimed it, and anything asking what a file contains — the CLI's model-to-file
        // map, which decides the path a finding is reported against — could name the encrypted
        // package for a class whose source is checked out. The same detach keeps the graph honest
        // when formatting restructures a library and classes genuinely move between files.
        if (!string.IsNullOrEmpty(modelNode.ContainingFileId) && modelNode.ContainingFileId != fileNodeId)
        {
            GetNode<FileNode>(modelNode.ContainingFileId)?.ContainedModelIds.Remove(modelNodeId);
            RemoveEdge(modelNode.ContainingFileId, modelNodeId);
        }

        fileNode.AddContainedModel(modelNodeId);
        modelNode.ContainingFileId = fileNodeId;
        AddEdge(fileNodeId, modelNodeId);
    }

    /// <summary>
    /// Lets go of a model node's file before that node is replaced by another. The replacement is a
    /// different object, so it does not carry the old one's file link — and the file went on listing
    /// a class it no longer holds. An encrypted package kept claiming classes whose real source had
    /// since been loaded, and asking that file what it contained handed back the real ones.
    /// </summary>
    private void DetachFromFile(ModelNode model)
    {
        if (string.IsNullOrEmpty(model.ContainingFileId))
            return;

        GetNode<FileNode>(model.ContainingFileId)?.ContainedModelIds.Remove(model.Id);
        RemoveEdge(model.ContainingFileId, model.Id);
    }

    /// <summary>
    /// Whether a file is an encrypted package, which holds no readable source at all — only classes
    /// MLQT rebuilt from the vendor's documentation. Decided by the extension rather than by what the
    /// file currently contains, so the answer does not depend on how much of it has been registered
    /// yet.
    /// </summary>
    private static bool IsExternalStubFile(FileNode fileNode) =>
        ExternalStubBuilder.IsEncryptedPackageFile(fileNode.FilePath);

    /// <summary>
    /// Creates a relationship where one model uses another model.
    /// </summary>
    public void AddModelUsesModel(string usingModelId, string usedModelId)
    {
        var usingModel = GetNode<ModelNode>(usingModelId);
        var usedModel = GetNode<ModelNode>(usedModelId);

        if (usingModel == null || usedModel == null)
            throw new ArgumentException("Both model nodes must exist.");

        usingModel.AddUsedModel(usedModelId);
        usedModel.AddUsedByModel(usingModelId);
        AddEdge(usingModelId, usedModelId);
    }

    /// <summary>
    /// Gets all models contained in a file.
    /// </summary>
    public IEnumerable<ModelNode> GetModelsInFile(string fileNodeId)
    {
        var fileNode = GetNode<FileNode>(fileNodeId);
        if (fileNode == null)
            return Enumerable.Empty<ModelNode>();

        // Copied under the lock rather than returned as a query over the live set: the caller decides
        // when to enumerate, and a class moving between files mutates that set from a loading thread.
        // An enumeration that outlives the lock is an enumeration of something that can change under
        // it.
        string[] ids;
        lock (_lock)
        {
            ids = fileNode.ContainedModelIds.ToArray();
        }

        return ids
            .Select(id => GetNode<ModelNode>(id))
            .Where(node => node != null)
            .Cast<ModelNode>();
    }

    /// <summary>
    /// Gets all models that a given model uses/depends on.
    /// </summary>
    public IEnumerable<ModelNode> GetUsedModels(string modelNodeId)
    {
        var modelNode = GetNode<ModelNode>(modelNodeId);
        if (modelNode == null)
            return Enumerable.Empty<ModelNode>();

        return modelNode.UsedModelIds
            .Select(id => GetNode<ModelNode>(id))
            .Where(node => node != null)
            .Cast<ModelNode>();
    }

    /// <summary>
    /// Gets all models that use/depend on a given model.
    /// </summary>
    public IEnumerable<ModelNode> GetModelUsedBy(string modelNodeId)
    {
        var modelNode = GetNode<ModelNode>(modelNodeId);
        if (modelNode == null)
            return Enumerable.Empty<ModelNode>();

        return modelNode.UsedByModelIds
            .Select(id => GetNode<ModelNode>(id))
            .Where(node => node != null)
            .Cast<ModelNode>();
    }

    #region Resource Node Management

    /// <summary>
    /// Generates a unique ID for a resource file node based on normalized path.
    /// </summary>
    public static string GenerateResourceFileId(string resolvedPath)
    {
        var normalizedPath = NormalizePath(resolvedPath);
        return $"resource:file:{normalizedPath}";
    }

    /// <summary>
    /// Generates a unique ID for a resource directory node based on normalized path.
    /// </summary>
    public static string GenerateResourceDirectoryId(string resolvedPath)
    {
        var normalizedPath = NormalizePath(resolvedPath);
        return $"resource:dir:{normalizedPath}";
    }

    /// <summary>
    /// Normalizes a file system path for consistent ID generation.
    /// </summary>
    private static string NormalizePath(string path)
    {
        // Normalize to lowercase on Windows for case-insensitive comparison
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        if (OperatingSystem.IsWindows())
            normalized = normalized.ToLowerInvariant();
        return normalized.TrimEnd(Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Gets or creates a resource file node for the given resolved path.
    /// Returns existing node if one already exists for the path.
    /// </summary>
    public ResourceFileNode GetOrCreateResourceFileNode(string resolvedPath)
    {
        var id = GenerateResourceFileId(resolvedPath);

        lock (_lock)
        {
            if (_nodes.TryGetValue(id, out var existing) && existing is ResourceFileNode existingNode)
                return existingNode;

            var node = new ResourceFileNode(id, resolvedPath);
            node.FileExists = File.Exists(resolvedPath);

            // Detect if this is an image file
            var extension = Path.GetExtension(resolvedPath).ToLowerInvariant();
            node.IsImageFile = extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".bmp" or ".ico";

            _nodes[id] = node;
            _edges[id] = new HashSet<string>();
            return node;
        }
    }

    /// <summary>
    /// Gets or creates a resource directory node for the given resolved path.
    /// Returns existing node if one already exists for the path.
    /// </summary>
    public ResourceDirectoryNode GetOrCreateResourceDirectoryNode(string resolvedPath)
    {
        var id = GenerateResourceDirectoryId(resolvedPath);

        lock (_lock)
        {
            if (_nodes.TryGetValue(id, out var existing) && existing is ResourceDirectoryNode existingNode)
                return existingNode;

            var node = new ResourceDirectoryNode(id, resolvedPath);
            node.DirectoryExists = Directory.Exists(resolvedPath);

            _nodes[id] = node;
            _edges[id] = new HashSet<string>();
            return node;
        }
    }

    /// <summary>
    /// Adds an edge from a model to a resource with associated metadata.
    /// </summary>
    public void AddModelReferencesResource(string modelId, string resourceNodeId, ResourceEdge edgeData)
    {
        var modelNode = GetNode<ModelNode>(modelId);
        if (modelNode == null)
            throw new ArgumentException($"Model node '{modelId}' not found.");

        var resourceNode = GetNode(resourceNodeId);
        if (resourceNode == null)
            throw new ArgumentException($"Resource node '{resourceNodeId}' not found.");

        lock (_lock)
        {
            // Set edge IDs
            edgeData.ModelId = modelId;
            edgeData.ResourceNodeId = resourceNodeId;

            // Store edge metadata
            var edgeKey = edgeData.GetEdgeKey();
            _resourceEdges[edgeKey] = edgeData;

            // Update model's resource references
            modelNode.AddReferencedResource(resourceNodeId);

            // Update resource's back-references
            if (resourceNode is ResourceFileNode fileNode)
                fileNode.AddReferencingModel(modelId);
            else if (resourceNode is ResourceDirectoryNode dirNode)
                dirNode.AddReferencingModel(modelId);

            // Add graph edge
            _edges[modelId].Add(resourceNodeId);
        }
    }

    /// <summary>
    /// Removes all model-to-model dependency edges for a given model.
    /// Clears its UsedModelIds list and removes the reverse UsedByModelIds entries
    /// from every model it referenced. Call before re-analyzing a model's dependencies.
    /// </summary>
    public void RemoveModelDependencyEdges(string modelId)
    {
        var modelNode = GetNode<ModelNode>(modelId);
        if (modelNode == null)
            return;

        lock (_lock)
        {
            foreach (var usedId in modelNode.UsedModelIds.ToList())
            {
                var usedModel = GetNode<ModelNode>(usedId);
                usedModel?.RemoveUsedByModel(modelId);
                if (_edges.TryGetValue(modelId, out var forwardEdges))
                    forwardEdges.Remove(usedId);
            }
            modelNode.UsedModelIds.Clear();
        }
    }

    /// <summary>
    /// Reconciles model-to-model dependency edge state after an incremental file reload.
    /// When models are removed and re-added with the same IDs, RemoveNode cleans up the
    /// forward _edges entries but leaves UsedModelIds on other nodes intact.
    /// This method:
    ///   - Removes stale UsedModelIds entries pointing to nodes that no longer exist.
    ///   - Restores missing forward _edges and reverse UsedByModelIds for entries that
    ///     point to nodes that DO exist (i.e., the model was removed then re-added).
    /// This is O(total dependency edges) and requires no AST re-parsing.
    /// </summary>
    public void ReconcileDependencyEdges()
    {
        var models = ModelNodes.ToList(); // snapshot without lock

        lock (_lock)
        {
            foreach (var model in models)
            {
                if (!_edges.TryGetValue(model.Id, out var forwardEdges))
                    continue;

                // Remove stale references to nodes that were deleted and not re-added
                var staleIds = model.UsedModelIds
                    .Where(id => !_nodes.ContainsKey(id))
                    .ToList();
                foreach (var staleId in staleIds)
                    model.UsedModelIds.Remove(staleId);

                // Restore forward _edges and reverse UsedByModelIds for valid references
                // that were disrupted by RemoveNode during the reload cycle
                foreach (var usedId in model.UsedModelIds)
                {
                    forwardEdges.Add(usedId);

                    if (_nodes.TryGetValue(usedId, out var usedNode) && usedNode is ModelNode usedModel)
                        usedModel.AddUsedByModel(model.Id);
                }
            }
        }
    }

    /// <summary>
    /// Removes all resource edges for a given model.
    /// Used when a model is reloaded and needs fresh resource analysis.
    /// </summary>
    public void RemoveModelResourceEdges(string modelId)
    {
        var modelNode = GetNode<ModelNode>(modelId);
        if (modelNode == null)
            return;

        lock (_lock)
        {
            // Get all resource IDs referenced by this model
            var resourceIds = modelNode.ReferencedResourceIds.ToList();

            foreach (var resourceId in resourceIds)
            {
                // Remove back-reference from resource node
                var resourceNode = GetNode(resourceId);
                if (resourceNode is ResourceFileNode fileNode)
                    fileNode.RemoveReferencingModel(modelId);
                else if (resourceNode is ResourceDirectoryNode dirNode)
                    dirNode.RemoveReferencingModel(modelId);

                // Remove graph edge
                _edges[modelId].Remove(resourceId);

                // Remove edge metadata
                var edgeKeysToRemove = _resourceEdges.Keys
                    .Where(k => k.StartsWith($"{modelId}|{resourceId}|"))
                    .ToList();
                foreach (var key in edgeKeysToRemove)
                    _resourceEdges.Remove(key, out _);
            }

            // Clear model's resource references
            modelNode.ReferencedResourceIds.Clear();
        }
    }

    /// <summary>
    /// Gets all resource edges for a given model.
    /// </summary>
    public IEnumerable<ResourceEdge> GetResourceEdgesForModel(string modelId)
    {
        return _resourceEdges.Values.Where(e => e.ModelId == modelId);
    }

    /// <summary>
    /// Gets all resource edges pointing to a given resource node.
    /// </summary>
    public IEnumerable<ResourceEdge> GetModelEdgesToResource(string resourceNodeId)
    {
        return _resourceEdges.Values.Where(e => e.ResourceNodeId == resourceNodeId);
    }

    /// <summary>
    /// Gets all resources (files and directories) referenced by a model.
    /// </summary>
    public IEnumerable<IGraphNode> GetResourcesForModel(string modelId)
    {
        var modelNode = GetNode<ModelNode>(modelId);
        if (modelNode == null)
            return Enumerable.Empty<IGraphNode>();

        return modelNode.ReferencedResourceIds
            .Select(id => GetNode(id))
            .Where(node => node != null)
            .Cast<IGraphNode>();
    }

    /// <summary>
    /// Removes resource nodes that are no longer referenced by any model.
    /// Call after removing model resource edges to clean up orphaned nodes.
    /// </summary>
    public void CleanupOrphanedResourceNodes()
    {
        lock (_lock)
        {
            var orphanedIds = new List<string>();

            foreach (var node in _nodes.Values)
            {
                if (node is ResourceFileNode fileNode && fileNode.ReferencedByModelIds.Count == 0)
                    orphanedIds.Add(fileNode.Id);
                else if (node is ResourceDirectoryNode dirNode && dirNode.ReferencedByModelIds.Count == 0)
                    orphanedIds.Add(dirNode.Id);
            }

            foreach (var id in orphanedIds)
            {
                _nodes.Remove(id, out _);
                _edges.Remove(id, out _);
            }
        }
    }

    #endregion

    /// <summary>
    /// Clears all nodes and edges from the graph.
    /// </summary>
    public void Clear()
    {
        lock (_lock) {
            _nodes.Clear();
            _edges.Clear();
            _resourceEdges.Clear();
        }
        InvalidateDependencyAnalysis();
    }
}
