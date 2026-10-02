using System.Text;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;

namespace ModelicaGraph.Analysis;

/// <summary>
/// What a component reference such as <c>inertia1.flange_b.tau</c> names: the element each segment
/// resolved to, outermost first, and the class the last one's type resolves to.
/// </summary>
/// <param name="Path">
/// One element per segment. Each is the element as its declaring class wrote it, with the default
/// value a modification further out gave it where there is one - <c>Inertia inertia1(J = 2)</c> makes
/// <c>inertia1.J</c>'s default <c>2</c> - and <see cref="ResolvedElement.ModifiedIn"/> naming the class
/// that expression is written in. For a reference through a class (<c>Modelica.Constants.pi</c>) only
/// the component segments are here; the class they were found in is <see cref="Scope"/>.
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

/// <summary>
/// Resolves the component references written in one class. Make one per class and ask it about every
/// reference in that class: it reads the class's own members once and keeps them for as long as it
/// is held, and nothing longer - see <see cref="ClassElementResolver.ReferencesIn"/>.
/// </summary>
public sealed class ComponentReferences
{
    private readonly DirectedGraph _graph;
    private readonly ModelNode _class;
    private readonly ClassElementResolver.InterfaceCache? _interfaces;
    private readonly TypeResolver.AncestorCache? _ancestors;
    private readonly Lazy<(Dictionary<string, ResolvedElement> Members, IReadOnlyList<string> Imports)> _own;

    internal ComponentReferences(
        DirectedGraph graph, ModelNode classNode,
        ClassElementResolver.InterfaceCache? interfaces, TypeResolver.AncestorCache? ancestors)
    {
        _graph = graph;
        _class = classNode;
        _interfaces = interfaces;
        _ancestors = ancestors;
        _own = new Lazy<(Dictionary<string, ResolvedElement>, IReadOnlyList<string>)>(() =>
        {
            // The class's own members, protected included, read without remembering the class - only
            // its bases go into the run's cache, as for any other Collect.
            var all = ClassElementResolver.Collect(graph, classNode, includeProtected: true, includeInherited: true, interfaces);
            var imports = all.Where(e => e.Element.Kind == ClassElementKind.Import).Select(e => e.Element.Name).ToList();
            return (ClassElementResolver.MemberTable(all), imports);
        });
    }

    /// <summary>
    /// The element <paramref name="reference"/> names and its type, or null when any segment cannot
    /// be found.
    /// </summary>
    /// <remarks>
    /// <para><b>Lookup follows the language</b> (MLS §5.3): the first segment is looked for among the
    /// class's own elements, inherited ones included and protected ones too, since the reference is
    /// written inside the class; then among each enclosing class's elements, innermost first, which is
    /// how a constant declared in a package reaches the models in it. <b>Only a constant is found that
    /// way</b> - anything else found in an enclosing class is not visible, and the reference does not
    /// resolve - and the search stops at an <c>encapsulated</c> class. Every later segment is looked
    /// for among the <b>public</b> elements of the previous one's type, because a protected element is
    /// not reachable through a dot. Where the first segment names a class rather than a component, the
    /// longest prefix that resolves as a class (through <see cref="TypeResolver.ResolveWithInheritance"/>,
    /// imports and all) is taken as the scope and the rest are looked for in it. A leading dot is a
    /// global name and goes straight to that.</para>
    ///
    /// <para><b>Subscripts are dropped</b>, so an array element resolves to the array's declaration:
    /// for a type, a unit or a connector, every element of an array is the same. So is a reference's
    /// whitespace, outside a quoted identifier.</para>
    ///
    /// <para><b>A built-in variable resolves to null</b>, as an unknown name does: <c>time</c> is
    /// declared by the language, not by any class, so there is no element to return. Ask
    /// <see cref="ModelicaLanguage.IsBuiltInName"/> to tell the two apart.</para>
    ///
    /// <para>A short class (<c>model R2 = Resistor(R = 2)</c>) has its base's members, with its
    /// modifications applied. What is <b>not</b> modelled is a <c>redeclare</c> in a modification:
    /// the element keeps its declared type.</para>
    /// </remarks>
    public ResolvedReference? Resolve(string reference)
    {
        if (ClassElementResolver.ReferenceSegments(reference) is not { } parsed)
            return null;
        var (segments, global) = parsed;

        if (!global)
        {
            if (_own.Value.Members.TryGetValue(segments[0], out var own))
                return own.Element.Kind == ClassElementKind.Component
                    ? Follow(_class, own, segments)
                    : ResolveQualified(segments, global);

            // `time` is no class's element, and walking every enclosing package to find that out
            // is the cost of the commonest reference in an equation.
            if (segments.Count == 1 && ModelicaLanguage.IsBuiltInName(segments[0]))
                return null;

            foreach (var scope in EnclosingScopes())
            {
                if (!Members(scope, includeProtected: true).TryGetValue(segments[0], out var found))
                    continue;
                if (found.Element.Kind != ClassElementKind.Component)
                    break;
                // An enclosing class lends its constants and nothing else (MLS §5.3.1).
                return found.Element.Variability == "constant" ? Follow(scope, found, segments) : null;
            }
        }

        return ResolveQualified(segments, global);
    }

    // The classes enclosing this one, innermost first, as far as the first encapsulated one -
    // none at all when the class itself is encapsulated.
    private IEnumerable<ModelNode> EnclosingScopes()
    {
        if (IsEncapsulated(_class))
            yield break;

        var parts = _class.Id.Split('.');
        for (var take = parts.Length - 1; take > 0; take--)
        {
            if (_graph.GetNode<ModelNode>(string.Join('.', parts.Take(take))) is not { } enclosing)
                continue;
            yield return enclosing;
            if (IsEncapsulated(enclosing))
                yield break;
        }
    }

    // The class itself is read without being remembered; an enclosing package is shared by every
    // class inside it, so it is.
    private bool IsEncapsulated(ModelNode node) =>
        (_interfaces is null
            ? ClassElementResolver.InterfaceCache.Extract(node)
            : _interfaces.Of(node, remember: node != _class))?.IsEncapsulated == true;

    // A class-qualified reference: the longest prefix that names a class, then its members.
    private ResolvedReference? ResolveQualified(List<string> segments, bool global)
    {
        for (var take = segments.Count - 1; take > 0; take--)
        {
            var prefix = string.Join('.', segments.Take(take));
            var scope = global
                ? _graph.GetNode<ModelNode>(prefix)
                : TypeResolver.ResolveWithInheritance(_graph, _class.Id, prefix, _own.Value.Imports, _ancestors);
            if (scope is null)
                continue;

            return Members(scope, includeProtected: false).TryGetValue(segments[take], out var first)
                   && first.Element.Kind == ClassElementKind.Component
                ? Follow(scope, first, segments.Skip(take).ToList())
                : null;
        }

        return null;
    }

    // Walks the rest of a reference from its first component, each segment a public member of the
    // previous one's type.
    private ResolvedReference? Follow(ModelNode scope, ResolvedElement first, IReadOnlyList<string> segments)
    {
        var path = new List<ResolvedElement> { first };
        var type = TypeOf(first);

        for (var i = 1; i < segments.Count; i++)
        {
            if (type is null
                || !Members(type, includeProtected: false).TryGetValue(segments[i], out var next)
                || next.Element.Kind != ClassElementKind.Component)
                return null;

            path.Add(WithInstanceModification(next, path, segments, i));
            type = TypeOf(next);
        }

        return new ResolvedReference(path, type, scope);
    }

    private Dictionary<string, ResolvedElement> Members(ModelNode node, bool includeProtected) =>
        _interfaces is null
            ? ClassElementResolver.MemberTable(
                ClassElementResolver.Collect(_graph, node, includeProtected, includeInherited: true))
            : _interfaces.MembersOf(_graph, node, includeProtected);

    // The class an element's declared type resolves to, in the scope of the class that declares it.
    // `Flange[2] fs` has the type text `Flange`: the extractor keeps array dimensions out of it.
    private ModelNode? TypeOf(ResolvedElement element) =>
        TypeResolver.ResolveWithInheritance(_graph, element.OwnerId, element.Element.Type, element.OwnerImports, _ancestors);

    // A modification written on a component further out sets this element's default: in
    // `Inertia inertia1(J = 2)`, `inertia1.J` defaults to 2 whatever Inertia declares. The outermost
    // modification wins, as it does in the language, and is an expression in the class that declared
    // the component it was written on.
    private static ResolvedElement WithInstanceModification(
        ResolvedElement element, List<ResolvedElement> outer, IReadOnlyList<string> segments, int index)
    {
        for (var j = 0; j < outer.Count; j++)
        {
            var key = string.Join('.', segments.Skip(j + 1).Take(index - j));
            if (outer[j].Element.Modifications?.TryGetValue(key, out var value) == true)
                return element with
                {
                    Element = element.Element with { DefaultValue = value },
                    ModifiedIn = outer[j].OwnerId
                };
        }

        return element;
    }
}

public static partial class ClassElementResolver
{
    /// <summary>
    /// What resolves the component references written in <paramref name="classNode"/>. Hold it while
    /// resolving every reference in that class, then let it go: it keeps the class's own members
    /// for its lifetime, while the classes references pass through - connector and component types,
    /// enclosing packages - go into <paramref name="interfaces"/> for the run.
    /// </summary>
    /// <param name="interfaces">
    /// As for <see cref="Collect"/>, and more so: the same connector and component types are passed
    /// through by every equation that mentions them, so their members are kept for the run. Pass one
    /// whenever more than one class's references are resolved.
    /// </param>
    /// <param name="ancestors">For the type resolution of each segment; see
    /// <see cref="TypeResolver.ResolveWithInheritance"/>.</param>
    public static ComponentReferences ReferencesIn(
        DirectedGraph graph, ModelNode classNode,
        InterfaceCache? interfaces = null, TypeResolver.AncestorCache? ancestors = null)
        => new(graph, classNode, interfaces, ancestors);

    /// <summary>
    /// Resolves one component reference written in <paramref name="classNode"/>; see
    /// <see cref="ComponentReferences.Resolve"/>. For several references in one class, use
    /// <see cref="ReferencesIn"/>, which reads the class once rather than once per reference.
    /// </summary>
    public static ResolvedReference? ResolveReference(
        DirectedGraph graph, ModelNode classNode, string reference,
        InterfaceCache? interfaces = null, TypeResolver.AncestorCache? ancestors = null)
        => ReferencesIn(graph, classNode, interfaces, ancestors).Resolve(reference);

    // Components and nested classes by name. Collect lists the more-derived declaration first, and
    // that is the one a name means.
    internal static Dictionary<string, ResolvedElement> MemberTable(IEnumerable<ResolvedElement> elements)
    {
        var table = new Dictionary<string, ResolvedElement>(StringComparer.Ordinal);
        foreach (var e in elements)
            if (e.Element.Kind is ClassElementKind.Component or ClassElementKind.Class)
                table.TryAdd(e.Element.Name, e);
        return table;
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
