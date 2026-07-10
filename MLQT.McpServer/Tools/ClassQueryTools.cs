using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Read-only query and navigation tools over the classes in the loaded libraries. Class ids are
/// fully-qualified dotted Modelica names (e.g. "Modelica.Blocks.Continuous.Integrator").
/// </summary>
[McpServerToolType]
public sealed class ClassQueryTools
{
    private const int MaxListLimit = 1000;
    private const int MaxTreeDepth = 8;

    private readonly ILibraryDataService _libraries;

    public ClassQueryTools(ILibraryDataService libraries) => _libraries = libraries;

    [McpServerTool(Name = "get_class_info")]
    [Description("Get structural metadata for a single Modelica class by its fully-qualified id: " +
                "class type (model/block/package/function/record/connector/type/class), whether it is " +
                "partial, its containing file and line span, package version, 'uses' dependencies from " +
                "the annotation, whether it carries an experiment() annotation (i.e. is simulatable), and " +
                "parse health. Does NOT return the source code (use get_class_source) or the dependency " +
                "graph (use get_dependencies / find_usages).")]
    public object GetClassInfo(
        [Description("Fully-qualified class id, e.g. 'Modelica.Blocks.Continuous.Integrator'.")]
        string classId)
    {
        var node = _libraries.GetModelById(classId);
        if (node is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);

        string? filePath = null;
        if (node.ContainingFileId is not null)
            filePath = _libraries.CombinedGraph.GetNode<FileNode>(node.ContainingFileId)?.FilePath;

        return new ClassInfo(
            node.Id,
            node.Name,
            node.ClassType,
            node.IsPartial,
            IsPackage: node.ClassType == "package",
            ElementPrefix: string.IsNullOrEmpty(node.ElementPrefix) ? null : node.ElementPrefix,
            node.ParentModelName,
            node.IsNested,
            filePath,
            node.StartLine,
            node.StopLine,
            node.Version,
            node.Uses,
            node.HasExperimentAnnotation,
            node.CanBeStoredStandalone,
            node.HasParserErrors,
            node.HasFatalParseFailure);
    }

    [McpServerTool(Name = "get_class_source")]
    [Description("Get the Modelica source code for a class. By default (include_annotations=false) the " +
                "graphical/experiment/Documentation annotations are stripped, returning just the " +
                "structural code — much smaller, and still valid parseable Modelica. Set " +
                "include_annotations=true to get the verbatim source including all annotations.")]
    public object GetClassSource(
        [Description("Fully-qualified class id, e.g. 'Modelica.Blocks.Continuous.Integrator'.")]
        string classId,
        [Description("Include annotation blocks (icons, diagrams, experiment, Documentation). " +
                     "Default false, which strips them to reduce size.")]
        bool includeAnnotations = false)
    {
        var node = _libraries.GetModelById(classId);
        if (node is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);

        var code = node.Definition.ModelicaCode ?? string.Empty;

        // Verbatim requested, or nothing safely renderable: return the stored source as-is.
        if (includeAnnotations || node.IsParseFailurePlaceholder || string.IsNullOrWhiteSpace(code))
            return new ClassSourceResult(node.Id, node.ClassType, includeAnnotations, code);

        try
        {
            // Re-render with annotations off. Pass the token stream so comments are preserved.
            var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(code);
            var renderer = new ModelicaRenderer(
                renderForCodeEditor: false,
                showAnnotations: false,
                excludeClassDefinitions: false,
                tokenStream: tokenStream);
            renderer.VisitStored_definition(parseTree);
            var stripped = string.Join("\n", renderer.Code);
            return new ClassSourceResult(node.Id, node.ClassType, AnnotationsIncluded: false, stripped);
        }
        catch
        {
            // Fall back to verbatim source if rendering fails for any reason.
            return new ClassSourceResult(node.Id, node.ClassType, AnnotationsIncluded: true, code);
        }
    }

    [McpServerTool(Name = "list_classes")]
    [Description("List classes across the loaded libraries, with optional filtering by library and by " +
                "class type. Paginated: results are ordered by id; use offset/limit to page. Returns the " +
                "total match count so you know how many pages remain. Use search_classes to find classes " +
                "by name substring instead.")]
    public object ListClasses(
        [Description("Optional: restrict to one library, by its id (GUID from list_libraries) or its " +
                     "name (e.g. 'Modelica'). Omit for all libraries. Not a class id.")]
        string? libraryId = null,
        [Description("Optional class type filter, e.g. 'model', 'package', 'function', 'block', " +
                     "'record', 'connector', 'type'.")]
        string? classType = null,
        [Description("Max items to return (default 100, max 1000).")] int limit = 100,
        [Description("Number of items to skip for pagination (default 0).")] int offset = 0)
    {
        if (ToolDiagnostics.RequireLibrary(_libraries, "listing classes") is { } noLib)
            return noLib;

        limit = Math.Clamp(limit, 1, MaxListLimit);
        offset = Math.Max(offset, 0);

        IEnumerable<ModelNode> models;
        if (libraryId is not null)
        {
            var (library, error) = EntityResolver.ResolveLibrary(_libraries, libraryId);
            if (error is not null)
                return error;
            models = library!.ModelIds
                .Select(id => _libraries.GetModelById(id))
                .Where(m => m is not null)!
                .Cast<ModelNode>();
        }
        else
        {
            models = _libraries.GetAllModels();
        }

        if (!string.IsNullOrWhiteSpace(classType))
            models = models.Where(m => string.Equals(m.ClassType, classType, StringComparison.OrdinalIgnoreCase));

        var ordered = models.OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
        var page = ordered.Skip(offset).Take(limit)
            .Select(m => new ClassListItem(m.Id, m.Name, m.ClassType, m.ParentModelName))
            .ToList();

        return new ClassListResult(ordered.Count, offset, page.Count, page);
    }

    [McpServerTool(Name = "search_classes")]
    [Description("Find classes whose fully-qualified id contains the given text (case-insensitive). " +
                "Matches on the id, so 'Integrator' finds 'Modelica.Blocks.Continuous.Integrator'. " +
                "Results are ordered with exact leaf-name matches first, then by id. Use for locating a " +
                "class when you don't know its full path.")]
    public object SearchClasses(
        [Description("Substring to search for within class ids (case-insensitive).")] string query,
        [Description("Max results to return (default 50, max 1000).")] int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new ToolError("query must be a non-empty search string.");
        if (ToolDiagnostics.RequireLibrary(_libraries, "searching classes") is { } noLib)
            return noLib;

        limit = Math.Clamp(limit, 1, MaxListLimit);

        var matches = _libraries.GetAllModels()
            .Where(m => m.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var total = matches.Count;
        var page = matches
            .OrderByDescending(m => string.Equals(m.Name, query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .Take(limit)
            .Select(m => new ClassListItem(m.Id, m.Name, m.ClassType, m.ParentModelName))
            .ToList();

        return new ClassListResult(total, 0, page.Count, page);
    }

    [McpServerTool(Name = "get_package_tree")]
    [Description("Get the hierarchical package/class tree. Without root_class_id, returns the top-level " +
                "classes of every loaded library. With root_class_id, returns that class and its nested " +
                "children. max_depth bounds how many levels are expanded (default 1 = immediate children); " +
                "each node reports its childCount so you can drill in with further calls. Use this to " +
                "navigate structure; use list_classes for a flat, filterable listing.")]
    public object GetPackageTree(
        [Description("Optional class id to root the tree at. Omit for all libraries' top-level classes.")]
        string? rootClassId = null,
        [Description("How many levels to expand (default 1, max 8).")] int maxDepth = 1)
    {
        if (ToolDiagnostics.RequireLibrary(_libraries, "browsing the package tree") is { } noLib)
            return noLib;

        maxDepth = Math.Clamp(maxDepth, 1, MaxTreeDepth);

        // Parent id -> child ids, merged across all libraries. Ids are globally unique, so no clashes.
        var childrenByParent = new Dictionary<string, List<string>>();
        foreach (var library in _libraries.Libraries)
            foreach (var kvp in library.ChildrenByParent)
                childrenByParent[kvp.Key] = kvp.Value;

        IEnumerable<string> rootIds;
        if (rootClassId is not null)
        {
            if (_libraries.GetModelById(rootClassId) is null)
                return ToolDiagnostics.ClassNotFound(_libraries, rootClassId);
            rootIds = [rootClassId];
        }
        else
        {
            rootIds = _libraries.Libraries.SelectMany(l => l.TopLevelModelIds);
        }

        var tree = rootIds
            .Select(id => BuildTree(id, childrenByParent, depth: 0, maxDepth))
            .Where(n => n is not null)
            .Cast<PackageTreeNode>()
            .ToList();

        return tree;
    }

    private PackageTreeNode? BuildTree(string id, Dictionary<string, List<string>> childrenByParent, int depth, int maxDepth)
    {
        var node = _libraries.GetModelById(id);
        if (node is null)
            return null;

        childrenByParent.TryGetValue(id, out var childIds);
        var childCount = childIds?.Count ?? 0;

        IReadOnlyList<PackageTreeNode>? children = null;
        if (childCount > 0 && depth < maxDepth)
        {
            children = childIds!
                .Select(childId => BuildTree(childId, childrenByParent, depth + 1, maxDepth))
                .Where(n => n is not null)
                .Cast<PackageTreeNode>()
                .ToList();
        }

        return new PackageTreeNode(node.Id, node.Name, node.ClassType, childCount, children);
    }
}
