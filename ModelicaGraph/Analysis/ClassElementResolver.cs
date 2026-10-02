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
    IReadOnlyList<string> OwnerImports)
{
    /// <summary>
    /// The class whose modification set <see cref="ClassElement.DefaultValue"/> - a more-derived
    /// class's <c>extends</c> clause, a short class definition, or the class declaring a component
    /// further out along a reference - or null when the value is the declaration's own binding.
    ///
    /// <para><b>The value is an expression written in that class</b>, so that is where its names
    /// resolve: in <c>Inertia inertia1(J = Jb)</c>, <c>Jb</c> is the enclosing model's parameter, and
    /// looking it up in <see cref="OwnerId"/> (<c>Inertia</c>) finds nothing, or something else.</para>
    /// </summary>
    public string? ModifiedIn { get; init; }
}

/// <summary>
/// Collects the full element set of a class, following its <c>extends</c> clauses so inherited
/// parameters, connectors and other members are included — the complete picture the class presents,
/// not just what it declares directly. A derived declaration shadows a same-named inherited one, and
/// diamond inheritance is visited once. Imports and extends clauses themselves are reported only for
/// the queried class (they are not "inherited members"). Shared by the analyses and the MCP tooling.
/// </summary>
public static partial class ClassElementResolver
{
    private const int MaxDepth = 32;

    // A modification's value, and the class it is written in.
    private static readonly IReadOnlyDictionary<string, (string Value, string Scope)> NoMods =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal);

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
        private readonly ConcurrentDictionary<string, Lazy<ClassInterface?>> _interfaces = new(StringComparer.Ordinal);

        /// <summary>
        /// This class's interface, remembering it only when it is the kind that gets read again.
        /// </summary>
        /// <param name="remember">
        /// True for a class reached through an <c>extends</c> clause. <b>Only those are worth
        /// keeping</b>: a base class near the root of a library is on the chain of hundreds of
        /// others, while the class a caller asked about is walked exactly once per query, so its
        /// interface is stored and never read. Since callers ask about every class in a library and
        /// only some of them are base classes, remembering both was most of the memory for none of
        /// the saving (B147).
        ///
        /// <para>An already-remembered interface is returned either way: a base class asked about
        /// directly should not be re-derived just because this query entered through it.</para>
        /// </param>
        internal ClassInterface? Of(ModelNode node, bool remember)
        {
            // Already known - as a base class of something walked earlier - so this is free, whichever
            // way it was reached.
            if (_interfaces.TryGetValue(node.Id, out var known))
                return known.Value;

            // Lazy, and one per class: extracting is not safe to do twice at once on the same class.
            // ModelDefinition.Borrow parses the tree if it is absent and releases it again afterwards,
            // so two threads on one class race - the first to finish clears the tree while the second
            // is still reading it, and the loser gets an empty interface. That was invisible while
            // every walked class was cached, because the second thread read the first one's answer.
            var entry = _interfaces.GetOrAdd(
                node.Id,
                static (_, n) => new Lazy<ClassInterface?>(() => Extract(n), LazyThreadSafetyMode.ExecutionAndPublication),
                node);

            var iface = entry.Value;

            // Dropped again unless it is worth keeping, and only if this is still the entry we made:
            // another thread may have cached the same class as a base class in the meantime, and
            // removing that would throw away the one copy worth having.
            if (!remember)
                _interfaces.TryRemove(new KeyValuePair<string, Lazy<ClassInterface?>>(node.Id, entry));

            return iface;
        }

        /// <summary>
        /// How many interfaces are being held.
        /// </summary>
        /// <remarks>
        /// Public because what this cache costs is a property worth being able to ask about — it is
        /// the half of the trade that went unmeasured when it was first written (B147), and a caller
        /// resolving a very large library may want to know.
        /// </remarks>
        public int Count => _interfaces.Count;

        private readonly ConcurrentDictionary<(string Id, bool Protected), Lazy<Dictionary<string, ResolvedElement>>>
            _members = new();

        /// <summary>
        /// A class's members - components and nested classes, inherited ones included - by name, kept
        /// for the run. For the classes a component reference passes <em>through</em>: a connector or
        /// component type, or an enclosing package, which every equation mentioning it passes
        /// through again. Never for the class the references are written in, which is read once per
        /// <see cref="ComponentReferences"/> and is the one class a run must not keep (B147).
        /// </summary>
        internal Dictionary<string, ResolvedElement> MembersOf(DirectedGraph graph, ModelNode node, bool includeProtected) =>
            _members.GetOrAdd(
                (node.Id, includeProtected),
                key => new Lazy<Dictionary<string, ResolvedElement>>(
                    () => MemberTable(CollectRemembered(graph, node, key.Protected, this)),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;

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
             depth: 0, interfaces, rememberRoot: false);
        return result;
    }

    // Collect, for a class that is about to be asked again: a reference's intermediate types
    // (Inertia, Flange_a) are walked once for every reference that passes through them, so unlike
    // the class a caller asks about directly, they are worth keeping.
    private static List<ResolvedElement> CollectRemembered(
        DirectedGraph graph, ModelNode node, bool includeProtected, InterfaceCache? interfaces)
    {
        var result = new List<ResolvedElement>();
        Walk(graph, node, includeProtected, includeInherited: true, origin: null, NoMods, result,
             new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal),
             depth: 0, interfaces, rememberRoot: true);
        return result;
    }

    private static void Walk(
        DirectedGraph graph, ModelNode node, bool includeProtected, bool includeInherited,
        string? origin, IReadOnlyDictionary<string, (string Value, string Scope)> mods,
        List<ResolvedElement> result, HashSet<string> seen, HashSet<string> visited, int depth,
        InterfaceCache? interfaces, bool rememberRoot)
    {
        if (depth > MaxDepth || !visited.Add(node.Id))
            return;

        // Borrowed: the queried class is usually one the caller is holding a tree for and must keep,
        // while every base class up the chain is one this walk parsed and should hand back. See
        // ModelDefinition.Borrow. A ClassInterface is names and defaults - no parse tree - so keeping
        // one costs a fraction of what re-deriving it does.
        var iface = interfaces is null
            ? InterfaceCache.Extract(node)
            : interfaces.Of(node, remember: rememberRoot || origin is not null);
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
                    // A modification from a more-derived extends clause overrides this inherited
                    // default, and is an expression in the class that wrote it.
                    result.Add(e.Kind == ClassElementKind.Component && mods.TryGetValue(e.Name, out var m)
                        ? new ResolvedElement(e with { DefaultValue = m.Value }, origin, node.Id, imports)
                            { ModifiedIn = m.Scope }
                        : new ResolvedElement(e, origin, node.Id, imports));
                    break;
            }
        }

        if (!includeInherited)
            return;

        foreach (var (baseType, baseMods) in Bases(iface))
        {
            var baseNode = TypeResolver.Resolve(graph, node.Id, baseType, imports);
            if (baseNode is not null)
                Walk(graph, baseNode, includeProtected, includeInherited, baseNode.Id,
                    MergeMods(baseMods, node.Id, mods), result, seen, visited, depth + 1, interfaces,
                    rememberRoot);
        }
    }

    // What a class inherits from, with the modifications it applies: each extends clause, or - for
    // `model R2 = Resistor(R = 2)`, which has no elements of its own - the short class's base, which
    // is an extends clause in all but syntax (MLS §4.5.1).
    private static IEnumerable<(string? Type, IReadOnlyDictionary<string, string>? Modifications)> Bases(
        ClassInterface iface)
    {
        if (iface.ShortClassBase is { } shortBase)
            return [(shortBase, iface.ShortClassModifications)];
        return iface.Elements
            .Where(e => e.Kind == ClassElementKind.Extends)
            .Select(e => (e.Type, e.Modifications));
    }

    /// <summary>
    /// Every class <paramref name="node"/> inherits from, transitively, in the order their layers are
    /// drawn: each base after the bases it extends itself, and the first <c>extends</c> clause's
    /// before the second's. The class itself is not included, and a base reached twice (a diamond)
    /// is listed once. A clause whose base is not loaded is skipped.
    ///
    /// <para>For what a class inherits that is not an element: its Diagram layer and its
    /// <c>connect</c> equations (B316).</para>
    /// </summary>
    public static List<ModelNode> BaseClasses(DirectedGraph graph, ModelNode node)
    {
        var result = new List<ModelNode>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { node.Id };
        WalkBases(graph, node, result, visited, depth: 0);
        return result;
    }

    private static void WalkBases(
        DirectedGraph graph, ModelNode node, List<ModelNode> result, HashSet<string> visited, int depth)
    {
        if (depth > MaxDepth)
            return;

        foreach (var (_, baseNode) in DirectBases(graph, node))
        {
            if (!visited.Add(baseNode.Id))
                continue;
            WalkBases(graph, baseNode, result, visited, depth + 1);
            result.Add(baseNode);
        }
    }

    /// <summary>
    /// The classes <paramref name="node"/>'s own <c>extends</c> clauses name, in clause order, each
    /// with the name as written in the clause. A clause whose base is not loaded is skipped.
    ///
    /// <para>For a question the first clause answers differently from the others: which base lends
    /// a class its coordinate system (MLS 3.6 §18.6.1.1, B394).</para>
    /// </summary>
    public static List<(string Written, ModelNode Base)> DirectBases(DirectedGraph graph, ModelNode node)
    {
        var result = new List<(string, ModelNode)>();
        if (InterfaceCache.Extract(node) is not { } iface)
            return result;

        var imports = iface.Elements
            .Where(e => e.Kind == ClassElementKind.Import)
            .Select(e => e.Name)
            .ToList();

        foreach (var ext in iface.Elements.Where(e => e.Kind == ClassElementKind.Extends))
            if (TypeResolver.Resolve(graph, node.Id, ext.Type, imports) is { } baseNode)
                result.Add((ext.Type ?? string.Empty, baseNode));

        return result;
    }

    // Modifications applying to a base's members: this extends clause's, with any already-accumulated
    // (more-derived) modification winning on a key clash.
    // Each value is kept with the class that wrote it (`scope`), since that is where it resolves.
    private static IReadOnlyDictionary<string, (string Value, string Scope)> MergeMods(
        IReadOnlyDictionary<string, string>? baseMods, string scope,
        IReadOnlyDictionary<string, (string Value, string Scope)> moreDerived)
    {
        if (baseMods is null || baseMods.Count == 0)
            return moreDerived;
        var merged = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var kv in baseMods)
            merged[kv.Key] = (kv.Value, scope);
        foreach (var kv in moreDerived)
            merged[kv.Key] = kv.Value;
        return merged;
    }
}
