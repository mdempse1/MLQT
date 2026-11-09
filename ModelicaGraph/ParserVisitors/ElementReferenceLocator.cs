using Antlr4.Runtime.Tree;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser;

namespace ModelicaGraph;

/// <summary>What an <see cref="ElementSite"/> is: how the renamed element is written there.</summary>
public enum ElementSiteKind
{
    /// <summary>The element's own declaration: <c>parameter Real Fz</c> in the class declaring it.</summary>
    Declaration,

    /// <summary>A reference that reaches it: <c>Fz</c>, <c>wheel.Fz</c>, <c>axle.wheel.Fz</c>.</summary>
    Reference,

    /// <summary>A modification that sets it: <c>wheel(Fz = 1)</c>, <c>extends Wheel(Fz = 1)</c>.</summary>
    Modification,

    /// <summary>A named argument that passes it, to a function's input or a record's constructor.</summary>
    NamedArgument,
}

/// <summary>
/// One place an element's name is written as that element, with the identifier's span in the parsed
/// text, the class it is written in, and the <c>for</c> iterators in force there - a name an iterator
/// declares would take over a reference renamed to it.
/// </summary>
public sealed record ElementSite(
    ElementSiteKind Kind,
    NameSegment Name,
    string ScopeId,
    int Line,
    IReadOnlyList<string> Iterators);

/// <summary>
/// Locates every place in a parsed file that names one element - a component, parameter or constant,
/// identified by the class that declares it and its name - so a rename can rewrite exactly those
/// identifiers and nothing else that happens to be spelled the same.
/// </summary>
/// <remarks>
/// <para><b>Decided by what each name resolves to, never by its text.</b> A reference is resolved in
/// the class it is written in (<see cref="ComponentReferences"/>), and a segment is a site only when
/// it resolves to an element named <see cref="Name"/> declared in <see cref="DeclaringClassId"/> - which
/// is what an inherited copy of it is too. So <c>Fz</c> in a class that declares a <c>Fz</c> of its
/// own, <c>other.Fz</c> on a type unrelated to the declaring class, a class called <c>FzTable</c> and
/// an iterator called <c>Fz</c> are all left alone.</para>
///
/// <para><b>Where it is found</b>: its declaration; references in equations, algorithms, bindings,
/// conditions, subscripts and the expressions of annotations (<c>Dialog(enable = useFz)</c>), followed
/// through any number of components; the target of a modification, on a component, an extends clause
/// or a short class, nested (<c>axle(wheel(Fz = 1))</c>) or dotted (<c>extends Car(axle.Fz = 1)</c>);
/// and a named argument to the declaring class, or a class derived from it, called as a function or
/// record constructor. A modification inside an annotation is annotation grammar, not a member, and is
/// never one. Text inside strings - an icon label's <c>%Fz</c>, documentation - is not looked in.</para>
/// </remarks>
public sealed class ElementReferenceLocator : modelicaBaseVisitor<object?>
{
    private readonly DirectedGraph _graph;
    private readonly ReferenceLocator.Shared _shared;
    private readonly List<ElementSite> _sites = [];
    private readonly Stack<(string ClassId, List<ImportInfo> Imports)> _scopes = new();
    private readonly Dictionary<string, bool> _derived = new(StringComparer.Ordinal);
    private string _withinPrefix = string.Empty;

    /// <param name="graph">The graph references are resolved against.</param>
    /// <param name="declaringClassId">The class that declares the element.</param>
    /// <param name="name">The element's name.</param>
    /// <param name="shared">Caches shared with the other locators of one operation; null for one of its own.</param>
    public ElementReferenceLocator(DirectedGraph graph, string declaringClassId, string name, ReferenceLocator.Shared? shared = null)
    {
        _graph = graph;
        DeclaringClassId = declaringClassId;
        Name = name;
        _shared = shared ?? new ReferenceLocator.Shared();
    }

    /// <summary>The class that declares the element.</summary>
    public string DeclaringClassId { get; }

    /// <summary>The element's name.</summary>
    public string Name { get; }

    /// <summary>Every place found, in the order the walk met them.</summary>
    public IReadOnlyList<ElementSite> Sites => _sites;

    /// <summary>Locate the sites in a parsed file.</summary>
    public static IReadOnlyList<ElementSite> Locate(
        DirectedGraph graph, modelicaParser.Stored_definitionContext tree, string declaringClassId, string name,
        ReferenceLocator.Shared? shared = null)
    {
        var locator = new ElementReferenceLocator(graph, declaringClassId, name, shared);
        locator.Visit(tree);
        return locator._sites;
    }

    public override object? VisitStored_definition(modelicaParser.Stored_definitionContext context)
    {
        if (context.name() is { } within)
            _withinPrefix = string.Join(".", within.IDENT().Select(t => t.GetText()));
        return base.VisitStored_definition(context);
    }

    public override object? VisitClass_definition(modelicaParser.Class_definitionContext context)
    {
        var spec = context.class_specifier();
        var leaf = spec?.long_class_specifier()?.IDENT() is { Length: > 0 } longName ? longName[0].GetText()
            : spec?.short_class_specifier()?.IDENT()?.GetText()
            ?? (spec?.der_class_specifier()?.IDENT() is { Length: > 0 } derName ? derName[0].GetText() : null);
        if (leaf is null)
            return base.VisitClass_definition(context);

        var parentId = _scopes.Count > 0 ? _scopes.Peek().ClassId : (_withinPrefix.Length > 0 ? _withinPrefix : null);
        _scopes.Push((parentId is null ? leaf : $"{parentId}.{leaf}", ReferenceResolver.CollectClassImports(context)));
        base.VisitClass_definition(context);
        _scopes.Pop();
        return null;
    }

    /// <summary>The element's own declaration, in the class declaring it - not a redeclaration written in a modification.</summary>
    public override object? VisitDeclaration(modelicaParser.DeclarationContext context)
    {
        if (_scopes.Count > 0 && _scopes.Peek().ClassId == DeclaringClassId
            && context.IDENT() is { } ident && ident.GetText() == Name && !InsideModification(context))
            Add(ElementSiteKind.Declaration, ident, context);
        return base.VisitDeclaration(context);
    }

    public override object? VisitComponent_reference(modelicaParser.Component_referenceContext context)
    {
        // A called name - sin(x), Lib.f(x) - is a class, not a component reference.
        if (_scopes.Count > 0 && !(context.Parent is modelicaParser.PrimaryContext primary && primary.function_call_args() is not null))
        {
            var idents = context.IDENT();
            if (!Iterators(context).Contains(idents[0].GetText()) && References(_scopes.Peek().ClassId) is { } references
                && references.Resolve(ReferenceResolver.GetComponentReferenceName(context)) is { } resolved)
            {
                for (var i = 0; i < resolved.Path.Count; i++)
                {
                    var at = resolved.QualifierSegments + i;
                    if (at < idents.Length && IsTarget(resolved.Path[i]))
                        Add(ElementSiteKind.Reference, idents[at], context);
                }
            }
        }
        return base.VisitComponent_reference(context);
    }

    public override object? VisitElement_modification(modelicaParser.Element_modificationContext context)
    {
        if (_scopes.Count > 0 && context.name() is { } name && ModifiedClass(context) is { } modified
            && References(modified.Id) is { } references
            && references.Resolve(string.Join(".", name.IDENT().Select(t => t.GetText()))) is { } resolved)
        {
            var idents = name.IDENT();
            for (var i = 0; i < resolved.Path.Count && i < idents.Length; i++)
                if (IsTarget(resolved.Path[i]))
                    Add(ElementSiteKind.Modification, idents[i], context);
        }
        return base.VisitElement_modification(context);
    }

    public override object? VisitNamed_argument(modelicaParser.Named_argumentContext context)
    {
        if (_scopes.Count > 0 && context.IDENT() is { } ident && ident.GetText() == Name
            && CalledClass(context) is { } called && IsOrDerives(called))
            Add(ElementSiteKind.NamedArgument, ident, context);
        return base.VisitNamed_argument(context);
    }

    private void Add(ElementSiteKind kind, ITerminalNode ident, IParseTree at) =>
        _sites.Add(new ElementSite(
            kind, new NameSegment(ident.GetText(), ident.Symbol.StartIndex, ident.Symbol.StopIndex),
            _scopes.Peek().ClassId, ident.Symbol.Line, Iterators(at)));

    private bool IsTarget(ResolvedElement element) =>
        element.Element.Kind == ModelicaParser.DataTypes.ClassElementKind.Component
        && element.Element.Name == Name && element.OwnerId == DeclaringClassId;

    private ComponentReferences? References(string classId)
    {
        if (!_shared.References.TryGetValue(classId, out var references))
        {
            references = _graph.GetNode<ModelNode>(classId) is { } node
                ? ClassElementResolver.ReferencesIn(_graph, node, _shared.Interfaces, _shared.Ancestors)
                : null;
            _shared.References[classId] = references;
        }
        return references;
    }

    private bool IsOrDerives(ModelNode node)
    {
        if (node.Id == DeclaringClassId)
            return true;
        if (!_derived.TryGetValue(node.Id, out var derives))
            _derived[node.Id] = derives = ClassElementResolver.BaseClasses(_graph, node).Any(b => b.Id == DeclaringClassId);
        return derives;
    }

    /// <summary>
    /// The class a modification is written on: the type of the component, the base of the extends clause
    /// or short class it belongs to, followed down the modifications it is nested in. Null for one inside
    /// an annotation, which is annotation grammar, and for one a class extends is written on.
    /// </summary>
    private ModelNode? ModifiedClass(modelicaParser.Element_modificationContext modification)
    {
        var nested = new List<string>();
        modelicaParser.Type_specifierContext? typeSpecifier = null;
        for (var node = modification.Parent; node is not null && typeSpecifier is null; node = node.Parent)
        {
            switch (node)
            {
                case modelicaParser.AnnotationContext:
                case modelicaParser.Class_definitionContext:
                case modelicaParser.Long_class_specifierContext:
                    return null;
                case modelicaParser.Element_modificationContext outer:
                    nested.Insert(0, string.Join(".", outer.name().IDENT().Select(t => t.GetText())));
                    break;
                case modelicaParser.Extends_clauseContext extendsClause:
                    typeSpecifier = extendsClause.type_specifier();
                    break;
                case modelicaParser.Component_clauseContext component:
                    typeSpecifier = component.type_specifier();
                    break;
                case modelicaParser.Component_clause1Context component1:
                    typeSpecifier = component1.type_specifier();
                    break;
                case modelicaParser.Short_class_specifierContext shortClass:
                    typeSpecifier = shortClass.type_specifier();
                    break;
            }
        }

        if (typeSpecifier?.name() is not { } typeName)
            return null;
        var frame = _scopes.Peek();
        var modified = ReferenceResolver.ResolvePath(
            _graph, frame.ClassId, frame.Imports, ReferenceResolver.GetQualifiedName(typeName), _shared.Ancestors)?.Node;

        foreach (var component in nested)
        {
            if (modified is null)
                return null;
            modified = References(modified.Id)?.Resolve(component)?.Type;
        }
        return modified;
    }

    /// <summary>The class a named argument is passed to: the function or record called.</summary>
    private ModelNode? CalledClass(modelicaParser.Named_argumentContext argument)
    {
        for (var node = argument.Parent; node is not null; node = node.Parent)
        {
            if (node is modelicaParser.PrimaryContext { } primary && primary.function_call_args() is not null)
            {
                if (primary.component_reference() is not { } called)
                    return null;
                var frame = _scopes.Peek();
                return ReferenceResolver.ResolvePath(
                    _graph, frame.ClassId, frame.Imports, ReferenceResolver.GetComponentReferenceName(called), _shared.Ancestors)?.Node;
            }
            if (node is modelicaParser.Class_definitionContext)
                return null;
        }
        return null;
    }

    /// <summary>Whether a declaration is written inside a modification - a redeclaration, not the element's own.</summary>
    private static bool InsideModification(IParseTree node)
    {
        for (var at = node.Parent; at is not null; at = at.Parent)
        {
            if (at is modelicaParser.Class_modificationContext or modelicaParser.Element_redeclarationContext)
                return true;
            if (at is modelicaParser.Class_definitionContext)
                return false;
        }
        return false;
    }

    /// <summary>The names <c>for</c> iterators declare where <paramref name="node"/> is written, innermost first.</summary>
    private static IReadOnlyList<string> Iterators(IParseTree node)
    {
        var names = new List<string>();
        for (var at = node.Parent; at is not null && at is not modelicaParser.Class_definitionContext; at = at.Parent)
        {
            var indices = at switch
            {
                modelicaParser.For_equationContext forEquation => forEquation.for_indices(),
                modelicaParser.For_statementContext forStatement => forStatement.for_indices(),
                modelicaParser.Function_argumentsContext arguments => arguments.for_indices(),
                modelicaParser.Array_argumentsContext arguments => arguments.for_indices(),
                _ => null,
            };
            if (indices is not null)
                names.AddRange(indices.for_index().Select(i => i.IDENT().GetText()));
        }
        return names;
    }
}
