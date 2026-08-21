using MLQT.Cli;

namespace MLQT.Cli.Tests;

/// <summary>
/// `prune` and `update` both drop findings you have fixed; only `update` can ADD, accepting a
/// violation nobody reviewed. That is the one way to defeat the ratchet by accident, so the
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
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "mlqt-baseline-" + Guid.NewGuid().ToString("N"));

        public TempLibrary()
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Path, ".mlqt"));
            File.WriteAllText(
                System.IO.Path.Combine(Path, ".mlqt", "settings.json"),
                """{ "ParameterHasDescription": true }""");
        }

        public TempLibrary WithSettings(string json)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, ".mlqt", "settings.json"), json);
            return this;
        }

        public TempLibrary WithModel(string content)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "Lib.mo"), content);
            return this;
        }

        public string BaselineFile => System.IO.Path.Combine(Path, ".mlqt", "baseline.json");
        public string BaselineText => File.ReadAllText(BaselineFile);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    private static (int code, string stdout, string stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CliEntry.RunAsync(args, stdout, stderr).GetAwaiter().GetResult();
        return (code, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Baselines two findings, then fixes one and introduces another.</summary>
    private static TempLibrary Drifted()
    {
        var lib = new TempLibrary().WithModel(TwoUndescribed);
        Run("baseline", "create", lib.Path);
        lib.WithModel(OneFixedOneNew);
        return lib;
    }

    [Fact]
    public void Prune_DropsFixedEntries_ButNeverAcceptsNewOnes()
    {
        using var lib = Drifted();

        var (code, stdout, _) = Run("baseline", "prune", lib.Path);

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

        var (code, _, stderr) = Run("baseline", "update", lib.Path);

        Assert.Equal(2, code);
        Assert.Equal(before, lib.BaselineText);   // untouched
        Assert.Contains("would absorb 1 finding(s)", stderr);
        Assert.Contains("baseline prune", stderr);   // points at the non-destructive option
    }

    [Fact]
    public void UpdateWithForce_AbsorbsAndSaysSo()
    {
        using var lib = Drifted();

        var (code, stdout, _) = Run("baseline", "update", lib.Path, "--force");

        Assert.Equal(0, code);
        Assert.DoesNotContain("\"element\": \"a\"", lib.BaselineText);
        Assert.Contains("\"element\": \"c\"", lib.BaselineText);
        Assert.Contains("absorbed 1 new as accepted debt, dropped 1 fixed", stdout);
    }

    [Fact]
    public void Update_WithOnlyFixesToDrop_NeedsNoForce()
    {
        // Nothing to accept, so update is doing what prune does and should not demand --force.
        var lib = new TempLibrary().WithModel(TwoUndescribed);
        using (lib)
        {
            Run("baseline", "create", lib.Path);
            lib.WithModel("""
                model Lib "described"
                  parameter Real a = 1.0 "now described";
                  parameter Real b = 2.0;
                end Lib;
                """);

            var (code, stdout, _) = Run("baseline", "update", lib.Path);

            Assert.Equal(0, code);
            Assert.Contains("absorbed 0 new as accepted debt, dropped 1 fixed", stdout);
        }
    }

    [Fact]
    public void PruneAndUpdate_AgreeWhenThereIsNothingNewToAccept()
    {
        // Why they look interchangeable most of the time: with no new findings they produce the
        // same file. The divergence only shows when something new has appeared.
        using var pruned = new TempLibrary().WithModel(TwoUndescribed);
        Run("baseline", "create", pruned.Path);
        using var updated = new TempLibrary().WithModel(TwoUndescribed);
        Run("baseline", "create", updated.Path);

        Run("baseline", "prune", pruned.Path);
        Run("baseline", "update", updated.Path);

        // Compare the entry lists; the files differ only by their generation timestamp.
        static string Entries(string json) =>
            string.Join(",", json.Split('\n').Where(l => l.Contains("\"fingerprint\"")).Select(l => l.Trim()));

        Assert.Equal(Entries(pruned.BaselineText), Entries(updated.BaselineText));
    }

    // ---- rule-set drift -------------------------------------------------------------------------

    [Fact]
    public void Check_WarnsWhenARuleWasEnabledSinceTheBaseline()
    {
        // The silent failure this catches: the newly enabled rule's pre-existing violations are
        // reported as new, so the change looks like it caused a regression it had nothing to do with.
        using var lib = new TempLibrary().WithModel(TwoUndescribed);
        Run("baseline", "create", lib.Path);

        lib.WithSettings("""{ "ParameterHasDescription": true, "ClassHasDescription": true }""");
        var (_, _, stderr) = Run("check", lib.Path, "--baseline", lib.BaselineFile, "--no-color");

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
        Run("baseline", "create", lib.Path);

        lib.WithSettings("""{ "ParameterHasDescription": true }""");
        var (_, _, stderr) = Run("check", lib.Path, "--baseline", lib.BaselineFile, "--no-color");

        Assert.Contains("disabled since: MLQT.Doc.ClassDescription", stderr);
    }

    [Fact]
    public void Check_WarnsWhenASeverityChanged()
    {
        using var lib = new TempLibrary().WithModel(TwoUndescribed);
        Run("baseline", "create", lib.Path);

        lib.WithSettings("""
            { "ParameterHasDescription": true,
              "RuleSeverities": { "MLQT.Doc.ParameterDescription": "Error" } }
            """);
        var (_, _, stderr) = Run("check", lib.Path, "--baseline", lib.BaselineFile, "--no-color");

        Assert.Contains("severity changed: MLQT.Doc.ParameterDescription (Warning -> Error)", stderr);
    }

    [Fact]
    public void Check_SaysNothingWhenTheRulesAreUnchanged()
    {
        using var lib = new TempLibrary().WithModel(TwoUndescribed);
        Run("baseline", "create", lib.Path);

        var (_, _, stderr) = Run("check", lib.Path, "--baseline", lib.BaselineFile, "--no-color");

        Assert.DoesNotContain("different configuration", stderr);
        Assert.DoesNotContain("predates configuration recording", stderr);
    }

    [Fact]
    public void Check_DoesNotGuessForABaselineWithoutARecordedRuleSet()
    {
        // An older file has nothing to compare against; say so once rather than warn about a
        // difference that cannot be established.
        using var lib = new TempLibrary().WithModel(TwoUndescribed);
        Run("baseline", "create", lib.Path);
        File.WriteAllText(lib.BaselineFile, """
            { "version": 2, "findings": [] }
            """);

        var (_, _, stderr) = Run("check", lib.Path, "--baseline", lib.BaselineFile, "--no-color");

        Assert.Contains("predates configuration recording", stderr);
        Assert.DoesNotContain("different configuration", stderr);
    }

    [Fact]
    public void PruneAndUpdate_RefreshTheRecordedRuleSet()
    {
        // Both rewrite the file, so both should clear the drift they were run to resolve.
        using var lib = new TempLibrary().WithModel(TwoUndescribed);
        Run("baseline", "create", lib.Path);
        lib.WithSettings("""{ "ParameterHasDescription": true, "ClassHasDescription": true }""");

        Run("baseline", "prune", lib.Path);
        var (_, _, afterPrune) = Run("check", lib.Path, "--baseline", lib.BaselineFile, "--no-color");
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

        Run("baseline", "create", lib.Path, "--dependency", dependency.Path);
        Assert.Contains("\"dependencies\"", lib.BaselineText);

        var (_, _, stderr) = Run("check", lib.Path, "--baseline", lib.BaselineFile, "--no-color");

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

        Run("baseline", "create", lib.Path, "--dependency", dependency.Path);
        var (_, _, stderr) = Run(
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

        Run("baseline", "create", lib.Path);
        var (_, _, stderr) = Run(
            "check", lib.Path, "--baseline", lib.BaselineFile, "--dependency", dependency.Path, "--no-color");

        Assert.Contains("loaded this time but not when baselined", stderr);
    }

    [Fact]
    public void MissingDependencyPath_ExitsTwo()
    {
        using var lib = new TempLibrary().WithModel(TwoUndescribed);

        var (code, _, stderr) = Run(
            "check", lib.Path, "--dependency", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nope-xyz"));

        Assert.Equal(2, code);
        Assert.Contains("dependency path not found", stderr);
    }

}
