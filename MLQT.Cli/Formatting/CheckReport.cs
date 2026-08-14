using ModelicaParser.DataTypes;

namespace MLQT.Cli;

/// <summary>The result of a check run, ready to hand to a formatter.</summary>
internal sealed record CheckReport(
    string LibraryPath,
    int ModelsChecked,
    IReadOnlyList<Finding> Findings,
    IReadOnlyDictionary<string, string> ModelToFile)
{
    /// <summary>The source file for a finding's model, or null if unknown.</summary>
    public string? FileFor(Finding f) => ModelToFile.TryGetValue(f.ModelId, out var p) ? p : null;

    public int CountOf(RuleSeverity severity) => Findings.Count(f => f.Severity == severity);
}

internal interface IFindingFormatter
{
    string Format(CheckReport report);
}
