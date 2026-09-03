using System.Text;
using ModelicaParser.DataTypes;
using MLQT.Services.Checking;

namespace MLQT.Cli;

/// <summary>
/// The pieces of markdown that more than one report is made of.
///
/// <para>Both the summary a CI job pastes into a pull request and the body of a review posted to one
/// list findings in a table and credit what was fixed. They were written twice, byte for byte, which
/// is how a column added to one becomes a column missing from the other — and how the pipe escaping
/// gets fixed in one place and not the other.</para>
/// </summary>
internal static class Markdown
{
    /// <summary>
    /// A cell's text. A pipe in a message would otherwise end the cell early and shift every column
    /// after it, and a newline would end the row — silently, since the result is still valid
    /// markdown, just describing something else.
    /// </summary>
    public static string Cell(string s) =>
        s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");

    public static string Severity(RuleSeverity severity) => severity.ToString().ToLowerInvariant();

    /// <summary>The findings table, header included. Empty string for no findings — the caller says
    /// what "none" means in its own words, which differ between a summary and a review.</summary>
    public static string FindingsTable(CheckReport report, IEnumerable<ClassifiedFinding> findings)
    {
        var list = findings.ToList();
        if (list.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("| Severity | Status | Rule | Model | Line | Message |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- |");

        foreach (var c in list)
        {
            var f = c.Finding;
            sb.AppendLine(
                $"| {Severity(f.Severity)} | {c.Status} | {f.RuleId} | {Cell(f.ModelId)} | " +
                $"{report.LineFor(f)} | {Cell(f.Message)} |");
        }

        return sb.ToString();
    }

    /// <summary>
    /// What the change fixed, as a bullet list. Worth saying: a report that only ever lists what is
    /// wrong makes cleaning up look like it achieved nothing.
    /// </summary>
    public static string FixedEntries(IReadOnlyList<BaselineEntry> entries)
    {
        if (entries.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine($"**Fixed in changed models ({entries.Count}):**");
        sb.AppendLine();

        foreach (var e in entries
                     .OrderBy(e => e.Model, StringComparer.Ordinal)
                     .ThenBy(e => e.RuleId, StringComparer.Ordinal))
            sb.AppendLine($"- {e.RuleId} — {Cell(e.Model)}: {Cell(e.Message)}");

        return sb.ToString();
    }
}
