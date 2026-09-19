using ModelicaGraph;
using ModelicaGraph.DataTypes;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.Services;

/// <summary>
/// Service that analyzes the impact of changes to selected models: a BFS over the "used by" edges
/// finding every transitively dependent class, with the immediate source(s) that pulled each into
/// the impact set.
///
/// <para>It computes no geometry. It used to — a circular placement, deterministic jitter and a
/// fifty-iteration force relaxation producing node coordinates, edge endpoints and an SVG canvas
/// size — and nothing had read any of it since the page moved to Cytoscape, which lays the graph out
/// itself. It survived because three tests looped over the results asserting bounds the code could
/// not violate, so it was covered, never checked, and never questioned (B222).</para>
/// </summary>
public class ImpactAnalysisService : IImpactAnalysisService
{
    /// <inheritdoc/>
    public ImpactAnalysisResult AnalyzeImpact(
        DirectedGraph graph,
        IEnumerable<string> selectedModelIds)
    {
        var result = new ImpactAnalysisResult();

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

        // Create network nodes for selected models
        foreach (var selectedModel in selectedModels)
        {
            var isAlsoImpacted = allImpactedIds.Contains(selectedModel.Id);

            result.Nodes.Add(new NetworkNode
            {
                Id = selectedModel.Id,
                ShortName = GetShortName(selectedModel.Id),
                FullName = selectedModel.Id,
                Color = isAlsoImpacted ? "var(--mud-palette-error)" : "var(--mud-palette-success)",
                BorderColor = isAlsoImpacted ? "var(--mud-palette-error-darken)" : "var(--mud-palette-success-darken)",
                IsSelected = true,
                IsImpacted = isAlsoImpacted
            });
        }

        // Create network nodes for impacted models
        foreach (var impactedId in purelyImpacted)
        {
            result.Nodes.Add(new NetworkNode
            {
                Id = impactedId,
                ShortName = GetShortName(impactedId),
                FullName = impactedId,
                Color = "var(--mud-palette-warning)",
                BorderColor = "var(--mud-palette-warning-darken)",
                IsSelected = false,
                IsImpacted = true
            });
        }

        // Create network edges. Every edge's ends must be nodes in the same result — Cytoscape
        // silently drops an edge naming an id it has no node for, so a violation would show as a
        // missing arrow rather than as an error. The BFS is believed not to produce one; this is the
        // cheap guarantee rather than the discovered defect.
        var nodeIds = result.Nodes.Select(n => n.Id).ToHashSet();
        foreach (var (from, to) in edgeSet)
        {
            if (nodeIds.Contains(from) && nodeIds.Contains(to))
            {
                result.Edges.Add(new NetworkEdge { FromId = from, ToId = to });
            }
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

    private string GetShortName(string fullName)
    {
        var parts = fullName.Split('.');
        var lastName = parts[^1];
        if (lastName.Length > 8)
            return lastName.Substring(0, 7) + "..";
        return lastName;
    }
}
