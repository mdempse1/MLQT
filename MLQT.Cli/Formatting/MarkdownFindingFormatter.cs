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
        var gate = report.GatePassed ? "passed" : "failed";

        sb.AppendLine($"## MLQT check — {newCount} new, {touched} touched, {accepted} accepted (gate: {gate})");
        sb.AppendLine();

        var actionable = report.Findings
            .Where(c => c.Status != FindingStatus.AcceptedDebt)
            .ToList();

        if (actionable.Count == 0)
        {
            sb.AppendLine("No new findings.");
            return sb.ToString();
        }

        sb.AppendLine("| Severity | Status | Rule | Model | Line | Message |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var c in actionable)
        {
            var f = c.Finding;
            sb.AppendLine(
                $"| {f.Severity.ToString().ToLowerInvariant()} | {c.Status} | {f.RuleId} | " +
                $"{Cell(f.ModelId)} | {f.LineNumber} | {Cell(f.Message)} |");
        }

        return sb.ToString();
    }

    private static string Cell(string s) => s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");
}
