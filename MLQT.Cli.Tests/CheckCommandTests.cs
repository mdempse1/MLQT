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
        var failCode = Run("check", lib.Path, "--baseline", BaselinePathIn(lib),
            "--changed-from", baseRev, "--touched-debt", "fail", "--fail-on", "warning", "--no-color").code;
        Assert.Equal(1, failCode);

        var warnCode = Run("check", lib.Path, "--baseline", BaselinePathIn(lib),
            "--changed-from", baseRev, "--touched-debt", "warn", "--fail-on", "warning", "--no-color").code;
        Assert.Equal(0, warnCode);
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
}
