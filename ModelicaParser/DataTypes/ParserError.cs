namespace ModelicaParser.DataTypes;

/// <summary>
/// Represents a parsing error with location and message information.
/// </summary>
public class ParserError
{
    /// <summary>
    /// Line number where the error occurred.
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
}
