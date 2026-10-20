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
        Assert.NotEmpty(host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult().ModelIds);
        return dir;
    }

    // mlqt_load, one path and no dependency search, as the retired mlqt_load was: how each shape
    // of path loads, and what it says when one loads nothing.
    private static async Task<LoadResult> LoadOne(TestHost host, string path) =>
        ToolAssert.Ok<LoadResult>(await new LoadTools(host.Libraries, host.Repositories, host.Resources, host.Session)
        {
            DesktopSettingsDirectory = null,
            Environment_ = _ => null,
        }.Load([path], withDependencies: false));

    [Fact]
    public async Task Load_FromDirectory_LoadsModels()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });

        var res = await LoadOne(host, dir);

        Assert.Equal("TestLib", Assert.Single(res.Loaded).Name);
        var library = Assert.Single(host.Libraries.Libraries);
        Assert.Equal(MLQT.Services.DataTypes.LibrarySourceType.Directory, library.SourceType);
        Assert.True(library.ModelIds.Count >= 4);
    }

    [Fact]
    public async Task Load_FromPackageMoPath_LoadsWholeLibrary()
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
        // Pointing at the package.mo file loads the whole library, not just that file.
        var res = await LoadOne(host, Path.Combine(dir, "package.mo"));

        Assert.Equal("MyLib", Assert.Single(res.Loaded).Name);
        Assert.Equal(MLQT.Services.DataTypes.LibrarySourceType.Directory, Assert.Single(host.Libraries.Libraries).SourceType);
        Assert.NotNull(host.Libraries.GetModelById("MyLib.Inner"));
        Assert.NotNull(host.Libraries.GetModelById("MyLib.Extra")); // the standalone child was included
    }

    [Fact]
    public async Task Load_FromSingleFile_Loads()
    {
        using var host = new TestHost();
        var path = host.WriteMoFile("Thing.mo", "model Thing \"t\"\n  Real x;\nequation\n x=1;\nend Thing;");

        await LoadOne(host, path);

        Assert.Equal(MLQT.Services.DataTypes.LibrarySourceType.File, Assert.Single(host.Libraries.Libraries).SourceType);
    }

    [Fact]
    public async Task Load_EmptyDirectory_SaysSo_AndLeavesNothingLoaded()
    {
        using var host = new TestHost();

        var res = await LoadOne(host, host.NewTempDir());

        Assert.Contains("No Modelica classes were found", Assert.Single(res.Errors));
        Assert.Empty(host.Libraries.Libraries); // the empty library was not left loaded
    }

    [Fact]
    public async Task Load_EncryptedCopyOfLoadedSource_SaysTheSourceIsUsed()
    {
        // B268: an encrypted build of a library whose source is loaded is retired on arrival, so its
        // index is empty - exactly like one that ships no documentation. The tool has to say which,
        // because the two call for opposite responses from the agent reading it.
        using var host = new TestHost();
        var source = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
        await LoadOne(host, source);

        var encrypted = Path.Combine(host.NewTempDir(), "TestLib 2.0.0");
        Directory.CreateDirectory(Path.Combine(encrypted, "help"));
        File.WriteAllText(Path.Combine(encrypted, "package.moe"), "not readable");
        File.WriteAllText(Path.Combine(encrypted, "help", "TestLib.html"),
            "<html><head><meta name=\"HTML-Generator\" content=\"Dymola\"></head><body>" +
            "<h2><a name=\"TestLib\"></a>TestLib</h2>" +
            "<p><span class=\"ModelicaDescription\">encrypted</span></p></body></html>");

        var res = await LoadOne(host, encrypted);

        var warning = Assert.Single(res.Warnings);
        Assert.Contains("a readable copy is already loaded", warning);
        Assert.DoesNotContain("no usable documentation", warning);
        Assert.Empty(res.Loaded);
        Assert.Equal(MLQT.Services.DataTypes.LibrarySourceType.Directory, Assert.Single(host.Libraries.Libraries).SourceType);
    }

    [Fact]
    public async Task Load_BadPath_ReturnsError()
    {
        using var host = new TestHost();

        var res = await LoadOne(host, Path.Combine(Path.GetTempPath(), "does-not-exist-xyz"));

        Assert.Contains("is not a library folder, a package.mo or a .mo file", Assert.Single(res.Errors));
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

        var ok = ToolAssert.Ok<UnloadLibraryResult>(session.UnloadLibrary(libId));
        Assert.True(ok.Success);
        Assert.Equal(libId, ok.UnloadedLibraryId);
        Assert.Empty(session.ListLibraries());
    }

    [Fact]
    public void UnloadLibrary_ByName_Works()
    {
        using var host = new TestHost();
        LoadPackage(host, out var session);

        var ok = ToolAssert.Ok<UnloadLibraryResult>(session.UnloadLibrary("TestLib")); // by name, not the GUID id
        Assert.Equal("TestLib", ok.Name);
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
        // Everything discovered was loaded, so by default the discovered list is not repeated (B503).
        Assert.Empty(result.DiscoveredLibraries);
        Assert.Contains(result.LoadedLibraries, l => l.Name == "TestLib");

        var repos = session.ListRepositories();
        Assert.Single(repos);
        Assert.Equal(result.RepositoryId, repos[0].Id);
    }

    [Fact]
    public async Task LoadRepository_Detailed_ListsWhatItDiscovered_EvenWhenAllWasLoaded()
    {
        using var host = new TestHost();
        var repoRoot = host.WriteLibraryDir(new Dictionary<string, string> { ["TestLib/package.mo"] = Package });
        var session = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session);

        var result = ToolAssert.Ok<LoadRepositoryResult>(await session.LoadRepository(repoRoot, detailed: true));
        Assert.Contains(result.DiscoveredLibraries, d => d.LibraryName == "TestLib");
    }

    [Fact]
    public async Task ListLibraries_ByDefault_ListsOnlyTheDependenciesNotLoaded()
    {
        using var host = new TestHost();
        var needy = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = "within;\npackage Needy\n  model M\n  end M;\n  annotation (uses(Helper(version=\"1.0\"), Gone(version=\"2.0\")));\nend Needy;\n",
        });
        var helper = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = "within;\npackage Helper\n  model H\n  end H;\nend Helper;\n",
        });
        await host.Libraries.AddLibraryFromDirectoryAsync(needy);
        await host.Libraries.AddLibraryFromDirectoryAsync(helper);
        var session = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session);

        var concise = session.ListLibraries().Single(l => l.Name == "Needy");
        Assert.Equal(["Gone"], concise.Dependencies.Select(d => d.Name).ToArray());
        var detailed = session.ListLibraries(detailed: true).Single(l => l.Name == "Needy");
        Assert.Equal(["Gone", "Helper"], detailed.Dependencies.Select(d => d.Name).Order().ToArray());
    }

    [Fact]
    public async Task LoadRepository_DiscoverOnly_ListsWithoutLoading()
    {
        using var host = new TestHost();
        var repoRoot = host.WriteLibraryDir(new Dictionary<string, string> { ["TestLib/package.mo"] = Package });
        var session = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session);

        var result = ToolAssert.Ok<LoadRepositoryResult>(await session.LoadRepository(repoRoot, loadLibraries: false));
        Assert.Empty(result.LoadedLibraries);
        Assert.Contains(result.DiscoveredLibraries, d => d.LibraryName == "TestLib");
        Assert.Empty(host.Libraries.Libraries);
    }

    // A look first and then the load is the natural order with mlqt_load's discoverOnly, and registered
    // the working copy twice: everything defaulting to "the one repository loaded" stopped knowing which.
    [Fact]
    public async Task LoadRepository_Again_IsTheSameRepository()
    {
        using var host = new TestHost();
        var repoRoot = host.WriteLibraryDir(new Dictionary<string, string> { ["TestLib/package.mo"] = Package });
        var session = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session);

        var look = ToolAssert.Ok<LoadRepositoryResult>(await session.LoadRepository(repoRoot, loadLibraries: false));
        var load = ToolAssert.Ok<LoadRepositoryResult>(await session.LoadRepository(repoRoot + Path.DirectorySeparatorChar));

        Assert.Equal(look.RepositoryId, load.RepositoryId);
        Assert.Single(session.ListRepositories());
        Assert.Contains(load.LoadedLibraries, l => l.Name == "TestLib");
        Assert.Single(host.Libraries.Libraries);
    }

    [Fact]
    public void ListRepositories_EmptyInitially()
    {
        using var host = new TestHost();
        var session = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session);
        Assert.Empty(session.ListRepositories());
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

        var res = ToolAssert.Ok<ClassSearchResult>(query.SearchClasses("Base"));
        var baseHit = Assert.Single(res.Items, i => i.Id == "TestLib.Base");
        Assert.Equal("Base model", baseHit.Description); // description surfaced

        Assert.IsType<ToolError>(query.SearchClasses("  "));
    }

    [Fact]
    public void SearchClasses_IncludesDocumentationSnippet()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] =
                "within;\npackage Lib \"l\"\n  model Widget \"a widget\"\n    Real x;\n" +
                "    annotation (Documentation(info=\"<html><p>Use this for <b>closed loops</b>.</p></html>\"));\n" +
                "  end Widget;\nend Lib;",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        var query = new ClassQueryTools(host.Libraries);

        var hit = Assert.Single(ToolAssert.Ok<ClassSearchResult>(query.SearchClasses("Widget")).Items);
        Assert.Equal("a widget", hit.Description);
        Assert.Equal("Use this for closed loops.", hit.DocSnippet); // HTML stripped, whitespace collapsed
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

    [Fact]
    public void GetPackageTree_StopsExpandingAtMaxNodes_KeepingTheChildCount()
    {
        using var host = new TestHost();
        LoadPackage(host, out _);
        var query = new ClassQueryTools(host.Libraries);

        var root = Assert.Single(ToolAssert.Ok<List<PackageTreeNode>>(query.GetPackageTree("TestLib", maxDepth: 1, maxNodes: 1)));
        Assert.NotNull(root.Children); // the one node the budget allows

        var unexpanded = Assert.Single(ToolAssert.Ok<List<PackageTreeNode>>(query.GetPackageTree("TestLib", maxDepth: 2, maxNodes: 1)));
        Assert.All(unexpanded.Children!, child => Assert.Null(child.Children));
        Assert.Equal(3, unexpanded.ChildCount);
    }
}
