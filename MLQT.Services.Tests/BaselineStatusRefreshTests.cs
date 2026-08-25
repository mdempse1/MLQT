using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using MLQT.Services.Checking;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// When <see cref="BaselineStatusService"/> tells the UI its answer has moved.
///
/// <para>The desktop app reads the snapshot at render time and only re-renders when told to, so a
/// refresh that changes the classification without announcing it leaves the "changed vs baseline"
/// count showing the previous answer indefinitely — until something unrelated re-renders the page.
/// Leaving the tab and coming back appeared to "fix" it, because that re-mounted the component.</para>
/// </summary>
public class BaselineStatusRefreshTests
{
    private static LogMessage Message(string modelId, string fingerprint) =>
        new(modelId, "Warning", 1, "summary") { Fingerprint = fingerprint, RuleId = "MLQT.Doc.ClassDescription" };

    private static Baseline BaselineWith(params string[] fingerprints) =>
        new(fingerprints
            .Select(f => new BaselineEntry(f, "MLQT.Doc.ClassDescription", "Lib.A", null, "summary"))
            .ToList());

    /// <summary>
    /// The snapshot classifies from the baseline and the pending-commit set, so both have to be able
    /// to move the answer — not just the two numbers that happened to be on screen.
    /// </summary>
    [Fact]
    public void TouchedModelsChanging_ChangesTheClassification()
    {
        var baseline = BaselineWith("fp1");
        var byModel = new Dictionary<string, Baseline>(StringComparer.Ordinal) { ["Lib.A"] = baseline, ["Lib.B"] = baseline };

        // Same number of touched files, different models — the old guard compared only the count.
        var before = new BaselineStatusSnapshot(byModel, new HashSet<string>(StringComparer.Ordinal) { "Lib.A" }, 1);
        var after = new BaselineStatusSnapshot(byModel, new HashSet<string>(StringComparer.Ordinal) { "Lib.B" }, 1);

        var message = Message("Lib.B", "fp1");

        Assert.Equal(FindingStatus.AcceptedDebt, before.StatusOf(message));
        Assert.Equal(FindingStatus.TouchedDebt, after.StatusOf(message));

        // Which means the count a view shows really does differ between the two snapshots.
        Assert.False(before.IsChangedFromBaseline(message));
        Assert.True(after.IsChangedFromBaseline(message));
    }

    /// <summary>
    /// A regenerated baseline covering the same models, with the same number of pending files, still
    /// changes every answer — the old guard saw nothing to announce.
    /// </summary>
    [Fact]
    public void BaselineContentChanging_ChangesTheClassification()
    {
        var touched = new HashSet<string>(StringComparer.Ordinal);
        var before = new BaselineStatusSnapshot(
            new Dictionary<string, Baseline>(StringComparer.Ordinal) { ["Lib.A"] = BaselineWith("fp1") }, touched, 0);
        var after = new BaselineStatusSnapshot(
            new Dictionary<string, Baseline>(StringComparer.Ordinal) { ["Lib.A"] = BaselineWith("fp1", "fp2") }, touched, 0);

        var message = Message("Lib.A", "fp2");

        Assert.Equal(FindingStatus.New, before.StatusOf(message));
        Assert.Equal(FindingStatus.AcceptedDebt, after.StatusOf(message));
    }
}
