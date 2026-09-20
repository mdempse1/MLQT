using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using ModelicaParser.Helpers;

namespace ModelicaParser.Visitors;

/// <summary>
/// Syntax highlighting that <b>does not rewrite the code</b>: the source is emitted exactly as it
/// was written, with each token wrapped in the same <c>&lt;CATEGORY&gt;</c> tag
/// <see cref="ModelicaRenderer"/> would have given it, so <c>CodeViewer</c> and <c>DiffViewer</c>
/// consume it unchanged.
///
/// <para><b>Why this exists.</b> The categories the renderer assigns look semantic and are entirely
/// positional — <c>TYPE</c> is "an <c>IDENT</c> under a <c>name</c> reached through a
/// <c>type_specifier</c>", <c>FUNCTION</c> is "an <c>IDENT</c> in a <c>component_reference</c> that
/// a <c>function_call_args</c> follows". Every one of them is a question about the parse tree, so
/// the colouring never needed the reformat the renderer performs in order to produce it. Measured
/// over the Modelica Standard Library and Modelica Buildings: the categories agree with the
/// renderer's on <b>99.9989% / 99.9984%</b> of 2.2M word tokens, and the emitted text is the source
/// character for character over <b>8,367 files and 1,103,108 lines</b>.</para>
///
/// <para><b>Emit from character offsets, never from token text.</b> Concatenating
/// <c>IToken.Text</c> silently drops whatever the lexer skipped; it lost characters on 2 of 5
/// deliberately malformed inputs. Driven by offsets with the gaps copied through, the output is
/// exact by construction whatever the lexer made of the file.</para>
///
/// <para><b>Three tiers, so there is always an answer.</b> A class that parses is coloured from its
/// tree; one that does not is coloured from the token stream alone (the lexer has never failed, on
/// any input tried); and if even lexing throws, the source is returned verbatim. The old behaviour
/// for a class with parser errors was no colour at all.</para>
///
/// <para><b>What the output is.</b> One string per line, never containing a newline. A token whose
/// text spans lines closes and reopens its tag at every line boundary, because the tag regexes in
/// <c>CodeViewer</c> and <c>DiffViewer</c> run per line and do not cross one — 0.18% of tokens span
/// lines but they cover a quarter of all lines, so getting this wrong loses the colour on a quarter
/// of the file. Text <b>inside</b> a tag is raw, because those consumers HTML-encode it themselves;
/// everything outside one is encoded here, since nothing downstream will.</para>
/// </summary>
public static class ModelicaTokenClassifier
{
    /// <summary>
    /// Highlights <paramref name="source"/> from its parse tree — the top tier, and the one the
    /// viewer uses. Returns one markup string per line of the source.
    ///
    /// <para>A class that does not parse is still coloured: the parser recovers rather than
    /// throwing, and the tokens it recovered carry their categories lexically. The
    /// <b>lexer-only</b> tier is chosen by the caller instead, through the overload below, because
    /// what it is for is a class too large to be worth parsing (18 ms against 206 on the largest
    /// file in the Modelica Standard Library) rather than one that failed to.</para>
    /// </summary>
    public static List<string> Highlight(string source)
    {
        if (string.IsNullOrEmpty(source))
            return [];

        try
        {
            var (tree, stream) = ModelicaParserHelper.ParseWithTokens(source);
            return Highlight(tree, stream, source);
        }
        catch
        {
            // Nothing in either measured library made the parser throw rather than recover. If some
            // input manages it, showing the class uncoloured beats not showing it.
            return Plain(source);
        }
    }

    /// <summary>
    /// Highlights <paramref name="source"/> using a parse tree and token stream the caller already
    /// has. <paramref name="tree"/> may be null, in which case every token is categorised
    /// lexically — which is the right answer for a class that did not parse, and a much cheaper one
    /// for a class too large to be worth parsing.
    /// </summary>
    /// <remarks>
    /// <paramref name="stream"/> must be the stream <paramref name="source"/> was lexed from:
    /// the token offsets are read against the source text, so a stream from different text produces
    /// nonsense rather than an error. <see cref="ModelicaParserHelper.ParseWithTokens"/> and
    /// <see cref="TokensOnly"/> both satisfy that.
    /// </remarks>
    public static List<string> Highlight(
        modelicaParser.Stored_definitionContext? tree, BufferedTokenStream? stream, string source)
    {
        if (string.IsNullOrEmpty(source))
            return [];
        if (stream is null)
            return Plain(source);

        var categories = new Dictionary<int, string>();
        if (tree is not null)
            Walk(tree, categories, default);

        // The offsets are against the text the lexer saw, which PreprocessCode has normalised. The
        // same normalisation here, and nowhere else, is what makes them line up.
        var text = ModelicaParserHelper.NormalizeLineEndings(source);
        var markup = new StringBuilder(text.Length + text.Length / 3);
        var cursor = 0;

        for (var i = 0; i < stream.Size; i++)
        {
            var token = stream.Get(i);
            if (token.Type == TokenConstants.EOF)
                break;

            int start = token.StartIndex, stop = token.StopIndex;

            // A synthesised token, or one past the end of the text being emitted: PreprocessCode
            // appends a ';' when the source has none, and that ';' is not in the source to mark up.
            if (start < 0 || stop < start || stop >= text.Length)
                continue;

            if (start > cursor)
                AppendUntagged(markup, text.AsSpan(cursor, start - cursor));

            var span = text.AsSpan(start, stop - start + 1);
            cursor = stop + 1;

            // Whitespace and anything else off the default channel carries no colour, but it does
            // carry position, so it is emitted rather than skipped.
            if (token.Channel != TokenConstants.DefaultChannel)
            {
                AppendUntagged(markup, span);
                continue;
            }

            AppendTagged(markup, span,
                categories.TryGetValue(i, out var category) ? category : Lexical(token));
        }

        if (cursor < text.Length)
            AppendUntagged(markup, text.AsSpan(cursor));

        return [.. markup.ToString().Split('\n')];
    }

    /// <summary>
    /// The source with no highlighting at all, one HTML-safe string per line. The bottom tier, and
    /// what the viewer shows when the user turns highlighting off.
    /// </summary>
    public static List<string> Plain(string source)
    {
        if (string.IsNullOrEmpty(source))
            return [];

        var text = ModelicaParserHelper.NormalizeLineEndings(source);
        var lines = text.Split('\n');
        var result = new List<string>(lines.Length);
        foreach (var line in lines)
            result.Add(System.Net.WebUtility.HtmlEncode(line));
        return result;
    }

    /// <summary>
    /// Lexes without parsing. 18 ms where a parse of the same file costs 206 — the tier for a class
    /// that will not parse, or one large enough that parsing it is not worth the wait.
    /// </summary>
    public static BufferedTokenStream TokensOnly(string source)
    {
        var lexer = new modelicaLexer(new AntlrInputStream(ModelicaParserHelper.PreprocessCode(source)));
        lexer.RemoveErrorListeners();
        var stream = new CommonTokenStream(lexer);
        stream.Fill();
        return stream;
    }

    /// <summary>
    /// Writes the token with its tag, closing and reopening at every line boundary inside it so no
    /// tag ever spans a line.
    /// </summary>
    private static void AppendTagged(StringBuilder markup, ReadOnlySpan<char> text, string category)
    {
        while (true)
        {
            var newline = text.IndexOf('\n');
            var segment = newline < 0 ? text : text[..newline];

            markup.Append('<').Append(category).Append('>')
                  .Append(segment)
                  .Append("</").Append(category).Append('>');

            if (newline < 0)
                return;

            markup.Append('\n');
            text = text[(newline + 1)..];
        }
    }

    /// <summary>
    /// Text that is not part of any token — whitespace, and characters the lexer skipped. Nothing
    /// downstream encodes it, so it is encoded here; whitespace encodes to itself, and a stray
    /// <c>&lt;</c> in a file that does not lex cleanly stops being markup.
    /// </summary>
    private static void AppendUntagged(StringBuilder markup, ReadOnlySpan<char> text) =>
        markup.Append(System.Net.WebUtility.HtmlEncode(text.ToString()));

    /// <summary>The category a token has on its own, with no tree to place it in.</summary>
    private static string Lexical(IToken token) => token.Type switch
    {
        modelicaParser.STRING => "STRING",
        modelicaParser.UNSIGNED_NUMBER => "NUMBER",
        modelicaParser.COMMENT => "COMMENT",
        modelicaParser.LINE_COMMENT => "COMMENT",
        modelicaParser.IDENT => "IDENT",
        _ => token.Text is { Length: > 0 } s && (char.IsLetter(s[0]) || s[0] == '_') ? "KEYWORD" : "OPERATOR",
    };

    /// <summary>
    /// The renderer state the categories depend on, carried down the walk instead of held in fields.
    /// The renderer keeps these as mutable fields and it costs it: <c>_isFunction</c> leaks into a
    /// reference's array subscripts and is then cleared by a nested call, so <c>den2[i] := …</c>
    /// colours the subscript as a function and <c>m[integer(i),j] := …</c> loses <c>j</c>. Scoped
    /// here, neither happens — the single deliberate difference from the renderer's output, and the
    /// whole of the 0.0016% the two disagree on (backlog B232).
    /// </summary>
    private readonly record struct Scope(
        bool NameAsType,
        bool IsFunction,
        bool InAnnotation,
        bool ClassAnnotation,
        bool ModificationIsGraphics,
        int GraphicsLevel);

    private static void Walk(IParseTree node, Dictionary<int, string> categories, Scope scope)
    {
        if (node is ITerminalNode terminal)
        {
            Categorise(terminal, categories, scope);
            return;
        }

        var context = node as ParserRuleContext;

        // The graphics array is recognised where the renderer recognises it (:2954): a '{' primary
        // that is the value of a modification named `graphics`, inside a class annotation.
        var entersGraphics = context is modelicaParser.PrimaryContext
                             && scope is { ClassAnnotation: true, ModificationIsGraphics: true }
                             && context.GetText().StartsWith('{');

        for (var i = 0; i < node.ChildCount; i++)
        {
            var child = node.GetChild(i);
            var inner = scope;

            // A primary consumes the pending `graphics=`; anything nested inside it is not it.
            if (context is modelicaParser.PrimaryContext)
                inner = inner with { ModificationIsGraphics = false };

            if (entersGraphics && child is modelicaParser.Array_argumentsContext)
                inner = inner with { GraphicsLevel = 1, ClassAnnotation = false };

            if (context is modelicaParser.AnnotationContext)
                inner = inner with { InAnnotation = true };

            // Only the annotations the composition itself carries are class annotations.
            if (context is modelicaParser.CompositionContext && child is modelicaParser.AnnotationContext)
                inner = inner with { ClassAnnotation = true };

            if (context is modelicaParser.Element_modificationContext modification)
                inner = inner with { ModificationIsGraphics = modification.name()?.GetText() == "graphics" };

            if (context is modelicaParser.Type_specifierContext)
                inner = inner with { NameAsType = true };

            if (context is modelicaParser.Import_clauseContext && child is modelicaParser.NameContext)
                inner = inner with { NameAsType = true };

            if (child is modelicaParser.Component_referenceContext)
                inner = inner with { IsFunction = IsCallTarget(context, child) };

            // One level per nested argument, in the three places the renderer counts them — and the
            // middle one is why a NAMED argument's value gets a level, since
            // `named_argument : IDENT '=' function_argument`. That is the level `DynamicSelect`
            // inside `Ellipse(lineColor=DynamicSelect(…))` is coloured at.
            if (inner.GraphicsLevel > 0 && BumpsGraphicsLevel(context, child))
                inner = inner with { GraphicsLevel = inner.GraphicsLevel + 1 };

            Walk(child, categories, inner);
        }
    }

    private static void Categorise(
        ITerminalNode terminal, Dictionary<int, string> categories, Scope scope)
    {
        var symbol = terminal.Symbol;
        if (symbol.TokenIndex < 0)
            return;

        if (symbol.Type == modelicaParser.IDENT)
        {
            categories[symbol.TokenIndex] = terminal.Parent switch
            {
                modelicaParser.NameContext => scope.NameAsType ? "TYPE" : "NAME",
                modelicaParser.Component_referenceContext =>
                    scope.IsFunction && (!scope.InAnnotation || scope.GraphicsLevel > 2)
                        ? "FUNCTION"
                        : "IDENT",
                _ => "IDENT",
            };
            return;
        }

        // `der`, `initial` and `pure` are keywords rather than identifiers, but in
        // `primary: (component_reference | 'der' | 'initial' | 'pure') function_call_args` they are
        // calls, and the renderer colours them as calls (:2899).
        if (terminal.Parent is modelicaParser.PrimaryContext primary
            && primary.function_call_args() is not null
            && primary.component_reference() is null
            && symbol.Text is "der" or "initial" or "pure"
            && ReferenceEquals(primary.GetChild(0), terminal))
        {
            categories[symbol.TokenIndex] = "FUNCTION";
            return;
        }

        categories[symbol.TokenIndex] = Lexical(symbol);
    }

    /// <summary>
    /// The three places <see cref="ModelicaRenderer"/> increments its graphics-annotation level
    /// (:3066, :3149, :3195), which is what re-enables function colouring deep inside a graphics
    /// annotation.
    /// </summary>
    private static bool BumpsGraphicsLevel(ParserRuleContext? context, IParseTree child) => context switch
    {
        modelicaParser.Function_argumentsContext arguments => ReferenceEquals(child, arguments.expression()),
        modelicaParser.Function_argumentContext argument => ReferenceEquals(child, argument.expression()),
        modelicaParser.Array_argumentsContext => child is modelicaParser.ExpressionContext,
        _ => false,
    };

    /// <summary>
    /// Whether a <c>component_reference</c> is the thing being called, which is what the renderer
    /// tracks with <c>_isFunction</c>.
    ///
    /// <para>An equation and a statement both read
    /// <c>component_reference (':=' expression | function_call_args)</c> or its equation equivalent,
    /// so the reference is a call in one form and a plain variable in the other. This mirrored the
    /// renderer in answering <c>true</c> for every statement, which coloured the target of
    /// <c>x := 1</c> as a function — reported against <c>y_dd</c> in MultiBody's
    /// <c>maxWithoutEvent_dd</c> and fixed on both sides (B254).</para>
    /// </summary>
    private static bool IsCallTarget(ParserRuleContext? parent, IParseTree reference) => parent switch
    {
        modelicaParser.EquationContext equation => equation.function_call_args() is not null,
        modelicaParser.StatementContext statement =>
            statement.expression() is null && statement.function_call_args() is { Length: > 0 },
        modelicaParser.PrimaryContext primary => primary.function_call_args() is not null,
        _ => false,
    };
}
