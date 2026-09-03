using MLQT.Cli;

namespace MLQT.Cli.Tests;

/// <summary>
/// `prune` and `update` both drop findings you have fixed; only `update` can ADD, accepting a
/// finding nobody reviewed. That is the one way to defeat the ratchet by accident, so the
/// difference has to be visible at the point of use.
/// </summary>
public class BaselineMaintenanceTests
{
    private const string TwoUndescribed = """
        model Lib "described"
          parameter Real a = 1.0;
          parameter Real b = 2.0;
        end Lib;
        """;

    // 'a' now described (that entry becomes stale), and 'c' is new debt.
    private const string OneFixedOneNew = """
        model Lib "described"
          parameter Real a = 1.0 "now described";
          parameter Real b = 2.0;
          parameter Real c = 3.0;
        end Lib;
        """;

    private sealed class TempLibrary : IDisposable
    {
        private readonly TempWorkspace _workspace = new TempWorkspace("mlqt-baseline")
            .WithSettings("""{ "ParameterHasDescription": true }""");

        public string Path => _workspace.Root;
        public string BaselineFile => _workspace.PathTo(".mlqt", "baseline.json");
        public string BaselineText => File.ReadAllText(BaselineFile);

        public TempLibrary WithSettings(string json)
        {
            _workspace.WithSettings(json);
            return this;
        }

        public TempLibrary WithModel(string content)
        {
            _workspace.Write("Lib.mo", content);
            return this;
        }

        public void Dispose() => _workspace.Dispose();
    }

    /// <summary>Baselines two findings, then fixes one and introduces another.</summary>
    private static TempLibrary Drifted()
    {
        var lib = new TempLibrary().WithModel(TwoUndescribed);
        Cli.Run("baseline", "create", lib.Path);
        lib.WithModel(OneFixedOneNew);
        return lib;
    }

    [Fact]
    public void Prune_DropsFixedEntries_ButNeverAcceptsNewOnes()
    {
        using var lib = Drifted();

        var (code, stdout, _) = Cli.Run("baseline", "prune", lib.Path);

        Assert.Equal(0, code);
        Assert.DoesNotContain("\"element\": \"a\"", lib.BaselineText);   // fixed → dropped
        Assert.Contains("\"element\": \"b\"", lib.BaselineText);         // still debt → kept
        Assert.DoesNotContain("\"element\": \"c\"", lib.BaselineText);   // new → NOT accepted
        Assert.Contains("still fail the gate", stdout);
    }

    [Fact]
    public void Update_RefusesToAbsorbNewFindingsWithoutForce()
    {
        using var lib = Drifted();
        var before = lib.BaselineText;

        var (code, _, stderr) = Cli.Run("baseline", "update", lib.Path);

        Assert.Equal(2, code);
        Assert.Equal(before, lib.BaselineText);   // untouched
        Assert.Contains("would absorb 1 entry", stderr);
        Assert.Contains("baseline prune", stderr);   // points at the non-destructive option
    }

    [Fact]
    public void UpdateWithForce_AbsorbsAndSaysSo()
    {
        using var lib = Drifted();

        var (code, stdout, _) = Cli.Run("baseline", "update", lib.Path, "--force");

        Assert.Equal(0, code);
        Assert.DoesNotContain("\"element\": \"a\"", lib.BaselineText);
        Assert.Contains("\"element\": \"c\"", lib.BaselineText);
        Assert.Contains("absorbed 1 entry as accepted debt, dropped 1 entry now fixed", stdout);
    }

    [Fact]
    public void Update_WithOnlyFixesToDrop_NeedsNoForce()
    {
        // Nothing to accept, so update is doing what prune does and should not demand --force.
        var lib = new TempLibrary().WithModel(TwoUndescribed);
        using (lib)
        {
            Cli.Run("baseline", "create", lib.Path);
            lib.WithModel("""
                model Lib "described"
                  parameter Real a = 1.0 "now described";
                  parameter Real b = 2.0;
                end Lib;
                """);

            var (code, stdout, _) = Cli.Run("baseline", "update", lib.Path);

            Assert.Equal(0, code);
            Assert.Contains("absorbed 0 entries as accepted debt, dropped 1 entry now fixed", stdout);
        }
    }

    // ---- entries vs findings ---------------------------------------------------------------------

    /// <summary>
    /// Two misplaced imports violate ImportStatementsFirst twice in one class, and the rule carries no
    /// element or discriminator — so both findings share a fingerprint and the ledger holds one entry
    /// for the pair.
    /// </summary>
    private const string RepeatedFinding = """
        model Lib "described"
          Real x;
          import A.B;
          import C.D;
        end Lib;
        """;

    private const string RepeatedFindingSettings = """{ "ImportStatementsFirst": true }""";

    [Fact]
    public void Create_SaysHowManyFindingsTheEntriesCover()
    {
        // The confusing case: `create` reports fewer than the check did, and the check then reports
        // all of them accepted. Both are right, and neither is legible without the other number.
        using var lib = new TempLibrary()
            .WithSettings(RepeatedFindingSettings)
            .WithModel(RepeatedFinding);

        var (code, stdout, stderr) = Cli.Run("baseline", "create", lib.Path);

        Assert.Equal(0, code);
        Assert.Contains("Wrote 1 entry to", stdout);
        Assert.Contains("covering 2 finding(s).", stdout);
        Assert.Contains("1 finding(s) share an entry with another", stderr);
    }

    [Fact]
    public void Check_ReportsTheEntryCountAlongsideTheAcceptedFindings()
    {
        using var lib = new TempLibrary()
            .WithSettings(RepeatedFindingSettings)
            .WithModel(RepeatedFinding);
        Cli.Run("baseline", "create", lib.Path);

        var (code, stdout, stderr) = Cli.Run("check", lib.Path, "--baseline", lib.BaselineFile, "--no-color");

        Assert.Equal(0, code);
        Assert.Contains("baseline holds 1 entry", stderr);
        Assert.Contains("2 finding(s) accepted as baseline debt", stdout);
    }

    [Fact]
    public void Create_SaysWhenParseDiagnosticsWereNotRecorded()
    {
        // Answers the question the numbers provoke — "does the baseline drop errors?" — where it is
        // asked. Severity is not the filter; being unparseable is.
        using var lib = new TempLibrary().WithModel("""
            model Lib "described"
              parameter Real a = ;
            end Lib;
            """);

        var (code, _, stderr) = Cli.Run("baseline", "create", lib.Path);

        Assert.Equal(0, code);
        Assert.Contains("parse diagnostic(s) were not recorded", stderr);
        Assert.Contains("errors are baselined like anything else", stderr);
    }

    [Fact]
    public void PruneAndUpdate_AgreeWhenThereIsNothingNewToAccept()
    {
        // Why they look interchangeable most of the time: with no new findings they produce the
        // same file. The divergence only shows when something new has appeared.
        using var pruned = new TempLibrary().WithModel(TwoUndescribed);
        Cli.Run("baseline", "create", pruned.Path);
        using var updated = new TempLibrary().WithModel(TwoUndescribed);
        Cli.Run("baseline", "create", updated.Path);

        Cli.Run("baseline", "prune", pruned.Path);
        Cli.Run("baseline", "update", updated.Path);

        // Compare the entry lists; the files differ only by their generation timestamp.
        static string Entries(string json) =>
            string.Join(",", json.Split('\n').Where(l => l.Contains("\"fingerprint\"")).Select(l => l.Trim()));

        Assert.Equal(Entries(pruned.BaselineText), Entries(updated.BaselineText));
    }

    // ---- rule-set drift -------------------------------------------------------------------------

    [Fact]
    public void Check_WarnsWhenARuleWasEnabledSinceTheBaseline()
    {
        // The silent failure this catches: the newly enabled rule's pre-existing findings are
        // reported as new, so the change looks like it caused a regression it had nothing to do with.
        using var lib = new TempLibrary().WithModel(TwoUndescribed);
        Cli.Run("baseline", "create", lib.Path);

        lib.WithSettings("""{ "ParameterHasDescription": true, "ClassHasDescription": true }""");
        var (_, _, stderr) = Cli.Run("check", lib.Path, "--baseline", lib.BaselineFile, "--no-color");

        Assert.Contains("different configuration", stderr);
        Assert.Contains("enabled since: MLQT.Doc.ClassDescription", stderr);
        Assert.Contains("baseline update --force", stderr);
    }

    [Fact]
    public void Check_WarnsWhenARuleWasDisabledSinceTheBaseline()
    {
        using var lib = new TempLibrary()
            .WithSettings("""{ "ParameterHasDescription": true, "ClassHasDescription": true }""")
            .WithModel(TwoUndescribed);
        Cli.Run("baseline", "create", lib.Path);

        lib.WithSettings("""{ "ParameterHasDescription": true }""");
        var (_, _, stderr) = Cli.Run("check", lib.Path, "--baseline", lib.BaselineFile, "--no-color");

        Assert.Contains("disabled since: MLQT.Doc.ClassDescription", stderr);
    }

    [Fact]
    public void Check_WarnsWhenASeverityChanged()
    {
        using var lib = new TempLibrary().WithModel(TwoUndescribed);
        Cli.Run("baseline", "create", lib.Path);

        lib.WithSettings("""
            { "ParameterHasDescription": true,
              "RuleSeverities": { "MLQT.Doc.ParameterDescription": "Error" } }
            """);
        var (_, _, stderr) = Cli.Run("check", lib.Path, "--baseline", lib.BaselineFile, "--no-color");

        Assert.Contains("severity changed: MLQT.Doc.ParameterDescription (Warning -> Error)", stderr);
    }

    [Fact]
    public void Check_SaysNothingWhenTheRulesAreUnchanged()
    {
        using var lib = new TempLibrary().WithModel(TwoUndescribed);
        Cli.Run("baseline", "create", lib.Path);

        var (_, _, stderr) = Cli.Run("check", lib.Path, "--baseline", lib.BaselineFile, "--no-color");

        Assert.DoesNotContain("different configuration", stderr);
        Assert.DoesNotContain("predates configuration recording", stderr);
    }

    [Fact]
    public void Check_DoesNotGuessForABaselineWithoutARecordedRuleSet()
    {
        // An older file has nothing to compare against; say so once rather than warn about a
        // difference that cannot be established.
        using var lib = new TempLibrary().WithModel(TwoUndescribed);
        Cli.Run("baseline", "create", lib.Path);
        File.WriteAllText(lib.BaselineFile, """
            { "version": 2, "findings": [] }
            """);

        var (_, _, stderr) = Cli.Run("check", lib.Path, "--baseline", lib.BaselineFile, "--no-color");

        Assert.Contains("predates configuration recording", stderr);
        Assert.DoesNotContain("different configuration", stderr);
    }

    [Fact]
    public void PruneAndUpdate_RefreshTheRecordedRuleSet()
    {
        // Both rewrite the file, so both should clear the drift they were run to resolve.
        using var lib = new TempLibrary().WithModel(TwoUndescribed);
        Cli.Run("baseline", "create", lib.Path);
        lib.WithSettings("""{ "ParameterHasDescription": true, "ClassHasDescription": true }""");

        Cli.Run("baseline", "prune", lib.Path);
        var (_, _, afterPrune) = Cli.Run("check", lib.Path, "--baseline", lib.BaselineFile, "--no-color");
        Assert.DoesNotContain("different configuration", afterPrune);

        Assert.Contains("\"MLQT.Doc.ClassDescription\"", lib.BaselineText);
    }

    // ---- dependency drift ------------------------------------------------------------------------

    [Fact]
    public void Check_WarnsWhenABaselinedDependencyIsNotLoaded()
    {
        // The silent failure: baselining with MSL loaded and checking without it resolves fewer
        // references, so rules like "class has an icon" report findings the change did not cause.
        using var dependency = new TempLibrary().WithModel("""
            package Dep "a dependency"
              model Thing "described"
              end Thing;
            end Dep;
            """);
        using var lib = new TempLibrary().WithModel(TwoUndescribed);

        Cli.Run("baseline", "create", lib.Path, "--dependency", dependency.Path);
        Assert.Contains("\"dependencies\"", lib.BaselineText);

        var (_, _, stderr) = Cli.Run("check", lib.Path, "--baseline", lib.BaselineFile, "--no-color");

        Assert.Contains("different configuration", stderr);
        Assert.Contains("not loaded this time", stderr);
        Assert.Contains("--dependency", stderr);
    }

    [Fact]
    public void Check_SaysNothingWhenTheSameDependenciesAreLoaded()
    {
        using var dependency = new TempLibrary().WithModel("""
            package Dep "a dependency"
              model Thing "described"
              end Thing;
            end Dep;
            """);
        using var lib = new TempLibrary().WithModel(TwoUndescribed);

        Cli.Run("baseline", "create", lib.Path, "--dependency", dependency.Path);
        var (_, _, stderr) = Cli.Run(
            "check", lib.Path, "--baseline", lib.BaselineFile, "--dependency", dependency.Path, "--no-color");

        Assert.DoesNotContain("different configuration", stderr);
    }

    [Fact]
    public void Check_WarnsWhenADependencyWasAddedSinceTheBaseline()
    {
        using var dependency = new TempLibrary().WithModel("""
            package Dep "a dependency"
              model Thing "described"
              end Thing;
            end Dep;
            """);
        using var lib = new TempLibrary().WithModel(TwoUndescribed);

        Cli.Run("baseline", "create", lib.Path);
        var (_, _, stderr) = Cli.Run(
            "check", lib.Path, "--baseline", lib.BaselineFile, "--dependency", dependency.Path, "--no-color");

        Assert.Contains("loaded this time but not when baselined", stderr);
    }

    [Fact]
    public void MissingDependencyPath_ExitsTwo()
    {
        using var lib = new TempLibrary().WithModel(TwoUndescribed);

        var (code, _, stderr) = Cli.Run(
            "check", lib.Path, "--dependency", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nope-xyz"));

        Assert.Equal(2, code);
        Assert.Contains("dependency path not found", stderr);
    }


    // ---- dependency version mismatch -------------------------------------------------------------

    private const string DependencyAtTwo = """
        package Dep "a dependency"
          model Thing "described"
          end Thing;
          annotation(version="2.0.0");
        end Dep;
        """;

    private const string LibraryDeclaringOne = """
        package Lib "a library"
          model A "described"
          end A;
          annotation(uses(Dep(version="1.0.0")));
        end Lib;
        """;

    [Fact]
    public void Check_RefusesToRunWhenALoadedDependencyIsNotTheDeclaredVersion()
    {
        // Resolving against the wrong version reports findings that are not real, so the honest
        // outcome is to stop rather than hand back numbers nobody should act on.
        using var dependency = new TempLibrary().WithModel(DependencyAtTwo);
        using var lib = new TempLibrary().WithModel(LibraryDeclaringOne);

        var (code, stdout, stderr) = Cli.Run("check", lib.Path, "--dependency", dependency.Path, "--no-color");

        // 2 = setup error, not 1 = gate failed. In CI those mean different things: fix your
        // invocation versus fix your code.
        Assert.Equal(2, code);
        Assert.Contains("error: dependency version mismatch", stderr);
        Assert.Contains("Lib declares Dep 1.0.0, but 2.0.0 is loaded", stderr);
        Assert.Contains("--allow-version-mismatch", stderr);
        Assert.Equal("", stdout.Trim());   // no findings reported at all
    }

    [Fact]
    public void Check_AllowVersionMismatch_ContinuesButSaysTheFindingsMayNotBeReal()
    {
        // The escape hatch: a conversion(noneFromVersion=...) annotation can make a difference
        // legitimate, and MLQT does not read those.
        using var dependency = new TempLibrary().WithModel(DependencyAtTwo);
        using var lib = new TempLibrary().WithModel(LibraryDeclaringOne);

        var (code, _, stderr) = Cli.Run(
            "check", lib.Path, "--dependency", dependency.Path, "--allow-version-mismatch", "--no-color");

        Assert.Equal(0, code);
        Assert.Contains("warning: dependency version mismatch", stderr);
        Assert.Contains("may not be real", stderr);
    }

    [Fact]
    public void Baseline_AlsoRefusesOnAVersionMismatch()
    {
        // A baseline taken against the wrong versions bakes findings that are not real into the
        // ledger, where they are far harder to notice than in a single check.
        using var dependency = new TempLibrary().WithModel(DependencyAtTwo);
        using var lib = new TempLibrary().WithModel(LibraryDeclaringOne);

        var (code, _, stderr) = Cli.Run("baseline", "create", lib.Path, "--dependency", dependency.Path);

        Assert.Equal(2, code);
        Assert.Contains("dependency version mismatch", stderr);
        Assert.False(File.Exists(lib.BaselineFile));
    }

    [Fact]
    public void Check_MatchingVersions_RunNormally()
    {
        // Guards the premise: the refusal is about a real disagreement, not about --dependency itself.
        using var dependency = new TempLibrary().WithModel("""
            package Dep "a dependency"
              model Thing "described"
              end Thing;
              annotation(version="1.0.0");
            end Dep;
            """);
        using var lib = new TempLibrary().WithModel(LibraryDeclaringOne);

        var (code, _, stderr) = Cli.Run("check", lib.Path, "--dependency", dependency.Path, "--no-color");

        Assert.Equal(0, code);
        Assert.DoesNotContain("version mismatch", stderr);
    }

    [Fact]
    public void Check_SaysNothingWhenTheDeclaredVersionMatches()
    {
        using var dependency = new TempLibrary().WithModel("""
            package Dep "a dependency"
              model Thing "described"
              end Thing;
              annotation(version="1.0.0");
            end Dep;
            """);
        using var lib = new TempLibrary().WithModel("""
            package Lib "a library"
              model A "described"
              end A;
              annotation(uses(Dep(version="1.0.0")));
            end Lib;
            """);

        var (_, _, stderr) = Cli.Run("check", lib.Path, "--dependency", dependency.Path, "--no-color");

        Assert.DoesNotContain("version mismatch", stderr);
    }

    [Fact]
    public void Check_SaysNothingAboutADeclaredDependencyThatIsNotLoaded()
    {
        // Already visible as unresolved references; a version claim about an absent library is noise.
        using var lib = new TempLibrary().WithModel("""
            package Lib "a library"
              model A "described"
              end A;
              annotation(uses(Dep(version="1.0.0")));
            end Lib;
            """);

        var (_, _, stderr) = Cli.Run("check", lib.Path, "--no-color");

        Assert.DoesNotContain("version mismatch", stderr);
    }

}
