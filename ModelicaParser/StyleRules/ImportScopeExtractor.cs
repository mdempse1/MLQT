using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;

namespace ModelicaParser.StyleRules;

/// <summary>The name an <c>import</c> introduces into its class, and the line it is declared on.</summary>
public readonly record struct ImportBinding(string Alias, int Line);

/// <summary>
/// One class's import bindings together with every identifier used inside it — including inside
/// classes nested in the same source, because Modelica looks a simple name up in each enclosing
/// scope in turn, so a nested class's use of a name is a use of the enclosing class's import.
/// </summary>
public sealed record ImportScope(
    string ModelId, IReadOnlyList<ImportBinding> Imports, IReadOnlySet<string> UsedIdentifiers);

/// <summary>
/// Collects what <c>MLQT.Unused.Import</c> needs from one stored definition: the imports each class
/// declares, and the identifiers used within it. Identifiers inside an import clause are not counted
/// as uses — otherwise every import would mark itself used.
///
/// This deliberately decides nothing. An import is visible to the whole subtree below its class, and
/// in a real library that subtree spans other files (a package directory's children are lexically
/// nested inside its <c>package.mo</c>), so only a caller holding the graph can tell whether an
/// import is unused. See <c>UnusedImportAnalyzer</c>.
/// </summary>
public sealed class ImportScopeExtractor : VisitorWithModelNameTracking
{
    private sealed class Frame
    {
        public readonly List<ImportBinding> Imports = new();
        public readonly HashSet<string> UsedIdents = new(StringComparer.Ordinal);
    }

    private readonly Stack<Frame> _frames = new();
    private readonly List<ImportScope> _scopes = new();
    private bool _inImport;

    public ImportScopeExtractor(string basePackage = "") : base(basePackage) { }

    /// <summary>Every class scope in the source, innermost first — a scope is recorded as it closes,
    /// so the outermost class (the one the caller's model node represents) is last.</summary>
    public IReadOnlyList<ImportScope> Scopes => _scopes;

    /// <summary>The outermost class's scope, or null when the source held no class at all.</summary>
    public ImportScope? OutermostScope => _scopes.Count > 0 ? _scopes[^1] : null;

    protected override void OnClassEntered() => _frames.Push(new Frame());

    protected override void OnClassExited()
    {
        if (_frames.Count == 0)
            return;

        var frame = _frames.Pop();
        _scopes.Add(new ImportScope(CurrentModelName, frame.Imports, frame.UsedIdents));

        // A nested class's uses are uses in every enclosing scope too: name lookup walks outwards.
        if (_frames.Count > 0)
            _frames.Peek().UsedIdents.UnionWith(frame.UsedIdents);
    }

    public override object? VisitImport_clause([NotNull] modelicaParser.Import_clauseContext context)
    {
        if (_frames.Count > 0)
        {
            var alias = ImportAlias(context);
            if (alias is not null)
                _frames.Peek().Imports.Add(new ImportBinding(alias, context.Start.Line));
        }

        // Walk the import's own path/aliases without counting their identifiers as "uses".
        _inImport = true;
        var result = base.VisitImport_clause(context);
        _inImport = false;
        return result;
    }

    public override object? VisitTerminal(ITerminalNode node)
    {
        if (!_inImport && _frames.Count > 0 && node.Symbol.Type == modelicaParser.IDENT)
            _frames.Peek().UsedIdents.Add(StripQuotes(node.GetText()));
        return base.VisitTerminal(node);
    }

    // The name an import binds: the rename alias (`import X = A.B`), or the final segment of a plain
    // qualified import (`import A.B.C` → "C"). Wildcard/multi-import forms bind no single checkable name.
    private static string? ImportAlias(modelicaParser.Import_clauseContext context)
    {
        var rename = context.IDENT();
        if (rename is not null)
            return StripQuotes(rename.GetText());

        var text = context.GetText();
        if (text.Contains(".*") || text.Contains(".{"))
            return null;

        var ids = context.name()?.IDENT();
        return ids is { Length: > 0 } ? StripQuotes(ids[^1].GetText()) : null;
    }

    private static string StripQuotes(string s)
        => s.Length >= 2 && s[0] == '\'' && s[^1] == '\'' ? s[1..^1] : s;
}
