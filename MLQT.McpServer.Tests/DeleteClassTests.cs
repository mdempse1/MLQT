using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class DeleteClassTests
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

    // Directory package P with a nested A (package.mo) and a standalone B.mo child.
    private static string LoadPackageWithStandalone(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = PackageMo,
            ["B.mo"] = "within P;\nmodel B\n  Real y;\nend B;",
            ["package.order"] = "A\nB\n"
        });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return dir;
    }

    [Fact]
    public async Task Delete_Standalone_RemovesFileAndOrderEntry()
    {
        using var host = new TestHost();
        var dir = LoadPackageWithStandalone(host);

        var res = ToolAssert.Ok<DeleteClassResult>(await Edit(host).DeleteClass("P.B"));
        Assert.True(res.Deleted);
        Assert.Equal("standalone-file", res.Storage);

        Assert.False(File.Exists(Path.Combine(dir, "B.mo")));
        Assert.DoesNotContain("B", File.ReadAllLines(Path.Combine(dir, "package.order")));
        Assert.Null(host.Libraries.GetModelById("P.B"));
        Assert.NotNull(host.Libraries.GetModelById("P.A"));
    }

    [Fact]
    public async Task Delete_Nested_CutsFromPackageMo()
    {
        using var host = new TestHost();
        var dir = LoadPackageWithStandalone(host);

        var res = ToolAssert.Ok<DeleteClassResult>(await Edit(host).DeleteClass("P.A"));
        Assert.Equal("nested", res.Storage);
        Assert.DoesNotContain("model A", File.ReadAllText(Path.Combine(dir, "package.mo")));
        Assert.Null(host.Libraries.GetModelById("P.A"));
    }

    [Fact]
    public async Task Delete_ReportsDanglingReferences()
    {
        using var host = new TestHost();
        // B references A; deleting A leaves B dangling.
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = "within;\npackage P\n  model A\n    Real x;\n  end A;\n  model B\n    A a;\n  end B;\nend P;"
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        var deps = new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session);
        await deps.AnalyzeDependencies();

        var res = ToolAssert.Ok<DeleteClassResult>(await Edit(host).DeleteClass("P.A", preview: true));
        Assert.True(res.DependenciesChecked);
        Assert.Contains("P.B", res.DanglingReferences);
        Assert.NotNull(host.Libraries.GetModelById("P.A")); // preview didn't delete
    }

    [Fact]
    public async Task Delete_DirectoryPackage_RemovesFolderAndReportsDangling()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = "within;\npackage Root\n  model User\n    Sub.Widget w;\n  end User;\nend Root;",
            ["package.order"] = "Sub\nUser\n",
            ["Sub/package.mo"] = "within Root;\npackage Sub\n  model Widget\n    Real x;\n  end Widget;\nend Sub;",
            ["Sub/package.order"] = "Widget\n"
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        var deps = new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session);
        await deps.AnalyzeDependencies();

        var subDir = Path.Combine(dir, "Sub");
        Assert.NotNull(host.Libraries.GetModelById("Root.Sub.Widget"));

        var preview = ToolAssert.Ok<DeleteClassResult>(await Edit(host).DeleteClass("Root.Sub", preview: true));
        Assert.Equal("directory-package", preview.Storage);
        Assert.Contains("Root.User", preview.DanglingReferences);
        Assert.True(Directory.Exists(subDir)); // preview didn't delete

        var res = ToolAssert.Ok<DeleteClassResult>(await Edit(host).DeleteClass("Root.Sub"));
        Assert.True(res.Deleted);
        Assert.False(Directory.Exists(subDir));
        Assert.Null(host.Libraries.GetModelById("Root.Sub"));
        Assert.Null(host.Libraries.GetModelById("Root.Sub.Widget"));
        Assert.NotNull(host.Libraries.GetModelById("Root.User")); // external referencer remains (now dangling)
        Assert.DoesNotContain("Sub", File.ReadAllLines(Path.Combine(dir, "package.order")));
    }

    [Fact]
    public async Task Delete_ReadOnly_Aborts()
    {
        using var host = new TestHost();
        var dir = LoadPackageWithStandalone(host);
        var path = Path.Combine(dir, "B.mo");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var err = ToolAssert.Error(await Edit(host).DeleteClass("P.B"));
            Assert.Contains("read-only", err.Error, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(host.Libraries.GetModelById("P.B"));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }
}
