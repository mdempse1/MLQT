using System.Text.Json;
using MLQT.Cli;

namespace MLQT.Cli.Tests;

/// <summary>
/// Which root the file paths in SARIF are written against. A reader resolves them against a root of
/// its own — for GitHub code scanning, the repository it checked out — so a library that lives in a
/// subdirectory has to say so, or every annotation names a path that does not exist there.
/// </summary>
public class SarifBaseTests
{
    private const string Model = """
        model Undescribed
          parameter Real c = 3;
        end Undescribed;
        """;

    private const string Settings =
        """{ "RuleSeverities": { "MLQT.Doc.ClassDescription": "Warning" } }""";

    /// <summary>A repository with the library in <c>Libraries/Fix</c>, as a real one usually has.</summary>
    private sealed class TempRepo : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "mlqt-sarif-base-" + Guid.NewGuid().ToString("N"));

        public TempRepo()
        {
            Directory.CreateDirectory(LibraryPath);
            File.WriteAllText(System.IO.Path.Combine(LibraryPath, "Undescribed.mo"), Model);
            var settingsDir = System.IO.Path.Combine(LibraryPath, ".mlqt");
            Directory.CreateDirectory(settingsDir);
            File.WriteAllText(System.IO.Path.Combine(settingsDir, "settings.json"), Settings);
        }

        public string LibraryPath => System.IO.Path.Combine(Root, "Libraries", "Fix");

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

    private static List<string> UrisIn(string sarif)
    {
        using var document = JsonDocument.Parse(sarif);
        return document.RootElement.GetProperty("runs")[0].GetProperty("results")
            .EnumerateArray()
            .Select(r => r.GetProperty("locations")[0]
                .GetProperty("physicalLocation").GetProperty("artifactLocation")
                .GetProperty("uri").GetString()!)
            .ToList();
    }

    [Fact]
    public void WithoutABase_PathsAreRelativeToTheLibrary()
    {
        using var repo = new TempRepo();

        var (_, stdout, _) = Run("check", repo.LibraryPath, "--format", "sarif", "--fail-on", "off");

        Assert.Equal(["Undescribed.mo"], UrisIn(stdout));
    }

    [Fact]
    public void WithTheRepositoryRootAsBase_PathsAreRelativeToIt()
    {
        using var repo = new TempRepo();

        var (code, stdout, _) = Run(
            "check", repo.LibraryPath, "--format", "sarif", "--sarif-base", repo.Root, "--fail-on", "off");

        Assert.Equal(0, code);
        Assert.Equal(["Libraries/Fix/Undescribed.mo"], UrisIn(stdout));
    }

    [Fact]
    public void ARelativeBase_IsResolvedAgainstTheLibraryNotTheWorkingDirectory()
    {
        // The same convention as --config and --baseline: a CI job can name paths from the library
        // it is checking without knowing where the job happens to be running from.
        using var repo = new TempRepo();

        var (_, stdout, _) = Run(
            "check", repo.LibraryPath, "--format", "sarif", "--sarif-base", "../..", "--fail-on", "off");

        Assert.Equal(["Libraries/Fix/Undescribed.mo"], UrisIn(stdout));
    }

    [Fact]
    public void ABaseThatDoesNotContainTheLibrary_IsRefused()
    {
        // Paths would be written as ../.., which GitHub rejects — the same annotations-attach-to-
        // nothing failure the option exists to prevent.
        using var repo = new TempRepo();
        var elsewhere = System.IO.Path.Combine(repo.Root, "Elsewhere");
        Directory.CreateDirectory(elsewhere);

        var (code, _, stderr) = Run(
            "check", repo.LibraryPath, "--format", "sarif", "--sarif-base", elsewhere, "--fail-on", "off");

        Assert.Equal(2, code);
        Assert.Contains("does not contain the library", stderr);
    }

    [Fact]
    public void AMissingBaseDirectory_IsRefused()
    {
        using var repo = new TempRepo();

        var (code, _, stderr) = Run(
            "check", repo.LibraryPath, "--format", "sarif",
            "--sarif-base", System.IO.Path.Combine(repo.Root, "nope"), "--fail-on", "off");

        Assert.Equal(2, code);
        Assert.Contains("directory not found", stderr);
    }

    [Fact]
    public void AFileAsBase_IsRefused()
    {
        using var repo = new TempRepo();

        var (code, _, stderr) = Run(
            "check", repo.LibraryPath, "--format", "sarif",
            "--sarif-base", System.IO.Path.Combine(repo.LibraryPath, "Undescribed.mo"), "--fail-on", "off");

        Assert.Equal(2, code);
        Assert.Contains("must be a directory", stderr);
    }

    [Fact]
    public void WithAnotherFormat_ItSaysTheOptionDoesNothing()
    {
        // Silently ignoring it would leave a pipeline believing its paths had been rebased.
        using var repo = new TempRepo();

        var (code, _, stderr) = Run(
            "check", repo.LibraryPath, "--sarif-base", repo.Root, "--no-color", "--fail-on", "off");

        Assert.Equal(0, code);
        Assert.Contains("--sarif-base only affects SARIF output", stderr);
    }

    [Fact]
    public void WithSarifAsAnExtraReport_ItStillApplies()
    {
        // The note about doing nothing must not fire when the SARIF is coming from --report.
        using var repo = new TempRepo();
        var sarifPath = System.IO.Path.Combine(repo.Root, "mlqt.sarif");

        var (code, _, stderr) = Run(
            "check", repo.LibraryPath, "--no-color", "--fail-on", "off",
            "--sarif-base", repo.Root, "--report", $"sarif:{sarifPath}");

        Assert.Equal(0, code);
        Assert.DoesNotContain("--sarif-base only affects", stderr);
        Assert.Equal(["Libraries/Fix/Undescribed.mo"], UrisIn(File.ReadAllText(sarifPath)));
    }

    [Fact]
    public void WithoutAValue_ItIsAUsageError()
    {
        Assert.False(CheckOptions.TryParse(["lib", "--sarif-base"], out _, out var error));
        Assert.Contains("requires a value", error!);
    }

    [Fact]
    public void TheBaseIsCarriedOnTheParsedOptions()
    {
        Assert.True(CheckOptions.TryParse(["lib", "--sarif-base", "/repo"], out var options, out _));
        Assert.Equal("/repo", options!.SarifBasePath);
    }
}
