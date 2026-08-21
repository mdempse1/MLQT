using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;

namespace MLQT.Services.Checking;

/// <summary>
/// Turns the parser errors recorded on graph nodes into issue-list messages.
///
/// Parser errors are derived state: they live on <c>ModelNode.Definition.ParserErrors</c> and are
/// rewritten whenever a file is re-read. Anything that clears the issue list for a set of models has
/// to re-derive them afterwards, which is why the conversion lives here rather than inline in a page —
/// a page only surfaces what it happens to be mounted for.
/// </summary>
public static class ParserErrorReporter
{
    /// <summary>
    /// Stamped on every message produced here so a caller can refresh parser issues without
    /// disturbing style-checking findings (and vice versa).
    /// </summary>
    public const string SourceName = "Parser";

    /// <summary>
    /// One message per parser error across <paramref name="models"/>, in the flat shape the GUI's
    /// issue list consumes. Defined in terms of <see cref="ToFindings"/> so a parse error reads
    /// identically whichever surface reported it.
    /// </summary>
    public static List<LogMessage> ToLogMessages(IEnumerable<ModelNode> models)
        => ToFindings(models).Select(ToLogMessage).ToList();

    /// <summary>
    /// Projects a finding produced by <see cref="ToFindings"/> into the flat message shape.
    /// <see cref="Finding.ToLogMessage"/> deliberately renders every finding as a style warning, which
    /// is wrong for a parse diagnostic — it is an error, not an opinion, and it must not be cleared by
    /// a style-checking re-run.
    /// </summary>
    public static LogMessage ToLogMessage(Finding finding)
    {
        var isFatal = finding.RuleId == RuleIds.ParseFailure;
        return new LogMessage(
            finding.ModelId,
            isFatal ? "Fatal" : "Error",
            finding.LineNumber,
            isFatal ? "Fatal parse failure" : "Parser error",
            finding.Message)
        {
            Source = SourceName,
            RuleId = finding.RuleId,
            Fingerprint = finding.Fingerprint
        };
    }

    /// <summary>
    /// The same errors as structured findings, for the surfaces that report <see cref="Finding"/>s
    /// (the CLI and MCP) rather than the GUI's flat message list.
    ///
    /// These are emitted unconditionally at <see cref="RuleSeverity.Error"/>: unlike a style rule
    /// there is nothing to opt into, and a file that does not parse makes every other rule's result
    /// unreliable, so a check that stayed silent about it would be reporting a clean bill of health
    /// on code it never read. They carry a rule id so they flow through the normal formatting paths,
    /// but callers must not put them through the severity map.
    ///
    /// The error message is used as the fingerprint discriminator so several errors in one class stay
    /// distinct — a line number would not, since it moves whenever the file above it is edited.
    /// </summary>
    public static List<Finding> ToFindings(IEnumerable<ModelNode> models)
    {
        var findings = new List<Finding>();

        foreach (var model in models)
        {
            if (model?.Definition?.ParserErrors is not { Count: > 0 } errors)
                continue;

            foreach (var error in errors)
            {
                // A fatal failure means the file could not be parsed at all and a placeholder stands
                // in for it; a recovered syntax error means the rest of the file still loaded, so the
                // two are separate rule ids and read differently.
                var isFatal = error.Severity == ParserErrorSeverity.FatalParseFailure;
                findings.Add(new Finding
                {
                    RuleId = isFatal ? RuleIds.ParseFailure : RuleIds.SyntaxError,
                    ModelId = model.Id,
                    Discriminator = error.Message,
                    Message = error.Message +
                              (error.OffendingToken is not null ? $" (token: '{error.OffendingToken}')" : ""),
                    LineNumber = error.Line,
                    Severity = RuleSeverity.Error
                });
            }
        }

        return findings;
    }

    /// <summary>Counts parser errors by kind, for a load-time summary notification.</summary>
    public static (int Fatal, int Recovered) Count(IEnumerable<ModelNode> models)
    {
        var fatal = 0;
        var recovered = 0;

        foreach (var model in models)
        {
            if (model?.Definition?.ParserErrors is not { Count: > 0 } errors)
                continue;

            foreach (var error in errors)
            {
                if (error.Severity == ParserErrorSeverity.FatalParseFailure)
                    fatal++;
                else
                    recovered++;
            }
        }

        return (fatal, recovered);
    }
}
