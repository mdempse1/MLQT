using System.Text;
using ModelicaParser.DataTypes;
using MLQT.Services.Checking;

namespace MLQT.Cli;

/// <summary>Human-readable console output grouped by file.</summary>
internal sealed class ConsoleFindingFormatter(bool useColor) : IFindingFormatter
{
    private const char Esc = (char)27; // ANSI escape

    public string Format(CheckReport report) =>
        report.HasBaseline ? FormatWithBaseline(report) : FormatPlain(report);

    private string FormatPlain(CheckReport report)
    {
        var sb = new StringBuilder();

        if (report.Findings.Count == 0)
        {
            sb.AppendLine($"No findings in {report.ModelsChecked} model(s).");
            return sb.ToString();
        }

        AppendGrouped(sb, report, report.Findings, showStatus: false);

        var errors = report.CountOfSeverity(RuleSeverity.Error);
        var warnings = report.CountOfSeverity(RuleSeverity.Warning);
        var infos = report.CountOfSeverity(RuleSeverity.Info);
        sb.AppendLine(
            $"{report.Findings.Count} finding(s): {errors} error(s), {warnings} warning(s), {infos} info " +
            $"across {report.ModelsChecked} model(s).");
        return sb.ToString();
    }

    private string FormatWithBaseline(CheckReport report)
    {
        var sb = new StringBuilder();

        var actionable = report.Findings
            .Where(c => c.Status != FindingStatus.AcceptedDebt)
            .ToList();
        var accepted = report.CountOfStatus(FindingStatus.AcceptedDebt);
        var newCount = report.CountOfStatus(FindingStatus.New);
        var touched = report.CountOfStatus(FindingStatus.TouchedDebt);

        if (actionable.Count == 0)
            sb.AppendLine(
                $"No new findings ({accepted} finding(s) accepted as baseline debt) " +
                $"in {report.ModelsChecked} model(s).");
        else
            AppendGrouped(sb, report, actionable, showStatus: true);

        var fixedCount = report.FixedEntries.Count;
        if (fixedCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Fixed in changed models ({fixedCount}):");
            foreach (var e in report.FixedEntries
                         .OrderBy(e => e.Model, StringComparer.Ordinal)
                         .ThenBy(e => e.RuleId, StringComparer.Ordinal))
                sb.AppendLine($"  {e.Model}  {e.RuleId}: {e.Message}");
            sb.AppendLine();
        }

        var fixedText = fixedCount > 0 ? $", {fixedCount} fixed" : string.Empty;
        sb.AppendLine(
            $"{newCount} new, {touched} touched-debt, {accepted} accepted as baseline debt{fixedText} " +
            $"across {report.ModelsChecked} model(s).");
        return sb.ToString();
    }

    private void AppendGrouped(StringBuilder sb, CheckReport report, IReadOnlyList<ClassifiedFinding> items, bool showStatus)
    {
        // Group by model (not just file) so a file with several models still shows which model each
        // violation belongs to; the model's file is shown alongside for navigation.
        foreach (var group in items
                     .GroupBy(c => c.Finding.ModelId)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var file = report.FileFor(group.First().Finding);
            sb.AppendLine(file is null ? group.Key : $"{group.Key}  ({RelativeFile(report, file)})");
            foreach (var c in group)
            {
                var status = showStatus ? StatusTag(c.Status) + " " : string.Empty;
                sb.AppendLine($"  {status}{Severity(c.Finding.Severity)} {c.Finding.RuleId} (line {c.Finding.LineNumber}): {c.Finding.Message}");
            }
            sb.AppendLine();
        }
    }

    private static string RelativeFile(CheckReport report, string file)
    {
        try { return Path.GetRelativePath(report.LibraryPath, file).Replace('\\', '/'); }
        catch { return file; }
    }

    private static string StatusTag(FindingStatus status) => status switch
    {
        FindingStatus.New => "[new]",
        FindingStatus.TouchedDebt => "[touched]",
        _ => "[accepted]"
    };

    private string Severity(RuleSeverity severity)
    {
        var label = $"[{severity.ToString().ToLowerInvariant()}]";
        if (!useColor)
            return label;

        var code = severity switch
        {
            RuleSeverity.Error => "31",   // red
            RuleSeverity.Warning => "33", // yellow
            RuleSeverity.Info => "36",    // cyan
            _ => "0"
        };
        return $"{Esc}[{code}m{label}{Esc}[0m";
    }
}
