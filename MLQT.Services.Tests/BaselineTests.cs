using ModelicaGraph;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
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
    public void CoverageOf_AccountsForBothReasonsEntriesTrailFindings()
    {
        // The two numbers a user compares: what a check reports, and what `baseline create` says it
        // wrote. Coverage has to explain the whole gap or the difference reads as a lost finding.
        var repeated = F("R", "M", "x");
        var findings = new[]
        {
            repeated, repeated,                       // one entry between them
            F("R2", "M"),                             // its own entry
            F(RuleIds.SyntaxError, "M"),              // never baselined
        };

        var coverage = Baseline.CoverageOf(findings);

        Assert.Equal(4, coverage.Findings);
        Assert.Equal(2, coverage.Entries);
        Assert.Equal(1, coverage.ParseDiagnostics);
        Assert.Equal(3, coverage.Baselineable);
        Assert.Equal(1, coverage.SharingAnEntry);
        Assert.Equal(coverage.Entries, Baseline.FromFindings(findings).Entries.Count);
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

    // --- provenance metadata --------------------------------------------------------------------

    [Fact]
    public void Metadata_RoundTripsThroughTheFile()
    {
        var created = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var path = TempPath();
        try
        {
            Baseline.FromFindings([F("R", "M", "x")], created, new VcsStamp("abc123", "main")).Save(path);
            var loaded = Baseline.Load(path);

            Assert.Equal(created, loaded.CreatedUtc);
            Assert.Equal("abc123", loaded.Revision);
            Assert.Equal("main", loaded.Branch);
            Assert.Single(loaded.Entries);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void VersionOneFile_WithoutMetadata_StillLoads()
    {
        // An already-committed baseline must keep working untouched.
        var path = TempPath();
        try
        {
            File.WriteAllText(path, """
                {
                  "version": 1,
                  "findings": [
                    { "fingerprint": "abc", "ruleId": "R", "model": "M", "element": "x", "message": "m" }
                  ]
                }
                """);

            var loaded = Baseline.Load(path);

            Assert.Single(loaded.Entries);
            Assert.Null(loaded.CreatedUtc);
            Assert.Null(loaded.Revision);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NoWorkingCopy_LeavesMetadataNull_WithoutFailing()
    {
        // A library outside a working copy still gets a valid baseline; it just cannot name a revision.
        var baseline = Baseline.FromFindings([F("R", "M", "x")], DateTime.UtcNow, VcsStamp.None);

        Assert.Null(baseline.Revision);
        Assert.Null(baseline.Branch);
        Assert.Single(baseline.Entries);
    }

    [Fact]
    public void WithoutStale_ReStampsBecauseItRewritesTheContent()
    {
        var original = Baseline.FromFindings(
            [F("R", "M", "x"), F("R", "M", "y")],
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new VcsStamp("old", "main"));

        var pruned = original.WithoutStale(
            [F("R", "M", "x")],
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), new VcsStamp("new", "main"));

        Assert.Single(pruned.Entries);
        Assert.Equal("new", pruned.Revision);
        Assert.Equal(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), pruned.CreatedUtc);
    }

    [Fact]
    public void WithoutStale_KeepsTheOriginalStampWhenNoneIsSupplied()
    {
        var original = Baseline.FromFindings(
            [F("R", "M", "x")], new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new VcsStamp("old", "main"));

        var pruned = original.WithoutStale([F("R", "M", "x")]);

        Assert.Equal("old", pruned.Revision);
    }

    [Fact]
    public void SameFindingsAtSameRevision_ProduceAByteIdenticalFile()
    {
        // What lets CI skip a no-op commit of the baseline.
        var created = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var stamp = new VcsStamp("abc123", "main");
        string a = TempPath(), b = TempPath();
        try
        {
            Baseline.FromFindings([F("R", "M", "x"), F("R", "N", "y")], created, stamp).Save(a);
            Baseline.FromFindings([F("R", "N", "y"), F("R", "M", "x")], created, stamp).Save(b);

            Assert.Equal(File.ReadAllText(a), File.ReadAllText(b));
        }
        finally { File.Delete(a); File.Delete(b); }
    }

    // --- rule-set drift -------------------------------------------------------------------------
    // Both failure modes are silent: a rule enabled after baselining reports its pre-existing
    // violations as NEW (a change looks like it caused a regression it did not), and a rule disabled
    // since leaves entries that can never match again.

    private static StyleCheckingSettings Rules(params string[] enabled)
    {
        var settings = new StyleCheckingSettings();
        foreach (var id in enabled)
            settings.SetRuleEnabled(id, true);
        return settings;
    }

    private static Baseline BaselinedWith(StyleCheckingSettings settings)
        => Baseline.FromFindings([F("R", "M", "x")], DateTime.UtcNow, VcsStamp.None, settings);

    [Fact]
    public void SameRules_NoDrift()
    {
        var baseline = BaselinedWith(Rules(RuleIds.ClassDescription));

        var drift = baseline.DriftFrom(Rules(RuleIds.ClassDescription));

        Assert.True(drift.IsComparable);
        Assert.False(drift.HasDrifted);
        Assert.Empty(drift.Describe());
    }

    [Fact]
    public void RuleEnabledSince_IsReported()
    {
        var baseline = BaselinedWith(Rules(RuleIds.ClassDescription));

        var drift = baseline.DriftFrom(Rules(RuleIds.ClassDescription, RuleIds.ClassIcon));

        Assert.True(drift.HasDrifted);
        Assert.Equal([RuleIds.ClassIcon], drift.EnabledSince);
        Assert.Empty(drift.DisabledSince);
        Assert.Contains(drift.Describe(), l => l.Contains("enabled since") && l.Contains(RuleIds.ClassIcon));
    }

    [Fact]
    public void RuleDisabledSince_IsReported()
    {
        var baseline = BaselinedWith(Rules(RuleIds.ClassDescription, RuleIds.ClassIcon));

        var drift = baseline.DriftFrom(Rules(RuleIds.ClassDescription));

        Assert.Equal([RuleIds.ClassIcon], drift.DisabledSince);
        Assert.Empty(drift.EnabledSince);
    }

    [Fact]
    public void SeverityChange_IsReported()
    {
        var was = Rules(RuleIds.ClassDescription);
        var baseline = BaselinedWith(was);

        var now = Rules(RuleIds.ClassDescription);
        now.RuleSeverities[RuleIds.ClassDescription] = RuleSeverity.Error;
        var drift = baseline.DriftFrom(now);

        var changed = Assert.Single(drift.SeverityChanged);
        Assert.Equal(RuleIds.ClassDescription, changed.RuleId);
        Assert.Equal(RuleSeverity.Error, changed.Now);
        Assert.Contains(drift.Describe(), l => l.Contains("severity changed"));
    }

    [Fact]
    public void ChangedExclusions_AreReported()
    {
        // Un-excluding a library makes its findings appear as new, exactly like enabling a rule.
        var was = Rules(RuleIds.ClassDescription);
        was.ExcludedLibraries.Add("Tests");
        var baseline = BaselinedWith(was);

        var drift = baseline.DriftFrom(Rules(RuleIds.ClassDescription));

        Assert.True(drift.ExclusionsChanged);
        Assert.Contains(drift.Describe(), l => l.Contains("excluded libraries"));
    }

    [Fact]
    public void BaselineWithoutRecordedRules_IsNotComparable()
    {
        // An older file cannot be compared, and guessing would be worse than staying quiet.
        var baseline = Baseline.FromFindings([F("R", "M", "x")]);

        var drift = baseline.DriftFrom(Rules(RuleIds.ClassDescription));

        Assert.False(drift.IsComparable);
        Assert.False(drift.HasDrifted);
    }

    [Fact]
    public void RecordedRulesRoundTripThroughTheFile()
    {
        var path = TempPath();
        try
        {
            var settings = Rules(RuleIds.ClassDescription);
            settings.ExcludedLibraries.Add("Tests");
            BaselinedWith(settings).Save(path);

            var loaded = Baseline.Load(path);

            Assert.NotNull(loaded.Rules);
            Assert.Equal(RuleSeverity.Warning, loaded.Rules![RuleIds.ClassDescription]);
            Assert.Equal(["Tests"], loaded.ExcludedLibraries!);
            Assert.False(loaded.DriftFrom(settings).HasDrifted);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OnlyEnabledRulesAreRecorded()
    {
        // A disabled rule is absent from the map, not stored as Off — otherwise every default would
        // be written and the file would churn whenever the catalog grew.
        var settings = Rules(RuleIds.ClassDescription);
        settings.SetRuleEnabled(RuleIds.ClassIcon, true);
        settings.SetRuleEnabled(RuleIds.ClassIcon, false);

        var rules = BaselinedWith(settings).Rules!;

        Assert.Contains(RuleIds.ClassDescription, rules.Keys);
        Assert.DoesNotContain(RuleIds.ClassIcon, rules.Keys);
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
