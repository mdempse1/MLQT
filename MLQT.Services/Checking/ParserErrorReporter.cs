using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using MLQT.Services.Interfaces;

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
    public const string SourceName = LogMessage.ParserSource;

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

            // The load parses whole files, so an error it recorded is on the file's line. Findings
            // carry class-relative lines (see Finding.LineNumber), and for a class nested in a
            // package.mo the two are hundreds of lines apart — which is how a parse error came to
            // point at an unrelated line of the class the app was showing. An error recorded later,
            // by parsing the class's own source, is already relative to the class and is taken as it
            // is: subtracting the start line again put it on line 1 (B388).
            var classStart = model.StartLine > 0 ? model.StartLine : 1;

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
                    LineNumber = Math.Max(1, error.LineIsClassRelative ? error.Line : LoadLineInClass(model, error.Line - classStart + 1)),
                    Severity = RuleSeverity.Error
                });
            }
        }

        return findings;
    }

    /// <summary>
    /// A load-recorded error's line, already relative to the class as the file has it, in the text
    /// the class's other findings are counted against.
    ///
    /// <para>For most classes the two are the same text. A package whose inline standalone children
    /// were trimmed is checked as the file's lines with the children cut out, and keeps the load's
    /// errors through the trim; <see cref="ClassLocation.FileLine"/> and the Code Review page both
    /// put the cut lines back through <see cref="ModelNode.TrimElision"/>, so a line left in the
    /// untrimmed frame had them counted twice, and an error below an inline child landed that
    /// child's length too far down (B413). An error on lines that were cut - which the load gives to
    /// the child, not the package - is put on the last line kept above the cut.</para>
    /// </summary>
    private static int LoadLineInClass(ModelNode model, int lineInUntrimmedClass)
    {
        if (model.TrimElision is not { } trim || lineInUntrimmedClass < 1)
            return lineInUntrimmedClass;

        for (var line = lineInUntrimmedClass; line >= 1; line--)
        {
            if (trim.ToDisplayLine(line) is { } kept)
                return kept;
        }

        return 1;
    }

    /// <summary>
    /// Puts the parser findings for <paramref name="models"/> on <paramref name="store"/> as the
    /// classes have them now, replacing whatever parser findings it held for them - so reading
    /// again neither duplicates an error nor keeps one the class no longer has.
    ///
    /// <para><b>Read after anything that parses, not only after a load.</b> A class nothing has
    /// parsed yet records its errors when something first does, and for the app that is the style
    /// check. Read only before the check, such an error reached the list when something unrelated
    /// happened to read again, while the tree badge - which asks the class - showed it at once
    /// (B390, the ordering B352 fixed in <see cref="LibraryCheckSession"/>).</para>
    /// </summary>
    public static void Refresh(ICodeReviewService store, IReadOnlyCollection<ModelNode> models)
    {
        if (models.Count == 0)
            return;

        var ids = models.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        store.RemoveLogMessagesByPredicate(m => m.Source == SourceName && ids.Contains(m.ModelName));

        var messages = ToLogMessages(models);
        if (messages.Count > 0)
            store.AddLogMessages(messages);
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
