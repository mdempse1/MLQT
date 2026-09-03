using MLQT.Cli;

namespace MLQT.Cli.Tests;

/// <summary>
/// Several reports from one check. A pipeline usually wants two — one a person reads, one a machine
/// does — and running the check twice to get them costs minutes on a large library and produces two
/// reports that can disagree if anything on disk moved in between.
/// </summary>
public class MultipleReportTests
{
    private const string Model = """
        model Undescribed
          parameter Real c = 3;
        end Undescribed;
        """;

    private const string Settings =
        """{ "RuleSeverities": { "MLQT.Doc.ClassDescription": "Warning" } }""";

    private sealed class TempLibrary : IDisposable
    {
        private readonly TempWorkspace _workspace = new TempWorkspace("mlqt-reports")
            .Write("Undescribed.mo", Model)
            .WithSettings(Settings);

        public string Path => _workspace.Root;
        public string At(string name) => _workspace.PathTo(name);

        public void Dispose() => _workspace.Dispose();
    }

    [Fact]
    public void TheConsoleLogAndTwoFiles_ComeFromOneRun()
    {
        using var lib = new TempLibrary();
        var junit = lib.At("mlqt.xml");
        var sarif = lib.At("mlqt.sarif");

        var (code, stdout, _) = Cli.Run(
            "check", lib.Path, "--no-color", "--fail-on", "off",
            "--report", $"junit:{junit}", "--report", $"sarif:{sarif}");

        Assert.Equal(0, code);
        Assert.Contains("MLQT.Doc.ClassDescription", stdout);          // still the readable log
        Assert.Contains("<testsuites", File.ReadAllText(junit));
        Assert.Contains("\"$schema\"", File.ReadAllText(sarif));
    }

    [Fact]
    public void AnExtraReport_MatchesWhatThatFormatProducesOnItsOwn()
    {
        using var lib = new TempLibrary();
        var viaReport = lib.At("via-report.xml");
        var viaOut = lib.At("via-out.xml");

        Cli.Run("check", lib.Path, "--fail-on", "off", "--report", $"junit:{viaReport}");
        Cli.Run("check", lib.Path, "--fail-on", "off", "--format", "junit", "--out", viaOut);

        Assert.Equal(File.ReadAllText(viaOut), File.ReadAllText(viaReport));
    }

    [Fact]
    public void APrimaryOutputFileAndAnExtraReport_AreBothWritten()
    {
        using var lib = new TempLibrary();
        var primary = lib.At("primary.json");
        var extra = lib.At("extra.md");

        var (code, stdout, _) = Cli.Run(
            "check", lib.Path, "--fail-on", "off",
            "--format", "json", "--out", primary, "--report", $"markdown:{extra}");

        Assert.Equal(0, code);
        Assert.Equal("", stdout);                                   // the primary went to its file
        Assert.Contains("\"findings\"", File.ReadAllText(primary));
        Assert.Contains("|", File.ReadAllText(extra));
    }

    [Fact]
    public void AConsoleReportWrittenToAFile_CarriesNoColourCodes()
    {
        using var lib = new TempLibrary();
        var log = lib.At("log.txt");

        Cli.Run("check", lib.Path, "--fail-on", "off", "--report", $"console:{log}");

        Assert.DoesNotContain('\u001b', File.ReadAllText(log));
    }

    [Fact]
    public void TheGateStillDecidesTheExitCode()
    {
        using var lib = new TempLibrary();

        var (code, _, _) = Cli.Run(
            "check", lib.Path, "--fail-on", "warning", "--report", $"junit:{lib.At("g.xml")}");

        Assert.Equal(1, code);
        Assert.True(File.Exists(lib.At("g.xml")));   // and the report is written anyway
    }

    [Fact]
    public void AnUnwritableDestination_IsAnError()
    {
        using var lib = new TempLibrary();

        // A directory where a file should go: the write fails, and a pipeline that asked for two
        // files and silently got one would carry the gap into whatever reads them.
        var asDirectory = lib.At("taken.xml");
        Directory.CreateDirectory(asDirectory);

        var (code, _, stderr) = Cli.Run(
            "check", lib.Path, "--fail-on", "off", "--report", $"junit:{asDirectory}");

        Assert.Equal(2, code);
        Assert.Contains("failed to write", stderr);
    }

    [Theory]
    [InlineData("junit", "expected <format>:<path>")]
    [InlineData("junit:", "expected <format>:<path>")]
    [InlineData(":results.xml", "expected <format>:<path>")]
    [InlineData("xml:results.xml", "invalid --report format 'xml'")]
    public void AMalformedValue_IsAUsageError(string value, string expected)
    {
        Assert.False(CheckOptions.TryParse(["lib", "--report", value], out _, out var error));
        Assert.Contains(expected, error!);
    }

    [Fact]
    public void TwoReportsToTheSameFile_IsAUsageError()
    {
        // The second would overwrite the first, and the pipeline would carry on believing it had both.
        Assert.False(CheckOptions.TryParse(
            ["lib", "--report", "junit:out.xml", "--report", "sarif:out.xml"], out _, out var error));
        Assert.Contains("more than once", error!);
    }

    [Fact]
    public void AWindowsPathKeepsItsDriveLetter()
    {
        // Split at the first colon only.
        Assert.True(CheckOptions.TryParse(
            ["lib", "--report", @"junit:C:\build\mlqt.xml"], out var options, out _));

        var report = Assert.Single(options!.Reports);
        Assert.Equal(OutputFormat.JUnit, report.Format);
        Assert.Equal(@"C:\build\mlqt.xml", report.Path);
    }

    [Fact]
    public void WithoutAny_NothingChanges()
    {
        Assert.True(CheckOptions.TryParse(["lib"], out var options, out _));
        Assert.Empty(options!.Reports);
    }
}
