using MLQT.Services;
using MLQT.Services.DataTypes;
using Xunit;

namespace MLQT.Services.Tests.ExternalDocs;

/// <summary>
/// <see cref="LibraryDataService.AddLibraryFromPathAsync"/> — the one place that decides how a path
/// should be loaded.
///
/// <para>The app, the CLI and the MCP tools all funnel through it, so these cases are the contract
/// between them. They used to be four separate copies of the same decision, and the copy that was
/// missing is how an encrypted library inside a repository came to load as empty.</para>
/// </summary>
public class LibraryPathDispatchTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-dispatch", Guid.NewGuid().ToString("N"));

    public LibraryPathDispatchTests() => Directory.CreateDirectory(_root);

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

    private string WriteSourceLibrary()
    {
        var lib = Path.Combine(_root, "MyLib");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"), "package MyLib\nend MyLib;\n");
        File.WriteAllText(Path.Combine(lib, "package.order"), "Widget\n");
        File.WriteAllText(Path.Combine(lib, "Widget.mo"),
            "within MyLib;\nmodel Widget \"A widget\"\nend Widget;\n");
        return lib;
    }

    private string WriteEncryptedLibrary()
    {
        var lib = Path.Combine(_root, "Vendor 1.2.0");
        var help = Path.Combine(lib, "help");
        Directory.CreateDirectory(help);
        File.WriteAllText(Path.Combine(lib, "package.moe"), "not readable");
        File.WriteAllText(Path.Combine(help, "Vendor.html"),
            "<html><head><meta name=\"HTML-Generator\" content=\"Dymola\"></head><body>" +
            "<h2><a name=\"Vendor\"></a>Vendor</h2>" +
            "<p><span class=\"ModelicaDescription\">A vendor library</span></p>" +
            "</body></html>");
        return lib;
    }

    [Fact]
    public async Task Directory_LoadsAsSource()
    {
        var library = await new LibraryDataService().AddLibraryFromPathAsync(WriteSourceLibrary());

        Assert.Equal(LibrarySourceType.Directory, library.SourceType);
        Assert.Contains("MyLib.Widget", library.ModelIds);
    }

    [Fact]
    public async Task PackageMoFile_LoadsTheWholeLibraryNotJustThatFile()
    {
        // Callers routinely point at ".../MyLib/package.mo" meaning "load MyLib". Loading only that
        // one file would silently miss every standalone child beside it.
        var packageMo = Path.Combine(WriteSourceLibrary(), "package.mo");

        var library = await new LibraryDataService().AddLibraryFromPathAsync(packageMo);

        Assert.Contains("MyLib.Widget", library.ModelIds);
    }

    [Fact]
    public async Task StandaloneMoFile_LoadsAsASingleFile()
    {
        var loose = Path.Combine(_root, "Loose.mo");
        File.WriteAllText(loose, "model Loose \"Standalone\"\nend Loose;\n");

        var library = await new LibraryDataService().AddLibraryFromPathAsync(loose);

        Assert.Equal(LibrarySourceType.File, library.SourceType);
        Assert.Contains("Loose", library.ModelIds);
    }

    [Fact]
    public async Task EncryptedDirectory_LoadsFromItsDocumentation()
    {
        var library = await new LibraryDataService().AddLibraryFromPathAsync(WriteEncryptedLibrary());

        Assert.Equal(LibrarySourceType.EncryptedDirectory, library.SourceType);
        Assert.Equal("Vendor", library.Name);
        Assert.Contains("Vendor", library.ModelIds);
    }

    [Fact]
    public async Task UnknownPath_IsRejectedRatherThanLoadedAsEmpty()
    {
        // Loading nothing and reporting success is the failure mode this whole class exists to
        // prevent: it looks like it worked, and the damage shows up much later as unresolved
        // references.
        var service = new LibraryDataService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddLibraryFromPathAsync(Path.Combine(_root, "nothing-here")));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddLibraryFromPathAsync(Path.Combine(_root, "notes.txt")));
    }
}
