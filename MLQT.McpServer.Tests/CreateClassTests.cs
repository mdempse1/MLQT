using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class CreateClassTests
{
    private const string PackageMo = """
        within;
        package P "p"
          model A "a"
            Real x;
          end A;
        end P;
        """;

    private static EditTools Edit(TestHost h) => new(h.Libraries, h.Resources, h.Session);

    // A directory package P (package.mo + package.order) loaded into the library. Returns its directory.
    private static string LoadDirectoryPackage(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = PackageMo,
            ["package.order"] = "A\n"
        });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return dir;
    }

    [Fact]
    public async Task Create_Standalone_WritesFile_UpdatesOrder_AndWires()
    {
        using var host = new TestHost();
        var dir = LoadDirectoryPackage(host);

        var res = ToolAssert.Ok<CreateClassResult>(
            await Edit(host).CreateClass("P", "model B \"b\"\n  Real y;\nend B;"));

        Assert.True(res.Created);
        Assert.Equal("P.B", res.NewClassId);
        Assert.Equal("standalone", res.Storage);

        var newFile = Path.Combine(dir, "B.mo");
        Assert.True(File.Exists(newFile));
        Assert.Contains("within P;", File.ReadAllText(newFile));
        Assert.Contains("model B", File.ReadAllText(newFile));

        // package.order updated.
        Assert.Contains("B", File.ReadAllLines(Path.Combine(dir, "package.order")));

        // Wired into the graph under P.
        var node = host.Libraries.GetModelById("P.B");
        Assert.NotNull(node);
        Assert.Equal("P", node!.ParentModelName);
    }

    [Fact]
    public async Task Create_StandalonePackage_BecomesDirectoryPackage_AndNestsOneClassPerFile()
    {
        using var host = new TestHost();
        var dir = LoadDirectoryPackage(host);

        // A sub-package is stored as its own directory package (folder + package.mo + package.order)...
        var pkg = ToolAssert.Ok<CreateClassResult>(
            await Edit(host).CreateClass("P", "package Sub \"a subpackage\"\nend Sub;"));
        Assert.Equal("directory-package", pkg.Storage);
        var subDir = Path.Combine(dir, "Sub");
        Assert.True(File.Exists(Path.Combine(subDir, "package.mo")));
        Assert.True(File.Exists(Path.Combine(subDir, "package.order")));
        Assert.Contains("within P;", File.ReadAllText(Path.Combine(subDir, "package.mo")));
        // ...and registered in the parent's package.order.
        Assert.Contains("Sub", File.ReadAllLines(Path.Combine(dir, "package.order")));

        // A class added to the sub-package is now one-per-file (standalone), not nested in Sub/package.mo.
        var m = ToolAssert.Ok<CreateClassResult>(
            await Edit(host).CreateClass("P.Sub", "model M\n  Real x;\nend M;"));
        Assert.Equal("standalone", m.Storage);
        Assert.True(File.Exists(Path.Combine(subDir, "M.mo")));
        Assert.DoesNotContain("model M", File.ReadAllText(Path.Combine(subDir, "package.mo")));
        Assert.Contains("M", File.ReadAllLines(Path.Combine(subDir, "package.order")));

        Assert.Equal("P.Sub", host.Libraries.GetModelById("P.Sub.M")!.ParentModelName);
    }

    [Fact]
    public async Task Create_StandalonePackage_Preview_WritesNothing()
    {
        using var host = new TestHost();
        var dir = LoadDirectoryPackage(host);
        var res = ToolAssert.Ok<CreateClassResult>(
            await Edit(host).CreateClass("P", "package Sub\nend Sub;", preview: true));
        Assert.True(res.PreviewOnly);
        Assert.Equal("directory-package", res.Storage);
        Assert.False(Directory.Exists(Path.Combine(dir, "Sub")));
        Assert.Null(host.Libraries.GetModelById("P.Sub"));
    }

    [Fact]
    public async Task Create_NestedPackage_Forced_StaysInPackageMo()
    {
        using var host = new TestHost();
        var dir = LoadDirectoryPackage(host);
        var res = ToolAssert.Ok<CreateClassResult>(
            await Edit(host).CreateClass("P", "package Sub\nend Sub;", standalone: false));
        Assert.Equal("nested", res.Storage);
        Assert.False(Directory.Exists(Path.Combine(dir, "Sub")));
        Assert.Contains("package Sub", File.ReadAllText(Path.Combine(dir, "package.mo")));
    }

    [Fact]
    public async Task Create_Nested_InsertsIntoPackageMo()
    {
        using var host = new TestHost();
        var dir = LoadDirectoryPackage(host);

        var res = ToolAssert.Ok<CreateClassResult>(
            await Edit(host).CreateClass("P", "model C\n  Real z;\nend C;", standalone: false));

        Assert.Equal("nested", res.Storage);
        Assert.Contains("model C", File.ReadAllText(Path.Combine(dir, "package.mo")));
        Assert.False(File.Exists(Path.Combine(dir, "C.mo")));

        var node = host.Libraries.GetModelById("P.C");
        Assert.NotNull(node);
        Assert.Equal("P", node!.ParentModelName);
    }

    [Fact]
    public async Task Create_SingleFilePackage_ForcesNested()
    {
        using var host = new TestHost();
        var path = host.WriteMoFile("Lib.mo",
            "within;\npackage Lib \"l\"\n  model A\n    Real x;\n  end A;\nend Lib;");
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();

        // Auto placement: a single-file package is not a directory package, so it must nest.
        var res = ToolAssert.Ok<CreateClassResult>(
            await Edit(host).CreateClass("Lib", "model New\n  Real n;\nend New;"));

        Assert.Equal("nested", res.Storage);
        Assert.NotNull(host.Libraries.GetModelById("Lib.New"));
        Assert.Contains("model New", File.ReadAllText(path));
    }

    [Fact]
    public async Task Create_Standalone_ForcedButParentNotDirectory_Errors()
    {
        using var host = new TestHost();
        var path = host.WriteMoFile("Lib.mo", "within;\npackage Lib\n  model A\n    Real x;\n  end A;\nend Lib;");
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();

        var err = ToolAssert.Error(await Edit(host).CreateClass("Lib", "model N\n  Real x;\nend N;", standalone: true));
        Assert.Contains("directory package", err.Error);
    }

    [Fact]
    public async Task Create_Duplicate_Rejected()
    {
        using var host = new TestHost();
        LoadDirectoryPackage(host);
        var err = ToolAssert.Error(await Edit(host).CreateClass("P", "model A\n  Real x;\nend A;"));
        Assert.Contains("already exists", err.Error);
    }

    [Fact]
    public async Task Create_SyntaxError_Rejected()
    {
        using var host = new TestHost();
        LoadDirectoryPackage(host);
        var err = ToolAssert.Error(await Edit(host).CreateClass("P", "model Bad\n  Real x = ;\nend Bad;"));
        Assert.Contains("syntax", err.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_WithinClauseInSource_Rejected()
    {
        using var host = new TestHost();
        LoadDirectoryPackage(host);
        var err = ToolAssert.Error(await Edit(host).CreateClass("P", "within P;\nmodel B\n  Real y;\nend B;"));
        Assert.Contains("within", err.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_Preview_DoesNotWrite()
    {
        using var host = new TestHost();
        var dir = LoadDirectoryPackage(host);

        var res = ToolAssert.Ok<CreateClassResult>(
            await Edit(host).CreateClass("P", "model B\n  Real y;\nend B;", preview: true));

        Assert.True(res.PreviewOnly);
        Assert.NotNull(res.NewFileContent);
        Assert.Contains("within P;", res.NewFileContent!);
        Assert.False(File.Exists(Path.Combine(dir, "B.mo")));
        Assert.Null(host.Libraries.GetModelById("P.B"));
    }

    [Fact]
    public async Task Create_Nested_ReadOnlyPackageMo_Aborts()
    {
        using var host = new TestHost();
        var dir = LoadDirectoryPackage(host);
        var path = Path.Combine(dir, "package.mo");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var err = ToolAssert.Error(await Edit(host).CreateClass("P", "model C\n  Real z;\nend C;", standalone: false));
            Assert.Contains("read-only", err.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Null(host.Libraries.GetModelById("P.C"));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }
}
