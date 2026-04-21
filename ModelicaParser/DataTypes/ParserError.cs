namespace ModelicaParser.DataTypes;

/// <summary>
/// Severity of a <see cref="ParserError"/>.
/// </summary>
public enum ParserErrorSeverity
{
    /// <summary>
    /// A syntax error reported by the ANTLR parser or lexer. The parser recovered and
    /// the rest of the file was still processed.
    /// </summary>
    RecoveredSyntax,

    /// <summary>
    /// An unrecoverable failure while extracting models from a file (e.g., the parse tree
    /// was too malformed for the visitor to traverse). No models from the affected file
    /// were extracted and a placeholder was produced in its place.
    /// </summary>
    FatalParseFailure
}

/// <summary>
/// Represents a parsing error with location and message information.
/// </summary>
public class ParserError
{
    /// <summary>
    /// Line number where the error occurred. Zero if no position is known.
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// Character position within the line where the error occurred.
    /// </summary>
    public int CharPosition { get; set; }

    /// <summary>
    /// Error message describing the issue.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// The token that caused the error, if available.
    /// </summary>
    public string? OffendingToken { get; set; }

    /// <summary>
    /// Classification of this error. Recovered syntax errors did not prevent the rest
    /// of the file being processed; fatal parse failures did.
    /// </summary>
    public ParserErrorSeverity Severity { get; set; } = ParserErrorSeverity.RecoveredSyntax;
}
