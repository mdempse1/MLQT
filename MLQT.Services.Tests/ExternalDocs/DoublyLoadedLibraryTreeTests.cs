using MLQT.Services;
using MLQT.Services.DataTypes;
using Xunit;

namespace MLQT.Services.Tests.ExternalDocs;

/// <summary>
/// A project holding two repositories that each contain a copy of the same library: a tool's library
/// folder, which ships the encrypted build, and the vendor's own source checkout.
///
/// <para>The library browser filters each repository's tree by the library id stamped on a class, and
/// both copies claim the same top-level class — which is the same node object, since only one copy
/// survives in the graph. Adding it once per claiming library put the library in the tree twice, and
/// stamping the library id onto the shared node meant both copies were attributed to whichever
/// library happened to be processed last. The visible result was a library appearing twice under one
/// repository and not at all under the other, with which repository varying between libraries.</para>
/// </summary>
public class DoublyLoadedLibraryTreeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-doubly-loaded", Guid.NewGuid().ToString("N"));

    public DoublyLoadedLibraryTreeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    private string WriteSource(string name)
    {
        var lib = Path.Combine(_root, "source", name);
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"),
            $"package {name} \"{name} from source\"\n" +
            "  model Widget \"A widget\"\n  end Widget;\n" +
            $"end {name};\n");
        File.WriteAllText(Path.Combine(lib, "package.order"), "Widget\n");
        return lib;
    }

    private string WriteEncrypted(string name)
    {
        var lib = Path.Combine(_root, "tool", $"{name} 2026.1");
        var help = Path.Combine(lib, "help");
        Directory.CreateDirectory(help);
        File.WriteAllText(Path.Combine(lib, "package.moe"), "not readable");
        File.WriteAllText(Path.Combine(help, $"{name}.html"),
            "<html><head><meta name=\"HTML-Generator\" content=\"Dymola\"></head><body>" +
            $"<h2><a name=\"{name}\"></a>{name}</h2>" +
            $"<p><span class=\"ModelicaDescription\">{name} encrypted</span></p>" +
            "<h3>Package Content</h3>" +
            "<table summary=\"Package Content\" class=\"ModelicaTablePackageContent\">\n" +
            "<tr><th>Name</th><th>Description</th></tr>\n" +
            $"<tr><td><img src=\"{name}.WidgetS.png\" alt=\"{name}.Widget\">&nbsp;" +
            $"<a href=\"{name}.html#{name}.Widget\">Widget</a></td><td>Widget</td></tr>\n" +
            "</table>" +
            $"<h2><img src=\"{name}.WidgetI.png\" alt=\"{name}.Widget\">" +
            $"<a name=\"{name}.Widget\"></a>Widget</h2>" +
            "</body></html>");
        return lib;
    }

    /// <param name="encryptedFirst">Which repository loads first. The symptom flipped between
    /// libraries precisely because this varied.</param>
    private async Task<LibraryDataService> LoadBothAsync(string name, bool encryptedFirst)
    {
        var service = new LibraryDataService();
        var first = encryptedFirst ? WriteEncrypted(name) : WriteSource(name);
        var second = encryptedFirst ? WriteSource(name) : WriteEncrypted(name);

        await service.AddLibraryFromPathAsync(first);
        await service.AddLibraryFromPathAsync(second);
        return service;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheLibraryAppearsOnceInTheTree(bool encryptedFirst)
    {
        var service = await LoadBothAsync("Claytex", encryptedFirst);

        var topLevel = await service.GetTopLevelModelsAsync();

        Assert.Equal(1, topLevel.Count(m => m.Id == "Claytex"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ItIsAttributedToTheLibraryWhoseCopyIsActuallyLoaded(bool encryptedFirst)
    {
        // Source wins in the graph, so the source library is the one that owns the class — and the
        // one whose repository should show it, whichever order the two were loaded in.
        var service = await LoadBothAsync("Claytex", encryptedFirst);

        var claytex = (await service.GetTopLevelModelsAsync()).Single(m => m.Id == "Claytex");
        var owner = service.Libraries.Single(l => l.Id == claytex.LibraryId);

        Assert.NotEqual(LibrarySourceType.EncryptedDirectory, owner.SourceType);
        Assert.False(claytex.IsExternalStub);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChildrenComeFromTheOwningLibrary(bool encryptedFirst)
    {
        // The two copies list different children — the encrypted one knows only what its
        // documentation named. Expanding the package must show the source's children.
        var service = await LoadBothAsync("Claytex", encryptedFirst);

        var claytex = (await service.GetTopLevelModelsAsync()).Single(m => m.Id == "Claytex");
        var children = await service.GetChildModelsAsync(claytex);

        var widget = Assert.Single(children, c => c.Id == "Claytex.Widget");
        Assert.False(widget.IsExternalStub);
    }

    [Fact]
    public async Task AnEncryptedLibraryWithNoSourceCopy_IsStillItsOwnOwner()
    {
        // The fix must not make encrypted libraries disappear when there is no source copy to
        // prefer — that is the ordinary case for every other vendor library in the folder.
        var service = new LibraryDataService();
        await service.AddLibraryFromPathAsync(WriteEncrypted("Suspensions"));

        var top = (await service.GetTopLevelModelsAsync()).Single(m => m.Id == "Suspensions");
        var owner = service.Libraries.Single(l => l.Id == top.LibraryId);

        Assert.Equal(LibrarySourceType.EncryptedDirectory, owner.SourceType);
        Assert.True(top.IsExternalStub);
    }
}
