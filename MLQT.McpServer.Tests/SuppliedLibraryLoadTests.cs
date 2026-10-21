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

    private static LoadedLibrary VendorLib(TestHost host) =>
        Assert.Single(host.Libraries.Libraries, l => l.Name == "VendorLib");

    [Theory]
    [InlineData("2026.1")] // the same release
    [InlineData("2025.1")] // another release, which on its own would let the installed build win
    public async Task AnEncryptedBuild_NeverDisplacesTheSuppliedLibrary_WithoutReplace(string suppliedVersion)
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source(suppliedVersion));
        var encrypted = WriteEncrypted(host.NewTempDir(), "VendorLib 2026.1");

        var res = ToolAssert.Ok<LoadResult>(await Tools(host).Load([encrypted], withDependencies: false));

        Assert.Equal(ReadOnlySourceKind.Supplied, VendorLib(host).ReadOnlySource);
        Assert.Contains(res.Errors, e => e.Contains("'VendorLib' is already loaded", StringComparison.Ordinal));
        Assert.Empty(res.Replaced);
    }

    /// <summary>The route the library service decides alone: a folder of libraries, whose libraries were
    /// loaded without being asked about first. The installed build of another release retired the
    /// supplied copy there, unasked and unsaid.</summary>
    [Theory]
    [InlineData("2026.1")]
    [InlineData("2025.1")]
    public async Task AnEncryptedBuild_InAFolderOfLibraries_NeverDisplacesTheSuppliedLibrary(string suppliedVersion)
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source(suppliedVersion));
        var folder = host.NewTempDir();
        WriteEncrypted(folder, "VendorLib 2026.1");

        var res = ToolAssert.Ok<LoadResult>(await Tools(host).Load([folder], withDependencies: false));

        Assert.Equal(ReadOnlySourceKind.Supplied, VendorLib(host).ReadOnlySource);
        Assert.Contains(res.Errors, e => e.Contains("'VendorLib' is already loaded", StringComparison.Ordinal));
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
    [InlineData(true)]  // the installed encrypted build
    public async Task ReplacingTheSuppliedLibrary_FromAFolderOfLibraries_IsListedUnderReplaced(bool encrypted)
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source("2026.1"));
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

    /// <summary>The same release, which the supplied copy outranks: the library service turns the build
    /// away while the supplied copy is loaded, so replace has to take it out first.</summary>
    [Theory]
    [InlineData("2026.1")]
    [InlineData("2025.1")]
    public async Task ReplacingTheSuppliedLibrary_WithAnEncryptedBuild_IsListedUnderReplaced(string suppliedVersion)
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new Source(suppliedVersion));
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
        await host.Libraries.AddLibraryFromSourceAsync(new Source("2026.1"));
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
