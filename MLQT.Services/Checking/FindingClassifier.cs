using ModelicaParser.DataTypes;

namespace MLQT.Services.Checking;

/// <summary>
/// A finding's standing relative to the baseline and the change set:
/// <list type="bullet">
/// <item><see cref="New"/> — not in the baseline (a regression).</item>
/// <item><see cref="AcceptedDebt"/> — in the baseline, in an unchanged model (tolerated).</item>
/// <item><see cref="TouchedDebt"/> — in the baseline, but in a model this change touched.</item>
/// </list>
/// </summary>
public enum FindingStatus { New, AcceptedDebt, TouchedDebt }

public sealed record ClassifiedFinding(Finding Finding, FindingStatus Status);

public static class FindingClassifier
{
    /// <summary>
    /// Classifies findings against an optional baseline and an optional set of changed model ids.
    /// With no baseline, every finding is <see cref="FindingStatus.New"/> (so gate logic collapses
    /// to the no-baseline behaviour). With no change set, baseline hits are all
    /// <see cref="FindingStatus.AcceptedDebt"/>.
    /// </summary>
    public static IReadOnlyList<ClassifiedFinding> Classify(
        IEnumerable<Finding> findings, Baseline? baseline, IReadOnlySet<string>? changedModelIds)
        => findings.Select(f => Classify(f, baseline, changedModelIds)).ToList();

    public static ClassifiedFinding Classify(
        Finding finding, Baseline? baseline, IReadOnlySet<string>? changedModelIds)
    {
        if (baseline is null || !baseline.Contains(finding))
            return new ClassifiedFinding(finding, FindingStatus.New);

        var touched = changedModelIds is not null && changedModelIds.Contains(finding.ModelId);
        return new ClassifiedFinding(finding, touched ? FindingStatus.TouchedDebt : FindingStatus.AcceptedDebt);
    }
}
