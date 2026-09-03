using System.Text.Json;
using MLQT.Cli;

namespace MLQT.Cli.Tests;

/// <summary>
/// What `mlqt compare` reports about two copies of a library.
///
/// <para>The command exists to answer one question after a bulk edit — did anything get lost? — so the
/// behaviour that matters most is what it does <b>not</b> report: a library rearranged on disk, with
/// every class still present, has to come back clean, or the answer is unreadable for exactly the
/// libraries big enough to need it.</para>
/// </summary>
public class CompareCommandTests
{
    private const string BlocksInOneFile = """
        within Demo;
        package Blocks "Blocks"
          model Gain "A gain"
            parameter Real k = 1;
          end Gain;

          model Sum "A sum"
            parameter Real n = 2;
          end Sum;
        end Blocks;
        """;

    private const string TopLevelPackage = """
        package Demo "A demo library"
          annotation (version="1.0.0");
        end Demo;
        """;

    /// <summary>A temp directory holding one or more Modelica libraries.</summary>
    private sealed class TempLibrary : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "mlqt-compare-test-" + Guid.NewGuid().ToString("N"));

        public TempLibrary() => Directory.CreateDirectory(Path);

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

    /// <summary>Demo.Blocks with both classes inline in one Blocks.mo, the way it started.</summary>
    private static TempLibrary Original() =>
        new TempLibrary()
            .WithFile("Demo/package.mo", TopLevelPackage)
            .WithFile("Demo/package.order", "Blocks\n")
            .WithFile("Demo/Blocks.mo", BlocksInOneFile);

    /// <summary>The same classes, split into a package directory with a file each.</summary>
    private static TempLibrary Restructured() =>
        new TempLibrary()
            .WithFile("Demo/package.mo", TopLevelPackage)
            .WithFile("Demo/package.order", "Blocks\n")
            .WithFile("Demo/Blocks/package.mo", "within Demo;\npackage Blocks \"Blocks\"\nend Blocks;\n")
            .WithFile("Demo/Blocks/package.order", "Gain\nSum\n")
            .WithFile(
                "Demo/Blocks/Gain.mo",
                "within Demo.Blocks;\nmodel Gain \"A gain\"\n  parameter Real k = 1;\nend Gain;\n")
            .WithFile(
                "Demo/Blocks/Sum.mo",
                "within Demo.Blocks;\nmodel Sum \"A sum\"\n  parameter Real n = 2;\nend Sum;\n");

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CliEntry.RunAsync(args, stdout, stderr).GetAwaiter().GetResult();
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void IdenticalLibraries_ReportNothingMissing_AndPass()
    {
        using var left = Original();
        using var right = Original();

        var (code, stdout, _) = Run("compare", left.Path, right.Path);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("No classes are missing from B.", stdout);
        Assert.Contains("0 missing, 0 added", stdout);
    }

    [Fact]
    public void RestructuringOnDisk_IsNotADifference()
    {
        // The whole point: classes are matched on their Modelica name, so splitting a package file
        // into a directory of one file per class must come back clean.
        using var left = Original();
        using var right = Restructured();

        var (code, stdout, _) = Run("compare", left.Path, right.Path);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("No classes are missing from B.", stdout);
    }

    [Fact]
    public void ADeletedClass_IsListedWithWhereItWas_AndFails()
    {
        using var left = Original();
        using var right = Restructured();
        File.Delete(Path.Combine(right.Path, "Demo", "Blocks", "Sum.mo"));

        var (code, stdout, _) = Run("compare", left.Path, right.Path);

        Assert.Equal(ExitCodes.GateFailed, code);
        Assert.Contains("1 class is missing from B:", stdout);
        Assert.Contains("Demo.Blocks.Sum", stdout);
        Assert.Contains("Demo/Blocks.mo:7", stdout); // where it was in A, so it can be recovered
        Assert.DoesNotContain("Demo.Blocks.Gain", stdout);
    }

    [Fact]
    public void AClassThatLostItsWithinClause_IsReportedAsMissingWithTheAddedNameAsALead()
    {
        // The failure mode a restructure actually produces: the class is still there, but rooted
        // somewhere else, so it reads as one missing name and one added name.
        using var left = Original();
        using var right = Restructured();
        File.WriteAllText(
            Path.Combine(right.Path, "Demo", "Blocks", "Sum.mo"),
            "model Sum \"A sum\"\n  parameter Real n = 2;\nend Sum;\n");

        var (code, stdout, _) = Run("compare", left.Path, right.Path);

        Assert.Equal(ExitCodes.GateFailed, code);
        Assert.Contains("Demo.Blocks.Sum", stdout);
        Assert.Contains("B has a new class of this name: Sum", stdout);
        Assert.Contains("1 class is only in B:", stdout);
    }

    [Fact]
    public void NoAdded_LeavesTheAddedClassesOut()
    {
        using var left = Original();
        using var right = Restructured();
        File.WriteAllText(
            Path.Combine(right.Path, "Demo", "Blocks", "Sum.mo"),
            "model Sum \"A sum\"\nend Sum;\n");

        var (_, stdout, _) = Run("compare", left.Path, right.Path, "--no-added");

        Assert.Contains("missing from B:", stdout);
        Assert.DoesNotContain("only in B:", stdout);
        Assert.Contains("1 missing, 1 added", stdout); // still counted, just not listed
    }

    [Fact]
    public void AFileThatCannotBeParsed_IsCalledOutRatherThanReadAsDeletion()
    {
        using var left = Original();
        using var right = Restructured();
        File.WriteAllText(
            Path.Combine(right.Path, "Demo", "Blocks", "Gain.mo"),
            "within Demo.Blocks;\n<<<<<<< HEAD\nmodel Gain\n");

        var (code, stdout, _) = Run("compare", left.Path, right.Path);

        Assert.Equal(ExitCodes.GateFailed, code);
        Assert.Contains("could not be parsed", stdout);
        Assert.Contains("Demo/Blocks/Gain.mo", stdout);
    }

    [Fact]
    public void Json_CarriesTheCountsAndEveryMissingName()
    {
        using var left = Original();
        using var right = Restructured();
        File.Delete(Path.Combine(right.Path, "Demo", "Blocks", "Sum.mo"));

        var (code, stdout, _) = Run("compare", left.Path, right.Path, "--format", "json");

        Assert.Equal(ExitCodes.GateFailed, code);
        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        Assert.Equal("compare", root.GetProperty("command").GetString());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("missing").GetInt32());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("added").GetInt32());

        var missing = root.GetProperty("missing").EnumerateArray().Single();
        Assert.Equal("Demo.Blocks.Sum", missing.GetProperty("name").GetString());
        Assert.Equal("model", missing.GetProperty("classType").GetString());
        Assert.Equal("Demo/Blocks.mo", missing.GetProperty("file").GetString());
    }

    [Fact]
    public void Out_WritesTheReportToTheFileInsteadOfStdout()
    {
        using var left = Original();
        using var right = Original();
        var outPath = Path.Combine(Path.GetTempPath(), $"mlqt-compare-{Guid.NewGuid():N}.txt");

        try
        {
            var (code, stdout, _) = Run("compare", left.Path, right.Path, "--out", outPath);

            Assert.Equal(ExitCodes.Ok, code);
            Assert.Empty(stdout);
            Assert.Contains("No classes are missing from B.", File.ReadAllText(outPath));
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Fact]
    public void ASecondPathThatDoesNotExist_FailsAsSetup_BeforeLoadingTheFirst()
    {
        // Loading the left library is the expensive half. A CI job that mistyped the second path
        // should not pay for it before being told.
        using var left = Original();
        var missing = Path.Combine(Path.GetTempPath(), "mlqt-compare-does-not-exist");

        var (code, stdout, stderr) = Run("compare", left.Path, missing);

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains($"library path not found: {missing}", stderr);
        Assert.DoesNotContain("note: loading", stderr);
        Assert.Empty(stdout);
    }

    [Fact]
    public void ADirectoryWithNoLibrariesInIt_FailsAsSetup()
    {
        using var left = Original();
        using var empty = new TempLibrary();

        var (code, _, stderr) = Run("compare", left.Path, empty.Path);

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("no Modelica libraries found", stderr);
    }

    [Theory]
    [InlineData(new[] { "compare" }, "missing <library-a> and <library-b>")]
    [InlineData(new[] { "compare", "one" }, "missing <library-b>")]
    [InlineData(new[] { "compare", "one", "two", "three" }, "unexpected argument 'three'")]
    [InlineData(new[] { "compare", "one", "two", "--wat" }, "unknown option '--wat'")]
    [InlineData(new[] { "compare", "one", "two", "--format", "xml" }, "unknown format 'xml'")]
    [InlineData(new[] { "compare", "one", "two", "--out" }, "option '--out' requires a value")]
    public void ABadCommandLine_IsNamedAndFailsAsSetup(string[] args, string expected)
    {
        var (code, stdout, stderr) = Run(args);

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains(expected, stderr);
        Assert.Empty(stdout);
    }

    [Fact]
    public void TheUsageText_MentionsCompare()
    {
        var (_, stdout, _) = Run("--help");
        Assert.Contains("mlqt compare <library-a> <library-b>", stdout);
    }
}
