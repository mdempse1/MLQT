using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;
using MLQT.Services.DataTypes;
using ModelicaGraph;

namespace MLQT.McpServer.Tests;

/// <summary>
/// mlqt_load beside a library the host supplied from memory: only the host can supply it again, so an
/// explicit load may take its place only when asked to, and says so when it does.
/// </summary>
public class SuppliedLibraryLoadTests
{
    private sealed class Source(string version) : IReadOnlyClassSource
    {
        public string LibraryName => "VendorLib";
        public string? LibraryVersion => version;
        public ReadOnlySourceKind Kind => ReadOnlySourceKind.Supplied;
        public string? Location => null;
        public string ProvenanceNote => "From a test host - not the vendor's source.";

        public ReadOnlySourceContent Read(CancellationToken cancellationToken) => new()
        {
            Texts =
            [
                new SuppliedText("VendorLib/package.mo",
                    "within;\npackage VendorLib \"VendorLib supplied\"\n  model Widget \"A widget\"\n  end Widget;\n" +
                    $"  annotation (version = \"{version}\");\nend VendorLib;\n")
            ]
        };
    }

    private static LoadTools Tools(TestHost host) => new(host.Libraries, host.Repositories, host.Resources, host.Session)
    {
        DesktopSettingsDirectory = null,
        Environment_ = _ => null,
    };

    /// <summary>An encrypted build of VendorLib, with the help page MLQT recovers its classes from.</summary>
    private static string WriteEncrypted(string root, string folder)
    {
        var lib = Path.Combine(root, folder);
        var help = Path.Combine(lib, "help");
        Directory.CreateDirectory(help);
        File.WriteAllText(Path.Combine(lib, "package.moe"), "not readable");
        File.WriteAllText(Path.Combine(help, "VendorLib.html"),
            "<html><head><meta name=\"HTML-Generator\" content=\"Dymola\"></head><body>" +
            "<h2><a name=\"VendorLib\"></a>VendorLib</h2>" +
            "<p><span class=\"ModelicaDescription\">VendorLib encrypted</span></p>" +
            "<h3>Package Content</h3>" +
            "<table summary=\"Package Content\" class=\"ModelicaTablePackageContent\">\n" +
            "<tr><th>Name</th><th>Description</th></tr>\n" +
            "<tr><td><img src=\"VendorLib.WidgetS.png\" alt=\"VendorLib.Widget\">&nbsp;" +
            "<a href=\"VendorLib.html#VendorLib.Widget\">Widget</a></td><td>Widget</td></tr>\n" +
            "</table>" +
            "<h2><img src=\"VendorLib.WidgetI.png\" alt=\"VendorLib.Widget\"><a name=\"VendorLib.Widget\"></a>Widget</h2>" +
            "</body></html>");
        return lib;
    }

    private static string WriteReadable(string root, string folder, string name)
    {
        var lib = Path.Combine(root, folder);
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"),
            $"within;\npackage {name} \"{name} from source\"\n  model Widget \"A widget\"\n  end Widget;\nend {name};\n");
        return lib;
    }

    private static SessionTools Session(TestHost host) => new(host.Libraries, host.Repositories, host.Resources, host.Session);

    /// <summary>A reload replaces a supplied library as a load can: a working copy that gained readable
    /// source for it retires the supplied copy, which is intended, and the answer says so.</summary>
    [Theory]
    [InlineData(false)] // everything
    [InlineData(true)]  // the repository, by id
    public async Task AReload_ThatReplacesTheSuppliedLibrary_ListsItUnderReplaced(bool byRepository)
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source("2026.1"));
        var folder = host.NewTempDir();
        WriteReadable(folder, "Other", "Other");
        var load = ToolAssert.Ok<LoadResult>(await Tools(host).Load([folder], withDependencies: false));
        var repository = Assert.Single(load.Repositories!);
        var source = WriteReadable(folder, "VendorLib", "VendorLib");

        var res = ToolAssert.Ok<ReloadResult>(await Session(host).Reload(byRepository ? repository.RepositoryId : null));

        Assert.Null(VendorLib(host).ReadOnlySource);
        var replaced = Assert.Single(res.Replaced!);
        Assert.Equal("VendorLib", replaced.Name);
        Assert.Equal(ModelicaGraph.ReadOnlySources.InMemoryRoot("VendorLib"), replaced.OldPath);
        Assert.Equal(source, replaced.NewPath);
    }

    [Fact]
    public async Task AReload_ThatReplacesNothing_ListsNothing()
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source("2026.1"));
        var folder = host.NewTempDir();
        WriteReadable(folder, "Other", "Other");
        await Tools(host).Load([folder], withDependencies: false);
        var mine = WriteReadable(host.NewTempDir(), "Mine", "Mine");
        await Tools(host).Load([mine], withDependencies: false);

        var all = ToolAssert.Ok<ReloadResult>(await Session(host).Reload());
        var one = ToolAssert.Ok<ReloadResult>(await Session(host).Reload("Mine"));

        Assert.Null(all.Replaced);
        Assert.Null(one.Replaced);
        Assert.Equal(ReadOnlySourceKind.Supplied, VendorLib(host).ReadOnlySource);
    }

    private static LoadedLibrary VendorLib(TestHost host) =>
        Assert.Single(host.Libraries.Libraries, l => l.Name == "VendorLib");

    /// <summary>The supplied copy of a release is the vendor's own description of it, and wins over what
    /// MLQT rebuilds from the encrypted build's help pages - replace or not, and said, not refused: a
    /// refusal would offer replace, which cannot change it.</summary>
    [Theory]
    [InlineData("VendorLib 2026.1", false)] // the same release
    [InlineData("VendorLib 2026.1", true)]
    [InlineData("VendorLib", false)]        // a build whose release is not known: nothing tells them apart
    [InlineData("VendorLib", true)]
    public async Task AnEncryptedBuild_OfTheSuppliedRelease_NeverDisplacesIt(string folder, bool replace)
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source("2026.1"));
        var encrypted = WriteEncrypted(host.NewTempDir(), folder);

        var res = ToolAssert.Ok<LoadResult>(await Tools(host).Load([encrypted], withDependencies: false, replace: replace));

        Assert.Equal(ReadOnlySourceKind.Supplied, VendorLib(host).ReadOnlySource);
        Assert.Contains(res.Warnings, w => w.Contains("the release the host supplied", StringComparison.Ordinal));
        Assert.Empty(res.Errors);
        Assert.Empty(res.Replaced);
    }

    [Fact]
    public async Task AnEncryptedBuild_OfAnotherRelease_IsRefused_WithoutReplace()
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source("2025.1"));
        var encrypted = WriteEncrypted(host.NewTempDir(), "VendorLib 2026.1");

        var res = ToolAssert.Ok<LoadResult>(await Tools(host).Load([encrypted], withDependencies: false));

        Assert.Equal(ReadOnlySourceKind.Supplied, VendorLib(host).ReadOnlySource);
        Assert.Contains(res.Errors, e => e.Contains("'VendorLib' is already loaded", StringComparison.Ordinal));
        Assert.Empty(res.Replaced);
    }

    /// <summary>The route the library service decides alone: a folder of libraries, whose libraries were
    /// loaded without being asked about first. The installed build of another release retired the
    /// supplied copy there, unasked and unsaid.</summary>
    [Fact]
    public async Task AnEncryptedBuild_OfAnotherRelease_InAFolderOfLibraries_IsRefused_WithoutReplace()
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source("2025.1"));
        var folder = host.NewTempDir();
        WriteEncrypted(folder, "VendorLib 2026.1");

        var res = ToolAssert.Ok<LoadResult>(await Tools(host).Load([folder], withDependencies: false));

        Assert.Equal(ReadOnlySourceKind.Supplied, VendorLib(host).ReadOnlySource);
        Assert.Contains(res.Errors, e => e.Contains("'VendorLib' is already loaded", StringComparison.Ordinal));
        Assert.Empty(res.Replaced);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnEncryptedBuild_OfTheSuppliedRelease_InAFolderOfLibraries_NeverDisplacesIt(bool replace)
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source("2026.1"));
        var folder = host.NewTempDir();
        WriteEncrypted(folder, "VendorLib 2026.1");

        var res = ToolAssert.Ok<LoadResult>(await Tools(host).Load([folder], withDependencies: false, replace: replace));

        Assert.Equal(ReadOnlySourceKind.Supplied, VendorLib(host).ReadOnlySource);
        Assert.Contains(res.Warnings, w => w.Contains("the release the host supplied", StringComparison.Ordinal));
        Assert.Empty(res.Errors);
        Assert.Empty(res.Replaced);
    }

    [Fact]
    public async Task ReadableSource_InAFolderOfLibraries_IsRefused_WithoutReplace()
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source("2026.1"));
        var folder = host.NewTempDir();
        WriteReadable(folder, "VendorLib", "VendorLib");

        var res = ToolAssert.Ok<LoadResult>(await Tools(host).Load([folder], withDependencies: false));

        Assert.Equal(ReadOnlySourceKind.Supplied, VendorLib(host).ReadOnlySource);
        Assert.Contains(res.Errors, e => e.Contains("'VendorLib' is already loaded", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)] // readable source
    [InlineData(true)]  // the installed encrypted build, of another release than the one supplied
    public async Task ReplacingTheSuppliedLibrary_FromAFolderOfLibraries_IsListedUnderReplaced(bool encrypted)
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source(encrypted ? "2025.1" : "2026.1"));
        var folder = host.NewTempDir();
        if (encrypted)
            WriteEncrypted(folder, "VendorLib 2026.1");
        else
            WriteReadable(folder, "VendorLib", "VendorLib");

        var res = ToolAssert.Ok<LoadResult>(await Tools(host).Load([folder], withDependencies: false, replace: true));

        Assert.NotEqual(ReadOnlySourceKind.Supplied, VendorLib(host).ReadOnlySource);
        var replaced = Assert.Single(res.Replaced);
        Assert.Equal("VendorLib", replaced.Name);
        Assert.Equal(VendorLib(host).SourcePath, replaced.NewPath);
    }

    /// <summary>Two readable copies cannot be loaded side by side - their classes have the same ids - so
    /// the one loaded goes before the folder's is loaded, and the folder's is the one left.</summary>
    [Fact]
    public async Task ReplacingALibraryOnDisk_FromAFolderOfLibraries_LeavesOnlyTheNewCopy()
    {
        using var host = new TestHost();
        var first = WriteReadable(host.NewTempDir(), "VendorLib", "VendorLib");
        await Tools(host).Load([first], withDependencies: false);
        var folder = host.NewTempDir();
        var second = Path.Combine(folder, "VendorLib");
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(second, "package.mo"),
            "within;\npackage VendorLib \"VendorLib, second copy\"\n  model Widget \"A widget\"\n  end Widget;\n" +
            "  annotation (version = \"9.0\");\nend VendorLib;\n");

        var refused = ToolAssert.Ok<LoadResult>(await Tools(host).Load([folder], withDependencies: false));
        Assert.Contains(refused.Errors, e => e.Contains("'VendorLib' is already loaded", StringComparison.Ordinal));

        var res = ToolAssert.Ok<LoadResult>(await Tools(host).Load([folder], withDependencies: false, replace: true));

        Assert.Equal("9.0", VendorLib(host).Version);
        Assert.Equal(first, Assert.Single(res.Replaced).OldPath);
    }

    /// <summary>A build of another release than the one supplied is what is installed, so replace may put
    /// it in the supplied copy's place.</summary>
    [Fact]
    public async Task ReplacingTheSuppliedLibrary_WithABuildOfAnotherRelease_IsListedUnderReplaced()
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source("2025.1"));
        var encrypted = WriteEncrypted(host.NewTempDir(), "VendorLib 2026.1");

        var res = ToolAssert.Ok<LoadResult>(await Tools(host).Load([encrypted], withDependencies: false, replace: true));

        Assert.Equal(ReadOnlySourceKind.RecoveredFromDocumentation, VendorLib(host).ReadOnlySource);
        var replaced = Assert.Single(res.Replaced);
        Assert.Equal("VendorLib", replaced.Name);
        Assert.Equal(encrypted, replaced.NewPath);
        Assert.Empty(res.Errors);
    }

    /// <summary>A replacement that cannot load leaves the supplied copy where it was: only the host could
    /// supply it again.</summary>
    [Fact]
    public async Task AFailedReplacement_KeepsTheSuppliedLibrary()
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source("2025.1"));
        var encrypted = WriteEncrypted(host.NewTempDir(), "VendorLib 2026.1");
        Directory.Delete(Path.Combine(encrypted, "help"), recursive: true);

        var res = ToolAssert.Ok<LoadResult>(await Tools(host).Load([encrypted], withDependencies: false, replace: true));

        Assert.Equal(ReadOnlySourceKind.Supplied, VendorLib(host).ReadOnlySource);
        Assert.Empty(res.Replaced);
    }

    [Theory]
    [InlineData("VendorLib")]
    [InlineData("vendor-source")]
    public async Task ReplacingTheSuppliedLibrary_WithReadableSource_IsListedUnderReplaced(string folder)
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source("2026.1"));
        var source = WriteReadable(host.NewTempDir(), folder, "VendorLib");

        var res = ToolAssert.Ok<LoadResult>(await Tools(host).Load([source], withDependencies: false, replace: true));

        Assert.Null(VendorLib(host).ReadOnlySource);
        Assert.Contains(res.Replaced, r => r.Name == "VendorLib");
    }

    [Fact]
    public async Task ReadableSource_InAFolderNotNamedAfterIt_IsRefused_WithoutReplace()
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source("2026.1"));
        var source = WriteReadable(host.NewTempDir(), "vendor-source", "VendorLib");

        var res = ToolAssert.Ok<LoadResult>(await Tools(host).Load([source], withDependencies: false));

        Assert.Equal(ReadOnlySourceKind.Supplied, VendorLib(host).ReadOnlySource);
        Assert.Contains(res.Errors, e => e.Contains("'VendorLib' is already loaded", StringComparison.Ordinal));
    }

    /// <summary>The other way round, readable source over readable source: a folder named after a loaded
    /// library holding another one, given with replace, used to unload the loaded one and report it
    /// replaced by a library of another name.</summary>
    [Fact]
    public async Task AFolderNamedForALoadedLibrary_HoldingAnotherOne_WithReplace_UnloadsNothing()
    {
        using var host = new TestHost();
        var mine = WriteReadable(host.NewTempDir(), "Mine", "Mine");
        await Tools(host).Load([mine], withDependencies: false);
        var other = WriteReadable(host.NewTempDir(), "Mine", "Other");

        var res = ToolAssert.Ok<LoadResult>(await Tools(host).Load([other], withDependencies: false, replace: true));

        Assert.Contains(host.Libraries.Libraries, l => l.Name == "Mine");
        Assert.Contains(host.Libraries.Libraries, l => l.Name == "Other");
        Assert.Empty(res.Replaced);
    }

    /// <summary>The "already loaded" check names a library by what its files declare, not by its folder:
    /// a folder that happens to carry a loaded library's name holds another library altogether.</summary>
    [Fact]
    public async Task AFolderNamedForALoadedLibrary_HoldingAnotherOne_DoesNotTouchTheLoadedOne()
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source("2026.1"));
        var other = WriteReadable(host.NewTempDir(), "VendorLib", "Other");

        var res = ToolAssert.Ok<LoadResult>(await Tools(host).Load([other], withDependencies: false));

        Assert.Equal(ReadOnlySourceKind.Supplied, VendorLib(host).ReadOnlySource);
        Assert.Contains(host.Libraries.Libraries, l => l.Name == "Other");
        Assert.Empty(res.Errors);
        Assert.Empty(res.Replaced);
    }
}
