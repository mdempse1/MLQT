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
}

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

    /// <param name="graph">The graph used to resolve references.</param>
    /// <param name="targetIds">Only record references resolving to these ids; null records all resolvable references.</param>
    public ReferenceLocator(DirectedGraph graph, IEnumerable<string>? targetIds = null)
    {
        _graph = graph;
        _targets = targetIds is null ? null : new HashSet<string>(targetIds, StringComparer.Ordinal);
    }

    public IReadOnlyList<ReferenceSite> Sites => _sites;

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
        Record(ReferenceResolver.GetQualifiedName(context), context.IDENT());
        return base.VisitName(context);
    }

    public override object? VisitComponent_reference(modelicaParser.Component_referenceContext context)
    {
        Record(ReferenceResolver.GetComponentReferenceName(context), context.IDENT());
        return base.VisitComponent_reference(context);
    }

    private void Record(string reference, ITerminalNode[] idents)
    {
        // Only inside a class scope, and only for genuine dotted-name references.
        if (_scopes.Count == 0 || string.IsNullOrWhiteSpace(reference) || idents is not { Length: > 0 })
            return;

        var frame = _scopes.Peek();
        var targetId = ReferenceResolver.Resolve(_graph, frame.ClassId, frame.Imports, reference);
        if (targetId is null || (_targets is not null && !_targets.Contains(targetId)))
            return;

        var segments = idents
            .Select(t => new NameSegment(t.GetText(), t.Symbol.StartIndex, t.Symbol.StopIndex))
            .ToList();
        _sites.Add(new ReferenceSite(
            targetId, idents[0].Symbol.StartIndex, idents[^1].Symbol.StopIndex, reference, segments,
            idents[0].Symbol.Line));
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
