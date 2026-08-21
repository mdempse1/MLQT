using ModelicaGraph;
using ModelicaGraph.DataTypes;
using MLQT.McpServer.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// After a file edit + reload, incrementally rebuilds dependency edges (and the resource index) for the
/// affected models — but only for the analyses that have already been run this session — so
/// get_dependencies / find_usages / analyze_impact and the resource tools stay fresh after an edit
/// without paying for a full re-analysis. No-op if nothing has been analyzed yet.
/// </summary>
internal static class GraphRefresh
{
    public static async Task RefreshAfterEditAsync(
        IReadOnlyCollection<string> affectedModelIds,
        ILibraryDataService libraries,
        IExternalResourceService resources,
        SessionState session)
    {
        if (affectedModelIds.Count == 0)
            return;

        var graph = libraries.CombinedGraph;

        if (session.DependenciesAnalyzed)
        {
            var idSet = affectedModelIds.ToHashSet(StringComparer.Ordinal);
            await GraphBuilder.AnalyzeDependenciesForModelsAsync(graph, idSet, BuildLibraryInfos(libraries));
            graph.ReconcileDependencyEdges();
        }

        if (session.ResourcesAnalyzed)
            await resources.AnalyzeResourcesForModelsAsync(affectedModelIds, graph);
    }

    public static List<LibraryInfo> BuildLibraryInfos(ILibraryDataService libraries) =>
        libraries.GetLibraryInfos();
}
