using ModelicaParser.DataTypes;
using MLQT.Services.Checking;

namespace MLQT.Cli;

/// <summary>The classified result of a check run, ready to hand to a formatter.</summary>
internal sealed record CheckReport(
    string LibraryPath,
    int ModelsChecked,
    IReadOnlyList<ClassifiedFinding> Findings,
    IReadOnlyDictionary<string, string> ModelToFile,
    bool HasBaseline,
    int GateFailureCount)
{
    /// <summary>The source file for a finding's model, or null if unknown.</summary>
    public string? FileFor(Finding f) => ModelToFile.TryGetValue(f.ModelId, out var p) ? p : null;

    public int CountOfSeverity(RuleSeverity severity) => Findings.Count(c => c.Finding.Severity == severity);

    public int CountOfStatus(FindingStatus status) => Findings.Count(c => c.Status == status);

    public bool GatePassed => GateFailureCount == 0;
}

internal interface IFindingFormatter
{
    string Format(CheckReport report);
}
