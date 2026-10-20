using System.Reflection;
using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using ModelicaGraph;
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
[Obfuscation]
public sealed class ClassQueryTools
{
    private const int MaxListLimit = 1000;
    private const int MaxTreeDepth = 8;

    private readonly ILibraryDataService _libraries;

    public ClassQueryTools(ILibraryDataService libraries) => _libraries = libraries;

    internal object GetClassInfo(
        string classId)
    {
        var node = _libraries.GetModelById(classId);
        if (node is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);

        string? filePath = null;
        if (node.ContainingFileId is not null)
            filePath = _libraries.CombinedGraph.GetNode<FileNode>(node.ContainingFileId)?.FilePath;

        // Whether the class's file can actually be edited by this process (filesystem-inferred): false
        // for reference libraries installed read-only (e.g. under Program Files). Null if unknown.
        bool? writable = filePath is null ? null : FileWritability.IsWritable(filePath);

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
            node.HasFatalParseFailure,
            writable,
            ReadOnlySources.KindOf(node) == ReadOnlySourceKind.RecoveredFromDocumentation,
            ReadOnlySources.KindOf(node) is { } kind ? WireNames.Of(kind) : null);
    }

    internal object GetClassSource(
        string classId,
        bool includeAnnotations = false)
    {
        var node = _libraries.GetModelById(classId);
        if (node is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);

        var code = node.Definition.ModelicaCode ?? string.Empty;

        // Verbatim requested, or nothing safely parseable: return the stored source as-is.
        if (includeAnnotations || node.IsParseFailurePlaceholder || string.IsNullOrWhiteSpace(code))
            return new ClassSourceResult(node.Id, node.ClassType, includeAnnotations, code);

        try
        {
            // Cut the annotations out of the text rather than re-rendering the class without them
            // (B218). The renderer rebuilds every line it emits, so what came back was a reformat of
            // the class rather than the class, and its line numbers agreed with nothing - not the
            // file, and not the `modelLine` this same server reports against a finding.
            //
            // ElisionFinder takes the annotations out and leaves every other character as written.
            // It reports the lines left empty rather than deleting them, and they are blanked here
            // rather than dropped: an agent holds no line map, so preserving the numbering is what
            // makes mlqt_get_class's source view and mlqt_list_findings describe the same text.
            var (spliced, elision) = ElisionFinder.WithoutAnnotations(
                ModelicaParserHelper.Parse(code), code, elidedTextMustParse: true);
            var stripped = string.Join("\n", elision.Blank(spliced.Split('\n')));
            return new ClassSourceResult(node.Id, node.ClassType, AnnotationsIncluded: false, stripped);
        }
        catch
        {
            // Fall back to verbatim source if the parse fails for any reason.
            return new ClassSourceResult(node.Id, node.ClassType, AnnotationsIncluded: true, code);
        }
    }

    internal object ListClasses(
        string? libraryId = null,
        string? classType = null,
        int limit = 100,
        int offset = 0)
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

    internal object SearchClasses(
        string query,
        int limit = 50)
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
            .Select(m =>
            {
                var (description, snippet) = DescriptionAndSnippet(m);
                return new ClassSearchItem(m.Id, m.Name, m.ClassType, m.ParentModelName, description, snippet);
            })
            .ToList();

        return new ClassSearchResult(total, page.Count, page);
    }

    // The class's one-line description plus a short plain-text snippet of its Documentation(info) HTML, so
    // a search hit conveys what the class is for. Uses the cached parse tree; empty parts come back null.
    private static (string? Description, string? Snippet) DescriptionAndSnippet(ModelNode node)
    {
        var tree = node.Definition.EnsureParsed();
        if (tree is null)
            return (null, null);

        var description = ClassInterfaceExtractor.Extract(tree).Description;
        var (info, _) = DocumentationExtractor.Extract(tree);
        return (Trimmed(description), Snippet(info));
    }

    private static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Snippet(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;
        var text = System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " "));
        text = Regex.Replace(text, @"\s+", " ");
        text = Regex.Replace(text, @"\s+([.,;:!?])", "$1").Trim(); // no space before punctuation
        if (text.Length == 0)
            return null;
        return text.Length > 200 ? text[..200].TrimEnd() + "…" : text;
    }

    internal object GetPackageTree(
        string? rootClassId = null,
        int maxDepth = 1,
        int maxNodes = DefaultTreeNodes)
    {
        // Asked about one class with nothing loaded: name the library that class is in.
        if (rootClassId is not null && _libraries.Libraries.Count == 0)
            return ToolDiagnostics.ClassNotFound(_libraries, rootClassId);
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

        var budget = new NodeBudget(Math.Max(maxNodes, 1));
        var tree = rootIds
            .Select(id => BuildTree(id, childrenByParent, depth: 0, maxDepth, budget))
            .Where(n => n is not null)
            .Cast<PackageTreeNode>()
            .ToList();

        return tree;
    }

    internal const int DefaultTreeNodes = 200;

    /// <summary>How many more nodes may have their children listed, shared across one call (B503: a whole
    /// tree came back at 30k characters in the 2026-10-18 sessions).</summary>
    private sealed class NodeBudget(int left)
    {
        public int Left { get; set; } = left;
    }

    private PackageTreeNode? BuildTree(string id, Dictionary<string, List<string>> childrenByParent, int depth, int maxDepth, NodeBudget budget)
    {
        var node = _libraries.GetModelById(id);
        if (node is null)
            return null;

        childrenByParent.TryGetValue(id, out var childIds);
        var childCount = childIds?.Count ?? 0;

        IReadOnlyList<PackageTreeNode>? children = null;
        if (childCount > 0 && depth < maxDepth && budget.Left > 0)
        {
            budget.Left--;
            children = childIds!
                .Select(childId => BuildTree(childId, childrenByParent, depth + 1, maxDepth, budget))
                .Where(n => n is not null)
                .Cast<PackageTreeNode>()
                .ToList();
        }

        return new PackageTreeNode(node.Id, node.Name, node.ClassType, childCount, children);
    }
}
