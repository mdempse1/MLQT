using System.Reflection;
using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Dependency, usage and impact analysis over the loaded model graph. Dependency edges are NOT
/// built at load time: the analysis runs once, on the first call that needs it (it can be slow on a
/// large library). <c>mlqt_dependencies</c> runs it itself before calling these.
/// </summary>
[Obfuscation]
public sealed class DependencyTools
{
    private const int MaxImpactLimit = 2000;

    private readonly ILibraryDataService _libraries;
    private readonly IImpactAnalysisService _impact;
    private readonly IExternalResourceService _resources;
    private readonly SessionState _session;

    public DependencyTools(
        ILibraryDataService libraries,
        IImpactAnalysisService impact,
        IExternalResourceService resources,
        SessionState session)
    {
        _libraries = libraries;
        _impact = impact;
        _resources = resources;
        _session = session;
    }

    internal async Task<object> AnalyzeDependencies()
    {
        if (_libraries.Libraries.Count == 0)
            return new ToolError(ToolDiagnostics.NothingLoaded("analysing dependencies"));

        var graph = _libraries.CombinedGraph;
        // The service's list, not a copy of it: the copy this tool kept never marked an encrypted
        // library as one, so the MCP server analysed with a different view of it than the app (B369).
        var libraryInfos = _libraries.GetLibraryInfos();

        var sw = Stopwatch.StartNew();
        await GraphBuilder.AnalyzeDependenciesAsync(graph, libraryInfos);
        await _resources.AnalyzeResourcesAsync(graph);
        sw.Stop();

        _session.DependenciesAnalyzed = true;
        _session.ResourcesAnalyzed = true;

        var dependencyEdges = graph.ModelNodes.Sum(n => n.UsedModelIds.Count);
        return new AnalyzeDependenciesResult(
            Models: graph.ModelNodes.Count(),
            DependencyEdges: dependencyEdges,
            Resources: _resources.GetAllResources().Count,
            ResourceWarnings: _resources.GetWarnings().Count,
            ElapsedMs: sw.ElapsedMilliseconds);
    }

    internal object GetDependencies(
        string classId)
    {
        if (_libraries.GetModelById(classId) is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);
        if (!_session.DependenciesAnalyzed)
            return ToolDiagnostics.NotAnalyzed(_libraries, "getting a class's dependencies");

        var items = _libraries.CombinedGraph.GetUsedModels(classId)
            .Select(ToRef)
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        return new DependencyResult(classId, _session.DependenciesAnalyzed, items.Count, items);
    }

    internal object FindUsages(
        string classId)
    {
        if (_libraries.GetModelById(classId) is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);
        if (!_session.DependenciesAnalyzed)
            return ToolDiagnostics.NotAnalyzed(_libraries, "finding a class's usages");

        var items = _libraries.CombinedGraph.GetModelUsedBy(classId)
            .Select(ToRef)
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        return new DependencyResult(classId, _session.DependenciesAnalyzed, items.Count, items);
    }

    internal object AnalyzeImpact(
        string[] classIds,
        int limit = 100,
        int offset = 0)
    {
        if (classIds is null || classIds.Length == 0)
            return new ToolError("Provide at least one class id in classIds.");

        var missing = classIds.Where(id => _libraries.GetModelById(id) is null).ToList();
        if (missing.Count > 0)
            return ToolDiagnostics.ClassesNotFound(_libraries, missing);
        if (!_session.DependenciesAnalyzed)
            return ToolDiagnostics.NotAnalyzed(_libraries, "analysing change impact");

        limit = Math.Clamp(limit, 1, MaxImpactLimit);
        offset = Math.Max(offset, 0);

        var result = _impact.AnalyzeImpact(_libraries.CombinedGraph, classIds);
        var ordered = result.ImpactDetails.OrderBy(d => d.ModelId, StringComparer.Ordinal).ToList();
        var page = ordered.Skip(offset).Take(limit)
            .Select(d => new ImpactDetailDto(d.ModelId, d.ClassType, d.ImpactedBy))
            .ToList();

        return new ImpactResult(
            classIds, _session.DependenciesAnalyzed, result.ImpactedModelsCount,
            page.Count, ordered.Count > offset + page.Count, page);
    }

    private static ClassRef ToRef(ModelNode n) => new(n.Id, n.Name, n.ClassType);
}
