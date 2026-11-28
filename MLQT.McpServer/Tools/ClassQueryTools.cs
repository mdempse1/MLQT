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
using MLQT.Services.Helpers;
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
        int limit = 50,
        string? libraryId = null)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new ToolError("query must be a non-empty search string.");
        if (ToolDiagnostics.RequireLibrary(_libraries, "searching classes") is { } noLib)
            return noLib;

        // The same scope as ListClasses: the library's own classes.
        IReadOnlySet<string>? inLibrary = null;
        if (libraryId is not null)
        {
            var (library, error) = EntityResolver.ResolveLibrary(_libraries, libraryId);
            if (error is not null)
                return error;
            inLibrary = library!.ModelIds.ToHashSet(StringComparer.Ordinal);
        }

        limit = Math.Clamp(limit, 1, MaxListLimit);

        var matches = _libraries.GetAllModels()
            .Where(m => inLibrary is null || inLibrary.Contains(m.Id))
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
    // a search hit conveys what the class is for. Borrowed, so a search keeps none of the trees it parses
    // (ModelDefinition.Borrow); empty parts come back null.
    private static (string? Description, string? Snippet) DescriptionAndSnippet(ModelNode node) =>
        node.Definition.Borrow(tree =>
        {
            var description = ClassInterfaceExtractor.Extract(tree).Description;
            var (info, _) = DocumentationExtractor.Extract(tree);
            return (Trimmed(description), Snippet(info));
        }, ifUnparseable: ((string?)null, (string?)null));

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

        maxNodes = Math.Clamp(maxNodes, 1, MaxTreeNodes);

        // Breadth first, so one deep library cannot spend the whole budget before its siblings are seen,
        // and every class returned counts against maxNodes - not only the packages expanded, which is what
        // let depth=4, maxNodes=400 return thousands of classes, more than an agent's reply can hold.
        var roots = rootIds.Select(id => NewBranch(id, childrenByParent)).OfType<TreeBranch>().ToList();
        var count = roots.Count;
        var size = roots.Sum(r => r.Size);
        var bySize = false;
        var unexpanded = new List<TreeBranch>();
        var partly = new List<TreeBranch>();
        var queue = new Queue<(TreeBranch Branch, int Depth)>(roots.Select(r => (r, 0)));
        while (queue.Count > 0)
        {
            var (branch, depth) = queue.Dequeue();
            if (branch.ChildIds.Count == 0 || depth >= maxDepth)
                continue;

            var children = branch.ChildIds.Select(id => NewBranch(id, childrenByParent)).OfType<TreeBranch>().ToList();
            // How many of them fit, in order, within both the class count and the reply's size.
            var fit = 0;
            var fitSize = 0;
            while (fit < children.Count && count + fit < maxNodes && size + fitSize + children[fit].Size <= MaxTreeChars)
                fitSize += children[fit++].Size;
            if (fit < children.Count && count + fit < maxNodes)
                bySize = true;

            // A package's children are listed whole or not at all, so a listing never looks complete when it
            // is not - except while nothing has been listed yet, so a single flat package still shows something.
            var take = fit == children.Count ? fit
                     : count == roots.Count ? fit
                     : 0;
            if (take == 0)
            {
                unexpanded.Add(branch);
                continue;
            }
            if (take < children.Count)
                partly.Add(branch);

            branch.Children = children.Take(take).ToList();
            count += take;
            size += branch.Children.Sum(c => c.Size);
            foreach (var child in branch.Children)
                queue.Enqueue((child, depth + 1));
        }

        var truncated = unexpanded.Count > 0 || partly.Count > 0;
        return new PackageTreeResult(
            count,
            truncated,
            truncated ? TruncationNote(maxNodes, bySize, unexpanded, partly) : null,
            roots.Select(r => r.ToNode()).ToList());
    }

    internal const int DefaultTreeNodes = 200;

    /// <summary>The most classes one reply may hold, whatever is asked.</summary>
    internal const int MaxTreeNodes = 500;

    /// <summary>
    /// The most characters the classes of one reply may take, estimated as they serialise. Measured on the
    /// MSL, a class is 110-150 characters, and dotted ids break into many tokens, so 500 deep classes could
    /// reach a client's 25,000-token limit for a tool's answer; 40,000 characters stays well inside it, at
    /// some 300 MSL classes (B503: a whole tree came back at 30k characters in the 2026-10-18 sessions, and a
    /// later depth=4 call passed the client's limit).
    /// </summary>
    internal const int MaxTreeChars = 40_000;

    private static string TruncationNote(int maxNodes, bool bySize, List<TreeBranch> unexpanded, List<TreeBranch> partly)
    {
        var parts = new List<string>
        {
            bySize ? $"Cut at the reply's size limit, before maxNodes={maxNodes} was reached." : $"Cut at maxNodes={maxNodes}.",
        };
        if (partly.Count > 0)
        {
            var p = partly[0];
            parts.Add($"{p.Id} lists {p.Children!.Count} of its {p.ChildIds.Count} classes; " +
                      "use mlqt_find to search it.");
        }
        if (unexpanded.Count > 0)
        {
            const int named = 10;
            var names = string.Join(", ", unexpanded.Take(named).Select(b => b.Id));
            var more = unexpanded.Count > named ? $" and {unexpanded.Count - named} more" : "";
            parts.Add($"{unexpanded.Count} packages were not expanded (childCount says how many classes each " +
                      $"holds): {names}{more}. Browse one of them as root to see inside it.");
        }
        return string.Join(" ", parts);
    }

    private TreeBranch? NewBranch(string id, Dictionary<string, List<string>> childrenByParent)
    {
        var node = _libraries.GetModelById(id);
        if (node is null)
            return null;
        // In the package's own order, as the library browser shows it: the graph's lists are in no order a
        // reader would recognise, and which classes a cut keeps follows this order.
        var childIds = childrenByParent.TryGetValue(id, out var ids)
            ? ChildOrder.Sort(ids.Select(_libraries.GetModelById).OfType<ModelNode>(), node).Select(c => c.Id).ToList()
            : [];
        return new TreeBranch(node.Id, node.Name, node.ClassType, childIds);
    }

    /// <summary>A node while the tree is being built breadth first; <see cref="ToNode"/> freezes it.</summary>
    private sealed class TreeBranch(string id, string name, string classType, List<string> childIds)
    {
        public string Id { get; } = id;
        public List<string> ChildIds { get; } = childIds;
        public List<TreeBranch>? Children { get; set; }

        /// <summary>About what this class takes in the reply: its strings, and the property names and
        /// punctuation around them, which are some 60 characters (<c>{"id":"","name":"","classType":"",
        /// "childCount":0}</c>, a comma, and its share of a <c>"children":[]</c>).</summary>
        public int Size => Id.Length + name.Length + classType.Length + 60;

        public PackageTreeNode ToNode()
            => new(Id, name, classType, ChildIds.Count, Children?.Select(c => c.ToNode()).ToList());
    }
}
