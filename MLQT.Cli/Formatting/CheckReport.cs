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
    /// The file as a report shows it: relative to the library, with forward slashes. Absolute paths
    /// are an accident of how the command was typed and are noise in a report that already names the
    /// library it checked — and a path relative to the library is the one a reader can act on.
    /// Falls back to the absolute path when the file is outside the library (a dependency) or the
    /// two cannot be related at all (different drives).
    /// </summary>
    public string? RelativeFileFor(Finding f)
    {
        if (FileFor(f) is not { } file)
            return null;

        try
        {
            var relative = Path.GetRelativePath(LibraryPath, file);
            return Path.IsPathRooted(relative) ? file : relative.Replace('\\', '/');
        }
        catch
        {
            return file;
        }
    }

    /// <summary>
    /// The line in the file to report a finding at. Findings carry class-relative lines; a report
    /// that names a file has to name the file's line, or the annotation lands on unrelated code.
    /// Falls back to the finding's own number when the class is not in the map (a snippet, a class
    /// whose file is unknown) — the best available answer, and no worse than before.
    /// </summary>
    public int LineFor(Finding f) =>
        Locations.TryGetValue(f.ModelId, out var l) ? l.FileLine(f.LineNumber) : Math.Max(1, f.LineNumber);

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
