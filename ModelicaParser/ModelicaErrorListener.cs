using Antlr4.Runtime;
using ModelicaParser.DataTypes;

namespace ModelicaParser;

/// <summary>
/// Custom error listener that collects both parser and lexer errors for later analysis.
/// Implements IAntlrErrorListener&lt;int&gt; for lexer errors in addition to BaseErrorListener for parser errors.
/// </summary>
public class ModelicaErrorListener : BaseErrorListener, IAntlrErrorListener<int>
{
    /// <summary>
    /// Gets all errors collected during parsing.
    /// </summary>
    public List<ParserError> Errors { get; } = new();

    /// <summary>
    /// Handles parser errors (offending symbol is an IToken).
    /// </summary>
    public override void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        Errors.Add(new ParserError
        {
            Line = e?.OffendingToken?.Line ?? line,
            CharPosition = e?.OffendingToken?.Column ?? charPositionInLine,
            Message = e?.Message ?? msg,
            OffendingToken = offendingSymbol?.Text
        });
    }

    /// <summary>
    /// Handles lexer errors (offending symbol is an int character code).
    /// These produce "token recognition error" messages in the debug console by default.
    /// </summary>
    public void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        int offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        Errors.Add(new ParserError
        {
            Line = line,
            CharPosition = charPositionInLine,
            Message = msg
        });
    }
}
