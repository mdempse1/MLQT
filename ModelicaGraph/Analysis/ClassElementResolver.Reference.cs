using System.Text;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

namespace ModelicaGraph.Analysis;

/// <summary>
/// What a component reference such as <c>inertia1.flange_b.tau</c> names: the element each segment
/// resolved to, outermost first, and the class the last one's type resolves to.
/// </summary>
/// <param name="Path">
/// One element per segment. Each is the element as its declaring class wrote it, with the default
/// value a modification further out gave it where there is one - <c>Inertia inertia1(J = 2)</c> makes
/// <c>inertia1.J</c>'s default <c>2</c>. For a reference through a class (<c>Modelica.Constants.pi</c>)
/// only the component segments are here; the class they were found in is <see cref="Scope"/>.
/// </param>
/// <param name="Type">
/// The class the last segment's declared type resolves to, or null when that type is predefined
/// (<c>Real</c>, <c>Integer</c>...) or is not loaded. <c>Element.Element.Type</c> says which.
/// </param>
/// <param name="Scope">
/// Where the first segment of <see cref="Path"/> was found: the class the reference was written in,
/// one of its enclosing classes, or the class a qualified prefix named.
/// </param>
public sealed record ResolvedReference(IReadOnlyList<ResolvedElement> Path, ModelNode? Type, ModelNode Scope)
{
    /// <summary>The element the reference names - its last segment.</summary>
    public ResolvedElement Element => Path[^1];
}

public static partial class ClassElementResolver
{
    /// <summary>
    /// Resolves a component reference written in <paramref name="classNode"/> - <c>flange_a.tau</c>,
    /// <c>resistor[2].p.v</c>, <c>Modelica.Constants.pi</c> - to the element it names and that
    /// element's type, or null when any segment cannot be found.
    /// </summary>
    /// <remarks>
    /// <para><b>Lookup follows the language</b> (MLS §5.3): the first segment is looked for among the
    /// class's own elements, inherited ones included and protected ones too, since the reference is
    /// written inside the class; then among each enclosing class's elements, innermost first, which is
    /// how a constant declared in a package reaches the models in it. Every later segment is looked for
    /// among the <b>public</b> elements of the previous one's type, because a protected element is not
    /// reachable through a dot. Where the first segment names a class rather than a component, the
    /// longest prefix that resolves as a class (through <see cref="TypeResolver.ResolveWithInheritance"/>,
    /// imports and all) is taken as the scope and the rest are looked for in it. A leading dot is a
    /// global name and goes straight to that.</para>
    ///
    /// <para><b>Subscripts are dropped</b>, so an array element resolves to the array's declaration:
    /// for a type, a unit or a connector, every element of an array is the same. So is a reference's
    /// whitespace, outside a quoted identifier.</para>
    ///
    /// <para>A member of a short class (<c>model R2 = Resistor(R = 2)</c>) is looked for in its base.
    /// What is <b>not</b> modelled: a <c>redeclare</c> in a modification (the element keeps its
    /// declared type), and the modifications a short class definition applies to its base.</para>
    /// </remarks>
    /// <param name="interfaces">
    /// As for <see cref="Collect"/>, and more so: every class a reference passes through is remembered,
    /// because the same connector and component types are passed through by every equation that
    /// mentions them. Pass one whenever more than one reference is resolved.
    /// </param>
    /// <param name="ancestors">For the type resolution of each segment; see
    /// <see cref="TypeResolver.ResolveWithInheritance"/>.</param>
    public static ResolvedReference? ResolveReference(
        DirectedGraph graph, ModelNode classNode, string reference,
        InterfaceCache? interfaces = null, TypeResolver.AncestorCache? ancestors = null)
    {
        if (ReferenceSegments(reference) is not { } parsed)
            return null;
        var (segments, global) = parsed;

        if (!global)
        {
            // The class itself, then each enclosing class, innermost first - until the name is found
            // as a component (follow it) or as a class (a qualified name; resolved below).
            var scopes = new List<ModelNode> { classNode };
            var parts = classNode.Id.Split('.');
            for (var take = parts.Length - 1; take > 0; take--)
                if (graph.GetNode<ModelNode>(string.Join('.', parts.Take(take))) is { } enclosing)
                    scopes.Add(enclosing);

            foreach (var scope in scopes)
            {
                var first = Member(graph, scope, segments[0], includeProtected: true, interfaces);
                if (first is null)
                    continue;
                if (first.Element.Kind != ClassElementKind.Component)
                    break;
                return Follow(graph, scope, first, segments, interfaces, ancestors);
            }
        }

        // A class-qualified reference: the longest prefix that names a class, then its members.
        var imports = global ? null : ImportsOf(classNode, interfaces);
        for (var take = segments.Count - 1; take > 0; take--)
        {
            var prefix = string.Join('.', segments.Take(take));
            var scope = global
                ? graph.GetNode<ModelNode>(prefix)
                : TypeResolver.ResolveWithInheritance(graph, classNode.Id, prefix, imports, ancestors);
            if (scope is null)
                continue;

            var first = Member(graph, scope, segments[take], includeProtected: false, interfaces);
            return first is { Element.Kind: ClassElementKind.Component }
                ? Follow(graph, scope, first, segments.Skip(take).ToList(), interfaces, ancestors)
                : null;
        }

        return null;
    }

    // Walks the rest of a reference from its first component, each segment a public member of the
    // previous one's type.
    private static ResolvedReference? Follow(
        DirectedGraph graph, ModelNode scope, ResolvedElement first, IReadOnlyList<string> segments,
        InterfaceCache? interfaces, TypeResolver.AncestorCache? ancestors)
    {
        var path = new List<ResolvedElement> { first };
        var type = TypeOf(graph, first, ancestors);

        for (var i = 1; i < segments.Count; i++)
        {
            if (type is null)
                return null;
            var next = Member(graph, type, segments[i], includeProtected: false, interfaces);
            if (next is null || next.Element.Kind != ClassElementKind.Component)
                return null;

            path.Add(WithInstanceModification(next, path, segments, i));
            type = TypeOf(graph, next, ancestors);
        }

        return new ResolvedReference(path, type, scope);
    }

    // The named member of a class, inherited ones included. A short class has no members of its own -
    // its interface is empty - so its base's are looked for instead, reported as inherited from it.
    private static ResolvedElement? Member(
        DirectedGraph graph, ModelNode type, string name, bool includeProtected, InterfaceCache? interfaces)
    {
        var node = type;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var depth = 0; node is not null && depth <= MaxDepth && visited.Add(node.Id); depth++)
        {
            var hit = CollectRemembered(graph, node, includeProtected, interfaces)
                .FirstOrDefault(e => e.Element.Kind is ClassElementKind.Component or ClassElementKind.Class
                                     && string.Equals(e.Element.Name, name, StringComparison.Ordinal));
            if (hit is not null)
                return node == type ? hit : hit with { InheritedFrom = hit.InheritedFrom ?? node.Id };

            var shortBase = interfaces is null ? InterfaceCache.ReadShortBase(node) : interfaces.ShortBaseOf(node);
            node = shortBase is null
                ? null
                : TypeResolver.ResolveWithInheritance(graph, node.Id, shortBase, ImportsOf(node, interfaces));
        }

        return null;
    }

    // The class an element's declared type resolves to, in the scope of the class that declares it.
    // `Flange[2] fs` has the type text `Flange`: the extractor keeps array dimensions out of it.
    private static ModelNode? TypeOf(DirectedGraph graph, ResolvedElement element, TypeResolver.AncestorCache? ancestors) =>
        TypeResolver.ResolveWithInheritance(graph, element.OwnerId, element.Element.Type, element.OwnerImports, ancestors);

    // A modification written on a component further out sets this element's default: in
    // `Inertia inertia1(J = 2)`, `inertia1.J` defaults to 2 whatever Inertia declares. The outermost
    // modification wins, as it does in the language.
    private static ResolvedElement WithInstanceModification(
        ResolvedElement element, List<ResolvedElement> outer, IReadOnlyList<string> segments, int index)
    {
        for (var j = 0; j < outer.Count; j++)
        {
            var key = string.Join('.', segments.Skip(j + 1).Take(index - j));
            if (outer[j].Element.Modifications?.TryGetValue(key, out var value) == true)
                return element with { Element = element.Element with { DefaultValue = value } };
        }

        return element;
    }

    private static IReadOnlyList<string> ImportsOf(ModelNode node, InterfaceCache? interfaces)
    {
        var iface = interfaces is null ? InterfaceCache.Extract(node) : interfaces.Of(node, remember: true);
        return iface is null
            ? []
            : [.. iface.Elements.Where(e => e.Kind == ClassElementKind.Import).Select(e => e.Name)];
    }

    /// <summary>
    /// The segments of a component reference with subscripts and whitespace removed, and whether it
    /// was global (a leading dot). Null for text that is not a reference: an empty segment, an
    /// unbalanced bracket or an unterminated quoted identifier.
    /// </summary>
    internal static (List<string> Segments, bool Global)? ReferenceSegments(string? reference)
    {
        var text = reference?.Trim() ?? string.Empty;
        var global = text.StartsWith('.');
        if (global)
            text = text[1..];

        var segments = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                current.Append(c);
                if (c == '\\' && i + 1 < text.Length)
                    current.Append(text[++i]);
                else if (c == '\'')
                    quoted = false;
                continue;
            }

            switch (c)
            {
                case '[':
                    depth++;
                    break;
                case ']':
                    if (--depth < 0)
                        return null;
                    break;
                case '\'' when depth == 0:
                    quoted = true;
                    current.Append(c);
                    break;
                case '.' when depth == 0:
                    if (current.Length == 0)
                        return null;
                    segments.Add(current.ToString());
                    current.Clear();
                    break;
                default:
                    if (depth == 0 && !char.IsWhiteSpace(c))
                        current.Append(c);
                    break;
            }
        }

        if (quoted || depth != 0 || current.Length == 0)
            return null;
        segments.Add(current.ToString());
        return (segments, global);
    }
}
