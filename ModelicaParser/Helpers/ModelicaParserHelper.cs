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
        if (!code.EndsWith(';'))
            code += ";";
        return code;
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
