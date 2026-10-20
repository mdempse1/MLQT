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
    public void ADetectedInstall_SaysToPassAParameterTheToolHas()
    {
        // B504: the newest Dymola found need not be the one running, and only the agent can know
        // that - so the answer names the parameter to pass, which must be one mlqt_load has.
        var parameters = typeof(LoadTools).GetMethod(nameof(LoadTools.Load))!.GetParameters().Select(p => p.Name);

        Assert.Contains("toolInstall", parameters);
        Assert.Contains("pass toolInstall", ToolLibraryFolders.DetectedReason);
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
        Assert.Contains("'dymola', 'openmodelica' or 'none'", Assert.IsType<ToolError>(await tools.Load([w.Path_("Project/App")], tool: "matlab")).Error);
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
    public void AnUnknownTool_IsNamedInTheNote()
    {
        var folders = ToolLibraryFolders.For("simulink", null, null, _ => null);

        Assert.Empty(folders.Folders);
        Assert.Contains("'simulink'", folders.Note);
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
