using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class CreateLibraryTests
{
    private static SessionTools Session(TestHost h) => new(h.Libraries, h.Repositories, h.Resources, h.Session);

    [Fact]
    public async Task Create_WritesPackageMo_LoadsIt_AndCreateClassWorks()
    {
        using var host = new TestHost();
        var dir = host.NewTempDir();

        var res = ToolAssert.Ok<CreateLibraryResult>(
            await Session(host).CreateLibrary("MyLib", dir, description: "my library", version: "1.0.0"));

        Assert.True(res.Loaded);
        var libDir = Path.Combine(dir, "MyLib");
        Assert.True(File.Exists(Path.Combine(libDir, "package.mo")));
        Assert.True(File.Exists(Path.Combine(libDir, "package.order")));

        var content = File.ReadAllText(Path.Combine(libDir, "package.mo"));
        Assert.Contains("package MyLib \"my library\"", content);
        Assert.Contains("version=\"1.0.0\"", content);
        Assert.Contains("end MyLib;", content);

        // The library is loaded and usable as a create_class parent.
        Assert.NotNull(host.Libraries.GetModelById("MyLib"));
        var edit = new EditTools(host.Libraries, host.Resources, host.Session);
        ToolAssert.Ok<CreateClassResult>(
            await edit.CreateClass("MyLib", "model First\n  Real x;\nend First;"));
        Assert.NotNull(host.Libraries.GetModelById("MyLib.First"));
    }

    [Fact]
    public async Task Create_Minimal_NoDescriptionOrVersion()
    {
        using var host = new TestHost();
        var dir = host.NewTempDir();
        ToolAssert.Ok<CreateLibraryResult>(await Session(host).CreateLibrary("Bare", dir));
        var content = File.ReadAllText(Path.Combine(dir, "Bare", "package.mo"));
        Assert.Contains("package Bare\n", content);
        Assert.DoesNotContain("annotation", content);
        Assert.DoesNotContain("\"", content);
    }

    [Fact]
    public async Task Create_Preview_WritesNothing()
    {
        using var host = new TestHost();
        var dir = host.NewTempDir();
        var res = ToolAssert.Ok<CreateLibraryResult>(
            await Session(host).CreateLibrary("Prev", dir, preview: true));
        Assert.True(res.PreviewOnly);
        Assert.Contains("package Prev", res.PackageContent);
        Assert.False(Directory.Exists(Path.Combine(dir, "Prev")));
    }

    [Fact]
    public async Task Create_ExistingFolder_Rejected()
    {
        using var host = new TestHost();
        var dir = host.NewTempDir();
        Directory.CreateDirectory(Path.Combine(dir, "Taken"));
        Assert.IsType<ToolError>(await Session(host).CreateLibrary("Taken", dir));
    }

    [Fact]
    public async Task Create_InvalidName_Rejected()
    {
        using var host = new TestHost();
        var dir = host.NewTempDir();
        Assert.IsType<ToolError>(await Session(host).CreateLibrary("1Bad", dir));
    }
}
