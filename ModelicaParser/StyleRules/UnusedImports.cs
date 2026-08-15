using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Flags an <c>import</c> whose bound name is never referenced in the class that declares it.
/// Per the Modelica spec an import is visible only in its own class — not inherited, not visible to
/// nested classes — so a per-class-scoped usage check is exact: no cross-model resolution and no
/// inheritance false positives. A name is considered used if the import's alias appears as an
/// identifier anywhere in that class's own body (outside the import statements themselves); this
/// deliberately over-counts uses (e.g. a coincidental same-named identifier), so the analysis
/// under-reports rather than risk a false positive.
/// </summary>
public class UnusedImports : VisitorWithModelNameTracking
{
    private sealed class Scope
    {
        public readonly List<(string Alias, int Line)> Imports = new();
        public readonly HashSet<string> UsedIdents = new(StringComparer.Ordinal);
    }

    private readonly Stack<Scope> _scopes = new();
    private bool _inImport;

    public UnusedImports(string basePackage = "") : base(basePackage) { }

    protected override void OnClassEntered() => _scopes.Push(new Scope());

    protected override void OnClassExited()
    {
        if (_scopes.Count == 0)
            return;

        var scope = _scopes.Pop();
        foreach (var (alias, line) in scope.Imports)
            if (!scope.UsedIdents.Contains(alias))
                AddViolation(line, $"import '{alias}' is not used in this class", RuleIds.UnusedImport, alias);
    }

    public override object? VisitImport_clause([NotNull] modelicaParser.Import_clauseContext context)
    {
        if (_scopes.Count > 0)
        {
            var alias = ImportAlias(context);
            if (alias is not null)
                _scopes.Peek().Imports.Add((alias, context.Start.Line));
        }

        // Walk the import's own path/aliases without counting their identifiers as "uses".
        _inImport = true;
        var result = base.VisitImport_clause(context);
        _inImport = false;
        return result;
    }

    public override object? VisitTerminal(ITerminalNode node)
    {
        if (!_inImport && _scopes.Count > 0 && node.Symbol.Type == modelicaParser.IDENT)
            _scopes.Peek().UsedIdents.Add(StripQuotes(node.GetText()));
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
