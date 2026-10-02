using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.DataTypes;
using ModelicaParser.Visitors;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Best-effort resolution of a type name written inside a class to the <see cref="ModelNode"/> it
/// refers to in a <see cref="DirectedGraph"/>: scope by scope from the class outward, each scope's
/// classes and then its imports, the root last. <see cref="Resolve"/> does not look among inherited
/// classes - it is the lookup for an <c>extends</c> clause's own base name, which may not use what that
/// clause brings in - and <see cref="ResolveWithInheritance"/>, which dependency analysis uses for every
/// other name, does. Either way an unresolved result is "not found by these rules", not a guarantee the
/// type is undefined: a <c>redeclare</c> is not modelled. Shared by the analyses (metrics, shadowing)
/// and the MCP tooling.
/// </summary>
public static class TypeResolver
{
    /// <summary>
    /// <see cref="ModelicaLanguage.PredefinedTypes"/> plus <c>Complex</c>, which is not a language
    /// type at all: it is an operator record in MSL, written like a predefined type everywhere and
    /// resolvable as a class only when MSL is loaded. The addition is the whole difference between
    /// this list and the language's own, and is stated here rather than by keeping a second copy.
    /// </summary>
    private static readonly HashSet<string> PredefinedTypes =
        new(ModelicaLanguage.PredefinedTypes.Append("Complex"), StringComparer.Ordinal);

    /// <summary>True for the Modelica built-in/predefined types, which are never library classes.</summary>
    public static bool IsPredefined(string? typeName)
        => typeName is not null && PredefinedTypes.Contains(typeName.TrimStart('.').Trim());

    /// <summary>
    /// Resolve <paramref name="typeText"/> (as written in class <paramref name="ownerId"/>) to a class in
    /// the graph, or null if these rules cannot resolve it. <paramref name="imports"/> are the class's
    /// import statements (from the interface extractor), used to expand aliases/wildcards; null reads
    /// the class's own, so pass them only when you have them already.
    /// </summary>
    public static ModelNode? Resolve(
        DirectedGraph graph, string ownerId, string? typeText, IReadOnlyList<string>? imports = null)
    {
        if (string.IsNullOrWhiteSpace(typeText))
            return null;

        var trimmed = typeText.Trim();
        var name = trimmed.TrimStart('.');
        if (name.Length == 0 || IsPredefined(name))
            return null;

        return ResolveName(graph, ownerId, name, imports, global: trimmed.StartsWith('.'));
    }

    /// <summary>
    /// The lookup itself, for a name already known not to be predefined: <see cref="Resolve"/> and
    /// dependency analysis's <c>ReferenceResolver</c> both come here, so they cannot disagree about
    /// what a name refers to.
    /// </summary>
    /// <remarks>
    /// <para><b>Each enclosing scope's imports are visible, not only the class's own.</b> Modelica looks
    /// a name up in each enclosing class in turn — its elements, then its imports — so
    /// <c>import Modelica.Units.SI;</c> in <c>Modelica.Blocks</c> is what <c>SI.Time</c> means in every
    /// block below it. Asking only the class that wrote the name left MSL's controllers without edges to
    /// the types they use (B292). The enclosing classes' imports come from
    /// <see cref="ClassImports.For"/>, which reads each package once.</para>
    /// <para><b>The innermost scope that has the name wins</b>, and a name matched at the root - as
    /// written, fully qualified - is the last thing tried, not the first. In <c>Lib.Examples.Drive</c>,
    /// <c>Constants.pi</c> means <c>Lib.Constants</c> when there is one, even if a top-level
    /// <c>Constants</c> is loaded too; trying the name as written first answered with the top-level
    /// one. Within each scope its own classes come before its imports, as the language has them.</para>
    /// <para><b>Lookup stops at an <c>encapsulated</c> class</b>, after its own classes and imports:
    /// nothing outside it, the root included, is visible from inside it.</para>
    /// </remarks>
    /// <param name="global">
    /// True for a name written with a leading dot (<paramref name="name"/> is without it), which
    /// Modelica looks up from the top and nowhere else. Now that the root is tried last, losing the
    /// dot would let a nearer class of the same name answer for it.
    /// </param>
    /// <param name="inherited">
    /// True to look among each scope's <b>inherited</b> classes too, between its own and its imports,
    /// which is where Modelica looks (MLS §5.3.1): the replaceable <c>Medium</c> a base declares is
    /// what <c>Medium</c> means in a model extending it, whatever its package holds. False for an
    /// <c>extends</c> clause's own base name, which is looked up without the inherited elements it is
    /// about to bring in.
    /// </param>
    /// <param name="ancestors">Where <paramref name="inherited"/> lookups keep each scope's bases for
    /// the run; see <see cref="ResolveWithInheritance"/>.</param>
    internal static ModelNode? ResolveName(
        DirectedGraph graph, string ownerId, string name, IReadOnlyList<string>? imports, bool global = false,
        bool inherited = false, AncestorCache? ancestors = null)
        => ResolveNamePath(graph, ownerId, name, imports, global, inherited, ancestors)?.Node;

    /// <summary>
    /// An <c>extends</c> clause's base name, as written in <paramref name="ownerId"/>: looked up without
    /// the class's own inherited classes - the ones the clause is bringing in - and with everything
    /// else, the inherited classes of the packages around it included.
    /// </summary>
    internal static ModelNode? ResolveBase(
        DirectedGraph graph, string ownerId, string? typeText, IReadOnlyList<string>? imports, AncestorCache? ancestors)
    {
        if (string.IsNullOrWhiteSpace(typeText))
            return null;
        var trimmed = typeText.Trim();
        var name = trimmed.TrimStart('.');
        return name.Length == 0 || IsPredefined(name)
            ? null
            : ResolveName(graph, ownerId, name, imports, global: trimmed.StartsWith('.'), inherited: false, ancestors);
    }

    /// <summary>
    /// The lookup, saying how it got there: the class each segment of <paramref name="name"/> names,
    /// and how the first was bound. Null when the name resolves to no class.
    /// </summary>
    /// <remarks>
    /// <para><b>The first segment is looked up; every later one is a member of the class before
    /// it</b> (MLS §5.3.2) - nested in it, or in one of its bases, a short class's included. So
    /// <c>Medium.ThermodynamicState</c>, where <c>Medium</c> is
    /// <c>replaceable package Medium = Modelica.Media.Interfaces.PartialMedium</c>, is
    /// <c>PartialMedium.ThermodynamicState</c>: <c>Base.Medium.ThermodynamicState</c> is no class
    /// anyone declared. Matching the whole name as one id at each scope found nothing for that, and -
    /// worse - went on looking further out, where a package's own <c>Medium</c> answered for it.
    /// <b>Once the first segment is found, the search is over</b>: if the rest is not there, the name
    /// resolves to nothing, as the language has it.</para>
    /// <para>Who asks what the first segment was bound to: the reference locator, so that a rename
    /// leaves an alias as written and a move leaves an inherited or imported name alone.</para>
    /// </remarks>
    internal static NameResolution? ResolveNamePath(
        DirectedGraph graph, string ownerId, string name, IReadOnlyList<string>? imports, bool global,
        bool inherited, AncestorCache? ancestors)
    {
        var segments = ModelicaName.Segments(name);
        if (segments.Count == 0 || segments.Any(s => s.Length == 0))
            return null;

        if (global)
            return Descend(graph, segments[0], NameBinding.Global, segments, ancestors);

        if (BindFirst(graph, ownerId, segments[0], imports, inherited, ancestors) is { } bound)
            return Descend(graph, bound.Id, bound.Binding, segments, ancestors);

        // The first segment is no class in the graph at all - a package not loaded, or a graph built
        // without its packages - so match the whole name at each scope, as an id, the way the lookup
        // always did before it went a segment at a time.
        return WholeName(graph, ownerId, name, segments, ancestors);
    }

    // The rest of a name, a member at a time, from the class its first segment is bound to. A package
    // on the way that the graph does not hold is stepped over by id: only the class at the end has to
    // be there.
    private static NameResolution? Descend(
        DirectedGraph graph, string firstId, NameBinding binding, IReadOnlyList<string> segments, AncestorCache? ancestors)
    {
        var path = new List<string>(segments.Count) { firstId };
        for (var i = 1; i < segments.Count; i++)
        {
            var current = path[^1];
            var next = $"{current}.{segments[i]}";
            if (graph.GetNode<ModelNode>(next) is null && graph.GetNode<ModelNode>(current) is { } owner)
            {
                if (MemberClass(graph, owner, segments[i], ancestors) is not { } inheritedClass)
                    return null;
                next = inheritedClass.Id;
            }
            path.Add(next);
        }
        return graph.GetNode<ModelNode>(path[^1]) is { } node ? new NameResolution(node, path, binding) : null;
    }

    // A name whose first segment is no class anywhere, matched whole at each scope from the class
    // outward, the root last.
    private static NameResolution? WholeName(
        DirectedGraph graph, string ownerId, string name, IReadOnlyList<string> segments, AncestorCache? ancestors)
    {
        foreach (var scopeId in ScopesOf(ownerId))
        {
            if (graph.GetNode<ModelNode>($"{scopeId}.{name}") is not null)
                return Descend(graph, $"{scopeId}.{segments[0]}", NameBinding.Own, segments, ancestors);
            if (graph.GetNode<ModelNode>(scopeId) is { } scope && ClassImports.IsEncapsulated(scope.Definition))
                return null;
        }
        return Descend(graph, segments[0], NameBinding.Root, segments, ancestors);
    }

    /// <summary>
    /// The class called <paramref name="name"/> inside <paramref name="owner"/>: nested in it, or
    /// inherited from one of its bases, nearest first - a short class's base included.
    /// </summary>
    internal static ModelNode? MemberClass(DirectedGraph graph, ModelNode owner, string name, AncestorCache? ancestors)
    {
        if (graph.GetNode<ModelNode>($"{owner.Id}.{name}") is { } nested)
            return nested;
        foreach (var ancestorId in AncestorsOf(graph, owner.Id, ancestors))
            if (graph.GetNode<ModelNode>($"{ancestorId}.{name}") is { } inheritedClass)
                return inheritedClass;
        return null;
    }

    // Where a first segment is found: from the class outward, each scope's own classes, then the ones
    // it inherits, then its imports; an encapsulated scope ends the search; the root comes last.
    private static (string Id, NameBinding Binding)? BindFirst(
        DirectedGraph graph, string ownerId, string first, IReadOnlyList<string>? imports, bool inherited,
        AncestorCache? ancestors)
    {
        foreach (var scopeId in ScopesOf(ownerId))
        {
            if (graph.GetNode<ModelNode>($"{scopeId}.{first}") is { } own)
                return (own.Id, NameBinding.Own);

            // An extends clause's base name is looked up without the inherited classes that clause is
            // bringing in - its own class's - but with every enclosing scope's (MLS §5.6.1).
            if (inherited || scopeId != ownerId)
                foreach (var ancestorId in AncestorsOf(graph, scopeId, ancestors))
                    if (graph.GetNode<ModelNode>($"{ancestorId}.{first}") is { } inheritedClass)
                        return (inheritedClass.Id, NameBinding.Inherited);

            // The owner's own imports are the ones it was given, or - given none - its own, read once;
            // an enclosing package's are read from it. Null was once "no imports", which cost nothing
            // while the root was tried first, and since an encapsulated class stops the lookup it
            // turned every name such a class imports into an unresolved one.
            var scope = graph.GetNode<ModelNode>(scopeId);
            var scopeImports = scopeId == ownerId && imports is not null
                ? imports
                : scope is not null ? ClassImports.For(scope.Definition) : null;
            if (scopeImports is not null && BindViaImports(graph, scopeImports, first, ancestors) is { } imported)
                return imported;

            // An encapsulated class is as far as lookup goes - not even the root is tried - which is
            // why such a class imports what it uses (MLS §5.3.1). The predefined types it may still
            // name were answered before the lookup began.
            if (scope is not null && ClassImports.IsEncapsulated(scope.Definition))
                return null;
        }

        return graph.GetNode<ModelNode>(first) is { } root ? (root.Id, NameBinding.Root) : null;
    }

    // A class and the classes enclosing it, innermost first.
    private static IEnumerable<string> ScopesOf(string classId)
    {
        if (classId.Length == 0)
            yield break;
        yield return classId;
        foreach (var enclosing in ModelicaName.EnclosingNamesOf(classId))
            yield return enclosing;
    }

    /// <summary>
    /// What one scope's imports make <paramref name="first"/> mean: a qualified or alias import before
    /// any wildcard, as MLS §5.3.1 orders them - declaration order said <c>import A.*; import B.X;</c>
    /// made <c>X</c> <c>A.X</c>.
    /// </summary>
    internal static (string Id, NameBinding Binding)? BindViaImports(
        DirectedGraph graph, IReadOnlyList<string> imports, string first, AncestorCache? ancestors)
    {
        foreach (var import in imports)
            if (!IsWildcard(import) && BindViaImport(graph, import, first, ancestors) is { } qualified)
                return qualified;
        foreach (var import in imports)
            if (IsWildcard(import) && BindViaImport(graph, import, first, ancestors) is { } unqualified)
                return unqualified;
        return null;
    }

    /// <summary>True for an unqualified import, <c>import A.B.*;</c>.</summary>
    internal static bool IsWildcard(string import)
    {
        var statement = import.Trim();
        return !statement.Contains('=') && statement.EndsWith(".*", StringComparison.Ordinal);
    }

    // What one import clause makes a first segment mean, and how. Its target is a name from the top,
    // resolved like any other - so a class an imported package inherits is in reach too.
    private static (string Id, NameBinding Binding)? BindViaImport(
        DirectedGraph graph, string import, string first, AncestorCache? ancestors)
    {
        var statement = import.Trim();

        // Alias: "SI = Modelica.Units.SI"
        var eq = statement.IndexOf('=');
        if (eq >= 0)
            return string.Equals(statement[..eq].Trim(), first, StringComparison.Ordinal)
                ? (GlobalId(graph, statement[(eq + 1)..].Trim(), ancestors), NameBinding.AliasImport)
                : null;

        // Wildcard: "Modelica.Units.SI.*"
        if (statement.EndsWith(".*", StringComparison.Ordinal))
            return ResolveGlobal(graph, $"{statement[..^2]}.{first}", ancestors) is { } member
                ? (member.Id, NameBinding.WildcardImport)
                : null;

        // Explicit list: "Modelica.Units.SI.{Voltage, Current}" is `import Modelica.Units.SI.Voltage;
        // import Modelica.Units.SI.Current;`, and makes those two names visible and no others. Read as
        // a wildcard it made every class in SI visible, so an enclosing package's list import captured
        // names that belong further out (B348).
        var listIdx = statement.IndexOf(".{", StringComparison.Ordinal);
        if (listIdx >= 0)
        {
            var members = statement[(listIdx + 2)..].TrimEnd('}')
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return members.Contains(first, StringComparer.Ordinal)
                ? (GlobalId(graph, $"{statement[..listIdx]}.{first}", ancestors), NameBinding.Import)
                : null;
        }

        // Plain: "Modelica.Units.SI" - its last segment is the name it brings in.
        return string.Equals(ModelicaName.LeafOf(statement), first, StringComparison.Ordinal)
            ? (GlobalId(graph, statement, ancestors), NameBinding.Import)
            : null;
    }

    // The id a name from the top means: the class it resolves to, or - where the graph does not hold
    // that class, a package not loaded - the name as written, for the rest of a name to go on from.
    // A wildcard does not use it: a clause that merely could bring a name in must not claim it.
    private static string GlobalId(DirectedGraph graph, string name, AncestorCache? ancestors)
        => ResolveGlobal(graph, name, ancestors)?.Id ?? name.Trim().TrimStart('.');

    /// <summary>A name from the top - what an import clause, or a name with a leading dot, means.</summary>
    internal static ModelNode? ResolveGlobal(DirectedGraph graph, string name, AncestorCache? ancestors = null)
        => ResolveNamePath(graph, string.Empty, name.Trim().TrimStart('.'), imports: null, global: true,
            inherited: false, ancestors)?.Node;

    /// <summary>
    /// What <paramref name="import"/> makes <paramref name="name"/> mean - its first segment brought in
    /// by the clause, the rest members of that class - or null when the clause does not reach it.
    /// </summary>
    internal static ModelNode? ResolveViaImport(DirectedGraph graph, string import, string name)
    {
        var segments = ModelicaName.Segments(name);
        return segments.Count > 0 && BindViaImport(graph, import, segments[0], ancestors: null) is { } bound
            ? Descend(graph, bound.Id, bound.Binding, segments, ancestors: null)?.Node
            : null;
    }

    /// <summary>The ancestors of a class, as <see cref="ResolveWithInheritance"/> caches them.</summary>
    public sealed class AncestorCache
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<string>> _byClass =
            new(StringComparer.Ordinal);

        internal IReadOnlyList<string> GetOrAdd(string classId, Func<string, IReadOnlyList<string>> collect)
            => _byClass.GetOrAdd(classId, collect);
    }

    /// <summary>
    /// Like <see cref="Resolve"/> but also finds the classes a class inherits - each ancestor's
    /// <b>nested classes</b>, as <c>Medium.ThermodynamicState</c> written in a model whose base
    /// declares <c>Medium</c> - and finds them <b>where Modelica looks</b>: after the class's own
    /// classes and before its imports and enclosing packages, at every scope on the way out. Looked
    /// for last, a package's own <c>Medium</c> answered for the one the base declares.
    /// </summary>
    /// <remarks>
    /// <para><b>An ancestor's nested classes, and nothing else of its scope.</b> A class inherits its
    /// bases' elements; it does not inherit the packages around them or their imports (MLS §5.6,
    /// §13.2.1). This once tried each ancestor's whole scope, so a model extending
    /// <c>Lib.Base</c> from another package could name <c>Constants.pi</c> and mean
    /// <c>Lib.Constants</c> - and an <c>encapsulated</c> class could reach past itself through its
    /// base's package. A name written in a base is resolved in the base, by asking with the base's
    /// id: that is the question <see cref="ResolvedElement.OwnerId"/> exists to answer.</para>
    /// </remarks>
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
        if (string.IsNullOrWhiteSpace(typeText))
            return null;

        var trimmed = typeText.Trim();
        var name = trimmed.TrimStart('.');
        if (name.Length == 0 || IsPredefined(name))
            return null;

        return ResolveName(graph, classId, name, imports, global: trimmed.StartsWith('.'), inherited: true, ancestors);
    }

    // A scope's ancestors, through the run's cache when there is one. Cheap without it too: what each
    // class extends is read once and kept on the class (ClassImports.BasesOf).
    private static IReadOnlyList<string> AncestorsOf(DirectedGraph graph, string scopeId, AncestorCache? ancestors)
    {
        // Finding a class's bases resolves their names, which looks at the bases of the scopes around
        // it - so a cycle through inheritance and enclosing scopes would come back here for the same
        // class. It gets no bases on the way round, and nothing is remembered from that answer.
        var collecting = _collecting ??= new HashSet<string>(StringComparer.Ordinal);
        if (!collecting.Add(scopeId))
            return [];
        try
        {
            return ancestors is null
                ? CollectAncestors(graph, scopeId, ancestors)
                : ancestors.GetOrAdd(scopeId, id => CollectAncestors(graph, id, ancestors));
        }
        finally
        {
            collecting.Remove(scopeId);
        }
    }

    [ThreadStatic]
    private static HashSet<string>? _collecting;

    // The class's ancestors, nearest first down each extends clause in turn, so a class a nearer base
    // redeclares is found before the one it replaces. Built on ClassElementResolver.DirectBases, the
    // one reading of what a class extends (short classes included); BaseClasses has the same set in
    // drawing order, deepest first, which is the wrong way round for a lookup.
    private static IReadOnlyList<string> CollectAncestors(DirectedGraph graph, string classId, AncestorCache? ancestors)
    {
        var result = new List<string>();
        if (graph.GetNode<ModelNode>(classId) is not { } node)
            return result;

        var visited = new HashSet<string>(StringComparer.Ordinal) { classId };
        Walk(node, 0);
        return result;

        void Walk(ModelNode current, int depth)
        {
            if (depth > 32)
                return;
            foreach (var (_, baseNode) in ClassElementResolver.DirectBases(graph, current, ancestors))
            {
                if (!visited.Add(baseNode.Id))
                    continue;
                result.Add(baseNode.Id);
                Walk(baseNode, depth + 1);
            }
        }
    }

}
