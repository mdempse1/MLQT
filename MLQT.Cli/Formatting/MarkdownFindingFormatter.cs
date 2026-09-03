using System.Text;
using MLQT.Services.Checking;

namespace MLQT.Cli;

/// <summary>A PR-comment-ready markdown summary.</summary>
internal sealed class MarkdownFindingFormatter : IFindingFormatter
{
    public string Format(CheckReport report)
    {
        var sb = new StringBuilder();

        var newCount = report.CountOfStatus(FindingStatus.New);
        var touched = report.CountOfStatus(FindingStatus.TouchedDebt);
        var accepted = report.CountOfStatus(FindingStatus.AcceptedDebt);
        var fixedCount = report.FixedEntries.Count;
        var gate = report.GatePassed ? "passed" : "failed";

        sb.AppendLine(
            $"## MLQT check — {newCount} new, {touched} touched, {accepted} accepted, " +
            $"{fixedCount} fixed (gate: {gate})");
        sb.AppendLine();

        var actionable = report.Findings
            .Where(c => c.Status != FindingStatus.AcceptedDebt)
            .ToList();

        if (actionable.Count == 0)
            sb.AppendLine("No new findings.");
        else
            sb.Append(Markdown.FindingsTable(report, actionable));

        if (fixedCount > 0)
        {
            sb.AppendLine();
            sb.Append(Markdown.FixedEntries(report.FixedEntries));
        }

        return sb.ToString();
    }
}
