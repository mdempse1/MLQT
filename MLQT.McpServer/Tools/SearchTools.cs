using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.SpellChecking;
using ModelicaParser.Visitors;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Search tools for finding classes when you don't know their names: by documentation/description text,
/// or by interface shape (class kind, parameter/connector counts, simulatability). Both scan the loaded
/// libraries; results are capped by 'limit'.
/// </summary>
[McpServerToolType]
public sealed class SearchTools
{
    private const int MaxLimit = 200;

    private readonly ILibraryDataService _libraries;

    public SearchTools(ILibraryDataService libraries) => _libraries = libraries;

    [McpServerTool(Name = "search_text")]
    [Description("Find classes whose description or Documentation prose contains the given text " +
                "(case-insensitive) — e.g. 'PID controller' or 'heat exchanger' — for when you don't know " +
                "the class name. Searches the human-readable text, not code identifiers (use search_classes " +
                "for name matching). Returns where it matched and a snippet. Scans loaded classes, so the " +
                "first search after loading a large library is slower (results are cached).")]
    public object SearchText(
        [Description("Text to look for in class descriptions and documentation (case-insensitive).")]
        string query,
        [Description("Max results to return (default 50, max 200).")] int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new ToolError("query must be a non-empty search string.");
        if (ToolDiagnostics.RequireLibrary(_libraries, "searching text") is { } noLib)
            return noLib;

        limit = Math.Clamp(limit, 1, MaxLimit);
        var q = query.Trim();
        var items = new List<TextSearchItem>();
        var total = 0;

        foreach (var node in _libraries.GetAllModels().OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            if (node.IsParseFailurePlaceholder)
                continue;
            var tree = node.Definition.EnsureParsed();
            if (tree is null)
                continue;

            var description = ClassInterfaceExtractor.Extract(tree).Description;
            string? where = null, text = null;
            if (!string.IsNullOrEmpty(description) && description.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                where = "description";
                text = description;
            }
            else
            {
                var info = DocumentationExtractor.Extract(tree).Info;
                var plain = info is null ? null : TextExtractor.StripHtml(info);
                if (!string.IsNullOrEmpty(plain) && plain.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    where = "documentation";
                    text = plain;
                }
            }

            if (where is null)
                continue;
            total++;
            if (items.Count < limit)
                items.Add(new TextSearchItem(node.Id, node.Name, node.ClassType, where, Snippet(text!, q)));
        }

        return new TextSearchResult(total, items.Count, items);
    }

    [McpServerTool(Name = "search_by_interface")]
    [Description("Find classes by interface shape rather than name — e.g. simulatable models, blocks with " +
                "connectors, or classes with parameters. Filter by class type, name substring, whether it " +
                "has an experiment() annotation (simulatable), and minimum parameter/connector counts " +
                "(counts include inherited members). Returns each match's parameter and connector counts.")]
    public object SearchByInterface(
        [Description("Optional class type filter, e.g. 'model', 'block', 'connector', 'function'.")]
        string? classType = null,
        [Description("Optional substring the class id must contain (case-insensitive).")]
        string? namePattern = null,
        [Description("Optional: require (true) or exclude (false) an experiment() annotation (simulatable).")]
        bool? hasExperiment = null,
        [Description("Optional minimum number of parameters/constants.")] int minParameters = 0,
        [Description("Optional minimum number of connectors.")] int minConnectors = 0,
        [Description("Max results to return (default 50, max 200).")] int limit = 50)
    {
        if (ToolDiagnostics.RequireLibrary(_libraries, "searching by interface") is { } noLib)
            return noLib;

        limit = Math.Clamp(limit, 1, MaxLimit);
        var items = new List<InterfaceSearchItem>();
        var total = 0;

        var candidates = _libraries.GetAllModels()
            .Where(m => !m.IsParseFailurePlaceholder)
            .Where(m => classType is null || string.Equals(m.ClassType, classType, StringComparison.OrdinalIgnoreCase))
            .Where(m => namePattern is null || m.Id.Contains(namePattern, StringComparison.OrdinalIgnoreCase))
            .Where(m => hasExperiment is null || m.HasExperimentAnnotation == hasExperiment.Value)
            .OrderBy(m => m.Id, StringComparer.Ordinal);

        foreach (var node in candidates)
        {
            var (paramCount, connectorCount) = CountInterface(node);
            if (paramCount < minParameters || connectorCount < minConnectors)
                continue;
            total++;
            if (items.Count < limit)
                items.Add(new InterfaceSearchItem(
                    node.Id, node.Name, node.ClassType, paramCount, connectorCount, node.HasExperimentAnnotation));
        }

        return new InterfaceSearchResult(total, items.Count, items);
    }

    private (int Parameters, int Connectors) CountInterface(ModelNode node)
    {
        var parameters = 0;
        var connectors = 0;
        foreach (var m in ClassElementResolver.Collect(_libraries, node, includeProtected: false, includeInherited: true))
        {
            if (m.Element.Kind != ClassElementKind.Component)
                continue;
            var typeNode = TypeResolver.Resolve(_libraries, m.OwnerId, m.Element.Type, m.OwnerImports);
            if (m.Element.Causality is not null || typeNode?.ClassType == "connector")
                connectors++;
            else if (m.Element.Variability is "parameter" or "constant")
                parameters++;
        }
        return (parameters, connectors);
    }

    private static string Snippet(string text, string query)
    {
        var flat = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        var idx = flat.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return flat.Length <= 160 ? flat : flat[..160] + "…";
        var start = Math.Max(0, idx - 40);
        var end = Math.Min(flat.Length, idx + query.Length + 80);
        var snippet = flat[start..end];
        return (start > 0 ? "…" : "") + snippet + (end < flat.Length ? "…" : "");
    }
}
