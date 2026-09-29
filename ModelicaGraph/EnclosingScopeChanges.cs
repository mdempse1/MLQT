using ModelicaGraph.DataTypes;
using ModelicaParser.StyleRules;

namespace ModelicaGraph;

/// <summary>
/// Which classes outside a set of reloaded files have to be re-analysed because a class in those
/// files, enclosing them, now declares different imports, classes or waivers.
///
/// <para>Since B292 a name is looked up through every enclosing scope's imports, so
/// <c>import Modelica.Units.SI;</c> in <c>P/package.mo</c> is what <c>SI.Time</c> means in
/// <c>P/Child.mo</c>. A reload re-analyses the classes in the files it reloaded and nothing else, so
/// adding or removing that import left <c>P.Child</c> with the edges and findings of the old one until
/// a full re-analysis (B347). Every incremental path hands its affected set to dependency analysis and
/// the style check, so widening the set here is what reaches them all.</para>
///
/// <para><b>The classes a scope declares are the other half of the same lookup (B387).</b> A simple
/// name is looked for in each enclosing scope before its imports are, so a new
/// <c>model Voltage</c> in <c>P/package.mo</c> — or a new file <c>P/Voltage.mo</c> — captures
/// <c>Voltage</c> in <c>P/Child.mo</c>, and removing it lets the name fall through to whatever it
/// meant before. So a class that appears or disappears in the reloaded files also widens the set: to
/// the classes in other files below its parent <em>whose own text mentions its name</em>. Only
/// those can have looked it up; widening to the whole subtree, as the import case does, would
/// re-analyse every class in a package each time a file was added to it. A class with no parent is
/// a library of its own, which is a load rather than a reload, and widens nothing.</para>
///
/// <para><b>So is what a scope waives (B499).</b> A class-level <c>__MLQT(suppress=…)</c> or
/// <c>spelling</c> reaches every class nested in the one carrying it (<see cref="ClassSuppressions.Enclosing"/>),
/// so adding <c>suppress="*"</c> to <c>P/package.mo</c> changes which findings <c>P/Child.mo</c> has.
/// A reloaded scope whose waivers reach its nested classes differently widens the set to the whole
/// subtree in other files, as a changed import does - every class below it filtered its findings
/// through them. Only the waivers that reach are compared
/// (<see cref="ModelicaParser.StyleRules.SuppressionSet.ReachesNestedClassesAs"/>), so an edit to a
/// top-level <c>package.mo</c> that leaves them alone re-checks nothing below it.</para>
///
/// <para>Use it in two steps around the reload: <see cref="Capture"/> before the old classes are
/// removed, <see cref="DescendantsToReanalyse"/> once the new ones are in.</para>
/// </summary>
public sealed class EnclosingScopeChanges
{
    private readonly HashSet<string> _fileIds;
    private readonly Dictionary<string, IReadOnlyList<string>> _importsBefore;
    private readonly Dictionary<string, SuppressionSet> _waiversBefore;
    private readonly Dictionary<string, List<string>> _descendantsBefore;
    private readonly HashSet<string> _classesBefore;

    private EnclosingScopeChanges(
        HashSet<string> fileIds,
        Dictionary<string, IReadOnlyList<string>> importsBefore,
        Dictionary<string, SuppressionSet> waiversBefore,
        Dictionary<string, List<string>> descendantsBefore,
        HashSet<string> classesBefore)
    {
        _fileIds = fileIds;
        _importsBefore = importsBefore;
        _waiversBefore = waiversBefore;
        _descendantsBefore = descendantsBefore;
        _classesBefore = classesBefore;
    }

    /// <summary>
    /// Records the imports and waivers of every class in <paramref name="fileIds"/> that encloses a
    /// class kept in another file. Call before the files' classes are removed from <paramref name="graph"/>.
    /// </summary>
    public static EnclosingScopeChanges Capture(DirectedGraph graph, IEnumerable<string> fileIds)
    {
        var files = new HashSet<string>(fileIds, StringComparer.Ordinal);
        var descendants = OutsideDescendants(graph, files);
        var imports = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var waivers = new Dictionary<string, SuppressionSet>(StringComparer.Ordinal);
        foreach (var scopeId in descendants.Keys)
            if (graph.GetNode<ModelNode>(scopeId) is { } scope)
            {
                imports[scopeId] = ClassImports.For(scope.Definition);
                waivers[scopeId] = ClassSuppressions.For(scope.Definition, scopeId);
            }
        return new EnclosingScopeChanges(files, imports, waivers, descendants, ClassesIn(graph, files));
    }

    /// <summary>
    /// The classes in other files below a reloaded class whose imports or reaching waivers differ from
    /// what <see cref="Capture"/> recorded - including a scope that appeared or disappeared with them -
    /// and the classes in other files below the parent of a class that appeared or disappeared in the
    /// reloaded files, where they mention its name. Only classes still in the graph are returned.
    /// </summary>
    public IReadOnlyList<string> DescendantsToReanalyse(DirectedGraph graph)
    {
        var descendantsAfter = OutsideDescendants(graph, _fileIds);
        var result = new HashSet<string>(StringComparer.Ordinal);

        AddThoseMentioningAChangedClass(graph, result);

        foreach (var scopeId in _descendantsBefore.Keys.Union(descendantsAfter.Keys, StringComparer.Ordinal))
        {
            var reloaded = graph.GetNode<ModelNode>(scopeId) is { } scope
                && scope.ContainingFileId is { } fileId && _fileIds.Contains(fileId)
                    ? scope
                    : null;

            var importsBefore = _importsBefore.GetValueOrDefault(scopeId) ?? [];
            var importsAfter = reloaded is null ? [] : ClassImports.For(reloaded.Definition);
            var waiversBefore = _waiversBefore.GetValueOrDefault(scopeId) ?? SuppressionSet.Empty;
            var waiversAfter = reloaded is null ? SuppressionSet.Empty : ClassSuppressions.For(reloaded.Definition, scopeId);
            if (importsBefore.SequenceEqual(importsAfter, StringComparer.Ordinal)
                && waiversBefore.ReachesNestedClassesAs(waiversAfter))
                continue;

            foreach (var id in _descendantsBefore.GetValueOrDefault(scopeId) ?? [])
                result.Add(id);
            foreach (var id in descendantsAfter.GetValueOrDefault(scopeId) ?? [])
                result.Add(id);
        }

        result.RemoveWhere(id => graph.GetNode<ModelNode>(id) is null);
        return [.. result.Order(StringComparer.Ordinal)];
    }

    // B387: a class that appeared or disappeared in the files changes what its simple name means in
    // every class below its parent. Those in the files are re-analysed with them; those elsewhere are
    // added here, where their own text mentions the name - nothing else can have looked it up.
    private void AddThoseMentioningAChangedClass(DirectedGraph graph, HashSet<string> result)
    {
        var classesAfter = ClassesIn(graph, _fileIds);
        var namesByParent = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var id in _classesBefore.Union(classesAfter, StringComparer.Ordinal))
        {
            if (_classesBefore.Contains(id) && classesAfter.Contains(id))
                continue;

            var dot = id.LastIndexOf('.');
            if (dot <= 0)
                continue;
            if (!namesByParent.TryGetValue(id[..dot], out var names))
                namesByParent[id[..dot]] = names = new HashSet<string>(StringComparer.Ordinal);
            names.Add(id[(dot + 1)..]);
        }
        if (namesByParent.Count == 0)
            return;

        foreach (var model in graph.ModelNodes)
        {
            if (model.ContainingFileId is { } fileId && _fileIds.Contains(fileId))
                continue;

            var id = model.Id;
            for (var dot = id.IndexOf('.'); dot > 0; dot = id.IndexOf('.', dot + 1))
            {
                if (namesByParent.TryGetValue(id[..dot], out var names)
                    && names.Any(name => Mentions(model.Definition.ModelicaCode, name)))
                {
                    result.Add(id);
                    break;
                }
            }
        }
    }

    /// <summary>Whether <paramref name="name"/> occurs in <paramref name="code"/> as a whole identifier.</summary>
    private static bool Mentions(string code, string name)
    {
        for (var at = code.IndexOf(name, StringComparison.Ordinal); at >= 0;
             at = code.IndexOf(name, at + 1, StringComparison.Ordinal))
        {
            var end = at + name.Length;
            if ((at == 0 || !IsIdentifierChar(code[at - 1])) && (end == code.Length || !IsIdentifierChar(code[end])))
                return true;
        }
        return false;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static HashSet<string> ClassesIn(DirectedGraph graph, HashSet<string> fileIds)
    {
        var classes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fileId in fileIds)
            foreach (var model in graph.GetModelsInFile(fileId))
                classes.Add(model.Id);
        return classes;
    }

    // Each class in the files that encloses a class held elsewhere, with those classes. One pass over
    // the graph: a class's enclosing scopes are the prefixes of its id.
    private static Dictionary<string, List<string>> OutsideDescendants(DirectedGraph graph, HashSet<string> fileIds)
    {
        var inFiles = ClassesIn(graph, fileIds);

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
