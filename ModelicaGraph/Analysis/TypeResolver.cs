using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Visitors;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Best-effort resolution of a type name written inside a class to the <see cref="ModelNode"/> it
/// refers to in a <see cref="DirectedGraph"/>. Tries an exact match, then the class's own imports, then
/// a relative lookup up the package hierarchy — mirroring how the dependency analyzer resolves
/// references. <see cref="Resolve"/> intentionally does NOT model names inherited through <c>extends</c>
/// (a documented limitation shared with dependency analysis), so an unresolved result is "not found by
/// these rules", not a guarantee the type is undefined; <see cref="ResolveWithInheritance"/> adds the
/// ancestor scopes. Shared by the analyses (metrics, shadowing) and the MCP tooling.
/// </summary>
public static class TypeResolver
{
    private static readonly HashSet<string> PredefinedTypes = new(StringComparer.Ordinal)
    {
        "Real", "Integer", "Boolean", "String", "Complex", "Clock"
    };

    /// <summary>True for the Modelica built-in/predefined types, which are never library classes.</summary>
    public static bool IsPredefined(string? typeName)
        => typeName is not null && PredefinedTypes.Contains(typeName.TrimStart('.').Trim());

    /// <summary>
    /// Resolve <paramref name="typeText"/> (as written in class <paramref name="ownerId"/>) to a class in
    /// the graph, or null if these rules cannot resolve it. <paramref name="imports"/> are the class's
    /// import statements (from the interface extractor), used to expand aliases/wildcards.
    /// </summary>
    public static ModelNode? Resolve(
        DirectedGraph graph, string ownerId, string? typeText, IReadOnlyList<string>? imports = null)
    {
        if (string.IsNullOrWhiteSpace(typeText))
            return null;

        var name = typeText.TrimStart('.').Trim();
        if (name.Length == 0 || IsPredefined(name))
            return null;

        // 1. Already fully-qualified.
        if (graph.GetNode<ModelNode>(name) is { } exact)
            return exact;

        // 2. Via the class's own imports.
        if (imports is not null)
            foreach (var import in imports)
                if (ResolveViaImport(graph, import, name) is { } viaImport)
                    return viaImport;

        // 3. Relative: start in the class's own scope and walk outward through enclosing packages.
        var parts = ownerId.Split('.');
        for (var take = parts.Length; take >= 0; take--)
        {
            var prefix = string.Join('.', parts.Take(take));
            var candidate = prefix.Length == 0 ? name : $"{prefix}.{name}";
            if (graph.GetNode<ModelNode>(candidate) is { } node)
                return node;
        }

        return null;
    }

    /// <summary>The ancestors of a class, as <see cref="ResolveWithInheritance"/> caches them.</summary>
    public sealed class AncestorCache
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary
            <string, List<(string Id, IReadOnlyList<string> Imports)>> _byClass = new(StringComparer.Ordinal);

        internal List<(string Id, IReadOnlyList<string> Imports)> GetOrAdd(
            string classId, Func<string, List<(string Id, IReadOnlyList<string> Imports)>> collect)
            => _byClass.GetOrAdd(classId, collect);
    }

    /// <summary>
    /// Like <see cref="Resolve"/> but also resolves names inherited into scope through <c>extends</c>:
    /// after trying the class's own scope, it tries each ancestor's scope (its package hierarchy, its
    /// imports and its nested classes). Used so an inherited type name is not wrongly reported as
    /// unresolved.
    /// </summary>
    /// <param name="ancestors">
    /// Somewhere to remember each class's extends chain for the duration of a run, or null to walk it
    /// afresh every time. <b>Pass one for anything that resolves more than a handful of names.</b>
    /// Collecting the chain reads each ancestor's interface, which parses it if nobody else is
    /// holding its tree — so without this, a class with twenty unresolved type names re-parses its
    /// whole ancestry twenty times. Measured over the Modelica Standard Library with
    /// <c>MissingUnits</c> on, that walk was <b>82% of the rule's time</b> and the rule was 91% of
    /// the check; caching it took the rule from 295.4s to 129.7s of thread-time with all 5,250
    /// findings identical (B174).
    ///
    /// <para>It is a parameter rather than something kept on the graph because it is only valid
    /// while the classes it describes are unchanged — the lifetime of a check, not of the graph,
    /// which is reloaded after a formatting pass or a VCS operation.</para>
    /// </param>
    public static ModelNode? ResolveWithInheritance(
        DirectedGraph graph, string classId, string? typeText, IReadOnlyList<string>? imports,
        AncestorCache? ancestors = null)
    {
        if (Resolve(graph, classId, typeText, imports) is { } direct)
            return direct;
        if (string.IsNullOrWhiteSpace(typeText) || IsPredefined(typeText))
            return null;

        var chain = ancestors is null
            ? CollectAncestors(graph, classId)
            : ancestors.GetOrAdd(classId, id => CollectAncestors(graph, id));

        foreach (var (ancestorId, ancestorImports) in chain)
            if (Resolve(graph, ancestorId, typeText, ancestorImports) is { } viaAncestor)
                return viaAncestor;
        return null;
    }

    // The class's ancestors (via extends), each with its own imports, so a name can be resolved in the
    // scope it is inherited from. Depth-guarded against cycles/diamonds.
    private static List<(string Id, IReadOnlyList<string> Imports)> CollectAncestors(
        DirectedGraph graph, string classId)
    {
        var result = new List<(string, IReadOnlyList<string>)>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { classId };

        void Walk(string id, int depth)
        {
            if (depth > 32)
                return;
            // Borrowed, not kept: this walks up an extends chain, so every class it reaches beyond
            // the first is one nobody else asked for. See ModelDefinition.Borrow.
            var iface = graph.GetNode<ModelNode>(id)?.Definition
                .Borrow<ClassInterface?>(ClassInterfaceExtractor.Extract);
            if (iface is null)
                return;
            var imports = iface.Elements.Where(e => e.Kind == ClassElementKind.Import).Select(e => e.Name).ToList();
            foreach (var ext in iface.Elements.Where(e => e.Kind == ClassElementKind.Extends))
            {
                var baseNode = Resolve(graph, id, ext.Type, imports);
                if (baseNode is null || !visited.Add(baseNode.Id))
                    continue;
                var baseImports = baseNode.Definition
                    .Borrow<IReadOnlyList<string>>(
                        tree => ClassInterfaceExtractor.Extract(tree).Elements
                            .Where(e => e.Kind == ClassElementKind.Import).Select(e => e.Name).ToList(),
                        [])
                    .ToList();
                result.Add((baseNode.Id, baseImports));
                Walk(baseNode.Id, depth + 1);
            }
        }

        Walk(classId, 0);
        return result;
    }

    private static ModelNode? ResolveViaImport(DirectedGraph graph, string import, string name)
    {
        var stmt = import.Trim();

        // Alias: "SI = Modelica.Units.SI"
        var eq = stmt.IndexOf('=');
        if (eq >= 0)
        {
            var alias = stmt[..eq].Trim();
            var target = stmt[(eq + 1)..].Trim();
            if (name == alias)
                return graph.GetNode<ModelNode>(target);
            if (name.StartsWith(alias + ".", StringComparison.Ordinal))
                return graph.GetNode<ModelNode>(target + name[alias.Length..]);
            return null;
        }

        // Wildcard: "Modelica.Units.SI.*"
        if (stmt.EndsWith(".*", StringComparison.Ordinal))
            return graph.GetNode<ModelNode>($"{stmt[..^2]}.{name}");

        // Explicit list: "Modelica.Units.SI.{Voltage, Current}"
        var listIdx = stmt.IndexOf(".{", StringComparison.Ordinal);
        if (listIdx >= 0)
            return graph.GetNode<ModelNode>($"{stmt[..listIdx]}.{name}");

        // Plain: "Modelica.Units.SI" — the last segment becomes the implicit alias.
        var lastDot = stmt.LastIndexOf('.');
        var leaf = lastDot >= 0 ? stmt[(lastDot + 1)..] : stmt;
        if (name == leaf)
            return graph.GetNode<ModelNode>(stmt);
        if (name.StartsWith(leaf + ".", StringComparison.Ordinal))
            return graph.GetNode<ModelNode>(stmt + name[leaf.Length..]);
        return null;
    }
}
