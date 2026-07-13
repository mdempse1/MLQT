using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class SessionAndClassToolsTests
{
    private const string Package = """
        within;
        package TestLib "Test library"
          model Base "Base model"
            Real b "state";
          equation
            b = time;
          end Base;

          model Middle "Middle model"
            Base base1 "a base";
          end Middle;

          block Gain "gain block"
            parameter Real k=1 "gain";
          end Gain;
        end TestLib;
        """;

    private static string LoadPackage(TestHost host, out SessionTools session)
    {
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
        session = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session);
        var result = session.LoadLibrary(dir).GetAwaiter().GetResult();
        ToolAssert.Ok<LibrarySummary>(result);
        return dir;
    }

    [Fact]
    public async Task LoadLibrary_FromDirectory_LoadsModels()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
        var session = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session);

        var summary = ToolAssert.Ok<LibrarySummary>(await session.LoadLibrary(dir));

        Assert.Equal("TestLib", summary.Name);
        Assert.Equal("Directory", summary.SourceType);
        Assert.True(summary.ModelCount >= 4);
        Assert.Equal(1, summary.TopLevelModelCount);
    }

    [Fact]
    public async Task LoadLibrary_SurfacesDeclaredDependencies()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] =
                "within;\npackage Dep \"d\"\n  model M\n    Real x;\n  end M;\n" +
                "  annotation (uses(Modelica(version=\"4.0.0\")));\nend Dep;",
        });
        var session = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session);

        var summary = ToolAssert.Ok<LibrarySummary>(await session.LoadLibrary(dir));

        var msl = Assert.Single(summary.Dependencies);
        Assert.Equal("Modelica", msl.Name);
        Assert.Equal("4.0.0", msl.Version);
    }

    [Fact]
    public async Task LoadLibrary_FromPackageMoPath_LoadsWholeLibrary()
    {
        using var host = new TestHost();
        // A directory package: package.mo defines the library (with one nested class), and Extra is a
        // standalone child .mo file — only picked up when the whole directory is loaded.
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = "within;\npackage MyLib \"l\"\n  model Inner \"i\"\n    Real z;\n  end Inner;\nend MyLib;",
            ["Extra.mo"] = "within MyLib;\nmodel Extra \"e\"\n  Real x;\nend Extra;",
            ["package.order"] = "Inner\nExtra\n",
        });
        var session = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session);

        // Pointing at the package.mo file loads the whole library, not just that file.
        var summary = ToolAssert.Ok<LibrarySummary>(await session.LoadLibrary(Path.Combine(dir, "package.mo")));

        Assert.Equal("MyLib", summary.Name);
        Assert.Equal("Directory", summary.SourceType);
        Assert.NotNull(host.Libraries.GetModelById("MyLib.Inner"));
        Assert.NotNull(host.Libraries.GetModelById("MyLib.Extra")); // the standalone child was included
    }

    [Fact]
    public async Task LoadLibrary_FromSingleFile_Loads()
    {
        using var host = new TestHost();
        var path = host.WriteMoFile("Thing.mo", "model Thing \"t\"\n  Real x;\nequation\n x=1;\nend Thing;");
        var session = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session);

        var summary = ToolAssert.Ok<LibrarySummary>(await session.LoadLibrary(path));
        Assert.Equal("File", summary.SourceType);
    }

    [Fact]
    public async Task LoadLibrary_EmptyDirectory_GuidesToPackageMo()
    {
        using var host = new TestHost();
        var emptyDir = host.NewTempDir();
        var session = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session);

        var err = ToolAssert.Error(await session.LoadLibrary(emptyDir));
        Assert.Contains("No Modelica models", err.Error);
        Assert.Empty(session.ListLibraries()); // the empty library was not left loaded
    }

    [Fact]
    public async Task LoadLibrary_BadPath_ReturnsError()
    {
        using var host = new TestHost();
        var session = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session);

        var result = await session.LoadLibrary(Path.Combine(Path.GetTempPath(), "does-not-exist-xyz"));
        Assert.Contains("not found", ToolAssert.Error(result).Error);
    }

    [Fact]
    public void ListLibraries_ReflectsLoaded()
    {
        using var host = new TestHost();
        LoadPackage(host, out var session);

        var libs = session.ListLibraries();
        Assert.Single(libs);
        Assert.Equal("TestLib", libs[0].Name);
    }

    [Fact]
    public void UnloadLibrary_ById_Works_AndBadValueErrors()
    {
        using var host = new TestHost();
        LoadPackage(host, out var session);
        var libId = session.ListLibraries()[0].Id;

        var err = ToolAssert.Error(session.UnloadLibrary("nope"));
        Assert.Contains("TestLib", err.Error); // error lists the loaded libraries to choose from

        var ok = session.UnloadLibrary(libId);
        Assert.IsNotType<ToolError>(ok);
        Assert.Empty(session.ListLibraries());
    }

    [Fact]
    public void UnloadLibrary_ByName_Works()
    {
        using var host = new TestHost();
        LoadPackage(host, out var session);

        var ok = session.UnloadLibrary("TestLib"); // by name, not the GUID id
        Assert.IsNotType<ToolError>(ok);
        Assert.Empty(session.ListLibraries());
    }

    [Fact]
    public async Task LoadRepository_LocalDirectory_DiscoversAndLoads()
    {
        using var host = new TestHost();
        var repoRoot = host.WriteLibraryDir(new Dictionary<string, string> { ["TestLib/package.mo"] = Package });
        var session = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session);

        var result = ToolAssert.Ok<LoadRepositoryResult>(await session.LoadRepository(repoRoot));
        Assert.True(result.Success);
        Assert.Contains(result.DiscoveredLibraries, d => d.LibraryName == "TestLib");
        Assert.NotEmpty(result.LoadedLibraries);

        var repos = session.ListRepositories();
        Assert.Single(repos);
        Assert.Equal(result.RepositoryId, repos[0].Id);
    }

    [Fact]
    public async Task LoadRepository_DiscoverOnly_ThenDiscoverTool()
    {
        using var host = new TestHost();
        var repoRoot = host.WriteLibraryDir(new Dictionary<string, string> { ["TestLib/package.mo"] = Package });
        var session = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session);

        var result = ToolAssert.Ok<LoadRepositoryResult>(await session.LoadRepository(repoRoot, loadLibraries: false));
        Assert.Empty(result.LoadedLibraries);
        Assert.Contains(result.DiscoveredLibraries, d => d.LibraryName == "TestLib");

        var discovered = await session.DiscoverLibraries(result.RepositoryId!);
        Assert.IsNotType<ToolError>(discovered);
    }

    [Fact]
    public void ListRepositories_EmptyInitially()
    {
        using var host = new TestHost();
        var session = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session);
        Assert.Empty(session.ListRepositories());
    }

    [Fact]
    public async Task DiscoverLibraries_UnknownRepo_Errors()
    {
        using var host = new TestHost();
        var session = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session);
        var result = await session.DiscoverLibraries("no-such-repo");
        Assert.IsType<ToolError>(result);
    }

    [Fact]
    public void GetClassInfo_ReturnsMetadata()
    {
        using var host = new TestHost();
        LoadPackage(host, out _);
        var query = new ClassQueryTools(host.Libraries);

        var info = ToolAssert.Ok<ClassInfo>(query.GetClassInfo("TestLib.Base"));
        Assert.Equal("model", info.ClassType);
        Assert.Equal("TestLib", info.ParentModelName);
        Assert.True(info.IsNested);
        Assert.False(info.HasParserErrors);
        Assert.NotNull(info.FilePath);
    }

    [Fact]
    public void GetClassInfo_Package_IsPackage()
    {
        using var host = new TestHost();
        LoadPackage(host, out _);
        var query = new ClassQueryTools(host.Libraries);

        var info = ToolAssert.Ok<ClassInfo>(query.GetClassInfo("TestLib"));
        Assert.True(info.IsPackage);
    }

    [Fact]
    public void GetClassInfo_Missing_Errors()
    {
        using var host = new TestHost();
        LoadPackage(host, out _);
        var query = new ClassQueryTools(host.Libraries);
        Assert.IsType<ToolError>(query.GetClassInfo("TestLib.Nope"));
    }

    [Fact]
    public void GetClassSource_StripsAnnotationsByDefault()
    {
        using var host = new TestHost();
        var path = host.WriteMoFile("Ann.mo",
            "model Ann \"d\"\n  Real x;\n  annotation(Documentation(info=\"<html>hi</html>\"));\nequation\n x=1;\nend Ann;");
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();
        var query = new ClassQueryTools(host.Libraries);

        var stripped = ToolAssert.Ok<ClassSourceResult>(query.GetClassSource("Ann", includeAnnotations: false));
        Assert.False(stripped.AnnotationsIncluded);
        Assert.DoesNotContain("Documentation", stripped.Source);

        var verbatim = ToolAssert.Ok<ClassSourceResult>(query.GetClassSource("Ann", includeAnnotations: true));
        Assert.Contains("Documentation", verbatim.Source);
    }

    [Fact]
    public void GetClassSource_Missing_Errors()
    {
        using var host = new TestHost();
        LoadPackage(host, out _);
        var query = new ClassQueryTools(host.Libraries);
        Assert.IsType<ToolError>(query.GetClassSource("Nope"));
    }

    [Fact]
    public void ListClasses_FiltersAndPaginates()
    {
        using var host = new TestHost();
        LoadPackage(host, out _);
        var query = new ClassQueryTools(host.Libraries);

        var all = ToolAssert.Ok<ClassListResult>(query.ListClasses());
        Assert.True(all.Total >= 4);

        var blocks = ToolAssert.Ok<ClassListResult>(query.ListClasses(classType: "block"));
        Assert.All(blocks.Items, i => Assert.Equal("block", i.ClassType));
        Assert.Contains(blocks.Items, i => i.Id == "TestLib.Gain");

        var page = ToolAssert.Ok<ClassListResult>(query.ListClasses(limit: 2, offset: 0));
        Assert.Equal(2, page.Count);
    }

    [Fact]
    public void ListClasses_ByLibraryName_Works()
    {
        using var host = new TestHost();
        LoadPackage(host, out _);
        var query = new ClassQueryTools(host.Libraries);
        // Filter by the library NAME rather than its GUID id.
        var res = ToolAssert.Ok<ClassListResult>(query.ListClasses(libraryId: "TestLib"));
        Assert.True(res.Total >= 4);
    }

    [Fact]
    public void ListClasses_BadLibrary_Errors()
    {
        using var host = new TestHost();
        LoadPackage(host, out _);
        var query = new ClassQueryTools(host.Libraries);
        Assert.IsType<ToolError>(query.ListClasses(libraryId: "nope"));
    }

    [Fact]
    public void SearchClasses_MatchesAndRanksExact()
    {
        using var host = new TestHost();
        LoadPackage(host, out _);
        var query = new ClassQueryTools(host.Libraries);

        var res = ToolAssert.Ok<ClassListResult>(query.SearchClasses("Base"));
        Assert.Contains(res.Items, i => i.Id == "TestLib.Base");

        Assert.IsType<ToolError>(query.SearchClasses("  "));
    }

    [Fact]
    public void GetPackageTree_RootAndChildren()
    {
        using var host = new TestHost();
        LoadPackage(host, out _);
        var query = new ClassQueryTools(host.Libraries);

        var roots = ToolAssert.Ok<List<PackageTreeNode>>(query.GetPackageTree());
        Assert.Contains(roots, n => n.Id == "TestLib");

        var sub = ToolAssert.Ok<List<PackageTreeNode>>(query.GetPackageTree("TestLib", maxDepth: 1));
        var testLib = Assert.Single(sub);
        Assert.Equal(3, testLib.ChildCount);
        Assert.NotNull(testLib.Children);

        Assert.IsType<ToolError>(query.GetPackageTree("TestLib.Nope"));
    }
}
