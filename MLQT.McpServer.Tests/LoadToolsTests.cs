using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// <c>mlqt_load</c>: explicit loads winning, and the search for dependencies in its order - beside the
/// library, the tool's folders, MODELICAPATH - with its version rules.
/// </summary>
public class LoadToolsTests
{
    private static string Package(string name, string? version = null, string uses = "", string extra = "")
    {
        var annotation = new List<string>();
        if (version is not null)
            annotation.Add($"version=\"{version}\"");
        if (uses.Length > 0)
            annotation.Add($"uses({uses})");
        if (extra.Length > 0)
            annotation.Add(extra);
        return $"within;\npackage {name} \"{name}\"\n  model M \"m\"\n  end M;\n" +
               (annotation.Count > 0 ? $"  annotation ({string.Join(", ", annotation)});\n" : "") +
               $"end {name};\n";
    }

    private sealed class World : IDisposable
    {
        public TestHost Host { get; } = new();
        public string Root { get; }
        public string DymolaExe { get; }
        public Dictionary<string, string?> Environment { get; } = new(StringComparer.Ordinal);

        public World()
        {
            Root = Host.NewTempDir();
            Write("Project/App", Package("App", uses: "Dep(version=\"1.0.0\"), Base(version=\"1.0\"), Extra(version=\"3.0\")"));
            Write("Project/Dep", Package("Dep", "1.0.0"));
            Write("Project/vendor/Dep 2.0.0", Package("Dep", "2.0.0", extra: "conversion(noneFromVersion=\"1.0.0\")"));
            Write("Dymola/Modelica/Library/Base 1.0", Package("Base"));
            Directory.CreateDirectory(Path.Combine(Root, "Dymola", "bin64"));
            DymolaExe = Path.Combine(Root, "Dymola", "bin64", "dymola.exe");
            File.WriteAllText(DymolaExe, "not a real Dymola");
            Write("Elsewhere/Extra 3.0", Package("Extra"));
            Environment["MODELICAPATH"] = Path.Combine(Root, "Elsewhere");
        }

        public string Path_(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

        public void Write(string folder, string packageMo)
        {
            Directory.CreateDirectory(Path_(folder));
            File.WriteAllText(Path.Combine(Path_(folder), "package.mo"), packageMo);
        }

        public LoadTools Tools() => new(Host.Libraries, Host.Repositories, Host.Resources, Host.Session)
        {
            DesktopSettingsDirectory = null,
            Environment_ = key => Environment.GetValueOrDefault(key),
            RunningDymola_ = null,   // the machine's Dymola, running or not, is no part of a test
        };

        public async Task<LoadResult> Load(params string[] relative) => await LoadWith(DymolaExe, relative);

        public async Task<LoadResult> LoadWith(string dymolaExe, params string[] relative) =>
            ToolAssert.Ok<LoadResult>(await Tools().Load(relative.Select(Path_).ToList(), tool: "dymola", toolInstall: dymolaExe));

        /// <summary>A second Dymola install, with its own library folder; <paramref name="withBase"/> puts a
        /// copy of Base in it.</summary>
        public string SecondInstall(bool withBase)
        {
            Directory.CreateDirectory(Path_("Dymola2/Modelica/Library"));
            if (withBase)
                Write("Dymola2/Modelica/Library/Base 1.0", Package("Base"));
            Directory.CreateDirectory(Path_("Dymola2/bin64"));
            var exe = Path_("Dymola2/bin64/dymola.exe");
            File.WriteAllText(exe, "not a real Dymola");
            return exe;
        }

        public void Dispose() => Host.Dispose();
    }

    private static LoadedLibraryItem Named(LoadResult r, string name) => Assert.Single(r.Loaded, l => l.Name == name);

    [Fact]
    public async Task Dependencies_AreFoundBesideTheLibrary_InTheToolsFolder_AndOnModelicaPath()
    {
        using var w = new World();
        var res = await w.Load("Project/App");

        Assert.Equal("explicit", Named(res, "App").Origin);
        var dep = Named(res, "Dep");
        Assert.Equal(("search", "beside App", "1.0.0", "App"), (dep.Origin, dep.FoundBy, dep.Version, dep.RequiredBy));
        Assert.Equal("the version asked for", dep.Rule);
        Assert.Equal("the tool's library folder", Named(res, "Base").FoundBy);
        Assert.Equal("MODELICAPATH", Named(res, "Extra").FoundBy);
        Assert.Empty(res.NotFound);
        Assert.Equal(Path.Combine(w.Root, "Dymola"), res.ToolInstall);
        Assert.Equal("as given", res.ToolInstallChosen);
    }

    // ---- A folder of libraries is a repository (B503) ------------------------------------------
    //
    // The consolidated set has no mlqt_load, so mlqt_load takes a working copy or a folder of
    // libraries itself: without it, no repository's style settings or changes could be reached.

    [Fact]
    public async Task AFolderOfLibraries_IsLoadedAsARepository_AndEachLibraryIsExplicit()
    {
        using var w = new World();
        w.Write("Repo/LibA", Package("LibA"));
        w.Write("Repo/LibB", Package("LibB"));

        var res = await w.Load("Repo");

        var repository = Assert.Single(res.Repositories!);
        Assert.True(repository.Success);
        Assert.Contains(w.Host.Repositories.Repositories, r => r.Id == repository.RepositoryId);
        foreach (var name in new[] { "LibA", "LibB" })
            Assert.Equal(("explicit", "in repository 'Repo'"), (Named(res, name).Origin, Named(res, name).FoundBy));
    }

    [Fact]
    public async Task DiscoverOnly_ListsARepositorysLibraries_AndLoadsNothing()
    {
        using var w = new World();
        w.Write("Repo/LibA", Package("LibA"));
        w.Write("Repo/LibB", Package("LibB"));

        var res = ToolAssert.Ok<LoadResult>(await w.Tools().Load([w.Path_("Repo")], discoverOnly: true));

        Assert.Equal(["LibA", "LibB"], Assert.Single(res.Repositories!).DiscoveredLibraries.Select(d => d.LibraryName).Order());
        Assert.Empty(w.Host.Libraries.Libraries);
        Assert.Empty(res.Loaded);
    }

    // ---- Another install: the search decides again (B504) -------------------------------------
    //
    // A search never replaces a loaded library, so a second load naming the install the simulator is
    // really running only filled the gaps and kept the first install's copies of everything else.

    [Fact]
    public async Task AnotherInstall_SearchesAgain_InPlaceOfWhatTheFirstSearchFound()
    {
        using var w = new World();
        await w.Load("Project/App");
        var second = w.SecondInstall(withBase: true);

        var res = await w.LoadWith(second, "Project/App");

        Assert.Equal("already loaded", Named(res, "App").FoundBy);     // what the caller named stays
        var replaced = Assert.Single(res.Replaced);                     // only Base moved
        Assert.Equal(("Base", w.Path_("Dymola/Modelica/Library/Base 1.0"), w.Path_("Dymola2/Modelica/Library/Base 1.0")),
            (replaced.Name, replaced.OldPath, replaced.NewPath));
        var bases = w.Host.Libraries.Libraries.Where(l => l.Name == "Base").ToList();
        Assert.Equal(w.Path_("Dymola2/Modelica/Library/Base 1.0"), Assert.Single(bases).SourcePath);
        // Found beside App and on MODELICAPATH: unloaded, found again where they were.
        Assert.Equal("beside App", Named(res, "Dep").FoundBy);
        Assert.Equal("MODELICAPATH", Named(res, "Extra").FoundBy);
        Assert.Contains(res.Warnings, x => x.Contains("tool install changed") && x.Contains("3 libraries"));
        Assert.Equal(Path.Combine(w.Root, "Dymola2"), res.ToolInstall);
    }

    [Fact]
    public async Task TheSameInstallAgain_ChangesNothing()
    {
        using var w = new World();
        await w.Load("Project/App");
        var before = w.Host.Libraries.Libraries.Single(l => l.Name == "Base").Id;

        var res = await w.Load("Project/App");

        Assert.Empty(res.Replaced);
        Assert.DoesNotContain(res.Warnings, x => x.Contains("tool install changed"));
        Assert.Equal(before, w.Host.Libraries.Libraries.Single(l => l.Name == "Base").Id);   // not reloaded
    }

    [Fact]
    public async Task ALibraryTheNewInstallDoesNotHave_IsUnloaded_AndSaidSo()
    {
        using var w = new World();
        await w.Load("Project/App");
        var second = w.SecondInstall(withBase: false);

        var res = await w.LoadWith(second, "Project/App");

        Assert.DoesNotContain(w.Host.Libraries.Libraries, l => l.Name == "Base");
        Assert.Contains(res.NotFound, m => m.Name == "Base");
        Assert.Contains(res.Warnings, x => x.Contains("Base") && x.Contains("no copy of it was found there"));
    }

    [Fact]
    public async Task ALibraryNamedExplicitly_StaysWhenTheInstallChanges()
    {
        using var w = new World();
        await w.Load("Project/App", "Dymola/Modelica/Library/Base 1.0");
        var second = w.SecondInstall(withBase: true);

        var res = await w.LoadWith(second, "Project/App");

        Assert.Empty(res.Replaced);
        Assert.Equal(w.Path_("Dymola/Modelica/Library/Base 1.0"),
            w.Host.Libraries.Libraries.Single(l => l.Name == "Base").SourcePath);
    }

    [Fact]
    public async Task ALibraryAScriptImportedFromTheInstall_IsReplacedWhenTheInstallChanges()
    {
        // The script names the library, but the install picks the copy: a reload naming the install the
        // project uses kept the beta's MSL, as explicit (a 2026-11 session).
        using var w = new World();
        var script = Path.Combine(w.Root, "startup.mos");
        File.WriteAllText(script, $"openModel(\"{w.Path_("Project/App/package.mo").Replace('\\', '/')}\");\nimport Base;\n");
        async Task<LoadResult> LoadScript(string exe) =>
            ToolAssert.Ok<LoadResult>(await w.Tools().Load(script: script, tool: "dymola", toolInstall: exe));

        var first = await LoadScript(w.DymolaExe);
        Assert.Equal("explicit", Named(first, "Base").Origin);
        var second = w.SecondInstall(withBase: true);

        var res = await LoadScript(second);

        Assert.Empty(res.Errors);
        Assert.Equal(w.Path_("Dymola2/Modelica/Library/Base 1.0"),
            w.Host.Libraries.Libraries.Single(l => l.Name == "Base").SourcePath);
        Assert.Contains(res.Replaced, r => r.Name == "Base" && r.NewPath == w.Path_("Dymola2/Modelica/Library/Base 1.0"));
        Assert.Equal("already loaded", Named(res, "App").FoundBy);     // opened by path: stays as it was
    }

    [Fact]
    public async Task ALibraryAScriptImported_IsStillReplaced_AfterItWasLoadedAgainUnderANewId()
    {
        // mlqt_session's reload loads a library again, under a new id; the record is by path, so it holds.
        using var w = new World();
        var script = Path.Combine(w.Root, "startup.mos");
        File.WriteAllText(script, "import Base;\n");
        await w.Tools().Load(script: script, tool: "dymola", toolInstall: w.DymolaExe);
        var old = w.Host.Libraries.Libraries.Single(l => l.Name == "Base");
        w.Host.Libraries.RemoveLibrary(old.Id);
        var again = await w.Host.Libraries.AddLibraryFromPathAsync(old.SourcePath);
        Assert.NotEqual(old.Id, again.Id);
        var second = w.SecondInstall(withBase: true);

        var res = ToolAssert.Ok<LoadResult>(await w.Tools().Load(script: script, tool: "dymola", toolInstall: second));

        Assert.Empty(res.Errors);
        Assert.Equal(w.Path_("Dymola2/Modelica/Library/Base 1.0"),
            w.Host.Libraries.Libraries.Single(l => l.Name == "Base").SourcePath);
    }

    [Fact]
    public void ADetectedInstall_SaysToPassAParameterTheToolHas()
    {
        // B504: the newest Dymola found need not be the one running, and only the agent can know
        // that - so the answer names the parameter to pass, which must be one mlqt_load has.
        var parameters = typeof(LoadTools).GetMethod(nameof(LoadTools.Load))!.GetParameters().Select(p => p.Name);

        Assert.Contains("tool", parameters);
        Assert.Contains("name it in tool, as 'Dymola 2026x Refresh 1'", ToolLibraryFolders.DetectedReason);
        Assert.Equal(("dymola", "2026x Refresh 1"), ToolLibraryFolders.ParseTool("Dymola 2026x Refresh 1"));
    }

    [Fact]
    public async Task AnExplicitLibrary_IsLoadedBeforeTheSearch_AndKept_WithAWarning()
    {
        // What a simulator's startup script opened is what the simulator uses, even where a uses
        // annotation asks for another version.
        using var w = new World();
        var res = await w.Load("Project/App", "Project/vendor/Dep 2.0.0");

        var dep = Named(res, "Dep");
        Assert.Equal(("explicit", "2.0.0"), (dep.Origin, dep.Version));
        Assert.Contains(res.Warnings, x => x.Contains("App uses Dep 1.0.0") && x.Contains("loaded explicitly and is kept"));
        Assert.Single(w.Host.Libraries.Libraries, l => l.Name == "Dep");
    }

    [Fact]
    public async Task AnExplicitLibrary_ReplacesOneASearchFound()
    {
        using var w = new World();
        await w.Load("Project/App");
        var res = await w.Load("Project/vendor/Dep 2.0.0");

        var replaced = Assert.Single(res.Replaced);
        Assert.Equal(("Dep", "1.0.0", "2.0.0"), (replaced.Name, replaced.OldVersion, replaced.NewVersion));
        Assert.Equal("2.0.0", Assert.Single(w.Host.Libraries.Libraries, l => l.Name == "Dep").Version);
    }

    // ---- A load that changes the libraries forgets the analysis (B506) --------------------------
    //
    // The edges were built over the libraries loaded then. A replaced copy left them pointing at
    // classes no longer loaded, and a library added later had none, while the session said analysed -
    // so nothing ran the analysis again.

    [Fact]
    public async Task ReplacingALibrary_MarksTheAnalysisAsNotRun()
    {
        using var w = new World();
        await w.Load("Project/App");
        w.Host.Session.DependenciesAnalyzed = true;
        w.Host.Session.ResourcesAnalyzed = true;

        await w.Load("Project/vendor/Dep 2.0.0");

        Assert.False(w.Host.Session.DependenciesAnalyzed);
        Assert.False(w.Host.Session.ResourcesAnalyzed);
    }

    [Fact]
    public async Task AddingALibrary_MarksTheAnalysisAsNotRun()
    {
        using var w = new World();
        await w.Load("Project/Dep");
        w.Host.Session.DependenciesAnalyzed = true;

        await w.Load("Elsewhere/Extra 3.0");

        Assert.False(w.Host.Session.DependenciesAnalyzed);
    }

    [Fact]
    public async Task TheSessionStatus_SaysHowEachLibraryCameIn()
    {
        // B513: only mlqt_load's own answer said explicit or search, and an agent asking later what is
        // loaded could not tell which copies a search would leave alone.
        using var w = new World();
        await w.Load("Project/App");

        var status = new SessionTools(w.Host.Libraries, w.Host.Repositories, w.Host.Resources, w.Host.Session)
            .ListLibraries();

        Assert.Equal("explicit", status.Single(l => l.Name == "App").Origin);
        Assert.Equal("search", status.Single(l => l.Name == "Dep").Origin);
    }

    [Fact]
    public async Task ALoadThatChangesNothing_KeepsTheAnalysis()
    {
        using var w = new World();
        await w.Load("Project/App");
        w.Host.Session.DependenciesAnalyzed = true;
        w.Host.Session.ResourcesAnalyzed = true;

        await w.Load("Project/App");

        Assert.True(w.Host.Session.DependenciesAnalyzed);
        Assert.True(w.Host.Session.ResourcesAnalyzed);
    }

    [Fact]
    public async Task TwoExplicitCopies_NeedReplace()
    {
        using var w = new World();
        await w.Load("Project/Dep");

        var refused = await w.Load("Project/vendor/Dep 2.0.0");
        Assert.Contains(refused.Errors, e => e.Contains("replace=true"));
        Assert.Equal("1.0.0", Assert.Single(w.Host.Libraries.Libraries, l => l.Name == "Dep").Version);

        var res = ToolAssert.Ok<LoadResult>(await w.Tools().Load([w.Path_("Project/vendor/Dep 2.0.0")], replace: true));
        Assert.Single(res.Replaced);
        Assert.Equal("2.0.0", Assert.Single(w.Host.Libraries.Libraries, l => l.Name == "Dep").Version);
    }

    [Fact]
    public async Task LoadingTheSamePathAgain_SaysItIsAlreadyLoaded()
    {
        using var w = new World();
        await w.Load("Project/Dep");
        var res = await w.Load("Project/Dep/package.mo");
        Assert.Equal("already loaded", Named(res, "Dep").FoundBy);
        Assert.Single(w.Host.Libraries.Libraries, l => l.Name == "Dep");
    }

    [Fact]
    public async Task WithoutTheExactVersion_ACopyDeclaringNoneFromVersion_IsTaken()
    {
        using var w = new World();
        w.Write("Project/Old", Package("Old", uses: "Lib(version=\"1.0\")"));
        w.Write("Project/Lib 2.0", Package("Lib", "2.0", extra: "conversion(noneFromVersion=\"1.0\")"));
        w.Write("Project/Lib 1.5", Package("Lib", "1.5"));

        var lib = Named(await w.Load("Project/Old"), "Lib");
        Assert.Equal("2.0", lib.Version);
        Assert.Contains("noneFromVersion=\"1.0\"", lib.Rule);
    }

    [Fact]
    public async Task AMissingDependency_IsReported_WithEveryCopySeen()
    {
        using var w = new World();
        w.Write("Project/Needy", Package("Needy", uses: "Gone(version=\"9.0\")"));
        w.Write("Project/Gone 1.0", Package("Gone", "1.0"));

        var res = await w.Load("Project/Needy");
        var missing = Assert.Single(res.NotFound);
        Assert.Equal(("Gone", "9.0", "Needy"), (missing.Name, missing.Version, missing.RequiredBy));
        Assert.Equal("1.0", Assert.Single(missing.Candidates).Version);
        Assert.DoesNotContain(w.Host.Libraries.Libraries, l => l.Name == "Gone");
    }

    [Fact]
    public async Task OfTwoCopiesOfOneRelease_TheLargerBuildIsTaken()
    {
        using var w = new World();
        w.Write("Project/User", Package("User", uses: "Twin(version=\"4.1.0\")"));
        w.Write("Project/Twin 4.1.0", Package("Twin", "4.1.0"));
        w.Write("Project/Twin 4.1.0+maint.om", Package("Twin", "4.1.0"));

        var twin = Named(await w.Load("Project/User"), "Twin");
        Assert.EndsWith("Twin 4.1.0+maint.om", twin.Path);
    }

    [Fact]
    public async Task WithoutDependencies_OnlyThePathsAreLoaded()
    {
        using var w = new World();
        var res = ToolAssert.Ok<LoadResult>(await w.Tools().Load([w.Path_("Project/App")], withDependencies: false));
        Assert.Equal("App", Assert.Single(res.Loaded).Name);
    }

    [Fact]
    public async Task BadArguments_AreRefusedOrReported()
    {
        using var w = new World();
        var tools = w.Tools();
        Assert.IsType<ToolError>(await tools.Load([]));
        Assert.Contains("'Dymola 2026x Refresh 1', 'OpenModelica 1.27.0' - or 'none'", Assert.IsType<ToolError>(await tools.Load([w.Path_("Project/App")], tool: "matlab")).Error);
        Assert.Contains("is not Dymola 2026x Refresh 1", Assert.IsType<ToolError>(await tools.Load([w.Path_("Project/App")],
            tool: "Dymola 2026x Refresh 1", toolInstall: Path.Combine(w.Path_("Programs"), "Dymola 2027x Beta 2"))).Error);
        var res = ToolAssert.Ok<LoadResult>(await tools.Load([w.Path_("Project/Nothing")]));
        Assert.Contains(res.Errors, e => e.Contains("not a library folder"));
    }

    [Fact]
    public async Task ANamedToolWithNoInstall_IsWarnedAbout()
    {
        using var w = new World();
        var res = ToolAssert.Ok<LoadResult>(await w.Tools().Load([w.Path_("Project/App")], tool: "dymola",
            toolInstall: w.Path_("NoSuchInstall")));
        Assert.Contains(res.Warnings, x => x.Contains("has no"));
    }
}

public class ModelicaVersionTests
{
    [Theory]
    [InlineData("4.1.0", "4.0.0", 1)]
    [InlineData("4.1", "4.1.0", 0)]
    [InlineData("4.1.0+maint.om", "4.1.0", 1)]
    [InlineData("4.1.0-beta.1", "4.1.0", -1)]
    [InlineData("2025.1", "2026.1", -1)]
    [InlineData("1.10", "1.9", 1)]
    public void OrdersAsTheSpecificationDoes(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(ModelicaVersion.Parse(a)!.CompareTo(ModelicaVersion.Parse(b))));

    [Fact]
    public void ABuildIsTheSameRelease_APreReleaseIsNot()
    {
        Assert.True(ModelicaVersion.Parse("4.1.0+maint.om")!.SameReleaseAs(ModelicaVersion.Parse("4.1")!));
        Assert.False(ModelicaVersion.Parse("4.1.0-rc1")!.SameReleaseAs(ModelicaVersion.Parse("4.1.0")!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("trunk")]
    public void SomethingThatIsNoVersion_IsNull(string? text) => Assert.Null(ModelicaVersion.Parse(text));
}

public class LibraryCandidatesTests
{
    [Fact]
    public void FindsEveryForm_AndReadsTheLibrarysOwnVersion_NotAUsesVersion()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["Lib/package.mo"] = "within;\npackage Lib\n  annotation (uses(Modelica(version=\"4.1.0\")), version=\"1.2\");\nend Lib;\n",
            ["Lib 2.0/package.mo"] = "within;\npackage Lib\n  annotation(version=\"2.0\", conversion(noneFromVersion=\"1.2\"));\nend Lib;\n",
            ["Lib.mo"] = "within;\npackage Lib\nend Lib;\n",
            ["Lib 3.0.mo"] = "within;\npackage Lib\nend Lib;\n",
            ["Library/package.mo"] = "within;\npackage Library\nend Library;\n",
            ["Lib.txt"] = "not a library",
        });

        var found = LibraryCandidates.Find(dir, "Lib");
        var byLeaf = found.ToDictionary(c => Path.GetFileName(c.Path), c => c);

        Assert.Equal(["Lib", "Lib 2.0", "Lib 3.0.mo", "Lib.mo"], byLeaf.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("1.2", byLeaf["Lib"].Version?.Text);
        Assert.Equal("2.0", byLeaf["Lib 2.0"].Version?.Text);
        Assert.Equal("1.2", Assert.Single(byLeaf["Lib 2.0"].NoneFromVersions).Text);
        Assert.Equal("3.0", byLeaf["Lib 3.0.mo"].Version?.Text);
        Assert.Null(byLeaf["Lib.mo"].Version);
    }

    [Fact]
    public void AFolderThatIsNotThere_HasNoCandidates() =>
        Assert.Empty(LibraryCandidates.Find(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), "Lib"));

    // Dymola's library folder holds encrypted builds beside readable ones; a build agent has no such
    // folder, so these are made here rather than found.
    [Fact]
    public void AnEncryptedBuild_IsACandidate_WithTheVersionInItsName()
    {
        using var host = new TestHost();
        var dir = host.NewTempDir();
        Directory.CreateDirectory(Path.Combine(dir, "Lib 4.0"));
        File.WriteAllText(Path.Combine(dir, "Lib 4.0", "package.moe"), "not readable");

        var found = Assert.Single(LibraryCandidates.Find(dir, "Lib"));

        Assert.True(found.Encrypted);
        Assert.Equal("4.0", found.Version?.Text);
        Assert.Empty(found.NoneFromVersions);
    }

    [Fact]
    public void AFolderNamedForTheLibrary_WithNoPackageInIt_IsNoCandidate()
    {
        using var host = new TestHost();
        var dir = host.NewTempDir();
        Directory.CreateDirectory(Path.Combine(dir, "Lib 1.0", "Resources"));

        Assert.Empty(LibraryCandidates.Find(dir, "Lib"));
    }
}

public class ToolLibraryFoldersTests
{
    [Theory]
    [InlineData("bin64/dymola.exe", "")]
    [InlineData("bin/omc.exe", "")]
    [InlineData("", "")]
    public void TheInstallRoot_IsAboveItsBinFolder(string below, string _)
    {
        using var host = new TestHost();
        var root = host.NewTempDir();
        var path = below.Length == 0 ? root : Path.Combine(root, below.Replace('/', Path.DirectorySeparatorChar));
        if (below.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
        }
        Assert.Equal(root, ToolLibraryFolders.InstallRoot(path));
    }

    [Fact]
    public void Dymola_IsTakenFromTheDesktopSettings_WhenNoneIsGiven()
    {
        using var host = new TestHost();
        var install = host.NewTempDir();
        Directory.CreateDirectory(Path.Combine(install, "Modelica", "Library"));
        Directory.CreateDirectory(Path.Combine(install, "bin64"));
        var exe = Path.Combine(install, "bin64", "dymola.exe");
        File.WriteAllText(exe, "x");
        var settings = host.NewTempDir();
        var dymola = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["DymolaPath"] = exe });
        File.WriteAllText(Path.Combine(settings, "settings.json"),
            System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["Dymola"] = dymola }));

        var folders = ToolLibraryFolders.For("dymola", null, settings, _ => null);

        Assert.Equal(install, folders.Install);
        Assert.Equal(Path.Combine(install, "Modelica", "Library"), Assert.Single(folders.Folders));
        Assert.Contains("desktop app", folders.Note);
        Assert.Equal("from the desktop app's External Tools settings", folders.InstallReason);
    }

    [Fact]
    public void OpenModelica_UsesOpenModelicaLibrary_WhenItIsSet()
    {
        using var host = new TestHost();
        var a = host.NewTempDir();
        var b = host.NewTempDir();
        var folders = ToolLibraryFolders.For("openmodelica", host.NewTempDir(), null,
            key => key == "OPENMODELICALIBRARY" ? a + Path.PathSeparator + b : null);
        Assert.Equal([a, b], folders.Folders);
    }

    [Fact]
    public void NoTool_SearchesNoToolFolder() => Assert.Empty(ToolLibraryFolders.For(null, null, null, _ => null).Folders);

    // What detection finds is the machine's: each case below is given its answer, so a machine with
    // Dymola and OpenModelica installed and a build agent with neither run the same code.
    private static string Install(TestHost host, string exe, params string[] folders)
    {
        var root = host.NewTempDir();
        foreach (var folder in folders)
            Directory.CreateDirectory(Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar)));
        var path = Path.Combine(root, exe.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Dymola_IsDetected_WhenNoneIsGivenOrSet_AndSaysToPassToolInstall()
    {
        using var host = new TestHost();
        var exe = Install(host, "bin64/dymola.exe", "Modelica/Library");

        var folders = ToolLibraryFolders.For("dymola", null, null, _ => null, findDymola: () => exe);

        Assert.Equal(ToolLibraryFolders.InstallRoot(exe), folders.Install);
        Assert.Single(folders.Folders);
        Assert.Contains("the newest found", folders.Note);
        Assert.Equal(ToolLibraryFolders.DetectedReason, folders.InstallReason);
    }

    // A 2027x beta installed beside the 2026x Refresh 1 a project used was taken as the newest, and MLQT
    // checked against the beta's MSL and DymolaCommands (a 2026-11 session).

    [Fact]
    public void TheDymolaRunning_WinsOverTheNewestInstalled()
    {
        using var host = new TestHost();
        var newest = Install(host, "bin64/dymola.exe", "Modelica/Library");
        var running = Install(host, "bin64/dymola.exe", "Modelica/Library");

        var folders = ToolLibraryFolders.For("dymola", null, null, _ => null, findDymola: () => newest,
            runningDymola: () => running);

        Assert.Equal(ToolLibraryFolders.InstallRoot(running), folders.Install);
        Assert.Equal(ToolLibraryFolders.RunningReason, folders.InstallReason);
    }

    [Fact]
    public void TheDymolaGiven_WinsOverTheOneRunning()
    {
        using var host = new TestHost();
        var given = Install(host, "bin64/dymola.exe", "Modelica/Library");

        var folders = ToolLibraryFolders.For("dymola", given, null, _ => null, findDymola: () => "",
            runningDymola: () => Install(host, "bin64/dymola.exe"));

        Assert.Equal(ToolLibraryFolders.InstallRoot(given), folders.Install);
    }

    [Fact]
    public void TheNewestTaken_NamesTheOtherInstalls_AndABeta()
    {
        using var host = new TestHost();
        var root = host.NewTempDir();
        var beta = Path.Combine(root, "Dymola 2027x Beta 2", "bin64", "dymola.exe");
        var release = Path.Combine(root, "Dymola 2026x Refresh 1", "bin64", "dymola.exe");

        var folders = ToolLibraryFolders.For("dymola", null, null, _ => null, findDymola: () => beta,
            allDymola: () => [beta, release]);

        Assert.StartsWith(ToolLibraryFolders.DetectedReason, folders.InstallReason);
        Assert.Contains($"Also installed: '{Path.Combine(root, "Dymola 2026x Refresh 1")}'", folders.InstallReason);
        Assert.Contains("It is a beta release", folders.InstallReason);
        Assert.Empty(ToolLibraryFolders.OtherInstalls(release, [release]));
    }

    [Theory]
    [InlineData(new[] { "C:/D/bin64/dymola.exe" }, "C:/D/bin64/dymola.exe")]
    [InlineData(new[] { "C:/D/bin64/dymola.exe", "c:/d/BIN64/dymola.exe" }, "C:/D/bin64/dymola.exe")]   // one release, twice
    [InlineData(new[] { "C:/A/bin64/dymola.exe", "C:/B/bin64/dymola.exe" }, null)]                     // two: the user says which
    [InlineData(new[] { "", null }, null)]                                                              // unreadable
    [InlineData(new string[0], null)]
    public void TheDymolaRunning_IsTheOneReleaseRunning_OrNone(string?[] paths, string? expected)
    {
        Assert.Equal(expected, ToolLibraryFolders.TheOneRunning(paths));
    }

    [Fact]
    public void TheDymolaRunning_IsAskedOfTheMachine_WithoutThrowing()
    {
        // What is running is the machine's; the point is that asking never fails a load.
        var running = ToolLibraryFolders.RunningDymola();
        Assert.True(running is null || Path.IsPathRooted(running));
    }

    [Fact]
    public void TheInstallsListed_AreTheReleaseFoldersThatHoldDymola_NewestFirst()
    {
        using var host = new TestHost();
        var programFiles = host.NewTempDir();
        foreach (var release in new[] { "Dymola 2026x Refresh 1", "Dymola 2027x Beta 2" })
        {
            Directory.CreateDirectory(Path.Combine(programFiles, release, "bin64"));
            File.WriteAllText(Path.Combine(programFiles, release, "bin64", "dymola.exe"), "x");
        }
        Directory.CreateDirectory(Path.Combine(programFiles, "Dymola 2025x", "bin64"));   // no executable

        var installs = ToolLibraryFolders.InstalledDymolas(windows: true, programFiles, pathVariable: null);

        Assert.Equal(["Dymola 2027x Beta 2", "Dymola 2026x Refresh 1"],
            installs.Select(i => Path.GetFileName(ToolLibraryFolders.InstallRoot(i))));
    }

    [Fact]
    public void TheNewestTaken_WithoutATestsDetection_ListsTheMachinesInstalls()
    {
        // No findDymola and no desktop settings, so detection and the listing are the machine's own: a
        // build agent finds none, and a workstation the newest with the others named after the reason.
        var folders = ToolLibraryFolders.For("dymola", null, null, _ => null, runningDymola: () => null);

        Assert.True(folders.Install is null
            ? folders.InstallReason is null
            : folders.InstallReason!.StartsWith(ToolLibraryFolders.DetectedReason, StringComparison.Ordinal));
    }

    [Fact]
    public void Dymola_NotFoundAnywhere_SaysToPassToolInstall()
    {
        var folders = ToolLibraryFolders.For("dymola", null, null, _ => null, findDymola: () => "");

        Assert.Empty(folders.Folders);
        Assert.Null(folders.Install);
        Assert.Contains("pass toolInstall", folders.Note);
    }

    [Fact]
    public void Dymola_GivenAnInstallWithNoLibraryFolder_SaysSo()
    {
        using var host = new TestHost();
        var exe = Install(host, "bin64/dymola.exe");

        var folders = ToolLibraryFolders.For("dymola", exe, null, _ => null, findDymola: () => "");

        Assert.Empty(folders.Folders);
        Assert.Contains("has no", folders.Note);
        Assert.Equal("as given", folders.InstallReason);
    }

    [Fact]
    public void AnUnknownTool_IsAnError_NamingIt()
    {
        var folders = ToolLibraryFolders.For("simulink", null, null, _ => null);

        Assert.Empty(folders.Folders);
        Assert.Contains("'simulink'", folders.Error);
    }

    [Theory]
    [InlineData("Dymola 2026x Refresh 1", "dymola", "2026x Refresh 1")]
    [InlineData("  dymola-2026x ", "dymola", "2026x")]
    [InlineData("Dymola2026x", "dymola", "2026x")]
    [InlineData("dymola", "dymola", null)]
    [InlineData("OpenModelica 1.27.0", "openmodelica", "1.27.0")]
    [InlineData("openmodelica", "openmodelica", null)]
    [InlineData("none", null, null)]
    [InlineData("", null, null)]
    [InlineData(null, null, null)]
    public void TheTool_IsTheSimulatorAndItsRelease(string? tool, string? kind, string? release)
    {
        Assert.Equal((kind, release), ToolLibraryFolders.ParseTool(tool));
    }

    [Theory]
    [InlineData("matlab")]
    [InlineData("dymolax")]
    public void ATool_NamingNoSimulatorWeKnow_IsNotParsed(string tool) => Assert.Null(ToolLibraryFolders.ParseTool(tool));

    [Theory]
    [InlineData("Dymola 2026x Refresh 1", "2026xrefresh1")]
    [InlineData("2026x Refresh 1", "2026xrefresh1")]
    [InlineData("dymola-2026x-x86_64", "2026x")]
    [InlineData("Dymola 2026x", "2026x")]
    public void DymolaReleases_CompareByLettersAndDigits(string name, string key)
        => Assert.Equal(key, ToolLibraryFolders.DymolaReleaseKey(name));

    // Installs named as Dymola names them, under one folder standing in for Program Files.
    private static (string Refresh, string Beta) TwoDymolas(TestHost host)
    {
        var programFiles = host.NewTempDir();
        string Make(string release)
        {
            Directory.CreateDirectory(Path.Combine(programFiles, release, "Modelica", "Library"));
            var exe = Path.Combine(programFiles, release, "bin64", "dymola.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "x");
            return exe;
        }
        return (Make("Dymola 2026x Refresh 1"), Make("Dymola 2027x Beta 2"));
    }

    [Fact]
    public void TheDymolaReleaseNamed_WinsOverTheNewestAndTheOneRunning()
    {
        // The defect: a bare 'dymola' took the newest (a beta) or the one running, while the Dymola server
        // had been told to open 2026x Refresh 1, so MLQT searched another release's MSL.
        using var host = new TestHost();
        var (refresh, beta) = TwoDymolas(host);

        var folders = ToolLibraryFolders.For("Dymola 2026x Refresh 1", null, null, _ => null,
            runningDymola: () => beta, allDymola: () => [beta, refresh]);

        Assert.Null(folders.Error);
        Assert.Equal(ToolLibraryFolders.InstallRoot(refresh), folders.Install);
        Assert.Equal(Path.Combine(folders.Install!, "Modelica", "Library"), Assert.Single(folders.Folders));
        Assert.Contains("Dymola 2026x Refresh 1 named in tool", folders.InstallReason);
    }

    [Fact]
    public void ADymolaReleaseNotInstalled_IsAnError_NamingThoseThatAre()
    {
        using var host = new TestHost();
        var (refresh, beta) = TwoDymolas(host);

        var folders = ToolLibraryFolders.For("Dymola 2025x", null, null, _ => null, allDymola: () => [beta, refresh]);

        Assert.Empty(folders.Folders);
        Assert.Contains("No Dymola 2025x was found", folders.Error);
        Assert.Contains(ToolLibraryFolders.InstallRoot(refresh), folders.Error);
        Assert.Contains(ToolLibraryFolders.InstallRoot(beta), folders.Error);
    }

    [Fact]
    public void AToolInstallOfAnotherRelease_IsRefused_AndOneNamingNoneIsTaken()
    {
        using var host = new TestHost();
        var (refresh, beta) = TwoDymolas(host);

        Assert.Contains("is not Dymola 2026x Refresh 1",
            ToolLibraryFolders.For("Dymola 2026x Refresh 1", beta, null, _ => null, allDymola: () => []).Error);

        var custom = Install(host, "bin64/dymola.exe", "Modelica/Library"); // a temp folder: no release in its name
        var folders = ToolLibraryFolders.For("Dymola 2026x Refresh 1", custom, null, _ => null, allDymola: () => [refresh]);
        Assert.Null(folders.Error);
        Assert.Equal(ToolLibraryFolders.InstallRoot(custom), folders.Install);
    }

    [Fact]
    public void ALinuxDymola_IsFoundByTheReleaseInItsFolderName()
    {
        using var host = new TestHost();
        var opt = host.NewTempDir();
        Directory.CreateDirectory(Path.Combine(opt, "dymola-2026x-x86_64", "Modelica", "Library"));
        var exe = Path.Combine(opt, "dymola-2026x-x86_64", "bin64", "dymola");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "x");
        var launcher = Path.Combine(host.NewTempDir(), "dymola-2026x-x86_64");
        File.WriteAllText(launcher, "x");

        // The launcher names the release too, but holds no library: the install's folder is taken.
        var folders = ToolLibraryFolders.For("Dymola 2026x", null, null, _ => null, allDymola: () => [launcher, exe]);

        Assert.Equal(Path.Combine(opt, "dymola-2026x-x86_64"), folders.Install);
    }

    [Fact]
    public void TheOpenModelicaVersionNamed_PicksItsInstall_AndOneNotInstalledIsRefused()
    {
        using var host = new TestHost();
        var programFiles = host.NewTempDir();
        string Make(string folder)
        {
            var exe = Path.Combine(programFiles, folder, "bin", "omc.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "x");
            return exe;
        }
        var newer = Make("OpenModelica1.27.0-64bit");
        var older = Make("OpenModelica1.26.1-64bit");

        var folders = ToolLibraryFolders.For("OpenModelica 1.26", null, null, _ => null, allOmc: () => [newer, older]);
        Assert.Equal(ToolLibraryFolders.InstallRoot(older), folders.Install);

        Assert.Contains("No OpenModelica 1.25.0 was found",
            ToolLibraryFolders.For("OpenModelica 1.25.0", null, null, _ => null, allOmc: () => [newer, older]).Error);
        Assert.Contains("names no OpenModelica version",
            ToolLibraryFolders.For("OpenModelica latest", null, null, _ => null, allOmc: () => [newer]).Error);
    }

    [Fact]
    public void AnOmcWhosePathNamesNoVersion_IsTaken_SayingItCouldNotBeChecked()
    {
        using var host = new TestHost();
        var omc = Install(host, "bin/omc", "lib");

        var folders = ToolLibraryFolders.For("OpenModelica 1.27.0", null, null, _ => null, allOmc: () => [omc]);

        Assert.Null(folders.Error);
        Assert.Equal(ToolLibraryFolders.InstallRoot(omc), folders.Install);
        Assert.Contains("could not be checked", folders.InstallReason);
    }

    [Fact]
    public void OpenModelica_SearchesTheUsersLibraryFolder_AndAnOlderInstallsBundledOne()
    {
        using var host = new TestHost();
        var home = host.NewTempDir();
        var user = Path.Combine(home, ".openmodelica", "libraries");
        Directory.CreateDirectory(user);
        var omc = Install(host, "bin/omc.exe", "lib/omlibrary");

        var folders = ToolLibraryFolders.For("openmodelica", null, null,
            key => key is "APPDATA" or "HOME" ? home : null, findOmc: () => omc);

        var root = ToolLibraryFolders.InstallRoot(omc);
        Assert.Equal([user, Path.Combine(root, "lib", "omlibrary")], folders.Folders);
        Assert.Equal(root, folders.Install);
        Assert.Contains("and install", folders.Note);
        Assert.Equal(ToolLibraryFolders.DetectedReason, folders.InstallReason);
    }

    [Fact]
    public void OpenModelica_WithNoInstall_SearchesOnlyTheUsersFolder_AndLeavesOutOneThatIsNotThere()
    {
        using var host = new TestHost();
        var home = host.NewTempDir();   // no .openmodelica under it

        var folders = ToolLibraryFolders.For("openmodelica", null, null,
            key => key is "APPDATA" or "HOME" ? home : null, findOmc: () => "");

        Assert.Empty(folders.Folders);
        Assert.Null(folders.Install);
        Assert.Equal("OpenModelica's user library folder.", folders.Note);
    }

    [Fact]
    public void OpenModelica_IsTakenFromTheDesktopSettings_WhenNoneIsGiven()
    {
        using var host = new TestHost();
        var omc = Install(host, "bin/omc.exe");
        var settings = host.NewTempDir();
        var openModelica = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["OmcPath"] = omc });
        File.WriteAllText(Path.Combine(settings, "settings.json"),
            System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["OpenModelica"] = openModelica }));

        var folders = ToolLibraryFolders.For("openmodelica", null, settings, _ => null, findOmc: () => "");

        Assert.Equal(ToolLibraryFolders.InstallRoot(omc), folders.Install);
        Assert.Equal("from the desktop app's External Tools settings", folders.InstallReason);
    }

    // A settings file the desktop app left half-written is no reason to fail a load: it is read only to
    // choose an install, and detection is the next choice.
    [Fact]
    public void ADesktopSettingsFileThatWillNotRead_FallsBackToDetection()
    {
        using var host = new TestHost();
        var exe = Install(host, "bin64/dymola.exe", "Modelica/Library");
        var settings = host.NewTempDir();
        File.WriteAllText(Path.Combine(settings, "settings.json"), "{ not json");

        var folders = ToolLibraryFolders.For("dymola", null, settings, _ => null, findDymola: () => exe);

        Assert.Equal(ToolLibraryFolders.DetectedReason, folders.InstallReason);
    }
}
