using ModelicaGraph.DataTypes;

namespace ModelicaGraph;

/// <summary>
/// Which classes outside a set of reloaded files have to be re-analysed because a class in those
/// files, enclosing them, now declares different imports.
///
/// <para>Since B292 a name is looked up through every enclosing scope's imports, so
/// <c>import Modelica.Units.SI;</c> in <c>P/package.mo</c> is what <c>SI.Time</c> means in
/// <c>P/Child.mo</c>. A reload re-analyses the classes in the files it reloaded and nothing else, so
/// adding or removing that import left <c>P.Child</c> with the edges and findings of the old one until
/// a full re-analysis (B347). Every incremental path hands its affected set to dependency analysis and
/// the style check, so widening the set here is what reaches them all.</para>
///
/// <para>Use it in two steps around the reload: <see cref="Capture"/> before the old classes are
/// removed, <see cref="DescendantsToReanalyse"/> once the new ones are in.</para>
/// </summary>
public sealed class EnclosingImportChanges
{
    private readonly HashSet<string> _fileIds;
    private readonly Dictionary<string, IReadOnlyList<string>> _importsBefore;
    private readonly Dictionary<string, List<string>> _descendantsBefore;

    private EnclosingImportChanges(
        HashSet<string> fileIds,
        Dictionary<string, IReadOnlyList<string>> importsBefore,
        Dictionary<string, List<string>> descendantsBefore)
    {
        _fileIds = fileIds;
        _importsBefore = importsBefore;
        _descendantsBefore = descendantsBefore;
    }

    /// <summary>
    /// Records the imports of every class in <paramref name="fileIds"/> that encloses a class kept in
    /// another file. Call before the files' classes are removed from <paramref name="graph"/>.
    /// </summary>
    public static EnclosingImportChanges Capture(DirectedGraph graph, IEnumerable<string> fileIds)
    {
        var files = new HashSet<string>(fileIds, StringComparer.Ordinal);
        var descendants = OutsideDescendants(graph, files);
        var imports = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var scopeId in descendants.Keys)
            if (graph.GetNode<ModelNode>(scopeId) is { } scope)
                imports[scopeId] = ClassImports.For(scope.Definition);
        return new EnclosingImportChanges(files, imports, descendants);
    }

    /// <summary>
    /// The classes in other files below a reloaded class whose imports differ from what
    /// <see cref="Capture"/> recorded - including a scope that appeared or disappeared with imports.
    /// Only classes still in the graph are returned.
    /// </summary>
    public IReadOnlyList<string> DescendantsToReanalyse(DirectedGraph graph)
    {
        var descendantsAfter = OutsideDescendants(graph, _fileIds);
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var scopeId in _descendantsBefore.Keys.Union(descendantsAfter.Keys, StringComparer.Ordinal))
        {
            var before = _importsBefore.GetValueOrDefault(scopeId) ?? [];
            var after = graph.GetNode<ModelNode>(scopeId) is { } scope
                && scope.ContainingFileId is { } fileId && _fileIds.Contains(fileId)
                    ? ClassImports.For(scope.Definition)
                    : [];
            if (before.SequenceEqual(after, StringComparer.Ordinal))
                continue;

            foreach (var id in _descendantsBefore.GetValueOrDefault(scopeId) ?? [])
                result.Add(id);
            foreach (var id in descendantsAfter.GetValueOrDefault(scopeId) ?? [])
                result.Add(id);
        }

        result.RemoveWhere(id => graph.GetNode<ModelNode>(id) is null);
        return [.. result.Order(StringComparer.Ordinal)];
    }

    // Each class in the files that encloses a class held elsewhere, with those classes. One pass over
    // the graph: a class's enclosing scopes are the prefixes of its id.
    private static Dictionary<string, List<string>> OutsideDescendants(DirectedGraph graph, HashSet<string> fileIds)
    {
        var inFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fileId in fileIds)
            foreach (var model in graph.GetModelsInFile(fileId))
                inFiles.Add(model.Id);

        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (inFiles.Count == 0)
            return result;

        foreach (var model in graph.ModelNodes)
        {
            if (model.ContainingFileId is { } fileId && fileIds.Contains(fileId))
                continue;

            var id = model.Id;
            for (var dot = id.IndexOf('.'); dot > 0; dot = id.IndexOf('.', dot + 1))
            {
                var prefix = id[..dot];
                if (!inFiles.Contains(prefix))
                    continue;
                if (!result.TryGetValue(prefix, out var list))
                    result[prefix] = list = [];
                list.Add(id);
            }
        }
        return result;
    }
}
