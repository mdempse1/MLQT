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
        {
            sb.AppendLine("No new findings.");
        }
        else
        {
            sb.AppendLine("| Severity | Status | Rule | Model | Line | Message |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var c in actionable)
            {
                var f = c.Finding;
                sb.AppendLine(
                    $"| {f.Severity.ToString().ToLowerInvariant()} | {c.Status} | {f.RuleId} | " +
                    $"{Cell(f.ModelId)} | {f.LineNumber} | {Cell(f.Message)} |");
            }
        }

        if (fixedCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"**Fixed in changed models ({fixedCount}):**");
            sb.AppendLine();
            foreach (var e in report.FixedEntries
                         .OrderBy(e => e.Model, StringComparer.Ordinal)
                         .ThenBy(e => e.RuleId, StringComparer.Ordinal))
                sb.AppendLine($"- {e.RuleId} — {Cell(e.Model)}: {Cell(e.Message)}");
        }

        return sb.ToString();
    }

    private static string Cell(string s) => s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");
}
