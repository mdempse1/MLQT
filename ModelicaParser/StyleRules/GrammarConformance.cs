using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using ModelicaParser.Helpers;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Reports code that parses only because <c>modelica.g4</c> accepts more than the concrete syntax of
/// the Modelica Language Specification 3.7 (Appendix A, and the keyword list of §2.3.3).
/// </summary>
/// <remarks>
/// <para><b>Why the grammar is wider than the language.</b> MLQT has to load what Dymola loads, so the
/// grammar was widened wherever Dymola accepts something the specification does not. A file that
/// leans on one of those widenings is fine in Dymola and may be refused by another tool, which is what
/// this rule is for: a library that has to load anywhere can turn it on and see each place.</para>
///
/// <para><b>The widenings, each a <see cref="Kind"/>:</b></para>
/// <list type="bullet">
/// <item><see cref="Kind.LeadingAnnotation"/> - a class annotation before the class's elements. MLS
/// <c>composition</c> has one class annotation, at the end of the body.</item>
/// <item><see cref="Kind.BasePrefix"/> - a short class definition with a prefix other than
/// <c>input</c>/<c>output</c> (<c>type T = parameter Real</c>). MLS <c>base-prefix</c> is
/// <c>[ input | output ]</c>; the grammar takes the whole <c>type_prefix</c>.</item>
/// <item><see cref="Kind.AssignmentModification"/> - a modification written with <c>:=</c>. MLS
/// <c>modification</c> has only <c>=</c>.</item>
/// <item><see cref="Kind.DerStatement"/> - a statement starting with <c>der</c> that is not
/// <c>der(component-reference) := expression</c>.</item>
/// <item><see cref="Kind.Comprehension"/> - a <c>for</c> comprehension after more than one argument
/// (<c>f(a, b for i in 1:n)</c>, <c>{a, b for i in 1:n}</c>), or as the subscript of a parenthesised
/// expression. MLS allows <c>for</c> only after a single expression, and a parenthesised expression
/// takes <c>array-subscripts</c>.</item>
/// <item><see cref="Kind.StringEscape"/> - a backslash escape MLS does not define, in a string or a
/// quoted identifier. MLS defines <c>\' \" \? \\ \a \b \f \n \r \t \v</c>; the lexer takes a backslash
/// before anything, because documentation HTML and Windows paths are full of them.</item>
/// <item><see cref="Kind.QuotedIdentifierWhitespace"/> - a tab or line break in a quoted identifier,
/// whose MLS <c>Q-CHAR</c> includes the space and no other white space.</item>
/// <item><see cref="Kind.ReservedName"/> - <c>time</c> used as an identifier, or an element or
/// enumeration literal named after a predefined type. MLS 3.7 makes <c>time</c> a keyword, and §2.3.3
/// forbids declaring an element named <c>Real</c>, <c>Integer</c>, <c>Boolean</c> or <c>String</c>; the
/// grammar treats <c>time</c> as an ordinary identifier.</item>
/// </list>
///
/// <para><b>Not reported: where the grammar is narrower than the language.</b> Some of MLS 3.7 does
/// not parse here at all - a unit on a literal (<c>1 'm'</c>), <c>(expr).name</c> and
/// <c>(expr)[:]</c>, an empty quoted identifier <c>''</c>, white space inside <c>.*</c> or <c>.{</c> of
/// an import, and comments in some positions. Those are syntax errors, which are already reported.</para>
/// </remarks>
public class GrammarConformance : VisitorWithModelNameTracking
{
    /// <summary>Which widening of the grammar a finding is about; the discriminator's first part.</summary>
    public enum Kind
    {
        LeadingAnnotation,
        BasePrefix,
        AssignmentModification,
        DerStatement,
        Comprehension,
        StringEscape,
        QuotedIdentifierWhitespace,
        ReservedName,
    }

    // §2.3.3: no element or enumeration literal may take one of these names.
    private static readonly HashSet<string> ReservedTypeNames = new(StringComparer.Ordinal)
    {
        "Real", "Integer", "Boolean", "String",
    };

    // The escapes of MLS A.1 S-ESCAPE, by the character after the backslash.
    private const string DefinedEscapes = "'\"?\\abfnrtv";

    public GrammarConformance(string basePackage = "") : base(basePackage)
    {
    }

    /// <summary>The text of <paramref name="kind"/> as a discriminator: stable, so never the enum's
    /// name read at run time.</summary>
    public static string Code(Kind kind) => kind switch
    {
        Kind.LeadingAnnotation => "leading-annotation",
        Kind.BasePrefix => "base-prefix",
        Kind.AssignmentModification => "assignment-modification",
        Kind.DerStatement => "der-statement",
        Kind.Comprehension => "comprehension",
        Kind.StringEscape => "string-escape",
        Kind.QuotedIdentifierWhitespace => "quoted-identifier-whitespace",
        Kind.ReservedName => "reserved-name",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public override object? VisitComposition([NotNull] modelicaParser.CompositionContext context)
    {
        // Of() already treats an annotation standing alone in a body as the trailing one, which is
        // where MLS puts it; what is left as leading has something after it.
        if (CompositionAnnotations.Of(context).Leading is { } leading)
            Report(leading.Start.Line, Kind.LeadingAnnotation, null, null,
                "a class annotation before the class's elements; MLS 3.7 allows it only at the end of the class body (A.2.2 composition)");
        return base.VisitComposition(context);
    }

    public override object? VisitShort_class_specifier([NotNull] modelicaParser.Short_class_specifierContext context)
    {
        if (context.base_prefix()?.type_prefix() is { } prefix)
            foreach (var word in Terminals(prefix))
                if (word.GetText() is "flow" or "stream" or "discrete" or "parameter" or "constant")
                    Report(word.Symbol.Line, Kind.BasePrefix, context.IDENT()?.GetText(), word.GetText(),
                        $"'{word.GetText()}' in a short class definition; MLS 3.7 allows only input or output there (A.2.2 base-prefix)");
        return base.VisitShort_class_specifier(context);
    }

    public override object? VisitModification([NotNull] modelicaParser.ModificationContext context)
    {
        if (context.GetChild(0) is ITerminalNode { Symbol.Text: ":=" } assign)
        {
            var name = context.Parent switch
            {
                modelicaParser.DeclarationContext d => d.IDENT()?.GetText(),
                modelicaParser.Element_modificationContext m => m.name()?.GetText(),
                _ => null,
            };
            Report(assign.Symbol.Line, Kind.AssignmentModification, name, null,
                "a modification written with ':='; MLS 3.7 modifications use '=' (A.2.5 modification)");
        }
        return base.VisitModification(context);
    }

    public override object? VisitStatement([NotNull] modelicaParser.StatementContext context)
    {
        if (context.GetChild(0) is ITerminalNode { Symbol.Text: "der" } der && !IsDerAssignment(context))
            Report(der.Symbol.Line, Kind.DerStatement, null, null,
                "a statement starting with der that is not der(variable) := expression (A.2.6 statement-or-procedure)");
        return base.VisitStatement(context);
    }

    public override object? VisitFunction_arguments([NotNull] modelicaParser.Function_argumentsContext context)
    {
        if (HasTerminal(context, "for") && HasTerminal(context, ","))
            Report(context.Start.Line, Kind.Comprehension, null, "arguments",
                "a for comprehension after more than one argument; MLS 3.7 allows for only after a single expression (A.2.7 function-arguments)");
        return base.VisitFunction_arguments(context);
    }

    public override object? VisitArray_arguments([NotNull] modelicaParser.Array_argumentsContext context)
    {
        if (context.Parent is modelicaParser.PrimaryContext primary
            && primary.GetChild(0) is ITerminalNode { Symbol.Text: "(" })
        {
            if (HasTerminal(context, "for"))
                Report(context.Start.Line, Kind.Comprehension, null, "subscript",
                    "a for comprehension as the subscript of a parenthesised expression; MLS 3.7 takes array-subscripts there (A.2.7 primary)");
        }
        else if (HasTerminal(context, "for") && HasTerminal(context, ","))
        {
            Report(context.Start.Line, Kind.Comprehension, null, "array",
                "a for comprehension after more than one array element; MLS 3.7 allows for only after a single expression (A.2.7 array-arguments)");
        }
        return base.VisitArray_arguments(context);
    }

    public override object? VisitDeclaration([NotNull] modelicaParser.DeclarationContext context)
    {
        ReportReservedTypeName(context.IDENT(), "an element");
        return base.VisitDeclaration(context);
    }

    public override object? VisitEnumeration_literal([NotNull] modelicaParser.Enumeration_literalContext context)
    {
        ReportReservedTypeName(context.IDENT(), "an enumeration literal");
        return base.VisitEnumeration_literal(context);
    }

    public override object? VisitTerminal(ITerminalNode node)
    {
        var token = node.Symbol;
        switch (token.Type)
        {
            case modelicaParser.STRING:
                CheckEscapes(token, "a string");
                break;
            case modelicaParser.IDENT when token.Text.StartsWith('\''):
                CheckEscapes(token, "a quoted identifier");
                CheckQuotedWhitespace(token);
                break;
            case modelicaParser.IDENT when token.Text == "time" && !IsTimeExpression(node):
                Report(token.Line, Kind.ReservedName, "time", "time",
                    "'time' used as an identifier; it is a keyword in MLS 3.7 (§2.3.3) and may only be used as the time variable");
                break;
        }
        return base.VisitTerminal(node);
    }

    private void ReportReservedTypeName(ITerminalNode? ident, string what)
    {
        if (ident is not null && ReservedTypeNames.Contains(ident.GetText()))
            Report(ident.Symbol.Line, Kind.ReservedName, ident.GetText(), ident.GetText(),
                $"{what} named '{ident.GetText()}'; MLS 3.7 §2.3.3 does not allow declaring an element or enumeration literal with the name of a predefined type");
    }

    private void CheckEscapes(IToken token, string what)
    {
        var text = token.Text;
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (text[i] != '\\')
                continue;
            var next = text[i + 1];
            if (DefinedEscapes.IndexOf(next) < 0)
            {
                var sequence = "\\" + (char.IsWhiteSpace(next) ? "<whitespace>" : next.ToString());
                Report(token.Line + LinesBefore(text, i), Kind.StringEscape, null, sequence,
                    $"the escape '{sequence}' in {what}; MLS 3.7 defines only \\' \\\" \\? \\\\ \\a \\b \\f \\n \\r \\t \\v (A.1 S-ESCAPE). Write '\\\\' for a backslash");
            }
            i++;
        }
    }

    private void CheckQuotedWhitespace(IToken token)
    {
        var text = token.Text;
        for (var i = 0; i < text.Length; i++)
            if (text[i] is '\t' or '\r' or '\n')
            {
                Report(token.Line + LinesBefore(text, i), Kind.QuotedIdentifierWhitespace, text.Trim(), null,
                    "a tab or line break in a quoted identifier; MLS 3.7 allows only the space (A.1 Q-CHAR)");
                return;
            }
    }

    private static int LinesBefore(string text, int index)
    {
        var lines = 0;
        for (var i = 0; i < index; i++)
            if (text[i] == '\n')
                lines++;
        return lines;
    }

    // `time` on its own as a reference is the time variable; anywhere else it is a keyword where an
    // identifier was expected - a declaration, a member (`a.time`), a modification's name.
    private static bool IsTimeExpression(ITerminalNode node) =>
        node.Parent is modelicaParser.Component_referenceContext { ChildCount: 1 };

    // der(x) := e, with exactly one argument that is a component reference and nothing else.
    private static bool IsDerAssignment(modelicaParser.StatementContext statement)
    {
        if (statement.function_call_args() is not [var args]
            || !HasTerminal(statement, ":=")
            || args.function_arguments() is not { } arguments
            || arguments.ChildCount != 1
            || arguments.expression() is not { } expression)
            return false;

        // Down the single-child chain an expression made of one primary reaches it by.
        IParseTree current = expression;
        while (current is ParserRuleContext { ChildCount: 1 } rule && rule is not modelicaParser.PrimaryContext)
            current = rule.GetChild(0);
        return current is modelicaParser.PrimaryContext { ChildCount: 1 } primary && primary.component_reference() is not null;
    }

    private static bool HasTerminal(ParserRuleContext context, string text) =>
        Terminals(context).Any(t => t.Symbol.Text == text);

    private static IEnumerable<ITerminalNode> Terminals(ParserRuleContext context)
    {
        for (var i = 0; i < context.ChildCount; i++)
            if (context.GetChild(i) is ITerminalNode terminal)
                yield return terminal;
    }

    private void Report(int line, Kind kind, string? elementPath, string? detail, string message)
    {
        var code = Code(kind);
        AddFinding(line, $"Not strict Modelica: {message}", RuleIds.GrammarConformance,
            elementPath, detail is null ? code : $"{code}:{detail}");
    }
}
