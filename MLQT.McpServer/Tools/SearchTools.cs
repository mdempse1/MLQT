using System.Reflection;
using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaGraph.Analysis;
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
[Obfuscation]
public sealed class SearchTools
{
    private const int MaxLimit = 200;

    private readonly ILibraryDataService _libraries;

    public SearchTools(ILibraryDataService libraries) => _libraries = libraries;

    internal object SearchText(
        string query,
        int limit = 50)
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

    internal object SearchByInterface(
        string? classType = null,
        string? namePattern = null,
        bool? hasExperiment = null,
        int minParameters = 0,
        int minConnectors = 0,
        int limit = 50)
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
        foreach (var m in ClassElementResolver.Collect(_libraries.CombinedGraph, node, includeProtected: false, includeInherited: true))
        {
            if (m.Element.Kind != ClassElementKind.Component)
                continue;
            var typeNode = TypeResolver.Resolve(_libraries.CombinedGraph, m.OwnerId, m.Element.Type, m.OwnerImports);
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
