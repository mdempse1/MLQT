using System.Text.Json;
using System.Text.Json.Serialization;

namespace MLQT.Cli;

/// <summary>Machine-readable JSON. Includes each finding's fingerprint (baseline-ready for Phase 3).</summary>
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
            findings = report.Findings.Select(f => new FindingJson(
                f.RuleId,
                f.Severity.ToString(),
                f.ModelId,
                f.ElementPath,
                f.LineNumber,
                f.Message,
                f.Fingerprint,
                report.FileFor(f))).ToList()
        };
        return JsonSerializer.Serialize(payload, Options);
    }

    private sealed record FindingJson(
        string RuleId, string Severity, string Model, string? Element,
        int Line, string Message, string Fingerprint, string? File);
}
