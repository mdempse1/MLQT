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
            // ANTLR's `msg` is the readable diagnosis ("mismatched input '<EOF>' expecting ..."),
            // while RecognitionException does not override Message, so e.Message is the useless
            // default "Exception of type 'Antlr4.Runtime.InputMismatchException' was thrown."
            // Prefer msg and fall back to the exception only when ANTLR gave us nothing.
            Message = !string.IsNullOrWhiteSpace(msg) ? msg : e?.Message ?? string.Empty,
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
            Message = DescribeLexerError(msg)
        });
    }

    private const string TokenRecognitionPrefix = "token recognition error at: ";

    /// <summary>
    /// Rewrites ANTLR's lexer message into something a Modelica author can act on.
    ///
    /// For an unterminated string ANTLR reports <c>token recognition error at: '&lt;everything from
    /// the opening quote to the end of the file&gt;'</c> — accurate, but it neither names the problem
    /// nor fits in an issues table. The common cause by far is a missing closing quote (e.g. in a
    /// <c>Documentation(info="...")</c> annotation), so say that and keep a short excerpt as evidence.
    /// </summary>
    public static string DescribeLexerError(string? msg)
    {
        if (string.IsNullOrEmpty(msg) || !msg.StartsWith(TokenRecognitionPrefix, StringComparison.Ordinal))
            return msg ?? string.Empty;

        var text = msg[TokenRecognitionPrefix.Length..].Trim();
        if (text.Length >= 2 && text[0] == '\'' && text[^1] == '\'')
            text = text[1..^1];   // ANTLR wraps the offending text in single quotes

        return text.StartsWith('"')
            ? $"Unterminated string literal — no closing '\"' before the end of the file. Starts: {Excerpt(text)}"
            : $"Unrecognised input: {Excerpt(text)}";
    }

    /// <summary>Collapses the offending text to a single short line. ANTLR escapes newlines as the
    /// two characters <c>\n</c>, so those are collapsed too.</summary>
    private static string Excerpt(string text, int maxLength = 60)
    {
        var oneLine = text.Replace("\\r", " ").Replace("\\n", " ")
                          .Replace('\r', ' ').Replace('\n', ' ')
                          .Trim();
        return oneLine.Length <= maxLength ? $"'{oneLine}'" : $"'{oneLine[..maxLength]}…'";
    }
}
