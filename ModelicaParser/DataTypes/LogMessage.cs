namespace ModelicaParser.DataTypes;

/// <summary>
/// Represents a log message for style checking or parsing issues.
/// </summary>
public class LogMessage
{
    /// <summary>
    /// Name of the model (full Modelica path).
    /// </summary>
    public string ModelName { get; set; }

    /// <summary>
    /// Summary of the issue found.
    /// </summary>
    public string Summary { get; set; }

    /// <summary>
    /// Details of the issue found.
    /// </summary>
    public string Details { get; set; }

    /// <summary>
    /// Severity level of the issue (e.g., Warning, Error).
    /// </summary>
    public string Severity { get; set; }

    /// <summary>
    /// Starting line number in the source file.
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Identifies the origin of this message (e.g., "StyleChecking", "Parser", "ExternalTool").
    /// Used to selectively clear messages when a subsystem re-runs.
    /// </summary>
    public string Source { get; set; } = "";

    public LogMessage(string modelName, string severity, int lineNumber, string summary, string details = "")
    {
        ModelName = modelName;
        Severity = severity;
        LineNumber = lineNumber;
        Summary = summary;
        Details = details;
    }
}
