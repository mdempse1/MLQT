using System.Text.Json;
using MLQT.Cli;

namespace MLQT.Cli.Tests;

/// <summary>
/// Gating on the coverage numbers. `--fail-on` answers "did this change introduce findings"; these
/// answer "is the library documented well enough", either against a figure the team chose or against
/// the last recorded snapshot — the ratchet, which a legacy library can adopt on day one.
/// </summary>
public class CoverageGateTests
{
    /// <summary>Four described-able classes (the package counts), three described: 75%.</summary>
    private const string Library = """
        within ;
        package Cov "A library"
          model A "Described"
          end A;
          model B "Described"
          end B;
          model C
          end C;
        end Cov;
        """;

    private const string Settings =
        """{ "RuleSeverities": { "MLQT.Doc.ClassDescription": "Warning" } }""";

    private sealed class TempLibrary : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "mlqt-coverage-" + Guid.NewGuid().ToString("N"));

        public TempLibrary(string source = Library, string order = "A\nB\nC\n")
        {
            Directory.CreateDirectory(Path);
            File.WriteAllText(System.IO.Path.Combine(Path, "package.mo"), source);
            File.WriteAllText(System.IO.Path.Combine(Path, "package.order"), order);
            var settingsDir = System.IO.Path.Combine(Path, ".mlqt");
            Directory.CreateDirectory(settingsDir);
            File.WriteAllText(System.IO.Path.Combine(settingsDir, "settings.json"), Settings);
        }

        public string Path => System.IO.Path.Combine(Root, "Cov");

        /// <summary>Rewrites the library, to move the numbers between runs.</summary>
        public void Replace(string source, string order)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "package.mo"), source);
            File.WriteAllText(System.IO.Path.Combine(Path, "package.order"), order);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
        }
    }

    private static (int code, string stdout, string stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CliEntry.RunAsync(args, stdout, stderr).GetAwaiter().GetResult();
        return (code, stdout.ToString(), stderr.ToString());
    }

    // ---- thresholds ----------------------------------------------------------------------------

    [Fact]
    public void BelowTheThreshold_FailsAndSaysByHowMuch()
    {
        using var lib = new TempLibrary();

        var (code, _, stderr) = Run("check", lib.Path, "--fail-on", "off", "--min-coverage", "80");

        Assert.Equal(1, code);
        Assert.Contains("coverage gate: Class description 75% is below the required 80%", stderr);
    }

    [Fact]
    public void AtOrAboveTheThreshold_Passes()
    {
        using var lib = new TempLibrary();

        var (code, _, stderr) = Run("check", lib.Path, "--fail-on", "off", "--min-coverage", "75");

        Assert.Equal(0, code);
        Assert.Contains("coverage gate passed", stderr);
    }

    [Fact]
    public void ANamedDimension_OverridesTheBlanketFigure()
    {
        // "80% everywhere, but 50% is enough on descriptions" — the shape a real policy takes.
        using var lib = new TempLibrary();

        var (code, _, _) = Run(
            "check", lib.Path, "--fail-on", "off",
            "--min-coverage", "80", "--min-coverage", "class-description=50");

        Assert.Equal(0, code);
    }

    [Theory]
    [InlineData("class-description=90")]
    [InlineData("ClassDescription=90")]
    [InlineData("Class description=90")]
    [InlineData("classdescription=90")]
    public void ADimensionCanBeSpelledHoweverIsNatural(string spec)
    {
        using var lib = new TempLibrary();

        var (code, _, stderr) = Run("check", lib.Path, "--fail-on", "off", "--min-coverage", spec);

        Assert.Equal(1, code);
        Assert.Contains("Class description", stderr);
    }

    [Fact]
    public void ADimensionTheRunDoesNotMeasure_WarnsRatherThanSilentlyCheckingNothing()
    {
        // The rule is off for this repository, so there is no number. A requirement that quietly
        // checks nothing is the failure a quality gate can least afford.
        using var lib = new TempLibrary();

        var (code, _, stderr) = Run("check", lib.Path, "--fail-on", "off", "--min-coverage", "icon=100");

        Assert.Equal(0, code);
        Assert.Contains("which this run does not measure", stderr);
    }

    [Theory]
    [InlineData("nonsense=50", "unknown coverage dimension")]
    [InlineData("abc", "expected a percentage")]
    [InlineData("120", "expected a percentage")]
    [InlineData("-1", "expected a percentage")]
    public void AMalformedRequirement_IsAUsageError(string spec, string expected)
    {
        Assert.False(CheckOptions.TryParse(["lib", "--min-coverage", spec], out _, out var error));
        Assert.Contains(expected, error!);
    }

    // ---- the ratchet ---------------------------------------------------------------------------

    [Fact]
    public void TheRatchetWithNoHistory_SaysSoAndPasses()
    {
        using var lib = new TempLibrary();

        var (code, _, stderr) = Run("check", lib.Path, "--fail-on", "off", "--coverage-ratchet");

        Assert.Equal(0, code);
        Assert.Contains("nothing to compare against yet", stderr);
    }

    [Fact]
    public void TheRatchetHoldsWhenNothingMoved_AndFailsWhenCoverageDrops()
    {
        using var lib = new TempLibrary(
            """
            within ;
            package Cov "A library"
              model A "Described"
              end A;
              model B "Described"
              end B;
            end Cov;
            """, "A\nB\n");

        Run("check", lib.Path, "--fail-on", "off", "--metrics");                     // 100%

        var (unchanged, _, _) = Run("check", lib.Path, "--fail-on", "off", "--coverage-ratchet");
        Assert.Equal(0, unchanged);

        lib.Replace(Library, "A\nB\nC\n");                                            // now 75%

        var (dropped, _, stderr) = Run("check", lib.Path, "--fail-on", "off", "--coverage-ratchet");
        Assert.Equal(1, dropped);
        Assert.Contains("is below the last recorded 100%", stderr);
    }

    [Fact]
    public void TheRatchetAllowsAnImprovement()
    {
        using var lib = new TempLibrary();
        Run("check", lib.Path, "--fail-on", "off", "--metrics");                      // 75%

        lib.Replace(
            """
            within ;
            package Cov "A library"
              model A "Described"
              end A;
              model B "Described"
              end B;
              model C "Described now"
              end C;
            end Cov;
            """, "A\nB\nC\n");

        var (code, _, _) = Run("check", lib.Path, "--fail-on", "off", "--coverage-ratchet");

        Assert.Equal(0, code);
    }

    // ---- how it sits beside the findings gate --------------------------------------------------

    [Fact]
    public void ItFailsEvenWithTheFindingsGateOff()
    {
        // Switching off --fail-on says "findings do not fail this build". It does not say "and neither
        // does the coverage requirement I also asked for".
        using var lib = new TempLibrary();

        var (code, _, _) = Run("check", lib.Path, "--fail-on", "off", "--min-coverage", "80");

        Assert.Equal(1, code);
    }

    [Fact]
    public void WithoutAnyRequirement_NothingIsJudgedOrReported()
    {
        using var lib = new TempLibrary();

        var (code, stdout, stderr) = Run("check", lib.Path, "--fail-on", "off", "--format", "json");

        Assert.Equal(0, code);
        Assert.DoesNotContain("coverage gate", stderr);
        using var document = JsonDocument.Parse(stdout);
        // The formatter omits null sections, so a report from a run with no coverage requirement
        // carries no coverage section at all.
        Assert.False(document.RootElement.TryGetProperty("coverageGate", out _));
    }

    [Fact]
    public void TheJsonReportCarriesEachRequirementAndItsVerdict()
    {
        using var lib = new TempLibrary();

        var (_, stdout, _) = Run(
            "check", lib.Path, "--fail-on", "off", "--format", "json", "--min-coverage", "80");

        using var document = JsonDocument.Parse(stdout);
        var entry = document.RootElement.GetProperty("coverageGate").EnumerateArray()
            .Single(e => e.GetProperty("Dimension").GetString() == "Class description");

        Assert.Equal(75, entry.GetProperty("Percent").GetDouble(), 1);
        Assert.Equal(80, entry.GetProperty("Required").GetDouble());
        Assert.Equal("threshold", entry.GetProperty("Requirement").GetString());
        Assert.False(entry.GetProperty("Passed").GetBoolean());
    }
}
