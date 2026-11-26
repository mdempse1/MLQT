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

    /// <summary>
    /// For tests: false parses every class, as the search did before <see cref="TextSearchPrefilter"/>,
    /// which is the reference a prefiltered search must agree with.
    /// </summary>
    internal bool UsePrefilter { get; init; } = true;

    /// <summary>
    /// Every class whose description, or failing that whose documentation prose, contains
    /// <paramref name="query"/>, ignoring case. All of them are counted, so <paramref name="limit"/> caps
    /// only the items returned.
    /// </summary>
    /// <remarks>
    /// <para><b>Only classes that could match are parsed</b> (<see cref="TextSearchPrefilter"/>), and each
    /// through <see cref="ModelDefinition.Borrow{T}"/>, so a search keeps no trees. It parsed and kept
    /// every class's, and took 31 s over the MSL.</para>
    /// <para><b>Cancellation is checked before each class</b>, so a cancelled search stops once the class
    /// it is reading is done, and throws <see cref="OperationCanceledException"/>.</para>
    /// </remarks>
    internal object SearchText(
        string query,
        int limit = 50,
        string? library = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new ToolError("query must be a non-empty search string.");
        if (ToolDiagnostics.RequireLibrary(_libraries, "searching text") is { } noLib)
            return noLib;

        string? onlyLibrary = null;
        if (library is not null)
        {
            var (resolved, error) = EntityResolver.ResolveLibrary(_libraries, library);
            if (error is not null)
                return error;
            onlyLibrary = resolved!.Id;
        }

        limit = Math.Clamp(limit, 1, MaxLimit);
        var q = query.Trim();
        var items = new List<TextSearchItem>();
        var total = 0;

        var candidates = _libraries.GetAllModels()
            .Where(m => !m.IsParseFailurePlaceholder)
            .Where(m => onlyLibrary is null || _libraries.GetOwningLibrary(m.Id)?.Id == onlyLibrary)
            .ToList();
        var prefilter = new TextSearchPrefilter(q, candidates);

        foreach (var node in candidates.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (UsePrefilter && !prefilter.MayMatch(node))
                continue;

            var hit = node.Definition.Borrow(tree => MatchText(tree, q), ifUnparseable: null);
            if (hit is not { } found)
                continue;
            total++;
            if (items.Count < limit)
                items.Add(new TextSearchItem(node.Id, node.Name, node.ClassType, found.Where, Snippet(found.Text, q)));
        }

        return new TextSearchResult(total, items.Count, items);
    }

    // Where in the class the query is: its description first, then its documentation as prose.
    private static (string Where, string Text)? MatchText(modelicaParser.Stored_definitionContext tree, string q)
    {
        var description = ClassInterfaceExtractor.Extract(tree).Description;
        if (!string.IsNullOrEmpty(description) && description.Contains(q, StringComparison.OrdinalIgnoreCase))
            return ("description", description);

        var info = DocumentationExtractor.Extract(tree).Info;
        var plain = info is null ? null : TextExtractor.StripHtml(info);
        if (!string.IsNullOrEmpty(plain) && plain.Contains(q, StringComparison.OrdinalIgnoreCase))
            return ("documentation", plain);
        return null;
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
