using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Whether a class's new name would mean something else where a reference uses it - "captured" by an
/// element of that name met first on the way out. Asked before a rename rewrites the reference.
/// </summary>
/// <remarks>
/// <para>Renaming <c>Root.Src.Pkg</c> to <c>Box</c> rewrites <c>Pkg.State</c> in
/// <c>Root.Src.Other</c> as <c>Box.State</c>. If Other declares a <c>Box</c> of its own, that is
/// what <c>Box</c> now means there: the file parses, the rename reports success, and the
/// reference names a different class.</para>
/// <para><b>The lookup is walked as Modelica walks it</b> (MLS §5.3.1) - each scope's own elements,
/// then the ones it inherits, then its imports, from the reference outward - and stops at whichever
/// comes first: the place the renamed class would be found (no capture), or another element of the
/// new name (captured). Something of that name further out than the renamed class captures
/// nothing, which is why a top-level <c>Box</c> never blocks a rename.</para>
/// <para><b>It errs towards saying "captured"</b> where it cannot tell: an import of the new name in
/// a scope the renamed class is found before is reported. A refusal costs another choice of name;
/// a missed capture costs a model that silently means something else.</para>
/// <para><b>Make one per rename and ask it about every reference</b>: what one class answers depends
/// only on that class, the new name and the renamed class, and most references share the packages
/// around them. Asked afresh, every reference re-read every class on its way out - 67 ms each over
/// MSL's Blocks, so a package used from a few thousand places spent minutes on this before a rename
/// wrote anything.</para>
/// <para><b>One per rename, and not shared across threads</b>: what it remembers is kept in a plain
/// dictionary.</para>
/// <para><b>It also answers for a full name a move writes</b> (<see cref="ForFullNames"/>): there the
/// "renamed class" is the top-level class the name starts with, so anything of that name met on the
/// way out captures it - and an <c>encapsulated</c> class on the way out is a problem too, since
/// the name cannot reach the top through it.</para>
/// </remarks>
public sealed class NameCapture
{
    private readonly DirectedGraph _graph;
    private readonly string _newName;
    private readonly string _renamedId;
    private readonly string _parent;
    private readonly string _oldName;
    private readonly ClassElementResolver.InterfaceCache _interfaces;
    private readonly Dictionary<string, Verdict> _byScope = new(StringComparer.Ordinal);

    // What one class says on the way out: something of the new name (captured), the renamed class
    // (found), an encapsulated class (the lookup stops there), or nothing - look further out.
    private readonly record struct Verdict(bool Ends, string? CapturedBy, string? Encapsulated = null);

    private static readonly Verdict LookFurther = new(Ends: false, CapturedBy: null);
    private static readonly Verdict NotCaptured = new(Ends: true, CapturedBy: null);

    /// <param name="graph">The graph as it is before the rename.</param>
    /// <param name="newName">The renamed class's new simple name.</param>
    /// <param name="renamedId">The renamed class's id as it is now.</param>
    /// <param name="interfaces">Kept for the rename, as for <see cref="ClassElementResolver.Collect"/>.</param>
    public NameCapture(
        DirectedGraph graph, string newName, string renamedId, ClassElementResolver.InterfaceCache? interfaces = null)
    {
        _graph = graph;
        _newName = newName;
        _renamedId = renamedId;
        var dot = renamedId.LastIndexOf('.');
        _parent = dot >= 0 ? renamedId[..dot] : string.Empty;
        _oldName = renamedId[(dot + 1)..];
        _interfaces = interfaces ?? new ClassElementResolver.InterfaceCache();
    }

    /// <summary>
    /// What <paramref name="newName"/> would mean at <paramref name="scopeId"/> instead of the renamed
    /// class <paramref name="renamedId"/>; see <see cref="CapturedAt"/>. For one question - for many,
    /// make one <see cref="NameCapture"/> and ask it each.
    /// </summary>
    public static string? CapturedBy(
        DirectedGraph graph, string scopeId, string newName, string renamedId,
        ClassElementResolver.InterfaceCache? interfaces = null)
        => new NameCapture(graph, newName, renamedId, interfaces).CapturedAt(scopeId);

    /// <summary>
    /// A checker for full names starting with the top-level class <paramref name="topLevel"/> -
    /// what <c>move_class</c> writes - to ask with <see cref="FullNameProblemAt"/>.
    /// </summary>
    public static NameCapture ForFullNames(DirectedGraph graph, string topLevel) => new(graph, topLevel, topLevel);

    /// <summary>
    /// Why a full name starting with this checker's top-level class would not mean it at
    /// <paramref name="scopeId"/> - something of that name found first, or an encapsulated class the
    /// lookup cannot get past - or null when it does. For a checker made by <see cref="ForFullNames"/>.
    /// </summary>
    public string? FullNameProblemAt(string scopeId)
    {
        var parts = scopeId.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var take = parts.Length; take > 0; take--)
        {
            var verdict = VerdictOf(string.Join('.', parts.Take(take)));
            if (verdict.Encapsulated is { } sealedClass)
                return $"{sealedClass} is encapsulated, so nothing outside it - {_newName} included - is visible there";
            if (verdict.Ends)
                return verdict.CapturedBy;
        }
        return null;
    }

    /// <summary>
    /// What the new name would mean at <paramref name="scopeId"/> instead of the renamed class - an id,
    /// or "component x of Y" - or null when the lookup reaches the renamed class first.
    /// </summary>
    public string? CapturedAt(string scopeId)
    {
        var parts = scopeId.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var take = parts.Length; take > 0; take--)
        {
            var verdict = VerdictOf(string.Join('.', parts.Take(take)));
            if (verdict.Ends)
                return verdict.CapturedBy;
        }

        // The root: the renamed class, if it is a top-level one; otherwise it was never reached this
        // way, and the rename does not change what this reference finds.
        return null;
    }

    private Verdict VerdictOf(string scopeId)
    {
        if (!_byScope.TryGetValue(scopeId, out var verdict))
            _byScope[scopeId] = verdict = Decide(scopeId);
        return verdict;
    }

    private Verdict Decide(string scopeId)
    {
        if (_graph.GetNode<ModelNode>(scopeId) is not { } scope)
            return LookFurther;

        // 1. The scope's elements, its own and inherited ones alike: anything already called the new
        //    name - in the renamed class's own parent, that is a clash with the class itself.
        var element = Element(scope) ?? _graph.GetNode<ModelNode>($"{scopeId}.{_newName}")?.Id;
        var bases = ClassElementResolver.BaseClasses(_graph, scope);
        element ??= bases.Select(b => _graph.GetNode<ModelNode>($"{b.Id}.{_newName}")?.Id).FirstOrDefault(id => id is not null);
        if (element is not null)
            return new Verdict(Ends: true, element);

        // 2. The renamed class: here, if this is its parent or extends its parent.
        if (scopeId == _parent || bases.Any(b => b.Id == _parent))
            return NotCaptured;

        // 3. Its imports: one that brings in the renamed class is where it is found - its own clause is
        //    renamed with it - and one that brings in the new name is a capture.
        foreach (var import in ClassImports.For(scope.Definition))
        {
            if (TypeResolver.ResolveViaImport(_graph, import, _oldName)?.Id == _renamedId)
                return NotCaptured;
            if (TypeResolver.ResolveViaImport(_graph, import, _newName) is { } imported)
                return new Verdict(Ends: true, imported.Id);
        }

        return ClassImports.IsEncapsulated(scope.Definition)
            ? new Verdict(Ends: true, CapturedBy: null, Encapsulated: scopeId)
            : LookFurther;
    }

    // An element of the class called the new name, declared or inherited - a component, or a nested
    // class its interface lists - described for a message, or null.
    private string? Element(ModelNode scope)
    {
        var element = ClassElementResolver
            .Collect(_graph, scope, includeProtected: true, includeInherited: true, _interfaces)
            .FirstOrDefault(e => e.Element.Kind is ClassElementKind.Component or ClassElementKind.Class
                                 && string.Equals(e.Element.Name, _newName, StringComparison.Ordinal));
        return element is null
            ? null
            : element.Element.Kind == ClassElementKind.Component
                ? $"component {_newName} of {element.OwnerId}"
                : $"{element.OwnerId}.{_newName}";
    }
}
