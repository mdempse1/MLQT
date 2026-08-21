using System.Text.Json;
using System.Xml.Linq;
using MLQT.Cli;

namespace MLQT.Cli.Tests;

public class CheckCommandTests
{
    private const string ModelWithOneUndescribedParam = """
        model TestModel
          parameter Real x = 1.0;
          parameter Real y = 2.0 "described";
        end TestModel;
        """;

    private const string ParamDescriptionSettings = """{ "ParameterHasDescription": true }""";

    /// <summary>Temp library fixture: a directory with a .mo file and optional .mlqt/settings.json.</summary>
    private sealed class TempLibrary : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "mlqt-cli-test-" + Guid.NewGuid().ToString("N"));

        public TempLibrary() => Directory.CreateDirectory(Path);

        public TempLibrary WithModel(string fileName, string content)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, fileName), content);
            return this;
        }

        public TempLibrary WithSettings(string json)
        {
            var dir = System.IO.Path.Combine(Path, ".mlqt");
            Directory.CreateDirectory(dir);
            File.WriteAllText(System.IO.Path.Combine(dir, "settings.json"), json);
            return this;
        }

        public TempLibrary WithFile(string relativePath, string content)
        {
            var full = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            return this;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }

    private static (int code, string stdout, string stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CliEntry.RunAsync(args, stdout, stderr).GetAwaiter().GetResult();
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static TempLibrary DefaultFixture() =>
        new TempLibrary()
            .WithModel("TestModel.mo", ModelWithOneUndescribedParam)
            .WithSettings(ParamDescriptionSettings);

    [Fact]
    public void Console_ReportsFinding_ExitsZeroByDefault()
    {
        using var lib = DefaultFixture();
        var (code, stdout, _) = Run("check", lib.Path, "--no-color");

        Assert.Equal(0, code); // default --fail-on error; the finding is a warning
        Assert.Contains("MLQT.Doc.ParameterDescription", stdout);
        Assert.Contains("Public parameter x must have a description", stdout);
        Assert.DoesNotContain("parameter y", stdout); // y is described
    }

    [Fact]
    public void FailOnWarning_ExitsOne()
    {
        using var lib = DefaultFixture();
        var (code, _, _) = Run("check", lib.Path, "--fail-on", "warning", "--no-color");
        Assert.Equal(1, code);
    }

    [Fact]
    public void FailOnOff_ExitsZero()
    {
        using var lib = DefaultFixture();
        var (code, _, _) = Run("check", lib.Path, "--fail-on", "off", "--no-color");
        Assert.Equal(0, code);
    }

    [Fact]
    public void Json_HasFindingCountAndFingerprint()
    {
        using var lib = DefaultFixture();
        var (_, stdout, _) = Run("check", lib.Path, "--format", "json");

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.Equal("mlqt", root.GetProperty("tool").GetString());
        Assert.Equal(1, root.GetProperty("findingCount").GetInt32());

        var finding = root.GetProperty("findings")[0];
        Assert.Equal("MLQT.Doc.ParameterDescription", finding.GetProperty("RuleId").GetString());
        Assert.Equal("x", finding.GetProperty("Element").GetString());
        Assert.False(string.IsNullOrEmpty(finding.GetProperty("Fingerprint").GetString()));
    }

    [Fact]
    public void Junit_IsValidXmlWithOneFailure()
    {
        using var lib = DefaultFixture();
        var (_, stdout, _) = Run("check", lib.Path, "--format", "junit");

        var doc = XDocument.Parse(stdout);
        var suites = doc.Root!;
        Assert.Equal("testsuites", suites.Name.LocalName);
        Assert.Equal("1", suites.Attribute("failures")!.Value);
        Assert.Single(suites.Descendants("testcase"));
        Assert.Single(suites.Descendants("failure"));
    }

    [Fact]
    public void Out_WritesToFile()
    {
        using var lib = DefaultFixture();
        var outPath = System.IO.Path.Combine(lib.Path, "results.json");
        var (code, stdout, _) = Run("check", lib.Path, "--format", "json", "--out", outPath);

        Assert.True(File.Exists(outPath));
        Assert.Contains("findingCount", File.ReadAllText(outPath));
        Assert.Equal("", stdout.Trim()); // nothing on stdout when --out is used
        Assert.Equal(0, code);
    }

    [Fact]
    public void NoSettings_ProducesNoFindings()
    {
        using var lib = new TempLibrary().WithModel("TestModel.mo", ModelWithOneUndescribedParam);
        var (code, stdout, _) = Run("check", lib.Path, "--no-color");

        Assert.Equal(0, code);
        Assert.Contains("No findings", stdout);
    }

    [Fact]
    public void ExplicitConfig_IsUsed()
    {
        using var lib = new TempLibrary().WithModel("TestModel.mo", ModelWithOneUndescribedParam);
        var configPath = System.IO.Path.Combine(lib.Path, "custom.json");
        File.WriteAllText(configPath, ParamDescriptionSettings);

        var (_, stdout, _) = Run("check", lib.Path, "--config", configPath, "--no-color");
        Assert.Contains("MLQT.Doc.ParameterDescription", stdout);
    }

    [Fact]
    public void MissingConfig_ExitsTwo()
    {
        using var lib = new TempLibrary().WithModel("TestModel.mo", ModelWithOneUndescribedParam);
        var (code, _, _) = Run("check", lib.Path, "--config", "no-such-file.json");
        Assert.Equal(2, code);
    }

    [Fact]
    public void BadPath_ExitsTwo()
    {
        var (code, _, stderr) = Run("check", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no-such-dir-xyz"));
        Assert.Equal(2, code);
        Assert.Contains("not found", stderr);
    }

    [Fact]
    public void Help_ExitsZero()
    {
        var (code, stdout, _) = Run("--help");
        Assert.Equal(0, code);
        Assert.Contains("mlqt check", stdout);
    }

    [Fact]
    public void NoArgs_ExitsTwo()
    {
        var (code, _, _) = Run();
        Assert.Equal(2, code);
    }

    [Fact]
    public void UnknownCommand_ExitsTwo()
    {
        var (code, _, _) = Run("frobnicate");
        Assert.Equal(2, code);
    }

    // ---- baseline / ratchet --------------------------------------------------------------------

    private const string ModelWithNewAndBaselinedParam = """
        model TestModel
          parameter Real x = 1.0;
          parameter Real z = 3.0;
        end TestModel;
        """;

    private static string BaselinePathIn(TempLibrary lib) =>
        System.IO.Path.Combine(lib.Path, ".mlqt", "baseline.json");

    [Fact]
    public void BaselineCreate_ThenCheck_AcceptsExistingDebt()
    {
        using var lib = DefaultFixture();
        var (createCode, createOut, _) = Run("baseline", "create", lib.Path);
        Assert.Equal(0, createCode);
        Assert.True(File.Exists(BaselinePathIn(lib)));
        Assert.Contains("Wrote 1", createOut);

        // The pre-existing finding is now accepted debt — even a strict warning gate passes.
        var (code, stdout, _) = Run("check", lib.Path, "--baseline", BaselinePathIn(lib), "--fail-on", "warning", "--no-color");
        Assert.Equal(0, code);
        Assert.Contains("accepted", stdout);
    }

    [Fact]
    public void NewFindingAfterBaseline_FailsAtWarning()
    {
        using var lib = DefaultFixture();
        Run("baseline", "create", lib.Path); // baselines the undescribed `x`

        // Introduce a new undescribed parameter `z`; `x` stays accepted debt.
        lib.WithModel("TestModel.mo", ModelWithNewAndBaselinedParam);

        var (code, stdout, _) = Run("check", lib.Path, "--baseline", BaselinePathIn(lib), "--fail-on", "warning", "--no-color");
        Assert.Equal(1, code);
        Assert.Contains("new", stdout);
        Assert.Contains("parameter z", stdout);
    }

    [Fact]
    public void BaselineCreate_RefusesOverwriteWithoutForce()
    {
        using var lib = DefaultFixture();
        Run("baseline", "create", lib.Path);

        var (code, _, stderr) = Run("baseline", "create", lib.Path);
        Assert.Equal(2, code);
        Assert.Contains("already exists", stderr);

        Assert.Equal(0, Run("baseline", "create", lib.Path, "--force").code);
    }

    [Fact]
    public void BaselinePrune_RemovesFixedEntry()
    {
        using var lib = DefaultFixture();
        Run("baseline", "create", lib.Path);

        // Fix the finding by describing x → its baseline entry becomes stale.
        lib.WithModel("TestModel.mo", "model TestModel\n  parameter Real x = 1.0 \"now described\";\nend TestModel;");

        var (code, stdout, _) = Run("baseline", "prune", lib.Path);
        Assert.Equal(0, code);
        Assert.Contains("Pruned 1", stdout);
    }

    [Fact]
    public void CheckWithBaseline_Json_HasStatusAndSummary()
    {
        using var lib = DefaultFixture();
        Run("baseline", "create", lib.Path);

        var (_, stdout, _) = Run("check", lib.Path, "--baseline", BaselinePathIn(lib), "--format", "json");
        using var doc = JsonDocument.Parse(stdout);
        Assert.True(doc.RootElement.GetProperty("hasBaseline").GetBoolean());
        Assert.Equal("AcceptedDebt", doc.RootElement.GetProperty("findings")[0].GetProperty("Status").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("summary").GetProperty("acceptedDebt").GetInt32());
    }

    [Fact]
    public void Reformatting_KeepsFindingAccepted()
    {
        using var lib = DefaultFixture();
        Run("baseline", "create", lib.Path);

        // Reformat: add blank lines so the finding's line number shifts, semantics unchanged.
        lib.WithModel("TestModel.mo",
            "model TestModel\n\n\n  parameter Real x = 1.0;\n  parameter Real y = 2.0 \"described\";\nend TestModel;");

        var (code, _, _) = Run("check", lib.Path, "--baseline", BaselinePathIn(lib), "--fail-on", "warning", "--no-color");
        Assert.Equal(0, code); // fingerprint excludes line number → still accepted
    }

    [Fact]
    public void CheckWithMissingBaselineFile_ExitsTwo()
    {
        using var lib = DefaultFixture();
        var (code, _, stderr) = Run("check", lib.Path, "--baseline", System.IO.Path.Combine(lib.Path, "nope.json"));
        Assert.Equal(2, code);
        Assert.Contains("baseline not found", stderr);
    }

    [Fact]
    public void ChangedFrom_EscalatesTouchedDebt()
    {
        using var lib = DefaultFixture(); // model with undescribed `x`

        LibGit2Sharp.Repository.Init(lib.Path);
        using var repo = new LibGit2Sharp.Repository(lib.Path);
        LibGit2Sharp.Commands.Stage(repo, "*");
        var sig = new LibGit2Sharp.Signature("t", "t@e.com", DateTimeOffset.Now);
        repo.Commit("init", sig, sig, new LibGit2Sharp.CommitOptions());
        var baseRev = repo.Head.Tip.Sha;

        Run("baseline", "create", lib.Path);                // `x` becomes accepted debt
        lib.WithModel("TestModel.mo",                       // touch the model (comment added)
            "model TestModel\n  parameter Real x = 1.0;\n  // touched\n  parameter Real y = 2.0 \"described\";\nend TestModel;");

        // Touched-debt in a changed model: fail policy gates, warn policy does not.
        var fail = Run("check", lib.Path, "--baseline", BaselinePathIn(lib),
            "--changed-from", baseRev, "--touched-debt", "fail", "--fail-on", "warning", "--no-color");
        Assert.Equal(1, fail.code);
        Assert.Contains("model(s) changed since", fail.stderr); // the diagnostic note

        var warnCode = Run("check", lib.Path, "--baseline", BaselinePathIn(lib),
            "--changed-from", baseRev, "--touched-debt", "warn", "--fail-on", "warning", "--no-color").code;
        Assert.Equal(0, warnCode);
    }

    [Fact]
    public void TouchedDebtIgnore_OmitsTouchedDebtFromTheReport()
    {
        // A library stored as one file has every model touched by any edit, so unfixed touched debt
        // swamps the report. `ignore` must drop it from the listing, not just from the gate.
        using var lib = DefaultFixture();

        LibGit2Sharp.Repository.Init(lib.Path);
        using var repo = new LibGit2Sharp.Repository(lib.Path);
        LibGit2Sharp.Commands.Stage(repo, "*");
        var sig = new LibGit2Sharp.Signature("t", "t@e.com", DateTimeOffset.Now);
        repo.Commit("init", sig, sig, new LibGit2Sharp.CommitOptions());
        var baseRev = repo.Head.Tip.Sha;

        Run("baseline", "create", lib.Path);
        lib.WithModel("TestModel.mo",
            "model TestModel\n  parameter Real x = 1.0;\n  // touched\n  parameter Real y = 2.0 \"described\";\nend TestModel;");

        var warn = Run("check", lib.Path, "--baseline", BaselinePathIn(lib),
            "--changed-from", baseRev, "--touched-debt", "warn", "--no-color");
        Assert.Contains("[touched]", warn.stdout);

        var ignore = Run("check", lib.Path, "--baseline", BaselinePathIn(lib),
            "--changed-from", baseRev, "--touched-debt", "ignore", "--fail-on", "warning", "--no-color");

        Assert.Equal(0, ignore.code);
        Assert.DoesNotContain("[touched]", ignore.stdout);
        Assert.Contains("0 touched-debt", ignore.stdout);
        Assert.Contains("touched-debt finding(s) counted as accepted debt", ignore.stderr);
    }

    [Fact]
    public void TouchedDebtIgnore_StillReportsNewFindings()
    {
        // Only the baselined debt is silenced — a genuinely new finding must still surface and gate.
        using var lib = DefaultFixture();

        LibGit2Sharp.Repository.Init(lib.Path);
        using var repo = new LibGit2Sharp.Repository(lib.Path);
        LibGit2Sharp.Commands.Stage(repo, "*");
        var sig = new LibGit2Sharp.Signature("t", "t@e.com", DateTimeOffset.Now);
        repo.Commit("init", sig, sig, new LibGit2Sharp.CommitOptions());
        var baseRev = repo.Head.Tip.Sha;

        Run("baseline", "create", lib.Path);
        lib.WithModel("TestModel.mo",                       // `z` is undescribed and not in the baseline
            "model TestModel\n  parameter Real x = 1.0;\n  parameter Real z = 3.0;\n" +
            "  parameter Real y = 2.0 \"described\";\nend TestModel;");

        var (code, stdout, _) = Run("check", lib.Path, "--baseline", BaselinePathIn(lib),
            "--changed-from", baseRev, "--touched-debt", "ignore", "--fail-on", "warning", "--no-color");

        Assert.Equal(1, code);
        Assert.Contains("[new]", stdout);
        Assert.DoesNotContain("[touched]", stdout);
    }

    [Fact]
    public void ChangedFrom_UnresolvableRef_ErrorsExitTwo()
    {
        using var lib = DefaultFixture();
        LibGit2Sharp.Repository.Init(lib.Path);
        using var repo = new LibGit2Sharp.Repository(lib.Path);
        LibGit2Sharp.Commands.Stage(repo, "*");
        var sig = new LibGit2Sharp.Signature("t", "t@e.com", DateTimeOffset.Now);
        repo.Commit("init", sig, sig, new LibGit2Sharp.CommitOptions());
        Run("baseline", "create", lib.Path);

        var (code, _, stderr) = Run("check", lib.Path, "--baseline", BaselinePathIn(lib), "--changed-from", "no-such-branch");
        Assert.Equal(2, code);
        Assert.Contains("could not resolve revision", stderr);
    }

    [Fact]
    public void ChangedFrom_ReportsFixedIssuesInChangedModels()
    {
        using var lib = DefaultFixture(); // TestModel with undescribed `x`

        LibGit2Sharp.Repository.Init(lib.Path);
        using var repo = new LibGit2Sharp.Repository(lib.Path);
        LibGit2Sharp.Commands.Stage(repo, "*");
        var sig = new LibGit2Sharp.Signature("t", "t@e.com", DateTimeOffset.Now);
        repo.Commit("init", sig, sig, new LibGit2Sharp.CommitOptions());
        var baseRev = repo.Head.Tip.Sha;

        Run("baseline", "create", lib.Path); // baselines the `x` finding

        // Fix x (add a description) — the finding disappears, and the model was touched.
        lib.WithModel("TestModel.mo",
            "model TestModel\n  parameter Real x = 1.0 \"described now\";\n  parameter Real y = 2.0 \"described\";\nend TestModel;");

        var (_, stdout, _) = Run("check", lib.Path, "--baseline", BaselinePathIn(lib), "--changed-from", baseRev, "--no-color");
        Assert.Contains("Fixed in changed models", stdout);
        Assert.Contains("MLQT.Doc.ParameterDescription", stdout);
        Assert.Contains("1 fixed", stdout);

        var (_, json, _) = Run("check", lib.Path, "--baseline", BaselinePathIn(lib), "--changed-from", baseRev, "--format", "json");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("summary").GetProperty("fixed").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("fixed").GetArrayLength());
    }

    // ---- Phase 4: severities + CI formats ------------------------------------------------------

    private const string ErrorSeveritySettings =
        """{ "ParameterHasDescription": true, "RuleSeverities": { "MLQT.Doc.ParameterDescription": "Error" } }""";

    private static TempLibrary ErrorSeverityFixture() =>
        new TempLibrary().WithModel("TestModel.mo", ModelWithOneUndescribedParam).WithSettings(ErrorSeveritySettings);

    [Fact]
    public void ErrorSeverity_FailsGateAtError()
    {
        using var lib = ErrorSeverityFixture();
        var (code, _, _) = Run("check", lib.Path, "--fail-on", "error", "--no-color");
        Assert.Equal(1, code); // the rule is configured as an error in .mlqt/settings.json
    }

    [Fact]
    public void Sarif_IsValid_WithLevelBaselineStateAndFingerprint()
    {
        using var lib = DefaultFixture();
        var (_, stdout, _) = Run("check", lib.Path, "--format", "sarif");

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.Equal("2.1.0", root.GetProperty("version").GetString());

        var result = root.GetProperty("runs")[0].GetProperty("results")[0];
        Assert.Equal("MLQT.Doc.ParameterDescription", result.GetProperty("ruleId").GetString());
        Assert.Equal("warning", result.GetProperty("level").GetString());
        Assert.Equal("new", result.GetProperty("baselineState").GetString());
        Assert.True(result.GetProperty("partialFingerprints").TryGetProperty("mlqt/v1", out _));
        Assert.Equal("TestModel.mo", result
            .GetProperty("locations")[0].GetProperty("physicalLocation")
            .GetProperty("artifactLocation").GetProperty("uri").GetString());
    }

    [Fact]
    public void Sarif_ErrorSeverity_MapsToErrorLevel()
    {
        using var lib = ErrorSeverityFixture();
        var (_, stdout, _) = Run("check", lib.Path, "--format", "sarif");
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal("error", doc.RootElement.GetProperty("runs")[0]
            .GetProperty("results")[0].GetProperty("level").GetString());
    }

    [Fact]
    public void TeamCity_EmitsStatisticsAndBuildProblemOnFailure()
    {
        using var lib = DefaultFixture();
        var (_, stdout, _) = Run("check", lib.Path, "--format", "teamcity", "--fail-on", "warning");
        Assert.Contains("buildStatisticValue key='mlqt.findings.new' value='1'", stdout);
        Assert.Contains("buildProblem", stdout);
    }

    [Fact]
    public void TeamCity_NoBuildProblem_WhenGatePasses()
    {
        using var lib = DefaultFixture(); // one warning finding
        var (_, stdout, _) = Run("check", lib.Path, "--format", "teamcity", "--fail-on", "error");
        Assert.DoesNotContain("buildProblem", stdout);
    }

    [Fact]
    public void Markdown_HasHeaderAndTable()
    {
        using var lib = DefaultFixture();
        var (_, stdout, _) = Run("check", lib.Path, "--format", "markdown");
        Assert.Contains("## MLQT check", stdout);
        Assert.Contains("| Severity | Status | Rule | Model | Line | Message |", stdout);
        Assert.Contains("MLQT.Doc.ParameterDescription", stdout);
    }

    // ---- repo-wide loading ---------------------------------------------------------------------

    [Fact]
    public void RepoWithMultipleLibraries_ChecksAll_WithOneSettingsAndBaseline()
    {
        using var repo = new TempLibrary()
            .WithFile("LibA/package.mo", "package LibA\n  model MA\n    parameter Real a = 1.0;\n  end MA;\nend LibA;")
            .WithFile("LibB/package.mo", "package LibB\n  model MB\n    parameter Real b = 2.0;\n  end MB;\nend LibB;")
            .WithSettings(ParamDescriptionSettings); // one .mlqt/settings.json at the repo root

        var (_, stdout, _) = Run("check", repo.Path, "--format", "json");
        using var doc = JsonDocument.Parse(stdout);
        var models = doc.RootElement.GetProperty("findings").EnumerateArray()
            .Select(f => f.GetProperty("Model").GetString())
            .ToList();
        Assert.Contains(models, m => m is not null && m.EndsWith(".MA"));
        Assert.Contains(models, m => m is not null && m.EndsWith(".MB"));

        // One baseline at the repo root covers both libraries.
        Assert.Equal(0, Run("baseline", "create", repo.Path).code);
        var baselinePath = System.IO.Path.Combine(repo.Path, ".mlqt", "baseline.json");
        Assert.True(File.Exists(baselinePath));

        var (checkCode, _, _) = Run("check", repo.Path, "--baseline", baselinePath, "--fail-on", "warning", "--no-color");
        Assert.Equal(0, checkCode); // both libraries' findings are accepted
    }

    [Fact]
    public void Suppression_HidesFinding_AndNoSuppressRevealsIt()
    {
        using var lib = new TempLibrary()
            .WithModel("TestModel.mo",
                "model TestModel\n  parameter Real x = 1.0 annotation(__MLQT(suppress=\"Doc.ParameterDescription\"));\nend TestModel;")
            .WithSettings(ParamDescriptionSettings);

        var (_, stdout, _) = Run("check", lib.Path, "--no-color");
        Assert.Contains("No findings", stdout); // the only finding is suppressed

        var (_, auditOut, _) = Run("check", lib.Path, "--no-suppress", "--no-color");
        Assert.Contains("MLQT.Doc.ParameterDescription", auditOut); // audit mode reveals it
    }

    [Fact]
    public void Console_MultiModelFile_ShowsEachModel()
    {
        // One file, two models — the console must show which model each violation belongs to.
        using var lib = new TempLibrary()
            .WithModel("Two.mo",
                "package P\n  model A\n    parameter Real x = 1.0;\n  end A;\n\n  model B\n    parameter Real y = 2.0;\n  end B;\nend P;")
            .WithSettings(ParamDescriptionSettings);

        var (_, stdout, _) = Run("check", lib.Path, "--no-color");
        Assert.Contains("P.A", stdout);
        Assert.Contains("P.B", stdout);
    }

    // ---- relative paths resolve against the library/repo, not the CWD --------------------------

    [Fact]
    public void RelativeBaselinePath_ResolvesAgainstLibraryPath()
    {
        using var lib = DefaultFixture();
        Assert.Equal(0, Run("baseline", "create", lib.Path).code); // writes <lib>/.mlqt/baseline.json

        // A relative --baseline must resolve against lib.Path, not the test runner's CWD.
        var (code, stdout, _) = Run("check", lib.Path, "--baseline", ".mlqt/baseline.json",
            "--fail-on", "warning", "--no-color");
        Assert.Equal(0, code); // found it → the existing finding is accepted debt
        Assert.Contains("accepted", stdout);
    }

    [Fact]
    public void RelativeConfigPath_ResolvesAgainstLibraryPath()
    {
        using var lib = new TempLibrary().WithModel("TestModel.mo", ModelWithOneUndescribedParam);
        File.WriteAllText(System.IO.Path.Combine(lib.Path, "myconfig.json"), ParamDescriptionSettings);

        var (_, stdout, _) = Run("check", lib.Path, "--config", "myconfig.json", "--no-color");
        Assert.Contains("MLQT.Doc.ParameterDescription", stdout); // config found relative to lib → rule ran
    }

    [Fact]
    public void BaselineCreate_RelativePath_ResolvesAgainstLibraryPath()
    {
        using var lib = DefaultFixture();
        Assert.Equal(0, Run("baseline", "create", lib.Path, "--baseline", "custom/base.json").code);
        Assert.True(File.Exists(System.IO.Path.Combine(lib.Path, "custom", "base.json")));
    }

    [Fact]
    public void AbsoluteBaselinePath_StillWorks()
    {
        using var lib = DefaultFixture();
        var abs = System.IO.Path.Combine(lib.Path, "abs-baseline.json");
        Assert.Equal(0, Run("baseline", "create", lib.Path, "--baseline", abs).code);
        Assert.True(File.Exists(abs));
        Assert.Equal(0, Run("check", lib.Path, "--baseline", abs, "--fail-on", "warning", "--no-color").code);
    }

    // ---- excluded libraries ---------------------------------------------------------------------
    // A customer repository holds the libraries under development alongside their test-case libraries,
    // and the same rules are not wanted on the tests.

    private const string LibraryUnderTest = """
        model Lib "described"
          parameter Real x = 1.0;
        end Lib;
        """;

    private const string TestLibrary = """
        model Tests "described"
          parameter Real y = 2.0;
        end Tests;
        """;

    private const string WildcardNamedTestLibrary = """
        model Foo_Tests "described"
          parameter Real y = 2.0;
        end Foo_Tests;
        """;

    [Fact]
    public void ExcludedLibrary_IsNotReportedOn()
    {
        using var lib = new TempLibrary()
            .WithModel("Lib.mo", LibraryUnderTest)
            .WithModel("Tests.mo", TestLibrary)
            .WithSettings("""{ "ParameterHasDescription": true }""");

        var before = Run("check", lib.Path, "--no-color");
        Assert.Contains("Lib", before.stdout);
        Assert.Contains("Tests", before.stdout);

        lib.WithSettings("""{ "ParameterHasDescription": true, "ExcludedLibraries": ["Tests"] }""");
        var (_, stdout, stderr) = Run("check", lib.Path, "--no-color");

        Assert.Contains("Lib", stdout);
        Assert.DoesNotContain("Tests", stdout);
        // Counted out loud, so a mistyped library name shows up rather than quietly passing.
        Assert.Contains("skipped as excluded libraries", stderr);
    }

    [Fact]
    public void ExcludedLibrary_AcceptsAWildcard()
    {
        using var lib = new TempLibrary()
            .WithModel("Lib.mo", LibraryUnderTest)
            .WithModel("Foo_Tests.mo", WildcardNamedTestLibrary)
            .WithSettings("""{ "ParameterHasDescription": true, "ExcludedLibraries": ["*_Tests"] }""");

        var (_, stdout, _) = Run("check", lib.Path, "--no-color");

        Assert.Contains("Lib", stdout);
        Assert.DoesNotContain("Foo_Tests", stdout);
    }
}
