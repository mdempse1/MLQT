using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class ReloadToolsTests
{
    private const string PackageV1 = "within;\npackage P \"p\"\n  model A\n    Real x;\n  end A;\nend P;";
    private const string PackageV2 = "within;\npackage P \"p\"\n  model A\n    Real x;\n  end A;\n  model B\n    Real y;\n  end B;\nend P;";

    private static SessionTools Session(TestHost h) => new(h.Libraries, h.Repositories, h.Resources, h.Session);

    [Fact]
    public async Task Reload_SingleFile_PicksUpExternalChange()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = PackageV1 });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        Assert.Null(host.Libraries.GetModelById("P.B"));

        // External edit adds model B.
        var path = Path.Combine(dir, "package.mo");
        File.WriteAllText(path, PackageV2);

        var res = ToolAssert.Ok<ReloadResult>(await Session(host).Reload(path));
        Assert.Equal("file", res.Scope);
        Assert.NotNull(host.Libraries.GetModelById("P.B"));
    }

    [Fact]
    public async Task Reload_Library_PicksUpNewStandaloneFile_AndResetsAnalysis()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = PackageV1,
            ["package.order"] = "A\n"
        });
        var lib = host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        host.Session.DependenciesAnalyzed = true;

        // A new standalone file appears on disk.
        File.WriteAllText(Path.Combine(dir, "C.mo"), "within P;\nmodel C\n  Real z;\nend C;");

        var res = ToolAssert.Ok<ReloadResult>(await Session(host).Reload(lib.Name));
        Assert.Equal("library", res.Scope);
        Assert.NotNull(host.Libraries.GetModelById("P.C"));
        Assert.False(host.Session.DependenciesAnalyzed); // reset by the reload
    }

    [Fact]
    public async Task Reload_All_RebuildsAndResets()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = PackageV1 });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        host.Session.DependenciesAnalyzed = true;
        File.WriteAllText(Path.Combine(dir, "package.mo"), PackageV2);

        var res = ToolAssert.Ok<ReloadResult>(await Session(host).Reload());
        Assert.Equal("all", res.Scope);
        Assert.NotNull(host.Libraries.GetModelById("P.B"));
        Assert.False(host.Session.DependenciesAnalyzed);
    }

    [Fact]
    public async Task Reload_UnknownTarget_Errors()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = PackageV1 });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();

        Assert.IsType<ToolError>(await Session(host).Reload("NoSuchThing"));
    }
}
