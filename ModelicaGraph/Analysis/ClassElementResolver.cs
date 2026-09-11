using System.Collections.Concurrent;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Visitors;

namespace ModelicaGraph.Analysis;

/// <summary>
/// One element of a class after inheritance is merged in: the raw element, the id of the class that
/// actually declares it (<see cref="OwnerId"/>) and that class's imports (so its type can be resolved
/// in the right scope), plus <see cref="InheritedFrom"/> — null when the element is declared in the
/// queried class itself, otherwise the base class it was inherited from.
/// </summary>
public sealed record ResolvedElement(
    ClassElement Element,
    string? InheritedFrom,
    string OwnerId,
    IReadOnlyList<string> OwnerImports);

/// <summary>
/// Collects the full element set of a class, following its <c>extends</c> clauses so inherited
/// parameters, connectors and other members are included — the complete picture the class presents,
/// not just what it declares directly. A derived declaration shadows a same-named inherited one, and
/// diamond inheritance is visited once. Imports and extends clauses themselves are reported only for
/// the queried class (they are not "inherited members"). Shared by the analyses and the MCP tooling.
/// </summary>
public static class ClassElementResolver
{
    private const int MaxDepth = 32;

    private static readonly IReadOnlyDictionary<string, string> NoMods =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Somewhere to keep each class's extracted interface for the length of a run.
    /// </summary>
    /// <remarks>
    /// <para><b>Pass one whenever this is called for more than one class.</b> Resolving a class means
    /// extracting the interface of every class it extends, and a base class is extracted again for
    /// every class that inherits it - which in a library means hundreds of times for the ones near
    /// the root. Each extraction parses the class if its tree has been released, which the checker
    /// does deliberately to bound memory, so the cost is a full parse.</para>
    ///
    /// <para>Measured over MSL: resolving inherited element names for the spell rules was <b>49% of
    /// the whole check</b>, 122 thread-seconds over 5,631 classes, and the only thing it was doing was
    /// this (B128).</para>
    ///
    /// <para>Explicitly per run rather than a static, because a class's source changes under the
    /// desktop application while it is open, and a cache that outlives the run would answer from
    /// before the edit.</para>
    /// </remarks>
    public sealed class InterfaceCache
    {
        private readonly ConcurrentDictionary<string, ClassInterface?> _interfaces = new(StringComparer.Ordinal);

        internal ClassInterface? Of(ModelNode node) =>
            _interfaces.GetOrAdd(node.Id, static (_, n) => Extract(n), node);

        internal static ClassInterface? Extract(ModelNode node) =>
            node.Definition.Borrow<ClassInterface?>(ClassInterfaceExtractor.Extract);
    }

    public static List<ResolvedElement> Collect(
        DirectedGraph graph, ModelNode node, bool includeProtected, bool includeInherited,
        InterfaceCache? interfaces = null)
    {
        var result = new List<ResolvedElement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Walk(graph, node, includeProtected, includeInherited, origin: null, NoMods, result, seen, visited,
             depth: 0, interfaces);
        return result;
    }

    private static void Walk(
        DirectedGraph graph, ModelNode node, bool includeProtected, bool includeInherited,
        string? origin, IReadOnlyDictionary<string, string> mods,
        List<ResolvedElement> result, HashSet<string> seen, HashSet<string> visited, int depth,
        InterfaceCache? interfaces)
    {
        if (depth > MaxDepth || !visited.Add(node.Id))
            return;

        // Borrowed: the queried class is usually one the caller is holding a tree for and must keep,
        // while every base class up the chain is one this walk parsed and should hand back. See
        // ModelDefinition.Borrow. A ClassInterface is names and defaults - no parse tree - so keeping
        // one costs a fraction of what re-deriving it does.
        var iface = interfaces is null ? InterfaceCache.Extract(node) : interfaces.Of(node);
        if (iface is null)
            return;

        var imports = iface.Elements
            .Where(e => e.Kind == ClassElementKind.Import)
            .Select(e => e.Name)
            .ToList();
        var inherited = origin is not null;

        foreach (var e in iface.Elements)
        {
            switch (e.Kind)
            {
                case ClassElementKind.Import:
                case ClassElementKind.Extends:
                    // Imports and extends belong to the queried class only.
                    if (!inherited && seen.Add($"{e.Kind}|{e.Name}"))
                        result.Add(new ResolvedElement(e, null, node.Id, imports));
                    break;

                default: // Component or nested Class
                    if (!e.IsPublic && !includeProtected)
                        break;
                    if (!seen.Add($"{e.Kind}|{e.Name}")) // derived (added first) shadows inherited
                        break;
                    // A modification from a more-derived extends clause overrides this inherited default.
                    var element = e.Kind == ClassElementKind.Component && mods.TryGetValue(e.Name, out var v)
                        ? e with { DefaultValue = v }
                        : e;
                    result.Add(new ResolvedElement(element, origin, node.Id, imports));
                    break;
            }
        }

        if (!includeInherited)
            return;

        foreach (var ext in iface.Elements.Where(e => e.Kind == ClassElementKind.Extends))
        {
            var baseNode = TypeResolver.Resolve(graph, node.Id, ext.Type, imports);
            if (baseNode is not null)
                Walk(graph, baseNode, includeProtected, includeInherited, baseNode.Id,
                    MergeMods(ext.Modifications, mods), result, seen, visited, depth + 1, interfaces);
        }
    }

    // Modifications applying to a base's members: this extends clause's, with any already-accumulated
    // (more-derived) modification winning on a key clash.
    private static IReadOnlyDictionary<string, string> MergeMods(
        IReadOnlyDictionary<string, string>? baseMods, IReadOnlyDictionary<string, string> moreDerived)
    {
        if (baseMods is null || baseMods.Count == 0)
            return moreDerived;
        if (moreDerived.Count == 0)
            return baseMods;
        var merged = new Dictionary<string, string>(baseMods, StringComparer.Ordinal);
        foreach (var kv in moreDerived)
            merged[kv.Key] = kv.Value;
        return merged;
    }
}
