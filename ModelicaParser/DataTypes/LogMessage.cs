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
    /// Identifies the origin of this message. Used to selectively clear messages when a subsystem
    /// re-runs, and to decide what a surface is looking at — see the constants below, which are the
    /// values it takes.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// A finding from the style rules or the whole-graph analyses.
    ///
    /// <para>A constant because this is a load-bearing string contract — the phase 1 note lists it as
    /// one — and it was written out as a literal in nine places across six files: the one that
    /// produces it and five that filter on it, including the two that decide what the Metrics tab
    /// counts and what the Code Review list clears. A typo in a consumer filters nothing and looks
    /// exactly like "there were no findings". <c>Parser</c> already had a constant; this did
    /// not.</para>
    /// </summary>
    public const string StyleCheckingSource = "StyleChecking";

    /// <summary>A parse diagnostic. Kept apart from a style finding so a style re-run cannot clear it.</summary>
    public const string ParserSource = "Parser";

    /// <summary>A message from Dymola or OpenModelica.</summary>
    public const string ExternalToolSource = "ExternalTool";

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
    /// For a rule that can fire more than once on the same element, what distinguishes this one, so
    /// that two findings on the same element are still told apart. Carried over from the originating
    /// <see cref="Finding"/>, where it is part of the fingerprint.
    ///
    /// <para>It is not a field to read a value out of: what it holds is whatever made the finding
    /// unique. A description spelling puts the word in it; a documentation spelling puts the section
    /// and the word ("documentation info:tyre"). Anything wanting the flagged word should use
    /// <c>SpellingMessage.WordFrom</c>.</para>
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
