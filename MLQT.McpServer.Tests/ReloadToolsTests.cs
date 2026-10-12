using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;
using ModelicaGraph;

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

    // ---------------------------------------------------------------- a library a host supplied

    private const string SuppliedPackage = "within;\npackage Vendor \"v\"\n  model S\n    Real s;\n  end S;\nend Vendor;";

    private sealed class SuppliedSource : IReadOnlyClassSource
    {
        public string LibraryName => "Vendor";
        public string? LibraryVersion => null;
        public ReadOnlySourceKind Kind => ReadOnlySourceKind.Supplied;
        public string? Location => null;
        public string ProvenanceNote => "Supplied by a test host.";

        public ReadOnlySourceContent Read(CancellationToken cancellationToken) =>
            new() { Texts = [new SuppliedText("Vendor/package.mo", SuppliedPackage)] };
    }

    /// <summary>
    /// Reported by #31: a reload of everything unloaded a supplied library and then tried to load it
    /// again from its <c>mlqt-readonly://</c> path, which threw - so the agent got an exception and the
    /// session lost the library. It has nothing on disk to re-read, so it is left as it was and named.
    /// </summary>
    [Fact]
    public async Task Reload_All_LeavesASuppliedLibraryLoadedAndSaysSo()
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new SuppliedSource());
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = PackageV1 });
        await host.Libraries.AddLibraryFromDirectoryAsync(dir);
        File.WriteAllText(Path.Combine(dir, "package.mo"), PackageV2);

        var res = ToolAssert.Ok<ReloadResult>(await Session(host).Reload());

        Assert.NotNull(host.Libraries.GetModelById("P.B"));      // the readable library was still reloaded
        Assert.DoesNotContain("Vendor", res.ReloadedLibraries);
        Assert.Equal(["Vendor"], res.NotReloaded!);
        Assert.Contains("Vendor", res.Note);
        var vendor = Assert.Single(host.Libraries.Libraries, l => l.Name == "Vendor");
        Assert.Equal(ReadOnlySourceKind.Supplied, vendor.ReadOnlySource);
        Assert.NotNull(host.Libraries.GetModelById("Vendor.S"));
    }

    [Fact]
    public async Task Reload_ASuppliedLibraryByName_ErrorsAndLeavesItLoaded()
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new SuppliedSource());
        host.Session.DependenciesAnalyzed = true;

        var error = Assert.IsType<ToolError>(await Session(host).Reload("Vendor"));

        Assert.Contains("supplied", error.Error);
        Assert.NotNull(host.Libraries.GetModelById("Vendor.S"));
        Assert.True(host.Session.DependenciesAnalyzed);         // nothing was reloaded, so nothing to reset
    }
}
