using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary><c>mlqt_write_class</c> and <c>mlqt_format</c>: many classes, or a whole library, in one call.</summary>
public class WriteFormatToolsTests
{
    private static string Lf(string text) => text.Replace("\r\n", "\n");

    private static readonly string PackageMo = Lf("""
        within;
        package P "p"
          model A "a"
            Real x;
          end A;
        end P;
        """);

    private static WriteFormatTools Load(TestHost h, IDictionary<string, string>? files = null)
    {
        var dir = h.WriteLibraryDir(files ?? new Dictionary<string, string> { ["package.mo"] = PackageMo });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return new WriteFormatTools(h.Libraries, h.Repositories, h.Resources, h.Session);
    }

    private static ClassToWrite Class(string parent, string source) => new() { ParentId = parent, Source = source };

    [Fact]
    public async Task WriteClass_CreatesSeveral_InOrder()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<WriteClassResult>(await Load(host).WriteClass(
        [
            Class("P", "package Sources \"sources\"\nend Sources;"),
            Class("P.Sources", "block Constant \"constant\"\n  parameter Real k = 1;\n  output Real y;\nequation\n  y = k;\nend Constant;"),
            Class("P.Sources", "block Ramp \"ramp\"\n  output Real y;\nequation\n  y = time;\nend Ramp;"),
        ]));

        Assert.Equal(["P.Sources", "P.Sources.Constant", "P.Sources.Ramp"], res.Written.Select(w => w.ClassId).ToArray());
        Assert.All(res.Written, w => Assert.Equal("created", w.Action));
        Assert.NotNull(host.Libraries.GetModelById("P.Sources.Ramp"));
    }

    // Told nothing, an agent added the class it had just written to package.order a second time, with sed.
    [Fact]
    public async Task WriteClass_NamesThePackageOrderItListedTheClassIn()
    {
        using var host = new TestHost();
        var tools = Load(host, new Dictionary<string, string>
        {
            ["package.mo"] = "within;\npackage Q \"q\"\nend Q;\n",
            ["package.order"] = "One\n",
            ["One.mo"] = "within Q;\nmodel One \"one\"\nend One;\n",
        });

        var res = ToolAssert.Ok<WriteClassResult>(await tools.WriteClass([Class("Q", "model Two \"two\"\nend Two;")]));

        var order = Assert.Single(res.Written).ListedInPackageOrder;
        Assert.NotNull(order);
        Assert.Equal("package.order", Path.GetFileName(order));
        Assert.Equal(["One", "Two"], File.ReadAllLines(order));
    }

    [Fact]
    public async Task WriteClass_NamesNoPackageOrder_WhereThereIsNone()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<WriteClassResult>(await Load(host).WriteClass([Class("P", "model B \"b\"\nend B;")]));
        Assert.Null(Assert.Single(res.Written).ListedInPackageOrder);
    }

    // Seen on WSL, where the tests' temp root was <export>/.tmp: the class file was written and the
    // write reported, but the reload took the whole library for a hidden folder and skipped it, so the
    // next call found no class. Only folders below the library may make a file hidden.
    [Fact]
    public async Task WriteClass_ReachesTheGraph_ForALibraryUnderADotFolder()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            [Path.Combine(".tmp", "UserLib", "package.mo")] = "within;\npackage UserLib \"u\"\nend UserLib;\n",
            [Path.Combine(".tmp", "UserLib", "package.order")] = "One\n",
            [Path.Combine(".tmp", "UserLib", "One.mo")] = "within UserLib;\nmodel One \"one\"\nend One;\n",
        });
        await host.Libraries.AddLibraryFromDirectoryAsync(Path.Combine(dir, ".tmp", "UserLib"));
        var tools = new WriteFormatTools(host.Libraries, host.Repositories, host.Resources, host.Session);

        ToolAssert.Ok<WriteClassResult>(await tools.WriteClass([Class("UserLib", "model Mine \"mine\"\nend Mine;")]));

        Assert.NotNull(host.Libraries.GetModelById("UserLib.Mine"));
        Assert.NotNull(host.Libraries.GetOwningLibrary("UserLib.Mine"));
    }

    [Fact]
    public async Task WriteClass_ReplacesOnlyWhenAsked()
    {
        using var host = new TestHost();
        var tools = Load(host);
        var replacement = "model A \"a, again\"\n  Real x;\n  Real y;\nend A;";

        var refused = Assert.IsType<ToolError>(await tools.WriteClass([Class("P", replacement)]));
        Assert.Contains("replace=true", refused.Error);
        Assert.Contains("Nothing was written", refused.Error);

        var res = ToolAssert.Ok<WriteClassResult>(await tools.WriteClass([Class("P", replacement)], replace: true));
        Assert.Equal("replaced", Assert.Single(res.Written).Action);
        Assert.Contains("Real y;", host.Libraries.GetModelById("P.A")!.Definition.ModelicaCode);
    }

    [Fact]
    public async Task WriteClass_StopsAtTheFirstFailure_SayingWhatWasWritten()
    {
        using var host = new TestHost();
        var err = Assert.IsType<ToolError>(await Load(host).WriteClass(
        [
            Class("P", "model B \"b\"\nend B;"),
            Class("P", "this is not Modelica"),
            Class("P", "model C \"c\"\nend C;"),
        ]));

        Assert.Contains("Class 1 was not written", err.Error);
        Assert.Contains("Written before it: P.B", err.Error);
        Assert.NotNull(host.Libraries.GetModelById("P.B"));
        Assert.Null(host.Libraries.GetModelById("P.C"));
    }

    [Fact]
    public async Task WriteClass_Preview_WritesNothing()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<WriteClassResult>(await Load(host).WriteClass([Class("P", "model B \"b\"\nend B;")], preview: true));
        Assert.True(res.PreviewOnly);
        Assert.Contains("model B", Assert.Single(res.Written).NewContent);
        Assert.Null(host.Libraries.GetModelById("P.B"));
    }

    [Fact]
    public async Task WriteClass_RefusesAnEmptyList()
    {
        using var host = new TestHost();
        Assert.IsType<ToolError>(await Load(host).WriteClass([]));
    }

    [Fact]
    public async Task Format_ALibrary_FormatsEachOfItsFiles()
    {
        using var host = new TestHost();
        var tools = Load(host, new Dictionary<string, string>
        {
            ["package.mo"] = "within;\npackage Q \"q\"\nend Q;\n",
            ["package.order"] = "One\nTwo\n",
            ["One.mo"] = "within Q;\nmodel One \"one\"\n  Real a;Real b;\nend One;\n",
            ["Two.mo"] = "within Q;\nmodel Two \"two\"\n  Real c;  Real d;\nend Two;\n",
        });

        var res = ToolAssert.Ok<LibraryFormatResult>(await tools.Format(library: "Q"));

        Assert.Equal(3, res.Files);
        Assert.Contains(res.ChangedFiles, f => f.EndsWith("One.mo"));
        Assert.Contains(res.ChangedFiles, f => f.EndsWith("Two.mo"));
        Assert.Empty(res.Failures);
        Assert.DoesNotContain(";Real", File.ReadAllText(res.ChangedFiles.Single(f => f.EndsWith("One.mo"))));
    }

    [Fact]
    public async Task Format_AClassOrSource_IsHandedToTheExistingTools()
    {
        using var host = new TestHost();
        var tools = Load(host);
        Assert.IsType<FormatClassResult>(await tools.Format(classId: "P.A", preview: true));
        Assert.IsType<FormatCodeResult>(await tools.Format(source: "model X\n  Real a;Real b;\nend X;"));
    }

    [Fact]
    public async Task Format_NeedsExactlyOneTarget_AndAKnownLibrary()
    {
        using var host = new TestHost();
        var tools = Load(host);
        Assert.Contains("exactly one", Assert.IsType<ToolError>(await tools.Format()).Error);
        Assert.Contains("exactly one", Assert.IsType<ToolError>(await tools.Format(classId: "P.A", library: "P")).Error);
        Assert.IsType<ToolError>(await tools.Format(library: "NoSuchLibrary"));
    }
}
