using System.Text;
using ModelicaParser.DataTypes;
using MLQT.Services.Checking;

namespace MLQT.Cli;

/// <summary>
/// TeamCity service messages. The build-statistic values let TeamCity graph the baseline-debt trend
/// across builds with no database; a buildProblem is emitted when the gate fails.
/// </summary>
internal sealed class TeamCityFindingFormatter : IFindingFormatter
{
    public string Format(CheckReport report)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"##teamcity[buildStatisticValue key='mlqt.findings.new' value='{report.CountOfStatus(FindingStatus.New)}']");
        sb.AppendLine($"##teamcity[buildStatisticValue key='mlqt.findings.acceptedDebt' value='{report.CountOfStatus(FindingStatus.AcceptedDebt)}']");
        sb.AppendLine($"##teamcity[buildStatisticValue key='mlqt.findings.touchedDebt' value='{report.CountOfStatus(FindingStatus.TouchedDebt)}']");

        foreach (var c in report.Findings.Where(x => x.Status != FindingStatus.AcceptedDebt))
        {
            var f = c.Finding;
            var status = f.Severity == RuleSeverity.Error ? "ERROR" : "WARNING";
            var text = $"{f.RuleId} [{f.ModelId}:{f.LineNumber}] {f.Message}";
            sb.AppendLine($"##teamcity[message text='{Escape(text)}' status='{status}']");
        }

        if (!report.GatePassed)
            sb.AppendLine($"##teamcity[buildProblem description='{Escape($"mlqt: {report.GateFailureCount} finding(s) failed the quality gate")}']");

        return sb.ToString();
    }

    // TeamCity service-message escaping: https://www.jetbrains.com/help/teamcity/service-messages.html
    private static string Escape(string s) => s
        .Replace("|", "||")
        .Replace("'", "|'")
        .Replace("\n", "|n")
        .Replace("\r", "|r")
        .Replace("[", "|[")
        .Replace("]", "|]");
}
