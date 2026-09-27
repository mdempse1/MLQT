using Antlr4.Runtime;
using ModelicaParser.DataTypes;
using ModelicaParser.Visitors;

namespace ModelicaParser.Helpers;

/// <summary>
/// Helper class for parsing Modelica source code using the ANTLR-generated parser.
/// </summary>
public class ModelicaParserHelper
{
    /// <summary>
    /// Normalizes line endings to LF (\n) for consistent processing.
    /// This prevents spurious "changes" when files are read with CRLF and written with LF.
    /// </summary>
    /// <param name="text">The text to normalize.</param>
    /// <returns>Text with all line endings converted to LF.</returns>
    public static string NormalizeLineEndings(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Replace CRLF with LF, then replace any remaining CR with LF
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    /// <summary>
    /// Prepares Modelica source code for parsing by normalizing line endings,
    /// trimming trailing whitespace, and ensuring a trailing semicolon.
    /// Leading whitespace is preserved to maintain accurate ANTLR line numbers.
    /// </summary>
    /// <param name="modelicaCode">The raw Modelica source code.</param>
    /// <returns>Preprocessed code ready for the ANTLR parser.</returns>
    public static string PreprocessCode(string modelicaCode)
    {
        var code = NormalizeLineEndings(modelicaCode).TrimEnd();
        if (code.EndsWith(';'))
            return code;

        // A file may end in a comment after its last `end X;` (B430). A ';' appended to a line
        // comment became part of it - `// trailer;`, written back by every save - and one after a
        // block comment was a stray ';' and a syntax error. So when the text ends in a comment, the
        // question is whether what comes before the comments ends in one. Lexed only then, which a
        // class's stored source (ending in its name) and a file ending in `end X;` never are.
        if (MayEndInComment(code))
        {
            var (last, lastSignificant) = LastTokens(code);
            if (last is { Type: modelicaLexer.COMMENT or modelicaLexer.LINE_COMMENT })
                return lastSignificant is null || lastSignificant.Text == ";"
                    ? code
                    : code + "\n;";   // on a line of its own, out of the comment's reach
        }

        return code + ";";
    }

    private static bool MayEndInComment(string code) =>
        code.EndsWith("*/", StringComparison.Ordinal)
        || code.AsSpan(code.LastIndexOf('\n') + 1).Contains("//", StringComparison.Ordinal);

    /// <summary>The last token on the default channel, and the last of those that is not a comment.</summary>
    private static (IToken? Last, IToken? LastSignificant) LastTokens(string code)
    {
        var lexer = new modelicaLexer(new AntlrInputStream(code));
        lexer.RemoveErrorListeners();
        IToken? last = null, lastSignificant = null;
        for (var token = lexer.NextToken(); token.Type != TokenConstants.EOF; token = lexer.NextToken())
        {
            if (token.Channel != TokenConstants.DefaultChannel)
                continue;
            last = token;
            if (token.Type is not (modelicaLexer.COMMENT or modelicaLexer.LINE_COMMENT))
                lastSignificant = token;
        }
        return (last, lastSignificant);
    }

    /// <summary>
    /// Parses Modelica source code and returns the parse tree.
    /// </summary>
    /// <param name="modelicaCode">The Modelica source code to parse.</param>
    /// <returns>The root node of the parse tree.</returns>
    public static modelicaParser.Stored_definitionContext Parse(string modelicaCode)
    {
        var code = PreprocessCode(modelicaCode);
        var inputStream = new AntlrInputStream(code);
        var lexer = new modelicaLexer(inputStream);
        lexer.RemoveErrorListeners();
        var tokenStream = new CommonTokenStream(lexer);
        var parser = new modelicaParser(tokenStream);
        parser.RemoveErrorListeners();

        return parser.stored_definition();
    }

    /// <summary>
    /// Parses Modelica source code and returns the parse tree along with any parser errors.
    /// </summary>
    /// <param name="modelicaCode">The Modelica source code to parse.</param>
    /// <returns>Tuple containing the parse tree and list of parser errors.</returns>
    public static (modelicaParser.Stored_definitionContext parseTree, List<ParserError> errors) ParseWithErrors(string modelicaCode)
    {
        var code = PreprocessCode(modelicaCode);
        var inputStream = new AntlrInputStream(code);
        var lexer = new modelicaLexer(inputStream);
        var errorListener = new ModelicaErrorListener();
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);
        var tokenStream = new CommonTokenStream(lexer);
        var parser = new modelicaParser(tokenStream);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errorListener);

        var parseTree = parser.stored_definition();
        return (parseTree, errorListener.Errors);
    }

    /// <summary>
    /// Parses Modelica source code and returns both the parse tree and token stream.
    /// </summary>
    /// <param name="modelicaCode">The Modelica source code to parse.</param>
    /// <returns>Tuple containing the parse tree and token stream.</returns>
    public static (modelicaParser.Stored_definitionContext parseTree, BufferedTokenStream tokenStream) ParseWithTokens(string modelicaCode)
    {
        var code = PreprocessCode(modelicaCode);
        var inputStream = new AntlrInputStream(code);
        var lexer = new modelicaLexer(inputStream);
        var errorListener = new ModelicaErrorListener();
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);
        var tokenStream = new CommonTokenStream(lexer);
        var parser = new modelicaParser(tokenStream);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errorListener);

        return (parser.stored_definition(), tokenStream);
    }

    /// <summary>
    /// Parses Modelica source code and returns the parse tree, its token stream, AND any lexer/parser
    /// errors — the combination needed to both render (the token stream preserves comments) and report
    /// syntax problems in a single parse.
    /// </summary>
    /// <param name="modelicaCode">The Modelica source code to parse.</param>
    /// <returns>Tuple containing the parse tree, token stream, and list of parser errors.</returns>
    public static (modelicaParser.Stored_definitionContext parseTree, BufferedTokenStream tokenStream, List<ParserError> errors) ParseWithTokensAndErrors(string modelicaCode)
    {
        var code = PreprocessCode(modelicaCode);
        var inputStream = new AntlrInputStream(code);
        var lexer = new modelicaLexer(inputStream);
        var errorListener = new ModelicaErrorListener();
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);
        var tokenStream = new CommonTokenStream(lexer);
        var parser = new modelicaParser(tokenStream);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errorListener);

        var parseTree = parser.stored_definition();
        return (parseTree, tokenStream, errorListener.Errors);
    }

    /// <summary>
    /// Parses a Modelica <c>modification_expression</c> — the grammar rule that
    /// covers everything a default-value binding (or other modifier expression)
    /// can be: any <c>expression</c> shape, plus the literal <c>break</c> used
    /// in <c>else break</c> contexts. Returns the parsed tree along with the
    /// token stream so callers can use ANTLR <c>Interval</c> ranges to extract
    /// original source text and run <see cref="Antlr4.Runtime.TokenStreamRewriter"/>.
    /// </summary>
    public static (modelicaParser.Modification_expressionContext parseTree,
                   CommonTokenStream tokenStream,
                   List<ParserError> errors)
        ParseModificationExpression(string expression)
    {
        var inputStream = new AntlrInputStream(expression ?? string.Empty);
        var lexer = new modelicaLexer(inputStream);
        var errorListener = new ModelicaErrorListener();
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);
        var tokenStream = new CommonTokenStream(lexer);
        var parser = new modelicaParser(tokenStream);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errorListener);

        var parseTree = parser.modification_expression();
        return (parseTree, tokenStream, errorListener.Errors);
    }

    /// <summary>
    /// Extracts all model definitions from Modelica source code.
    /// </summary>
    /// <param name="modelicaCode">The Modelica source code to parse.</param>
    /// <returns>List of model information extracted from the code.</returns>
    public static List<ModelInfo> ExtractModels(string modelicaCode)
    {
        var (models, _) = ExtractModelsWithErrors(modelicaCode);
        return models;
    }

    /// <summary>
    /// Extracts all model definitions from Modelica source code, also returning any parser/lexer errors.
    ///
    /// Catches exceptions thrown during tree traversal (which can happen when ANTLR error
    /// recovery leaves the parse tree in a shape the visitor can't handle) and records them
    /// as a <see cref="ParserErrorSeverity.FatalParseFailure"/> entry so callers can decide
    /// whether to produce a placeholder rather than propagate the crash.
    /// </summary>
    /// <param name="modelicaCode">The Modelica source code to parse.</param>
    /// <returns>Tuple containing the list of models and any parser errors encountered.</returns>
    public static (List<ModelInfo> models, List<ParserError> errors) ExtractModelsWithErrors(string modelicaCode)
    {
        var code = PreprocessCode(modelicaCode);
        var inputStream = new AntlrInputStream(code);
        var lexer = new modelicaLexer(inputStream);
        var errorListener = new ModelicaErrorListener();
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);
        var tokenStream = new CommonTokenStream(lexer);
        var parser = new modelicaParser(tokenStream);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errorListener);

        var visitor = new ModelExtractorVisitor(code);
        try
        {
            var parseTree = parser.stored_definition();
            visitor.Visit(parseTree);
        }
        catch (Exception ex)
        {
            // The parse tree (or the parser itself on a ParseCanceledException) didn't
            // survive long enough to produce a full model list. Use the first recovered
            // syntax error (if any) to point the user at the likely source of the problem;
            // that's almost always more informative than the stack trace.
            var firstSyntax = errorListener.Errors.FirstOrDefault();
            errorListener.Errors.Add(new ParserError
            {
                Line = firstSyntax?.Line ?? 0,
                CharPosition = firstSyntax?.CharPosition ?? 0,
                Message = firstSyntax != null
                    ? $"Unable to extract models from file. First syntax error at line {firstSyntax.Line}: {firstSyntax.Message}. Inner: {ex.GetType().Name}: {ex.Message}"
                    : $"Unable to extract models from file. {ex.GetType().Name}: {ex.Message}",
                OffendingToken = firstSyntax?.OffendingToken,
                Severity = ParserErrorSeverity.FatalParseFailure
            });
        }
        return (visitor.Models, errorListener.Errors);
    }

}
