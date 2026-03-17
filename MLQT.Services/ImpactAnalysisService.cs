using ModelicaGraph;
using ModelicaGraph.DataTypes;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.Services;

/// <summary>
/// Service that analyzes the impact of changes to selected models.
/// Uses BFS traversal to find all transitively dependent models and
/// calculates layout positions for network visualization.
/// </summary>
public class ImpactAnalysisService : IImpactAnalysisService
{
    /// <inheritdoc/>
    public ImpactAnalysisResult AnalyzeImpact(
        DirectedGraph graph,
        IEnumerable<string> selectedModelIds,
        int svgWidth = 700,
        int svgHeight = 450)
    {
        var result = new ImpactAnalysisResult
        {
            SvgWidth = svgWidth,
            SvgHeight = svgHeight
        };

        var selectedIds = new HashSet<string>(selectedModelIds);
        if (selectedIds.Count == 0)
            return result;

        // Get selected model nodes
        var selectedModels = new List<ModelNode>();
        foreach (var modelId in selectedIds)
        {
            var node = graph.GetNode<ModelNode>(modelId);
            if (node != null)
            {
                selectedModels.Add(node);
            }
        }

        if (selectedModels.Count == 0)
            return result;

        // Build the dependency network using BFS
        var (allImpactedIds, edgeSet, impactSources) = BuildDependencyNetwork(graph, selectedModels);

        var purelyImpacted = allImpactedIds.Except(selectedIds).ToHashSet();
        result.ImpactedModelsCount = purelyImpacted.Count;

        // Build impact details
        foreach (var impactedId in purelyImpacted)
        {
            var model = graph.GetNode<ModelNode>(impactedId);
            if (model != null)
            {
                var classType = model.ClassType;
                var sources = impactSources.TryGetValue(impactedId, out var src) ? src.ToList() : new List<string>();

                result.ImpactDetails.Add(new ImpactDetail
                {
                    ModelId = impactedId,
                    ClassType = classType,
                    ImpactedBy = sources
                });
            }
        }

        // Calculate positions using force-directed-like layout
        var nodePositions = CalculateForceDirectedPositions(
            graph, selectedIds, purelyImpacted, edgeSet, svgWidth, svgHeight);

        // Create network nodes for selected models
        foreach (var selectedModel in selectedModels)
        {
            if (nodePositions.TryGetValue(selectedModel.Id, out var pos))
            {
                var isAlsoImpacted = allImpactedIds.Contains(selectedModel.Id);

                result.Nodes.Add(new NetworkNode
                {
                    Id = selectedModel.Id,
                    ShortName = GetShortName(selectedModel.Id),
                    FullName = selectedModel.Id,
                    X = pos.x,
                    Y = pos.y,
                    Color = isAlsoImpacted ? "var(--mud-palette-error)" : "var(--mud-palette-success)",
                    BorderColor = isAlsoImpacted ? "var(--mud-palette-error-darken)" : "var(--mud-palette-success-darken)",
                    IsSelected = true,
                    IsImpacted = isAlsoImpacted
                });
            }
        }

        // Create network nodes for impacted models
        foreach (var impactedId in purelyImpacted)
        {
            if (nodePositions.TryGetValue(impactedId, out var pos))
            {
                result.Nodes.Add(new NetworkNode
                {
                    Id = impactedId,
                    ShortName = GetShortName(impactedId),
                    FullName = impactedId,
                    X = pos.x,
                    Y = pos.y,
                    Color = "var(--mud-palette-warning)",
                    BorderColor = "var(--mud-palette-warning-darken)",
                    IsSelected = false,
                    IsImpacted = true
                });
            }
        }

        // Create network edges
        foreach (var (from, to) in edgeSet)
        {
            var fromNode = result.Nodes.FirstOrDefault(n => n.Id == from);
            var toNode = result.Nodes.FirstOrDefault(n => n.Id == to);

            if (fromNode != null && toNode != null)
            {
                var (x1, y1, x2, y2) = CalculateEdgeEndpoints(fromNode, toNode);

                result.Edges.Add(new NetworkEdge
                {
                    FromId = from,
                    ToId = to,
                    X1 = x1,
                    Y1 = y1,
                    X2 = x2,
                    Y2 = y2
                });
            }
        }

        // Adjust SVG dimensions based on node positions
        if (result.Nodes.Count > 0)
        {
            result.SvgWidth = Math.Max(700, result.Nodes.Max(n => n.X) + 60);
            result.SvgHeight = Math.Max(450, result.Nodes.Max(n => n.Y) + 60);
        }

        return result;
    }

    /// <inheritdoc/>
    public HashSet<string> GetConnectedNodes(IEnumerable<NetworkEdge> edges, string nodeId)
    {
        var connected = new HashSet<string>();

        foreach (var edge in edges)
        {
            if (edge.FromId == nodeId)
            {
                connected.Add(edge.ToId);
            }
            else if (edge.ToId == nodeId)
            {
                connected.Add(edge.FromId);
            }
        }

        return connected;
    }

    // Plain packages are containers for models but don't participate in simulation.
    // Exclude them from impact results. (Prefixed packages like replaceable/redeclare
    // are not extracted as separate models, so all packages in the graph are plain.)
    private static bool IsPlainPackage(ModelNode node) =>
        node.ClassType == "package";

    private (HashSet<string> allImpactedIds, HashSet<(string from, string to)> edges, Dictionary<string, HashSet<string>> impactSources)
        BuildDependencyNetwork(DirectedGraph graph, List<ModelNode> selectedModels)
    {
        var allImpactedIds = new HashSet<string>();
        var edgeSet = new HashSet<(string from, string to)>();
        var impactSources = new Dictionary<string, HashSet<string>>();

        foreach (var selectedModel in selectedModels)
        {
            var visited = new HashSet<string> { selectedModel.Id };
            var queue = new Queue<string>();

            var directDependents = graph.GetModelUsedBy(selectedModel.Id);
            foreach (var dep in directDependents)
            {
                if (IsPlainPackage(dep)) continue;

                queue.Enqueue(dep.Id);
                visited.Add(dep.Id);
                edgeSet.Add((selectedModel.Id, dep.Id));

                if (!impactSources.ContainsKey(dep.Id))
                    impactSources[dep.Id] = new HashSet<string>();
                impactSources[dep.Id].Add(selectedModel.Id);
            }

            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();
                allImpactedIds.Add(currentId);

                var dependents = graph.GetModelUsedBy(currentId);
                foreach (var dep in dependents)
                {
                    if (IsPlainPackage(dep)) continue;

                    edgeSet.Add((currentId, dep.Id));

                    if (!impactSources.ContainsKey(dep.Id))
                        impactSources[dep.Id] = new HashSet<string>();
                    impactSources[dep.Id].Add(currentId);

                    if (!visited.Contains(dep.Id))
                    {
                        visited.Add(dep.Id);
                        queue.Enqueue(dep.Id);
                    }
                }
            }
        }

        return (allImpactedIds, edgeSet, impactSources);
    }

    private Dictionary<string, (int x, int y)> CalculateForceDirectedPositions(
        DirectedGraph graph,
        HashSet<string> selectedIds,
        HashSet<string> impactedIds,
        HashSet<(string from, string to)> edges,
        int svgWidth,
        int svgHeight)
    {
        var positions = new Dictionary<string, (int x, int y)>();
        var allIds = selectedIds.Union(impactedIds).ToList();

        if (allIds.Count == 0) return positions;

        int centerX = svgWidth / 2;
        int centerY = svgHeight / 2;

        // Place selected nodes in the center area
        var selectedList = selectedIds.ToList();
        if (selectedList.Count == 1)
        {
            positions[selectedList[0]] = (centerX, centerY);
        }
        else
        {
            // Arrange selected nodes in a small circle at center
            double selectedRadius = Math.Min(60, 30 * selectedList.Count);
            for (int i = 0; i < selectedList.Count; i++)
            {
                double angle = (2 * Math.PI * i / selectedList.Count) - Math.PI / 2;
                int x = (int)(centerX + selectedRadius * Math.Cos(angle));
                int y = (int)(centerY + selectedRadius * Math.Sin(angle));
                positions[selectedList[i]] = (x, y);
            }
        }

        // Calculate depths for impacted nodes (distance from selected nodes)
        var depths = new Dictionary<string, int>();
        var visited = new HashSet<string>(selectedIds);
        var queue = new Queue<(string id, int depth)>();

        foreach (var selectedId in selectedIds)
        {
            depths[selectedId] = 0;
            var dependents = graph.GetModelUsedBy(selectedId);
            foreach (var dep in dependents)
            {
                if (!visited.Contains(dep.Id))
                {
                    visited.Add(dep.Id);
                    depths[dep.Id] = 1;
                    queue.Enqueue((dep.Id, 1));
                }
            }
        }

        while (queue.Count > 0)
        {
            var (currentId, currentDepth) = queue.Dequeue();
            var dependents = graph.GetModelUsedBy(currentId);
            foreach (var dep in dependents)
            {
                if (!visited.Contains(dep.Id))
                {
                    visited.Add(dep.Id);
                    depths[dep.Id] = currentDepth + 1;
                    queue.Enqueue((dep.Id, currentDepth + 1));
                }
            }
        }

        // Group impacted nodes by depth
        var nodesByDepth = impactedIds
            .Where(id => depths.ContainsKey(id))
            .GroupBy(id => depths[id])
            .OrderBy(g => g.Key)
            .ToList();

        // Place impacted nodes in expanding rings around center
        int baseRadius = 100;
        int radiusIncrement = 70;

        foreach (var depthGroup in nodesByDepth)
        {
            var nodesAtDepth = depthGroup.ToList();
            int depth = depthGroup.Key;
            double radius = baseRadius + (depth - 1) * radiusIncrement;

            // Add some randomness to prevent perfect circles
            var random = new Random(depth * 42); // Deterministic seed

            for (int i = 0; i < nodesAtDepth.Count; i++)
            {
                // Spread nodes around the ring with some jitter
                double baseAngle = (2 * Math.PI * i / nodesAtDepth.Count);
                double jitter = (random.NextDouble() - 0.5) * 0.3;
                double angle = baseAngle + jitter;

                // Add some radius variation
                double radiusJitter = radius + (random.NextDouble() - 0.5) * 30;

                int x = (int)(centerX + radiusJitter * Math.Cos(angle));
                int y = (int)(centerY + radiusJitter * Math.Sin(angle));

                // Keep within bounds
                x = Math.Max(40, Math.Min(svgWidth - 40, x));
                y = Math.Max(40, Math.Min(svgHeight - 40, y));

                positions[nodesAtDepth[i]] = (x, y);
            }
        }

        // Simple force-directed adjustment to reduce overlaps
        for (int iteration = 0; iteration < 50; iteration++)
        {
            var adjustments = new Dictionary<string, (double dx, double dy)>();

            foreach (var id in allIds)
            {
                adjustments[id] = (0, 0);
            }

            // Repulsion between all nodes
            foreach (var id1 in allIds)
            {
                if (!positions.ContainsKey(id1)) continue;
                var pos1 = positions[id1];

                foreach (var id2 in allIds)
                {
                    if (id1 == id2 || !positions.ContainsKey(id2)) continue;
                    var pos2 = positions[id2];

                    double dx = pos1.x - pos2.x;
                    double dy = pos1.y - pos2.y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    if (dist < 60 && dist > 0)
                    {
                        double force = (60 - dist) / dist * 0.5;
                        var adj = adjustments[id1];
                        adjustments[id1] = (adj.dx + dx * force, adj.dy + dy * force);
                    }
                }
            }

            // Apply adjustments (but keep selected nodes more stable)
            foreach (var id in allIds)
            {
                if (!positions.ContainsKey(id)) continue;
                var pos = positions[id];
                var adj = adjustments[id];

                double factor = selectedIds.Contains(id) ? 0.1 : 0.3;
                int newX = (int)(pos.x + adj.dx * factor);
                int newY = (int)(pos.y + adj.dy * factor);

                // Keep within bounds
                newX = Math.Max(40, Math.Min(svgWidth - 40, newX));
                newY = Math.Max(40, Math.Min(svgHeight - 40, newY));

                positions[id] = (newX, newY);
            }
        }

        return positions;
    }

    private (int x1, int y1, int x2, int y2) CalculateEdgeEndpoints(NetworkNode from, NetworkNode to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length == 0) return (from.X, from.Y, to.X, to.Y);

        dx /= length;
        dy /= length;

        int nodeRadius = 18;
        int x1 = (int)(from.X + dx * (nodeRadius + 2));
        int y1 = (int)(from.Y + dy * (nodeRadius + 2));
        int x2 = (int)(to.X - dx * (nodeRadius + 6));
        int y2 = (int)(to.Y - dy * (nodeRadius + 6));

        return (x1, y1, x2, y2);
    }

    private string GetShortName(string fullName)
    {
        var parts = fullName.Split('.');
        var lastName = parts[^1];
        if (lastName.Length > 8)
            return lastName.Substring(0, 7) + "..";
        return lastName;
    }
}
