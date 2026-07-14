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
/// built at load time — call analyze_dependencies once (it can be slow on a large library) to
/// populate them, then get_dependencies / find_usages / analyze_impact return meaningful results.
/// </summary>
[McpServerToolType]
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

    [McpServerTool(Name = "analyze_dependencies")]
    [Description("Run the dependency and external-resource analysis over all loaded libraries. This is " +
                "an opt-in, potentially slow step (it parses every model and resolves references) and is " +
                "required before get_dependencies, find_usages, analyze_impact, get_class_resources and " +
                "get_resource_warnings return meaningful results. Returns counts of dependency edges and " +
                "resources found. Safe to re-run after loading more libraries.")]
    public async Task<object> AnalyzeDependencies()
    {
        if (_libraries.Libraries.Count == 0)
            return new ToolError("No libraries loaded. Load one first with load_library or load_repository.");

        var graph = _libraries.CombinedGraph;
        var libraryInfos = BuildLibraryInfos();

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

    [McpServerTool(Name = "get_dependencies")]
    [Description("List the classes that a class directly uses/depends on (one hop). Requires " +
                "analyze_dependencies to have been run. The result's dependenciesAnalyzed flag tells you " +
                "whether an empty list means 'no dependencies' (true) or 'not analyzed yet' (false).")]
    public object GetDependencies(
        [Description("Fully-qualified class id, e.g. 'Modelica.Blocks.Continuous.Integrator'.")]
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

    [McpServerTool(Name = "find_usages")]
    [Description("List the classes that directly use/depend on a class (one hop, i.e. the direct " +
                "dependents that would be affected if this class changed). Requires analyze_dependencies. " +
                "For the full transitive blast radius, use analyze_impact.")]
    public object FindUsages(
        [Description("Fully-qualified class id whose direct dependents you want.")] string classId)
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

    [McpServerTool(Name = "analyze_impact")]
    [Description("Compute the full transitive impact of changing one or more classes: every class that " +
                "transitively depends on them (the complete blast radius), with the immediate source(s) " +
                "that pulled each into the impact set. Requires analyze_dependencies. Returns the total " +
                "impacted count plus a page of details (use limit/offset; the count can be very large for " +
                "core classes).")]
    public object AnalyzeImpact(
        [Description("One or more fully-qualified class ids to assess as if they were changed.")]
        string[] classIds,
        [Description("Max detail rows to return (default 100, max 2000).")] int limit = 100,
        [Description("Detail rows to skip for pagination (default 0).")] int offset = 0)
    {
        if (classIds is null || classIds.Length == 0)
            return new ToolError("Provide at least one class id in classIds.");

        var missing = classIds.Where(id => _libraries.GetModelById(id) is null).ToList();
        if (missing.Count > 0)
            return _libraries.Libraries.Count == 0
                ? ToolDiagnostics.ClassNotFound(_libraries, missing[0])
                : new ToolError($"Unknown class id(s): {string.Join(", ", missing)}. Use search_classes to find them.");
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

    private List<LibraryInfo> BuildLibraryInfos() => _libraries.Libraries.Select(lib =>
    {
        var rootPath = lib.SourceType == LibrarySourceType.File
            ? Path.GetDirectoryName(lib.SourcePath) ?? lib.SourcePath
            : lib.SourcePath;
        return new LibraryInfo(lib.Name, rootPath);
    }).ToList();

    private static ClassRef ToRef(ModelNode n) => new(n.Id, n.Name, n.ClassType);
}
