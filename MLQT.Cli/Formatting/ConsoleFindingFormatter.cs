using System.Text;
using ModelicaParser.DataTypes;

namespace MLQT.Cli;

/// <summary>Human-readable console output grouped by file.</summary>
internal sealed class ConsoleFindingFormatter(bool useColor) : IFindingFormatter
{
    private const char Esc = (char)27; // ANSI escape

    public string Format(CheckReport report)
    {
        var sb = new StringBuilder();

        if (report.Findings.Count == 0)
        {
            sb.AppendLine($"No findings in {report.ModelsChecked} model(s).");
            return sb.ToString();
        }

        foreach (var group in report.Findings
                     .GroupBy(f => report.FileFor(f) ?? f.ModelId)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.AppendLine(group.Key);
            foreach (var f in group)
                sb.AppendLine($"  {Severity(f.Severity)} {f.RuleId} (line {f.LineNumber}): {f.Message}");
            sb.AppendLine();
        }

        var errors = report.CountOf(RuleSeverity.Error);
        var warnings = report.CountOf(RuleSeverity.Warning);
        var infos = report.CountOf(RuleSeverity.Info);
        sb.AppendLine(
            $"{report.Findings.Count} finding(s): {errors} error(s), {warnings} warning(s), {infos} info " +
            $"across {report.ModelsChecked} model(s).");
        return sb.ToString();
    }

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
