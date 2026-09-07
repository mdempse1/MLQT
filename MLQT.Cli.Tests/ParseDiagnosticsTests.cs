using System.Text.Json;
using MLQT.Cli;

namespace MLQT.Cli.Tests;

/// <summary>
/// A file that does not parse is the one problem no style rule can report, because every rule reads
/// a parse tree that is missing the code in question. These pin down that the CLI says so, loudly.
/// </summary>
public class ParseDiagnosticsTests
{
    // The real-world shape: a Documentation(info=...) annotation missing its closing quote. The class
    // still loads (the parser recovers), so nothing else flags it.
    private const string UnterminatedString = """
        model TestModel "a model"
          parameter Real x = 1.0 "described";
          annotation(Documentation(info="<html><p>docs</p>));
        end TestModel;
        """;

    private const string CleanModel = """
        model TestModel "a model"
          parameter Real x = 1.0 "described";
        end TestModel;
        """;

    private sealed class TempLibrary : IDisposable
    {
        private readonly TempWorkspace _workspace = new("mlqt-parse");

        public string Path => _workspace.Root;

        public TempLibrary WithModel(string fileName, string content)
        {
            _workspace.Write(fileName, content);
            return this;
        }

        public TempLibrary WithSettings(string json)
        {
            _workspace.WithSettings(json);
            return this;
        }

        public void Dispose() => _workspace.Dispose();
    }

    private static string BaselinePathIn(TempLibrary lib) =>
        System.IO.Path.Combine(lib.Path, ".mlqt", "baseline.json");

    [Fact]
    public void SyntaxError_IsReportedAndFailsTheDefaultGate()
    {
        using var lib = new TempLibrary()
            .WithModel("TestModel.mo", UnterminatedString)
            .WithSettings("""{ "ParameterHasDescription": true }""");

        var (code, stdout, _) = Cli.Run("check", lib.Path, "--no-color");

        // Errors, unlike every style rule (which defaults to warning), trip the default --fail-on error.
        Assert.Equal(1, code);
        Assert.Contains("MLQT.Parse.SyntaxError", stdout);
        Assert.Contains("Unterminated string literal", stdout);
    }

    [Fact]
    public void SyntaxError_IsReportedEvenWithNoStyleRulesEnabled()
    {
        // "No rules enabled" means no style opinions — it cannot mean staying silent about a file
        // that could not be read.
        using var lib = new TempLibrary()
            .WithModel("TestModel.mo", UnterminatedString)
            .WithSettings("{}");

        var (code, stdout, _) = Cli.Run("check", lib.Path, "--no-color");

        Assert.Equal(1, code);
        Assert.Contains("MLQT.Parse.SyntaxError", stdout);
    }

    [Fact]
    public void CleanLibrary_ReportsNoParseDiagnostics()
    {
        using var lib = new TempLibrary()
            .WithModel("TestModel.mo", CleanModel)
            .WithSettings("""{ "ParameterHasDescription": true }""");

        var (code, stdout, _) = Cli.Run("check", lib.Path, "--no-color");

        Assert.Equal(0, code);
        Assert.DoesNotContain("MLQT.Parse.", stdout);
    }

    [Fact]
    public void SyntaxError_CannotBeBaselined()
    {
        // A baseline records style debt someone chose to live with. Code that does not parse is not
        // something a gate should be able to accept, so `baseline create` must not capture it.
        using var lib = new TempLibrary()
            .WithModel("TestModel.mo", UnterminatedString)
            .WithSettings("""{ "ParameterHasDescription": true }""");

        Cli.Run("baseline", "create", lib.Path);
        var baselineJson = File.ReadAllText(BaselinePathIn(lib));
        Assert.DoesNotContain("MLQT.Parse.", baselineJson);

        // ...and it still fails the gate on the next run despite the baseline.
        var (code, stdout, _) = Cli.Run("check", lib.Path, "--baseline", BaselinePathIn(lib), "--no-color");

        Assert.Equal(1, code);
        Assert.Contains("MLQT.Parse.SyntaxError", stdout);
        Assert.Contains("[new]", stdout);
    }

    [Fact]
    public void Json_CarriesTheParseDiagnosticAsAnErrorFinding()
    {
        using var lib = new TempLibrary()
            .WithModel("TestModel.mo", UnterminatedString)
            .WithSettings("""{ "ParameterHasDescription": true }""");

        var (_, stdout, _) = Cli.Run("check", lib.Path, "--format", "json");

        using var doc = JsonDocument.Parse(stdout);
        var parse = doc.RootElement.GetProperty("findings").EnumerateArray()
            .Where(f => f.GetProperty("RuleId").GetString()!.StartsWith("MLQT.Parse."))
            .ToList();

        // One unterminated string produces both a lexer and a parser diagnostic.
        Assert.NotEmpty(parse);
        Assert.All(parse, f => Assert.Equal("Error", f.GetProperty("Severity").GetString()));
        Assert.Contains(parse, f => f.GetProperty("Message").GetString()!.Contains("Unterminated string literal"));
        // Distinct fingerprints, so several errors in one class don't collapse into one.
        Assert.Equal(parse.Count, parse.Select(f => f.GetProperty("Fingerprint").GetString()).Distinct().Count());
    }
}
