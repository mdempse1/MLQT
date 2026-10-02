using System.Text.RegularExpressions;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using ModelicaParser.DataTypes;

namespace ModelicaParser.Visitors;

/// <summary>
/// <b>The one reader of a class's <c>connect</c> equations.</b> Every connect in the class's own
/// equation sections, including those inside <c>for</c>, <c>if</c> and <c>when</c> equations, each
/// with the equations it is nested in. A connect in a loop is how an array of components is wired
/// (<c>for i in 1:n loop connect(a[i].p, b[i].n); end for;</c>), so a reader that looked only at an
/// equation section's direct children reported such a model as having no connections at all, while
/// <c>list_connections</c>, which walked the whole tree, found them: two tools giving two answers for
/// one class. A caller that cannot treat a nested connect like any other - one that writes a line
/// annotation into it, say - asks <see cref="ConnectClause.IsNested"/> and decides, rather than
/// reading less.
/// </summary>
public static class ConnectClauses
{
    /// <summary>The connects in <paramref name="composition"/>'s equation sections, in source order.</summary>
    public static IReadOnlyList<ConnectClause> In(modelicaParser.CompositionContext? composition)
        => [.. WithEquations(composition).Select(c => c.Clause)];

    /// <summary>
    /// The same connects, each with the <c>equation</c> node it is - for a caller that reads or edits
    /// what follows the clause (its annotation) as well as the clause.
    /// </summary>
    public static IEnumerable<(modelicaParser.EquationContext Equation, ConnectClause Clause)> WithEquations(
        modelicaParser.CompositionContext? composition)
    {
        if (composition?.children is null)
            yield break;

        foreach (var section in composition.children.OfType<modelicaParser.Equation_sectionContext>())
        foreach (var found in Walk(section.equation_or_comment(), []))
            yield return found;
    }

    private static IEnumerable<(modelicaParser.EquationContext, ConnectClause)> Walk(
        IEnumerable<modelicaParser.Equation_or_commentContext> body, IReadOnlyList<ConnectScope> scopes)
    {
        foreach (var item in body)
        {
            if (item.equation() is not { } equation)
                continue;

            IEnumerable<(modelicaParser.EquationContext, ConnectClause)> inner = [];

            if (equation.connect_clause() is { } connect)
            {
                var refs = connect.component_reference();
                // `equation c_comment* ';'`: the item's last token is the semicolon. Where the parser
                // recovered without one, the equation's last character - such a class cannot be
                // edited, so nothing cuts at it.
                var semicolon = item.Stop is { Text: ";" } end ? end.StartIndex : equation.Stop.StopIndex;
                if (refs.Length >= 2)
                    inner = [(equation, new ConnectClause(
                        refs[0].GetText(), refs[1].GetText(),
                        connect.Start.StartIndex, connect.Stop.StopIndex, semicolon, scopes))];
            }
            else if (equation.for_equation() is { } loop)
            {
                var indices = loop.for_indices()?.for_index()
                    .Select(i => i.IDENT()?.GetText())
                    .OfType<string>()
                    .ToList() ?? [];
                inner = Walk(loop.equation_or_comment(),
                    Inside(scopes, ConnectScopeKind.For, "for " + Text(loop.for_indices()), indices));
            }
            else if (equation.if_equation() is { } branch)
            {
                inner = Walk(branch.equation_or_comment(),
                    Inside(scopes, ConnectScopeKind.If, "if " + Text(branch.expression())));
                foreach (var elseIf in branch.elseif_equation())
                    inner = inner.Concat(Walk(elseIf.equation_or_comment(),
                        Inside(scopes, ConnectScopeKind.If, "elseif " + Text(elseIf.expression()))));
                if (branch.else_equation() is { } otherwise)
                    inner = inner.Concat(Walk(otherwise.equation_or_comment(),
                        Inside(scopes, ConnectScopeKind.If, "else")));
            }
            else if (equation.when_equation() is { } when)
            {
                inner = Walk(when.equation_or_comment(),
                    Inside(scopes, ConnectScopeKind.When, "when " + Text(when.expression())));
                foreach (var elseWhen in when.elsewhen_equation())
                    inner = inner.Concat(Walk(elseWhen.equation_or_comment(),
                        Inside(scopes, ConnectScopeKind.When, "elsewhen " + Text(elseWhen.expression()))));
            }

            foreach (var found in inner)
                yield return found;
        }
    }

    private static IReadOnlyList<ConnectScope> Inside(
        IReadOnlyList<ConnectScope> outer, ConnectScopeKind kind, string header, IReadOnlyList<string>? indices = null)
        => [.. outer, new ConnectScope(kind, header, indices ?? [])];

    // The text as written, spaces included - GetText() would join `i in 1:n` into `iin1:n`. A
    // condition written across lines becomes one line: a header is a label, read on one.
    private static string Text(ParserRuleContext? context)
        => context?.Start is null || context.Stop is null || context.Stop.StopIndex < context.Start.StartIndex
            ? string.Empty
            : LineBreak.Replace(
                context.Start.InputStream.GetText(Interval.Of(context.Start.StartIndex, context.Stop.StopIndex)), " ");

    private static readonly Regex LineBreak = new(@"[ \t]*\n[ \t]*", RegexOptions.Compiled);
}
