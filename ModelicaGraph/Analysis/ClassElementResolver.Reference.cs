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

    /// <summary>
    /// How many leading segments of the reference name <see cref="Scope"/>: 2 for
    /// <c>Modelica.Constants.pi</c>, 0 when the first segment is itself a component.
    /// </summary>
    public int QualifierSegments { get; init; }
}

/// <summary>
/// Where a component reference starts: the class its first component was found in, and how many
/// leading segments of the reference named that class - 0 when the first segment is a component of
/// the class the reference is written in, or a constant an enclosing class lends it.
/// </summary>
/// <remarks>
/// <b>A use of the class even when the rest goes nowhere.</b> <c>Modelica.Constants.pi</c> uses
/// <c>Modelica.Constants</c> whatever <c>pi</c>'s type turns out to be, which is why dependency
/// analysis and the reference locator ask this rather than <see cref="ComponentReferences.Resolve"/>:
/// it never walks a component's type, and it answers where the whole reference would not.
/// </remarks>
public sealed record ReferenceStart(ModelNode Scope, int QualifierSegments);

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
        if (ClassElementResolver.ReferenceSegments(reference) is not { } parsed
            || Locate(parsed.Segments, parsed.Global) is not { } start)
            return null;

        return Follow(start.Scope, start.First, parsed.Segments.Skip(start.Qualifier).ToList()) is { } resolved
            ? resolved with { QualifierSegments = start.Qualifier }
            : null;
    }

    /// <summary>
    /// Where <paramref name="reference"/> starts - the class its first component is found in, and how
    /// many leading segments named that class - or null when it starts nowhere: an unknown name or a
    /// built-in one. Found exactly as <see cref="Resolve"/> finds it, but without walking the rest.
    /// </summary>
    /// <remarks>
    /// <b>A class followed by something that is not one of its components still starts there</b>:
    /// the longest prefix naming a class is the start. <c>Types.Init.SteadyState</c> is an
    /// enumeration literal, which no interface lists, and it uses <c>Types.Init</c> all the same -
    /// renaming <c>Init</c> has to rewrite it. Never when the first segment is a component, of the
    /// class or lent by an enclosing one: <c>inertia1.phi</c> does not become a use of a class that
    /// happens to be called <c>inertia1</c>.
    /// </remarks>
    public ReferenceStart? Start(string reference)
    {
        if (ClassElementResolver.ReferenceSegments(reference) is not { } parsed)
            return null;
        if (Locate(parsed.Segments, parsed.Global) is { } start)
            return new ReferenceStart(start.Scope, start.Qualifier);
        if (!parsed.Global
            && (ModelicaLanguage.IsBuiltInName(parsed.Segments[0]) || StartsWithComponent(parsed.Segments[0])))
            return null;

        // A class by its whole name is not a reference through one.
        if (QualifyingClass(string.Join('.', parsed.Segments), parsed.Global) is not null)
            return null;

        for (var take = parsed.Segments.Count - 1; take > 0; take--)
            if (QualifyingClass(string.Join('.', parsed.Segments.Take(take)), parsed.Global) is { } scope)
                return new ReferenceStart(scope, take);
        return null;
    }

    // Whether a reference's first segment is a component in reach - the class's own, inherited ones
    // included, or one an enclosing class declares.
    private bool StartsWithComponent(string first)
    {
        if (_own.Value.Members.TryGetValue(first, out var own))
            return own.Element.Kind == ClassElementKind.Component;
        foreach (var scope in EnclosingScopes())
            if (Members(scope, includeProtected: true).TryGetValue(first, out var found))
                return found.Element.Kind == ClassElementKind.Component;
        return false;
    }

    // The class a reference's leading segments name, found as a type name is.
    private ModelNode? QualifyingClass(string prefix, bool global) => global
        ? _graph.GetNode<ModelNode>(prefix)
        : TypeResolver.ResolveWithInheritance(_graph, _class.Id, prefix, _own.Value.Imports, _ancestors);

    // The scope a reference's first component is found in, that component, and how many leading
    // segments named the scope.
    private (ModelNode Scope, ResolvedElement First, int Qualifier)? Locate(List<string> segments, bool global)
    {
        if (!global)
        {
            if (_own.Value.Members.TryGetValue(segments[0], out var own))
                return own.Element.Kind == ClassElementKind.Component
                    ? (_class, own, 0)
                    : LocateQualified(segments, global);

            // The language's own names - `time`, `Connections.branch` - are no class's elements, and
            // the first segment decides, as it does for whole-name lookup: a class that happens to be
            // called Connections is not what `Connections.branch` uses. Asked before the enclosing
            // packages, because walking them to find that out was the cost of the commonest
            // reference in an equation.
            if (ModelicaLanguage.IsBuiltInName(segments[0]))
                return null;

            foreach (var scope in EnclosingScopes())
            {
                if (!Members(scope, includeProtected: true).TryGetValue(segments[0], out var found))
                    continue;
                if (found.Element.Kind != ClassElementKind.Component)
                    break;
                // An enclosing class lends its constants and nothing else (MLS §5.3.1).
                return found.Element.Variability == "constant" ? (scope, found, 0) : null;
            }
        }

        return LocateQualified(segments, global);
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

    // Read once per class and kept on it, as its imports are: every reference that leaves the class
    // asks this of the class and of each package around it.
    private static bool IsEncapsulated(ModelNode node) => ClassImports.IsEncapsulated(node.Definition);

    // A class-qualified reference: the longest prefix that names a class, then its members.
    private (ModelNode Scope, ResolvedElement First, int Qualifier)? LocateQualified(List<string> segments, bool global)
    {
        for (var take = segments.Count - 1; take > 0; take--)
        {
            if (QualifyingClass(string.Join('.', segments.Take(take)), global) is not { } scope)
                continue;

            return Members(scope, includeProtected: false).TryGetValue(segments[take], out var first)
                   && first.Element.Kind == ClassElementKind.Component
                ? (scope, first, take)
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
    // modification wins, as it does in the language. At each component, what a more-derived extends
    // clause set below it (`extends Base(inertia1.J = 9)`) outranks what its own declaration says,
    // and each is an expression in the class that wrote it.
    private static ResolvedElement WithInstanceModification(
        ResolvedElement element, List<ResolvedElement> outer, IReadOnlyList<string> segments, int index)
    {
        for (var j = 0; j < outer.Count; j++)
        {
            var key = string.Join('.', segments.Skip(j + 1).Take(index - j));
            if (outer[j].ModificationsBelow?.TryGetValue(key, out var derived) == true)
                return Modified(element, derived.Value, derived.Scope);
            if (outer[j].Element.Modifications?.TryGetValue(key, out var value) == true)
                return Modified(element, value, outer[j].OwnerId);
        }

        return element;
    }

    private static ResolvedElement Modified(ResolvedElement element, string value, string scope) =>
        element with { Element = element.Element with { DefaultValue = value }, ModifiedIn = scope };
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
