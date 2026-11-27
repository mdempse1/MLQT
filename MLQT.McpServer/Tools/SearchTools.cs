using System.Collections.Concurrent;
using System.Reflection;
using System.ComponentModel;
using ModelicaGraph;
using ModelContextProtocol.Server;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
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
    /// Every class whose description and documentation prose hold every word of
    /// <paramref name="query"/> between them, in any order, ignoring case; a <c>"quoted phrase"</c> is
    /// one term, matched as written (<see cref="TextQuery"/>). All of them are counted, so
    /// <paramref name="limit"/> caps only the items returned.
    /// </summary>
    /// <remarks>
    /// <para><b>Only classes that could match are parsed</b> (<see cref="TextSearchPrefilter"/>), a package
    /// without the classes written inline in it (<see cref="TextSearchPrefilter.ReducedSource"/>), in
    /// parallel, and none is kept: a search keeps no trees. It parsed and kept every class's whole
    /// source, one at a time, and took 31 s over the MSL.</para>
    /// <para><b>Cancellation is checked before each class</b>, so a cancelled search stops once the
    /// classes it is reading are done, and throws <see cref="OperationCanceledException"/>.</para>
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
        var q = TextQuery.Parse(query.Trim());
        if (q.Terms.Count == 0)
            return new ToolError("query must hold at least one word: a pair of quotes with nothing in them is not one.");
        var items = new List<TextSearchItem>();
        var total = 0;

        var candidates = _libraries.GetAllModels()
            .Where(m => !m.IsParseFailurePlaceholder)
            .Where(m => onlyLibrary is null || _libraries.GetOwningLibrary(m.Id)?.Id == onlyLibrary)
            .ToList();
        // Every run of letters and digits of every term has to be in the class's text, which is what the
        // prefilter asks of the whole query: so it rules out exactly what it did for a phrase.
        var prefilter = new TextSearchPrefilter(string.Join(' ', q.Terms), candidates);

        cancellationToken.ThrowIfCancellationRequested();
        var toRead = candidates
            .Where(m => !UsePrefilter || prefilter.MayMatch(m))
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .ToList();

        // Read in parallel, each class on its own, and reported in id order as before. The options'
        // token is checked before each class is started.
        var hits = new (string Where, string Text)?[toRead.Count];
        EachInParallel(toRead.Count, cancellationToken, i => hits[i] = Read(toRead[i], q, UsePrefilter ? prefilter : null));

        for (var i = 0; i < toRead.Count; i++)
        {
            if (hits[i] is not { } found)
                continue;
            total++;
            if (items.Count < limit)
            {
                var node = toRead[i];
                items.Add(new TextSearchItem(node.Id, node.Name, node.ClassType, found.Where, Snippet(found.Text, q)));
            }
        }

        return new TextSearchResult(total, items.Count, items);
    }

    /// <summary>
    /// Runs <paramref name="body"/> for 0 to <paramref name="count"/> - 1 in parallel, checking the token
    /// before each, and throws what a body threw rather than the <see cref="AggregateException"/>
    /// <see cref="Parallel.For(int, int, ParallelOptions, Action{int})"/> wraps it in, so a failure reads
    /// as it did when the classes were read one at a time.
    /// </summary>
    internal static void EachInParallel(int count, CancellationToken cancellationToken, Action<int> body)
    {
        try
        {
            Parallel.For(0, count, new ParallelOptions { CancellationToken = cancellationToken }, body);
        }
        catch (AggregateException e) when (e.InnerExceptions.Count > 0)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
            throw;
        }
    }

    // Where in one class the query is. A tree something else holds is used as it is. Otherwise a class
    // with classes written inline in it is parsed without them, which is most of the time a search of
    // the MSL took, and as it is only when that text does not parse cleanly; the rest are borrowed.
    private static (string Where, string Text)? Read(ModelNode node, TextQuery q, TextSearchPrefilter? prefilter)
    {
        if (node.Definition.ParsedCode is { } held)
            return MatchText(held, q);
        if (prefilter?.ReducedSource(node) is { } reduced)
        {
            var (tree, errors) = ModelicaParserHelper.ParseWithErrors(reduced);
            if (errors.Count == 0)
                return MatchText(tree, q);
        }
        return node.Definition.Borrow(tree => MatchText(tree, q), ifUnparseable: null);
    }

    // Where in the class the query's terms are: all in its description, else all in its documentation as
    // prose, else between the two - a class described as a "Heat exchanger" whose documentation gives the
    // "effectiveness" is what "heat effectiveness" asks for. The text returned is the one a snippet shows.
    private static (string Where, string Text)? MatchText(modelicaParser.Stored_definitionContext tree, TextQuery q)
    {
        var description = ClassInterfaceExtractor.Extract(tree).Description;
        if (q.AllIn(description))
            return ("description", description!);

        var info = DocumentationExtractor.Extract(tree).Info;
        var plain = info is null ? null : TextExtractor.StripHtml(info);
        if (q.AllIn(plain))
            return ("documentation", plain!);
        if (q.AllInAny(description, plain))
            return ("description and documentation", $"{description} | {plain}");
        return null;
    }

    /// <summary>
    /// Every class of the shape asked for, with its public parameter and connector counts, inherited
    /// ones included. All of them are counted, so <paramref name="limit"/> caps only the items returned.
    /// </summary>
    /// <remarks>
    /// <para><b>Counting a class's interface means walking its bases</b>, and it was done with no cache,
    /// one class at a time: every class re-parsed its whole chain, and an unfiltered search over the
    /// MSL and eight Dymola libraries took 15 s. Now one <see cref="ClassElementResolver.InterfaceCache"/>
    /// and one table of resolved types serve the whole search, the classes are counted in parallel, and
    /// with no <paramref name="minParameters"/> or <paramref name="minConnectors"/> to test only the
    /// classes returned are counted at all.</para>
    /// <para><b>Cancellation is checked before each class</b>, as for a text search.</para>
    /// </remarks>
    internal object SearchByInterface(
        string? classType = null,
        string? namePattern = null,
        bool? hasExperiment = null,
        int minParameters = 0,
        int minConnectors = 0,
        int limit = 50,
        string? library = null,
        CancellationToken cancellationToken = default)
    {
        if (ToolDiagnostics.RequireLibrary(_libraries, "searching by interface") is { } noLib)
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
        var candidates = _libraries.GetAllModels()
            .Where(m => !m.IsParseFailurePlaceholder)
            .Where(m => classType is null || string.Equals(m.ClassType, classType, StringComparison.OrdinalIgnoreCase))
            .Where(m => namePattern is null || m.Id.Contains(namePattern, StringComparison.OrdinalIgnoreCase))
            .Where(m => hasExperiment is null || m.HasExperimentAnnotation == hasExperiment.Value)
            .Where(m => onlyLibrary is null || _libraries.GetOwningLibrary(m.Id)?.Id == onlyLibrary)
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .ToList();

        // With no count to test, every candidate is a result, and only the page returned needs counting.
        var counter = new InterfaceCounter(_libraries.CombinedGraph);
        var filtered = minParameters > 0 || minConnectors > 0;
        var toCount = filtered ? candidates : candidates.Take(limit).ToList();

        cancellationToken.ThrowIfCancellationRequested();
        var counts = new (int Parameters, int Connectors)[toCount.Count];
        EachInParallel(toCount.Count, cancellationToken, i => counts[i] = counter.Count(toCount[i]));

        var items = new List<InterfaceSearchItem>();
        var total = filtered ? 0 : candidates.Count;
        for (var i = 0; i < toCount.Count; i++)
        {
            var (paramCount, connectorCount) = counts[i];
            if (paramCount < minParameters || connectorCount < minConnectors)
                continue;
            if (filtered)
                total++;
            if (items.Count < limit)
            {
                var node = toCount[i];
                items.Add(new InterfaceSearchItem(
                    node.Id, node.Name, node.ClassType, paramCount, connectorCount, node.HasExperimentAnnotation));
            }
        }

        return new InterfaceSearchResult(total, items.Count, items);
    }

    /// <summary>
    /// Counts classes' public parameters and connectors, inherited ones included, for one search: the
    /// base classes' interfaces and the types components are declared with are worked out once and
    /// shared by every class, from any thread.
    /// </summary>
    private sealed class InterfaceCounter(DirectedGraph graph)
    {
        private readonly ClassElementResolver.InterfaceCache _interfaces = new();

        // A component's type, keyed by the class that declares it and the type as written there: an
        // inherited component is declared in its base, so every class extending that base asks again.
        private readonly ConcurrentDictionary<(string Owner, string Type), string?> _typeClass = new();

        public (int Parameters, int Connectors) Count(ModelNode node)
        {
            var parameters = 0;
            var connectors = 0;
            foreach (var m in ClassElementResolver.Collect(graph, node, includeProtected: false, includeInherited: true, _interfaces))
            {
                if (m.Element.Kind != ClassElementKind.Component)
                    continue;
                if (m.Element.Causality is not null || ClassTypeOf(m) == "connector")
                    connectors++;
                else if (m.Element.Variability is "parameter" or "constant")
                    parameters++;
            }
            return (parameters, connectors);
        }

        private string? ClassTypeOf(ResolvedElement m) =>
            m.Element.Type is not { } type
                ? null
                : _typeClass.GetOrAdd((m.OwnerId, type),
                    key => TypeResolver.Resolve(graph, key.Owner, key.Type, m.OwnerImports)?.ClassType);
    }

    // The text around the first term found in it.
    private static string Snippet(string text, TextQuery query)
    {
        var flat = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        var idx = query.FirstIndexIn(flat);
        if (idx < 0)
            return flat.Length <= 160 ? flat : flat[..160] + "…";
        var start = Math.Max(0, idx - 40);
        var end = Math.Min(flat.Length, idx + 120);
        var snippet = flat[start..end];
        return (start > 0 ? "…" : "") + snippet + (end < flat.Length ? "…" : "");
    }
}
