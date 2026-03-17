using Antlr4.Runtime;
using ModelicaParser.DataTypes;

namespace ModelicaParser;

/// <summary>
/// Custom error listener that collects parser errors for later analysis.
/// </summary>
public class ModelicaErrorListener : BaseErrorListener
{
    /// <summary>
    /// Gets all errors collected during parsing.
    /// </summary>
    public List<ParserError> Errors { get; } = new();

    public override void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        var error = new ParserError
        {
            Line = e != null ? e.OffendingToken.Line : line,
            CharPosition = e != null ? e.OffendingToken.Column : charPositionInLine,
            Message = e != null ? e.Message : msg,
            OffendingToken = offendingSymbol?.Text
        };

        Errors.Add(error);
    }
}
