using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using ModelicaParser;

namespace ModelicaGraph;

/// <summary>One dotted segment (IDENT) of a reference, with its character span in the parsed text.</summary>
public sealed record NameSegment(string Text, int StartIndex, int StopIndex);

/// <summary>
/// A located reference to a target class: the span of the whole reference plus each dotted segment, so
/// a rename can rewrite just the leaf segment and a move can re-qualify the prefix. All indices are
/// character offsets into the exact text that was parsed.
/// </summary>
public sealed record ReferenceSite(
    string TargetId,
    int StartIndex,
    int StopIndex,
    string Text,
    IReadOnlyList<NameSegment> Segments,
    int Line)
{
    /// <summary>The last segment — the class's own name in the reference.</summary>
    public NameSegment Leaf => Segments[^1];

    /// <summary>
    /// True when the reference names its target only because the class it is written in inherits it -
    /// <c>Medium.State</c> in a model whose base declares a replaceable <c>Medium</c>. <b>A move must
    /// leave it as written</b>: the extends clause it depends on is re-qualified anyway, and writing
    /// the target's full name would fix it to the base's default and undo every <c>redeclare</c>. A
    /// rename, which rewrites only the leaf, rewrites it like any other.
    /// </summary>
    public bool ThroughInheritance => FirstSegmentBinding == Analysis.NameBinding.Inherited;

    /// <summary>
    /// The id of the class each segment in <see cref="Segments"/> names, one per segment - what a
    /// rename asks to find the segment that names the renamed class, rather than trusting its text.
    /// </summary>
    public IReadOnlyList<string> SegmentIds { get; init; } = [];

    /// <summary>
    /// How the first segment was found: the class's own, inherited, through a plain or alias import, a
    /// wildcard, or from the top. An alias is a name the author chose, and a move leaves a name an
    /// import or inheritance already reaches as it is.
    /// </summary>
    public Analysis.NameBinding? FirstSegmentBinding { get; init; }

    /// <summary>
    /// The class the reference is written in - where a name in it is looked up from. Empty for one
    /// outside any class.
    /// </summary>
    public string ScopeId { get; init; } = string.Empty;

    /// <summary>
    /// True for the name in an <c>import</c> clause, which is always looked up from the top (MLS
    /// §13.2.1) - so, unlike any other reference, nothing where it is written can change what it means.
    /// </summary>
    public bool InImport { get; init; }
}

/// <summary>
/// A <c>redeclare</c> in a modification that replaces a target class: <c>Medium</c> in
/// <c>extends Base(redeclare package Medium = Water)</c> or <c>Base b(redeclare package Medium = Water)</c>.
/// <see cref="Name"/> is the redeclared name's token, which a rename of the class rewrites.
/// </summary>
public sealed record RedeclarationSite(string TargetId, NameSegment Name, int Line);

/// <summary>
/// A target class's own definition: the name identifier tokens (e.g. the opening and closing IDENT of
/// <c>model X ... end X</c>), so a rename can rewrite the declaration itself. All the tokens carry the
/// class's simple name.
/// </summary>
public sealed record DefinitionSite(
    string Id,
    IReadOnlyList<NameSegment> NameTokens,
    int StartIndex,
    int StopIndex,
    int Line);

/// <summary>
/// Locates the exact source spans of references to one or more target classes within a parsed file (or
/// class), so a precise rename/move can rewrite only those tokens. It is class-scope aware — it tracks
/// the fully-qualified id and imports of the class each reference sits in — and resolves every
/// reference with the shared <see cref="ReferenceResolver"/>, so it finds exactly the references
/// dependency analysis recognises. It reports only USAGES; a class's own definition name is not a
/// reference and is left to the caller to rename.
/// </summary>
public sealed class ReferenceLocator : modelicaBaseVisitor<object?>
{
    private readonly DirectedGraph _graph;
    private readonly HashSet<string>? _targets;
    private readonly List<ReferenceSite> _sites = new();
    private readonly List<DefinitionSite> _definitions = new();
    private readonly Stack<Frame> _scopes = new();
    private string _withinPrefix = string.Empty;

    private sealed record Frame(string ClassId, List<ImportInfo> Imports);

    /// <summary>
    /// What locators can share over one operation - a rename or move walks a locator over every file
    /// that refers to the class, and each asks about the same bases, packages and classes. Valid while
    /// the graph is unchanged: make one per operation, before its edits are applied.
    /// </summary>
    public sealed class Shared
    {
        internal Analysis.TypeResolver.AncestorCache Ancestors { get; } = new();
        internal Analysis.ClassElementResolver.InterfaceCache Interfaces { get; } = new();
        internal Dictionary<string, Analysis.ComponentReferences?> References { get; } = new(StringComparer.Ordinal);
    }

    private readonly Shared _shared;

    /// <param name="graph">The graph used to resolve references.</param>
    /// <param name="targetIds">Only record references resolving to these ids; null records all resolvable references.</param>
    /// <param name="shared">Caches to share with the other locators of one operation; null for a
    /// locator of its own.</param>
    public ReferenceLocator(DirectedGraph graph, IEnumerable<string>? targetIds = null, Shared? shared = null)
    {
        _graph = graph;
        _targets = targetIds is null ? null : new HashSet<string>(targetIds, StringComparer.Ordinal);
        _shared = shared ?? new Shared();
    }

    // One resolver per class the walk is inside, made the first time one of its references is not a
    // class by its whole name, and kept for the operation.
    private Analysis.ComponentReferences? ReferencesIn(string classId)
    {
        if (!_shared.References.TryGetValue(classId, out var references))
        {
            references = _graph.GetNode<DataTypes.ModelNode>(classId) is { } node
                ? Analysis.ClassElementResolver.ReferencesIn(_graph, node, _shared.Interfaces, _shared.Ancestors)
                : null;
            _shared.References[classId] = references;
        }
        return references;
    }

    public IReadOnlyList<ReferenceSite> Sites => _sites;

    /// <summary>The <c>redeclare</c>s in modifications that replace a target class.</summary>
    public IReadOnlyList<RedeclarationSite> Redeclarations => _redeclarations;

    private readonly List<RedeclarationSite> _redeclarations = new();

    /// <summary>Definition sites of the target classes encountered (their declaration name tokens).</summary>
    public IReadOnlyList<DefinitionSite> Definitions => _definitions;

    /// <summary>Locate references to <paramref name="targetIds"/> in a parsed stored_definition.</summary>
    public static IReadOnlyList<ReferenceSite> Locate(
        DirectedGraph graph, modelicaParser.Stored_definitionContext tree, IEnumerable<string>? targetIds = null)
    {
        var locator = new ReferenceLocator(graph, targetIds);
        locator.Visit(tree);
        return locator._sites;
    }

    public override object? VisitStored_definition(modelicaParser.Stored_definitionContext context)
    {
        // A file carries at most one within clause, and none for a top-level library.
        var name = context.name();
        if (name is not null)
            _withinPrefix = string.Join(".", name.IDENT().Select(t => t.GetText()));
        return base.VisitStored_definition(context);
    }

    public override object? VisitClass_definition(modelicaParser.Class_definitionContext context)
    {
        var leaf = ClassLeafName(context);
        if (leaf is null)
            return base.VisitClass_definition(context);

        var parentId = _scopes.Count > 0
            ? _scopes.Peek().ClassId
            : (_withinPrefix.Length > 0 ? _withinPrefix : null);
        var classId = parentId is null ? leaf : $"{parentId}.{leaf}";

        if (_targets is null || _targets.Contains(classId))
            _definitions.Add(new DefinitionSite(
                classId, ClassNameTokens(context.class_specifier()),
                context.Start.StartIndex, context.Stop?.StopIndex ?? context.Start.StopIndex, context.Start.Line));

        _scopes.Push(new Frame(classId, ReferenceResolver.CollectClassImports(context)));
        base.VisitClass_definition(context);
        _scopes.Pop();
        return null;
    }

    public override object? VisitName(modelicaParser.NameContext context)
    {
        Record(ReferenceResolver.GetReferenceName(context), context.IDENT(),
            inImport: context.Parent is modelicaParser.Import_clauseContext);
        return base.VisitName(context);
    }

    /// <summary>
    /// A <c>redeclare</c> in a modification names the class it replaces by its simple name, as an
    /// element of the class being modified - an extends clause's base, a component's type, or a short
    /// class's base, followed down any nested modification (<c>b(sub(redeclare package Medium = W))</c>).
    /// </summary>
    public override object? VisitElement_redeclaration(modelicaParser.Element_redeclarationContext context)
    {
        var spec = (context.short_class_definition() ?? context.element_replaceable()?.short_class_definition())
            ?.short_class_specifier();
        if (_scopes.Count > 0 && spec?.IDENT() is { } ident && ModifiedClass(context) is { } modified
            && Analysis.TypeResolver.MemberClass(_graph, modified, ident.GetText(), _shared.Ancestors) is { } replaced
            && (_targets is null || _targets.Contains(replaced.Id)))
            _redeclarations.Add(new RedeclarationSite(
                replaced.Id, new NameSegment(ident.GetText(), ident.Symbol.StartIndex, ident.Symbol.StopIndex),
                ident.Symbol.Line));
        return base.VisitElement_redeclaration(context);
    }

    // The class a modification holding `redeclaration` modifies: walk out through any nested element
    // modifications to the declaration, extends clause or short class it belongs to, resolve that
    // type, then follow the nested names down as components of it.
    private DataTypes.ModelNode? ModifiedClass(modelicaParser.Element_redeclarationContext redeclaration)
    {
        var nested = new List<string>();
        modelicaParser.Type_specifierContext? typeSpecifier = null;
        for (Antlr4.Runtime.Tree.IParseTree? node = redeclaration.Parent; node is not null && typeSpecifier is null; node = node.Parent)
        {
            switch (node)
            {
                case modelicaParser.Element_modificationContext modification:
                    nested.Insert(0, modification.name().GetText());
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
                case modelicaParser.Class_definitionContext:
                    return null;
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
            modified = Analysis.ClassElementResolver
                .ReferencesIn(_graph, modified, _shared.Interfaces, _shared.Ancestors)
                .Resolve(component)?.Type;
        }
        return modified;
    }

    public override object? VisitComponent_reference(modelicaParser.Component_referenceContext context)
    {
        Record(ReferenceResolver.GetComponentReferenceName(context), context.IDENT());
        return base.VisitComponent_reference(context);
    }

    private void Record(string reference, ITerminalNode[] idents, bool inImport = false)
    {
        // Only inside a class scope, and only for genuine dotted-name references.
        if (_scopes.Count == 0 || string.IsNullOrWhiteSpace(reference) || idents is not { Length: > 0 })
            return;

        var frame = _scopes.Peek();
        var resolved = ReferenceResolver.ResolvePath(_graph, frame.ClassId, frame.Imports, reference, _shared.Ancestors);
        var targetId = resolved?.Node.Id;
        var segmentIds = resolved?.Path ?? [];
        var binding = resolved?.Binding;

        // Not a class by its whole name: a component reached through one (Modelica.Constants.pi).
        // The site is the leading segments that name the class - renaming Constants rewrites them,
        // and leaves `pi` alone.
        if (targetId is null && ReferencesIn(frame.ClassId)?.Start(reference) is { QualifierSegments: > 0 } start
            && start.QualifierSegments <= idents.Length)
        {
            targetId = start.Scope.Id;
            segmentIds = start.QualifierPath;
            binding = start.QualifierBinding;
            idents = idents[..start.QualifierSegments];
        }

        if (targetId is null)
            return;

        // A name whose leading segments go through a target and whose last does not - `Medium.State`,
        // where Medium is a short class for Water and the target is Medium - is a site for those leading
        // segments, as a component reached through a class is: renaming Medium rewrites `Medium`.
        if (_targets is not null && !_targets.Contains(targetId))
        {
            var through = -1;
            for (var i = Math.Min(segmentIds.Count, idents.Length) - 1; i >= 0 && through < 0; i--)
                if (_targets.Contains(segmentIds[i]))
                    through = i;
            if (through < 0)
                return;
            targetId = segmentIds[through];
            segmentIds = [.. segmentIds.Take(through + 1)];
            idents = idents[..(through + 1)];
        }

        var segments = idents
            .Select(t => new NameSegment(t.GetText(), t.Symbol.StartIndex, t.Symbol.StopIndex))
            .ToList();
        _sites.Add(new ReferenceSite(
            targetId, idents[0].Symbol.StartIndex, idents[^1].Symbol.StopIndex, reference, segments,
            idents[0].Symbol.Line) { SegmentIds = segmentIds, FirstSegmentBinding = binding, ScopeId = frame.ClassId, InImport = inImport });
    }

    private static string? ClassLeafName(modelicaParser.Class_definitionContext context)
    {
        var spec = context.class_specifier();
        if (spec?.long_class_specifier() is { } l && l.IDENT().Length > 0)
            return l.IDENT(0).GetText();
        if (spec?.short_class_specifier() is { } s)
            return s.IDENT().GetText();
        if (spec?.der_class_specifier() is { } d && d.IDENT().Length > 0)
            return d.IDENT(0).GetText();
        return null;
    }

    // The identifier tokens that carry the class's own name (all equal to its simple name): for a long
    // class the opening and closing IDENT, for a short/der class the single leading IDENT.
    private static IReadOnlyList<NameSegment> ClassNameTokens(modelicaParser.Class_specifierContext? spec)
    {
        var tokens = new List<NameSegment>();
        if (spec?.long_class_specifier() is { } l)
        {
            foreach (var t in l.IDENT())
                tokens.Add(new NameSegment(t.GetText(), t.Symbol.StartIndex, t.Symbol.StopIndex));
        }
        else if (spec?.short_class_specifier() is { } s && s.IDENT() is { } sid)
        {
            tokens.Add(new NameSegment(sid.GetText(), sid.Symbol.StartIndex, sid.Symbol.StopIndex));
        }
        else if (spec?.der_class_specifier() is { } d && d.IDENT().Length > 0)
        {
            var t = d.IDENT(0);
            tokens.Add(new NameSegment(t.GetText(), t.Symbol.StartIndex, t.Symbol.StopIndex));
        }
        return tokens;
    }
}
