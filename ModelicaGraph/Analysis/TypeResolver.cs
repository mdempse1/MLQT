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
    {
        if (global)
            return graph.GetNode<ModelNode>(name);

        // Start in the class's own scope and walk outward through the enclosing packages, trying
        // each one's classes - its own, then the ones it inherits - and then its imports; the root -
        // the name as written - comes last.
        var parts = ownerId.Split('.');
        for (var take = parts.Length; take > 0; take--)
        {
            var prefix = string.Join('.', parts.Take(take));
            if (graph.GetNode<ModelNode>($"{prefix}.{name}") is { } node)
                return node;

            if (inherited)
                foreach (var ancestorId in AncestorsOf(graph, prefix, ancestors))
                    if (graph.GetNode<ModelNode>($"{ancestorId}.{name}") is { } inheritedClass)
                        return inheritedClass;

            // The owner's own imports are the ones it was given, or - given none - its own, read once;
            // an enclosing package's are read from it. Null was once "no imports", which cost nothing
            // while the root was tried first, and since an encapsulated class stops the lookup it
            // turned every name such a class imports into an unresolved one.
            var scope = graph.GetNode<ModelNode>(prefix);
            var scopeImports = take == parts.Length && imports is not null
                ? imports
                : scope is not null ? ClassImports.For(scope.Definition) : null;
            if (scopeImports is not null)
                foreach (var import in scopeImports)
                    if (ResolveViaImport(graph, import, name) is { } viaImport)
                        return viaImport;

            // An encapsulated class is as far as lookup goes - not even the root is tried - which is
            // why such a class imports what it uses (MLS §5.3.1). The predefined types it may still
            // name were answered before the lookup began.
            if (scope is not null && ClassImports.IsEncapsulated(scope.Definition))
                return null;
        }

        return graph.GetNode<ModelNode>(name);
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
        => ancestors is null
            ? CollectAncestors(graph, scopeId)
            : ancestors.GetOrAdd(scopeId, id => CollectAncestors(graph, id));

    // The class's ancestors, nearest first down each extends clause in turn, so a class a nearer base
    // redeclares is found before the one it replaces. Built on ClassElementResolver.DirectBases, the
    // one reading of what a class extends (short classes included); BaseClasses has the same set in
    // drawing order, deepest first, which is the wrong way round for a lookup.
    private static IReadOnlyList<string> CollectAncestors(DirectedGraph graph, string classId)
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
            foreach (var (_, baseNode) in ClassElementResolver.DirectBases(graph, current))
            {
                if (!visited.Add(baseNode.Id))
                    continue;
                result.Add(baseNode.Id);
                Walk(baseNode, depth + 1);
            }
        }
    }

    internal static ModelNode? ResolveViaImport(DirectedGraph graph, string import, string name)
    {
        var stmt = import.Trim();

        // Alias: "SI = Modelica.Units.SI"
        var eq = stmt.IndexOf('=');
        if (eq >= 0)
        {
            var alias = stmt[..eq].Trim();
            var target = stmt[(eq + 1)..].Trim();
            // `import SI = Modelica.SIunits;` makes SI.Voltage mean Modelica.SIunits.Voltage:
            // the name is re-rooted from the alias onto the target, and is unresolvable here if it
            // is not under the alias at all.
            return ModelicaName.ReRoot(name, alias, target) is { } aliased
                ? graph.GetNode<ModelNode>(aliased)
                : null;
        }

        // Wildcard: "Modelica.Units.SI.*"
        if (stmt.EndsWith(".*", StringComparison.Ordinal))
            return graph.GetNode<ModelNode>($"{stmt[..^2]}.{name}");

        // Explicit list: "Modelica.Units.SI.{Voltage, Current}" is `import Modelica.Units.SI.Voltage;
        // import Modelica.Units.SI.Current;`, and makes those two names visible and no others. Read as
        // a wildcard it made every class in SI visible, so an enclosing package's list import captured
        // names that belong further out, and the rules disagreed with dependency analysis, whose
        // ReferenceResolver.AddImport has always expanded the list exactly (B348).
        var listIdx = stmt.IndexOf(".{", StringComparison.Ordinal);
        if (listIdx >= 0)
        {
            var package = stmt[..listIdx];
            var members = stmt[(listIdx + 2)..].TrimEnd('}')
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var member in members)
                if (ModelicaName.ReRoot(name, member, $"{package}.{member}") is { } listed)
                    return graph.GetNode<ModelNode>(listed);
            return null;
        }

        // Plain: "Modelica.Units.SI" — the last segment becomes the implicit alias.
        var lastDot = stmt.LastIndexOf('.');
        var leaf = lastDot >= 0 ? stmt[(lastDot + 1)..] : stmt;
        // `import Modelica.SIunits;` makes SIunits.Voltage mean Modelica.SIunits.Voltage - the
        // same re-rooting, with the statement's last segment standing in for the alias.
        return ModelicaName.ReRoot(name, leaf, stmt) is { } imported
            ? graph.GetNode<ModelNode>(imported)
            : null;
    }
}
