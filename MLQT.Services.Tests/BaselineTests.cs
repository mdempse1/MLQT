using ModelicaParser.DataTypes;
using MLQT.Services.Checking;
using Xunit;

namespace MLQT.Services.Tests;

public class BaselineTests
{
    private static Finding F(string rule, string model, string? element = null, string msg = "m")
        => new() { RuleId = rule, ModelId = model, ElementPath = element, Message = msg, Severity = RuleSeverity.Warning };

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "mlqt-baseline-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void FromFindings_DedupesByFingerprint_AndContains()
    {
        var f = F("R", "M", "x");
        var baseline = Baseline.FromFindings([f, f]); // duplicate fingerprint
        Assert.Single(baseline.Entries);
        Assert.True(baseline.Contains(f));
        Assert.False(baseline.Contains(F("R", "M", "y")));
    }

    [Fact]
    public void SaveLoad_RoundTrips()
    {
        var path = TempPath();
        try
        {
            var original = Baseline.FromFindings([F("R1", "M", "x", "msg one"), F("R2", "N")]);
            original.Save(path);

            var loaded = Baseline.Load(path);
            Assert.Equal(2, loaded.Entries.Count);
            Assert.True(loaded.Contains(F("R1", "M", "x")));
            Assert.True(loaded.Contains(F("R2", "N")));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Save_IsStableRegardlessOfInputOrder()
    {
        var findings = new[] { F("B", "M2", "z"), F("A", "M1", "x"), F("A", "M1", "a") };
        var p1 = TempPath();
        var p2 = TempPath();
        try
        {
            Baseline.FromFindings(findings).Save(p1);
            Baseline.FromFindings(findings.Reverse().ToArray()).Save(p2);
            Assert.Equal(File.ReadAllText(p1), File.ReadAllText(p2)); // byte-identical
        }
        finally { File.Delete(p1); File.Delete(p2); }
    }

    [Fact]
    public void StaleEntries_And_WithoutStale()
    {
        var kept = F("R", "M", "x");
        var fixedUp = F("R", "M", "gone");
        var baseline = Baseline.FromFindings([kept, fixedUp]);

        var current = new[] { kept }; // fixedUp no longer reported

        var stale = baseline.StaleEntries(current);
        Assert.Single(stale);
        Assert.Equal(fixedUp.Fingerprint, stale[0].Fingerprint);

        var pruned = baseline.WithoutStale(current);
        Assert.Single(pruned.Entries);
        Assert.True(pruned.Contains(kept));
    }
}

public class FindingClassifierTests
{
    private static Finding F(string rule, string model, string? element = null)
        => new() { RuleId = rule, ModelId = model, ElementPath = element, Message = "m", Severity = RuleSeverity.Warning };

    [Fact]
    public void NoBaseline_AllNew()
    {
        var c = FindingClassifier.Classify([F("R", "M", "x")], baseline: null, changedModelIds: null);
        Assert.Equal(FindingStatus.New, c[0].Status);
    }

    [Fact]
    public void InBaseline_UnchangedModel_Accepted()
    {
        var f = F("R", "M", "x");
        var baseline = Baseline.FromFindings([f]);
        var c = FindingClassifier.Classify([f], baseline, changedModelIds: null);
        Assert.Equal(FindingStatus.AcceptedDebt, c[0].Status);
    }

    [Fact]
    public void InBaseline_ChangedModel_Touched()
    {
        var f = F("R", "M", "x");
        var baseline = Baseline.FromFindings([f]);
        var c = FindingClassifier.Classify([f], baseline, new HashSet<string> { "M" });
        Assert.Equal(FindingStatus.TouchedDebt, c[0].Status);
    }

    [Fact]
    public void NotInBaseline_ChangedModel_StillNew()
    {
        // Orthogonality: a new finding in a changed model is New, not TouchedDebt.
        var f = F("R", "M", "x");
        var baseline = Baseline.FromFindings([F("R", "M", "other")]);
        var c = FindingClassifier.Classify([f], baseline, new HashSet<string> { "M" });
        Assert.Equal(FindingStatus.New, c[0].Status);
    }
}
