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
}
