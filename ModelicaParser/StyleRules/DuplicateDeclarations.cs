using Antlr4.Runtime.Misc;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Flags names declared more than once within the same class — a duplicate component/variable
/// declaration, or the same name imported twice. Both are local, self-contained defects (Wave-1):
/// no cross-model resolution is needed, so they cannot produce library-visibility false positives.
/// Each duplicated name is reported once, on its first repeat. Inherited-member shadowing is a
/// separate (resolution-dependent) analysis.
/// </summary>
public class DuplicateDeclarations : VisitorWithModelNameTracking
{
    // Per-class state: the names seen so far and those already reported (so a name repeated three
    // times still yields a single finding). A stack because non-standalone nested classes
    // (replaceable/redeclare) are checked within their parent and get their own scope.
    private sealed class Scope
    {
        public readonly HashSet<string> SeenComponents = new(StringComparer.Ordinal);
        public readonly HashSet<string> ReportedComponents = new(StringComparer.Ordinal);
        public readonly HashSet<string> SeenImports = new(StringComparer.Ordinal);
        public readonly HashSet<string> ReportedImports = new(StringComparer.Ordinal);
    }

    private readonly bool _checkComponents;
    private readonly bool _checkImports;
    private readonly Stack<Scope> _scopes = new();

    public DuplicateDeclarations(bool checkComponents, bool checkImports, string basePackage = "")
        : base(basePackage)
    {
        _checkComponents = checkComponents;
        _checkImports = checkImports;
    }

    protected override void OnClassEntered() => _scopes.Push(new Scope());

    protected override void OnClassExited()
    {
        if (_scopes.Count > 0)
            _scopes.Pop();
    }

    public override object? VisitComponent_declaration([NotNull] modelicaParser.Component_declarationContext context)
    {
        if (_checkComponents && _scopes.Count > 0)
        {
            var name = StripQuotes(context.declaration()?.IDENT()?.GetText() ?? string.Empty);
            if (name.Length > 0)
            {
                var scope = _scopes.Peek();
                if (!scope.SeenComponents.Add(name) && scope.ReportedComponents.Add(name))
                    AddFinding(context.Start.Line, $"'{name}' is declared more than once in this class",
                        RuleIds.DuplicateDeclaration, name);
            }
        }
        return base.VisitComponent_declaration(context);
    }

    public override object? VisitImport_clause([NotNull] modelicaParser.Import_clauseContext context)
    {
        if (_checkImports && _scopes.Count > 0)
        {
            var alias = ImportAlias(context);
            if (alias is not null)
            {
                var scope = _scopes.Peek();
                if (!scope.SeenImports.Add(alias) && scope.ReportedImports.Add(alias))
                    AddFinding(context.Start.Line, $"'{alias}' is imported more than once in this class",
                        RuleIds.DuplicateImport, alias);
            }
        }
        return base.VisitImport_clause(context);
    }

    // The name an import binds into the class's scope: the rename alias (`import X = A.B`), or the
    // final segment of a plain qualified import (`import A.B.C` → "C"). Wildcard and multi-import
    // forms bind no single name, so they are not compared.
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
