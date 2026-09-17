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
    string? SarifBasePath = null,
    IReadOnlyList<CoverageGateResult>? CoverageGate = null,
    bool SarifIncludeAccepted = false,
    ChangedLineResult? Diff = null)
{
    /// <summary>The source file for a finding's model — an absolute path — or null if unknown.</summary>
    public string? FileFor(Finding f) => Locations.TryGetValue(f.ModelId, out var l) ? l.FilePath : null;

    /// <summary>
    /// The file as a report shows it: relative to the library, with forward slashes. See
    /// <see cref="ReportLocation.RelativeFile(string?,string?)"/>, which is the rule — the Code
    /// Review page's export answers the same question and has to give the same answer.
    /// </summary>
    public string? RelativeFileFor(Finding f) => ReportLocation.RelativeFile(FileFor(f), LibraryPath);

    /// <summary>
    /// The line in the file to report a finding at. Findings carry class-relative lines; a report
    /// that names a file has to name the file's line, or the annotation lands on unrelated code.
    /// See <see cref="ReportLocation.LineIn"/>.
    /// </summary>
    public int LineFor(Finding f) =>
        ReportLocation.LineIn(Locations.GetValueOrDefault(f.ModelId), f.LineNumber);

    /// <summary>
    /// The findings this run is actually about: everything except accepted debt.
    ///
    /// <para>Accepted debt is agreed history — it is in the ledger, it does not gate, and no report
    /// lists it among the things to look at. Every format needs that set, and each of them used to
    /// write the predicate out again, which is one edit away from two formats disagreeing about what
    /// a run found.</para>
    /// </summary>
    public IEnumerable<ClassifiedFinding> Actionable =>
        Findings.Where(c => c.Status != FindingStatus.AcceptedDebt);

    public int CountOfSeverity(RuleSeverity severity) => Findings.Count(c => c.Finding.Severity == severity);

    public int CountOfStatus(FindingStatus status) => Findings.Count(c => c.Status == status);

    public bool GatePassed => GateFailureCount == 0;

    /// <summary>True when no coverage requirement was asked for, or every one of them was met.</summary>
    public bool CoverageGatePassed => CoverageGate is null || CoverageGate.All(r => r.Passed);
}

internal interface IFindingFormatter
{
    string Format(CheckReport report);
}
