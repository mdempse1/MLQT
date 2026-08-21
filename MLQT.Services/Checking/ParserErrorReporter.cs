using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

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
    /// One message per parser error across <paramref name="models"/>. The message is keyed on the
    /// node id (the full Modelica path) rather than the short class name, because that is what
    /// <c>NavState.ModelID</c> holds — row-click navigation and the "only this model" filter both
    /// match against it.
    /// </summary>
    public static List<LogMessage> ToLogMessages(IEnumerable<ModelNode> models)
    {
        var messages = new List<LogMessage>();

        foreach (var model in models)
        {
            if (model?.Definition?.ParserErrors is not { Count: > 0 } errors)
                continue;

            foreach (var error in errors)
            {
                // A fatal failure means the file could not be parsed at all and a placeholder stands
                // in for it; a recovered syntax error means the rest of the file still loaded, so the
                // two need to read differently in the issues list.
                var isFatal = error.Severity == ParserErrorSeverity.FatalParseFailure;
                var details = error.Message +
                              (error.OffendingToken is not null ? $" (token: '{error.OffendingToken}')" : "");

                messages.Add(new LogMessage(
                    model.Id,
                    isFatal ? "Fatal" : "Error",
                    error.Line,
                    isFatal ? "Fatal parse failure" : "Parser error",
                    details)
                {
                    Source = SourceName
                });
            }
        }

        return messages;
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
