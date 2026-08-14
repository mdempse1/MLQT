using System.Text.Json;
using System.Text.Json.Serialization;
using MLQT.Services.Checking;

namespace MLQT.Cli;

/// <summary>Machine-readable JSON. Includes each finding's fingerprint and baseline status.</summary>
internal sealed class JsonFindingFormatter : IFindingFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Format(CheckReport report)
    {
        var payload = new
        {
            tool = "mlqt",
            library = report.LibraryPath,
            modelsChecked = report.ModelsChecked,
            findingCount = report.Findings.Count,
            hasBaseline = report.HasBaseline,
            summary = new
            {
                @new = report.CountOfStatus(FindingStatus.New),
                acceptedDebt = report.CountOfStatus(FindingStatus.AcceptedDebt),
                touchedDebt = report.CountOfStatus(FindingStatus.TouchedDebt)
            },
            findings = report.Findings.Select(c => new FindingJson(
                c.Finding.RuleId,
                c.Finding.Severity.ToString(),
                c.Status.ToString(),
                c.Finding.ModelId,
                c.Finding.ElementPath,
                c.Finding.LineNumber,
                c.Finding.Message,
                c.Finding.Fingerprint,
                report.FileFor(c.Finding))).ToList()
        };
        return JsonSerializer.Serialize(payload, Options);
    }

    private sealed record FindingJson(
        string RuleId, string Severity, string Status, string Model, string? Element,
        int Line, string Message, string Fingerprint, string? File);
}
