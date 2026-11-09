using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// mlqt_write_class's import: classes a script wrote brought into a package as though written here -
/// a file each, the package's within clause, the repository's formatting, a line in package.order, and
/// loaded. A data generator writes .mo files directly; package.order was then written by hand, the
/// library reloaded and the files formatted, each a step of its own.
/// </summary>
public class ImportClassesTests
{
    private static (WriteFormatTools Tools, string PackageDir) Load(TestHost host)
    {
        var root = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["P/package.mo"] = "within;\npackage P \"p\"\nend P;\n",
            ["P/A.mo"] = "within P;\nmodel A \"a\"\nend A;\n",
            ["P/package.order"] = "A\n",
            ["P/Sub.mo"] = "within P;\npackage Sub \"single file\"\nend Sub;\n",
        });
        File.AppendAllText(Path.Combine(root, "P", "package.order"), "Sub\n");
        var repository = host.Repositories.AddRepositoryAsync(root, startMonitoring: false).GetAwaiter().GetResult().Repository!;
        host.Repositories.LoadLibrariesAsync(repository.Id).GetAwaiter().GetResult();
        repository.StyleSettings!.ApplyFormattingRules = true;
        return (new WriteFormatTools(host.Libraries, host.Repositories, host.Resources, host.Session), Path.Combine(root, "P"));
    }

    /// <summary>A folder outside the library, as a generator's output directory.</summary>
    private static string Generated(TestHost host, Dictionary<string, string> files) => host.WriteLibraryDir(files);

    private static string Order(string packageDir) => File.ReadAllText(Path.Combine(packageDir, "package.order")).ReplaceLineEndings("\n");

    [Fact]
    public async Task AFolderOfGeneratedClasses_IsImported_Listed_Formatted_AndLoaded()
    {
        using var host = new TestHost();
        var (tools, packageDir) = Load(host);
        var output = Generated(host, new Dictionary<string, string>
        {
            ["Data1.mo"] = "model Data1 \"d1\"\nparameter Real x=1;\nend Data1;\n",
            ["Data2.mo"] = "within Somewhere.Else;\nmodel Data2 \"d2\"\nparameter Real y=2;\nend Data2;\n",
        });

        var res = ToolAssert.Ok<WriteClassResult>(await tools.WriteClass(importFrom: output, importInto: "P"));

        Assert.Equal(["P.Data1", "P.Data2"], res.Written.Select(w => w.ClassId));
        Assert.All(res.Written, w => Assert.Equal("created", w.Action));
        Assert.Equal("A\nSub\nData1\nData2\n", Order(packageDir));
        var data2 = File.ReadAllText(Path.Combine(packageDir, "Data2.mo"));
        Assert.StartsWith("within P;", data2);                          // the package's, not the generator's
        Assert.Contains("\n  parameter Real y=2;", data2);              // the repository's formatting: indented
        Assert.NotNull(host.Libraries.GetModelById("P.Data1"));
        Assert.NotNull(host.Libraries.GetModelById("P.Data2"));
    }

    [Fact]
    public async Task AFileWrittenStraightIntoThePackage_IsAdoptedWhereItIs()
    {
        using var host = new TestHost();
        var (tools, packageDir) = Load(host);
        var gen = Path.Combine(packageDir, "Gen.mo");
        File.WriteAllText(gen, "model Gen \"g\"\nReal z=3;\nend Gen;\n");

        ToolAssert.Ok<WriteClassResult>(await tools.WriteClass(importFrom: gen, importInto: "P"));

        Assert.EndsWith("Gen\n", Order(packageDir));
        Assert.StartsWith("within P;", File.ReadAllText(gen));
        Assert.NotNull(host.Libraries.GetModelById("P.Gen"));
    }

    [Fact]
    public async Task AClassTheGeneratorRewroteInPlace_IsAdopted_WithoutReplace()
    {
        using var host = new TestHost();
        var (tools, packageDir) = Load(host);
        File.WriteAllText(Path.Combine(packageDir, "A.mo"), "within P;\nmodel A \"a\"\nReal regenerated=1;\nend A;\n");

        var res = ToolAssert.Ok<WriteClassResult>(await tools.WriteClass(importFrom: Path.Combine(packageDir, "A.mo"), importInto: "P"));

        Assert.Equal("adopted", Assert.Single(res.Written).Action);
        Assert.Contains("regenerated", host.Libraries.GetModelById("P.A")!.Definition.ModelicaCode);
        Assert.Equal(1, Order(packageDir).Split('\n').Count(l => l == "A"));   // not listed twice
    }

    [Fact]
    public async Task AClassAlreadyThere_IsReplacedOnlyWhenAskedTo()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);
        var output = Generated(host, new Dictionary<string, string> { ["A.mo"] = "model A \"new\"\nend A;\n" });

        var refused = ToolAssert.Error(await tools.WriteClass(importFrom: output, importInto: "P"));
        var replaced = ToolAssert.Ok<WriteClassResult>(await tools.WriteClass(importFrom: output, importInto: "P", replace: true));

        Assert.Contains("replace=true", refused.Error);
        Assert.Equal("replaced", Assert.Single(replaced.Written).Action);
        Assert.Contains("\"new\"", host.Libraries.GetModelById("P.A")!.Definition.ModelicaCode);
    }

    [Fact]
    public async Task OneFileThatCannotBeImported_ImportsNothing_AndEveryProblemIsListed()
    {
        using var host = new TestHost();
        var (tools, packageDir) = Load(host);
        var output = Generated(host, new Dictionary<string, string>
        {
            ["Good.mo"] = "model Good \"g\"\nend Good;\n",
            ["Bad.mo"] = "model Bad \"b\"\n  Real = ;\nend Bad;\n",
            ["Two.mo"] = "model X \"x\"\nend X;\nmodel Y \"y\"\nend Y;\n",
            ["Swap.mo"] = "replaceable model Swap \"s\"\nend Swap;\n",
        });

        var err = ToolAssert.Error(await tools.WriteClass(importFrom: output, importInto: "P"));

        Assert.Contains("Bad.mo", err.Error);
        Assert.Contains("Two.mo holds 2 classes", err.Error);
        Assert.Contains("Swap.mo: line 1", err.Error);   // a file has no place for replaceable: it does not parse
        Assert.False(File.Exists(Path.Combine(packageDir, "Good.mo")));
        Assert.Equal("A\nSub\n", Order(packageDir));
    }

    [Fact]
    public async Task AFileInThePackageNamedForAnotherClass_IsRefused()
    {
        using var host = new TestHost();
        var (tools, packageDir) = Load(host);
        File.WriteAllText(Path.Combine(packageDir, "Wrong.mo"), "model Right \"r\"\nend Right;\n");

        var err = ToolAssert.Error(await tools.WriteClass(importFrom: Path.Combine(packageDir, "Wrong.mo"), importInto: "P"));

        Assert.Contains("Right.mo", err.Error);
    }

    [Fact]
    public async Task APackageStoredInOneFile_CannotTakeFilesOfItsOwn()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);
        var output = Generated(host, new Dictionary<string, string> { ["D.mo"] = "model D \"d\"\nend D;\n" });

        Assert.Contains("not a package stored as a directory",
            ToolAssert.Error(await tools.WriteClass(importFrom: output, importInto: "P.Sub")).Error);
    }

    [Fact]
    public async Task APreview_ShowsTheFiles_AndWritesNothing()
    {
        using var host = new TestHost();
        var (tools, packageDir) = Load(host);
        var output = Generated(host, new Dictionary<string, string> { ["D.mo"] = "model D \"d\"\nReal q=1;\nend D;\n" });

        var res = ToolAssert.Ok<WriteClassResult>(await tools.WriteClass(importFrom: output, importInto: "P", preview: true));

        Assert.True(res.PreviewOnly);
        Assert.Contains("\n  Real q=1;", Assert.Single(res.Written).NewContent);   // formatted, as it would be written
        Assert.False(File.Exists(Path.Combine(packageDir, "D.mo")));
        Assert.Null(host.Libraries.GetModelById("P.D"));
    }

    [Fact]
    public async Task TheArguments_AreChecked()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);

        Assert.Contains("both importFrom", ToolAssert.Error(await tools.WriteClass(importFrom: "x")).Error);
        Assert.Contains("not both", ToolAssert.Error(await tools.WriteClass(
            [new ClassToWrite { ParentId = "P", Source = "model Q end Q;" }], importFrom: "x", importInto: "P")).Error);
        Assert.Contains("neither a .mo file nor a folder", ToolAssert.Error(await tools.WriteClass(
            importFrom: Path.Combine(Path.GetTempPath(), "no-such-path-" + Guid.NewGuid()), importInto: "P")).Error);
        Assert.Contains("or give importFrom", ToolAssert.Error(await tools.WriteClass()).Error);
    }

    [Fact]
    public async Task ARelativePath_IsRefused()
    {
        // Relative to the server's working directory, which the agent cannot see.
        using var host = new TestHost();
        var (tools, _) = Load(host);

        Assert.Contains("relative path", ToolAssert.Error(await tools.WriteClass(importFrom: "generated", importInto: "P")).Error);
    }

    [Fact]
    public async Task AGeneratedFilesHeader_StaysAboveTheWithinClause()
    {
        using var host = new TestHost();
        var (tools, packageDir) = Load(host);
        var output = Generated(host, new Dictionary<string, string>
        {
            ["H.mo"] = "// Generated by a script - do not edit\nwithin Somewhere;\nmodel H \"h\"\nend H;\n",
        });

        ToolAssert.Ok<WriteClassResult>(await tools.WriteClass(importFrom: output, importInto: "P"));

        Assert.StartsWith("// Generated by a script - do not edit\nwithin P;\nmodel H",
            File.ReadAllText(Path.Combine(packageDir, "H.mo")).ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task AFileAlreadyAsItWouldBeWritten_IsLeftAlone_ButStillListed()
    {
        // Importing a package's own folder after a generator rewrote one file rewrote every file in it.
        using var host = new TestHost();
        var (tools, packageDir) = Load(host);
        var gen = Path.Combine(packageDir, "Gen.mo");
        File.WriteAllText(gen, "within P;\nmodel Gen \"g\"\nend Gen;\n");
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(gen, stamp);

        var res = ToolAssert.Ok<WriteClassResult>(await tools.WriteClass(importFrom: gen, importInto: "P"));

        Assert.Equal("unchanged", Assert.Single(res.Written).Action);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(gen));
        Assert.EndsWith("Gen\n", Order(packageDir));
        Assert.NotNull(host.Libraries.GetModelById("P.Gen"));
    }

    [Fact]
    public async Task AWriteThatFailsPartway_PutsBackEverythingAlreadyWritten()
    {
        using var host = new TestHost();
        var (tools, packageDir) = Load(host);
        var output = Generated(host, new Dictionary<string, string>
        {
            ["Data1.mo"] = "model Data1 \"d1\"\nend Data1;\n",
            ["Data2.mo"] = "model Data2 \"d2\"\nend Data2;\n",
        });
        // A folder where Data2.mo would go: it passes the check for writability, and the write fails.
        Directory.CreateDirectory(Path.Combine(packageDir, "Data2.mo"));

        var err = ToolAssert.Error(await tools.WriteClass(importFrom: output, importInto: "P"));

        Assert.Contains("put back", err.Error);
        Assert.False(File.Exists(Path.Combine(packageDir, "Data1.mo")));   // written first, then removed
        Assert.Equal("A\nSub\n", Order(packageDir));
        Assert.Null(host.Libraries.GetModelById("P.Data1"));
    }
}
