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

    /// <summary>
    /// For a style-check message, the structured rule id (e.g. "MLQT.Documentation.ParameterDescription")
    /// carried over from the originating <see cref="Finding"/>; <c>null</c> for other sources. Lets the UI
    /// offer a "suppress this rule here" action without re-deriving the rule.
    /// </summary>
    public string? RuleId { get; set; }

    /// <summary>
    /// For a style-check message, the element within the model the finding is about (e.g. a component
    /// name), or <c>null</c> for a class-level finding. Carried over from the originating
    /// <see cref="Finding"/>; used to scope a suppression to a single component.
    /// </summary>
    public string? ElementPath { get; set; }

    /// <summary>
    /// The originating <see cref="Finding"/>'s reformat-stable identity, or <c>null</c> for a message
    /// that has none (an external tool's output). Carried through so a consumer holding only messages
    /// — the desktop issues list — can still ask a baseline whether it already knows about this one.
    /// </summary>
    public string? Fingerprint { get; set; }

    /// <summary>
    /// For a rule that can fire more than once on the same element, what distinguishes this one — for
    /// a spelling rule, the flagged word. Carried over from the originating <see cref="Finding"/> so a
    /// consumer can act on the word itself rather than picking it back out of <see cref="Summary"/>,
    /// which cannot be done reliably: the word is quoted, and words contain quotes ("Stodola's").
    /// </summary>
    public string? Discriminator { get; set; }

    public LogMessage(string modelName, string severity, int lineNumber, string summary, string details = "")
    {
        ModelName = modelName;
        Severity = severity;
        LineNumber = lineNumber;
        Summary = summary;
        Details = details;
    }
}
