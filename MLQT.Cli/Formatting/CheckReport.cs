using ModelicaParser.DataTypes;
using MLQT.Services.Checking;

namespace MLQT.Cli;

/// <summary>The classified result of a check run, ready to hand to a formatter.</summary>
internal sealed record CheckReport(
    string LibraryPath,
    int ModelsChecked,
    IReadOnlyList<ClassifiedFinding> Findings,
    IReadOnlyDictionary<string, ClassLocation> Locations,
    bool HasBaseline,
    int GateFailureCount,
    IReadOnlyList<BaselineEntry> FixedEntries,
    string? SarifBasePath = null)
{
    /// <summary>The source file for a finding's model, or null if unknown.</summary>
    public string? FileFor(Finding f) => Locations.TryGetValue(f.ModelId, out var l) ? l.FilePath : null;

    /// <summary>
    /// The line in the file to report a finding at. Findings carry class-relative lines; a report
    /// that names a file has to name the file's line, or the annotation lands on unrelated code.
    /// Falls back to the finding's own number when the class is not in the map (a snippet, a class
    /// whose file is unknown) — the best available answer, and no worse than before.
    /// </summary>
    public int LineFor(Finding f) =>
        Locations.TryGetValue(f.ModelId, out var l) ? l.FileLine(f.LineNumber) : Math.Max(1, f.LineNumber);

    public int CountOfSeverity(RuleSeverity severity) => Findings.Count(c => c.Finding.Severity == severity);

    public int CountOfStatus(FindingStatus status) => Findings.Count(c => c.Status == status);

    public bool GatePassed => GateFailureCount == 0;
}

internal interface IFindingFormatter
{
    string Format(CheckReport report);
}
