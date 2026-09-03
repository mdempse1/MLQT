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
                touchedDebt = report.CountOfStatus(FindingStatus.TouchedDebt),
                @fixed = report.FixedEntries.Count
            },
            @fixed = report.FixedEntries
                .Select(e => new FixedJson(e.RuleId, e.Model, e.Element, e.Message, e.Fingerprint))
                .ToList(),
            findings = report.Findings.Select(c => new FindingJson(
                c.Finding.RuleId,
                c.Finding.Severity.ToString(),
                c.Status.ToString(),
                c.Finding.ModelId,
                c.Finding.ElementPath,
                report.LineFor(c.Finding),
                c.Finding.LineNumber,
                c.Finding.Message,
                c.Finding.Fingerprint,
                report.FileFor(c.Finding))).ToList()
        };
        return JsonSerializer.Serialize(payload, Options);
    }

    /// <param name="Line">The line in <paramref name="File"/> — what an editor or annotation wants.</param>
    /// <param name="ModelLine">The same finding's line within the class's own source, which is what a
    /// tool navigating by class (or the desktop app's code viewer) wants. The two differ for a class
    /// nested in a package.mo.</param>
    private sealed record FindingJson(
        string RuleId, string Severity, string Status, string Model, string? Element,
        int Line, int ModelLine, string Message, string Fingerprint, string? File);

    private sealed record FixedJson(
        string RuleId, string Model, string? Element, string Message, string Fingerprint);
}
