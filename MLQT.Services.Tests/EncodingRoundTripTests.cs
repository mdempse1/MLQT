using System.Text;
using MLQT.Services.Helpers;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// The load-then-save round trip, over the real pipeline, for each encoding a Modelica file turns
/// up in.
///
/// <para>These exist because the failure they guard against was silent and cumulative. Reading a
/// UTF-8 file as Latin-1 and writing it back as UTF-8 re-encoded the decoded characters: "ü" went
/// from two bytes to four, then to eight on the next save, and nothing reported it. The file just
/// quietly rotted in the repository, one format at a time.</para>
/// </summary>
public class EncodingRoundTripTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-roundtrip", Guid.NewGuid().ToString("N"));

    public EncodingRoundTripTests() => Directory.CreateDirectory(_root);

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

    // A description carrying a character that is encoded differently in UTF-8 and Latin-1.
    private const string PackageSource = "package EncLib \"Krüger boiler\"\nend EncLib;\n";

    private static Encoding Utf8NoBom { get; } = new UTF8Encoding(false);

    private string WriteLibrary(Encoding encoding, bool bom = false)
    {
        var lib = Path.Combine(_root, "EncLib");
        Directory.CreateDirectory(lib);

        var bytes = bom
            ? new UTF8Encoding(true).GetPreamble().Concat(Utf8NoBom.GetBytes(PackageSource)).ToArray()
            : encoding.GetBytes(PackageSource);

        File.WriteAllBytes(Path.Combine(lib, "package.mo"), bytes);
        File.WriteAllText(Path.Combine(lib, "package.order"), string.Empty);
        return lib;
    }

    /// <summary>
    /// Loads the library and formats it back over itself, which is what the application does: the
    /// save root is the library's parent, so every file is rewritten at the path it came from. That
    /// is the case where preserving the original encoding matters — a file written somewhere new
    /// has no previous encoding to preserve and is created as UTF-8.
    /// </summary>
    private async Task<string> LoadAndSaveInPlaceAsync(string libraryPath)
    {
        var service = new LibraryDataService();
        var library = await service.AddLibraryFromDirectoryAsync(libraryPath);

        ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            service.CombinedGraph, library.ModelIds, Path.GetDirectoryName(libraryPath)!,
            showAnnotations: true, oneOfEachSection: false,
            importsFirst: false, componentsBeforeClasses: false);

        return Path.Combine(libraryPath, "package.mo");
    }

    [Fact]
    public async Task Utf8WithoutBom_SurvivesLoadAndSave()
    {
        // The common case, and the one that was being corrupted: BOM-less UTF-8 is what Modelica
        // tooling writes, and it is what MSL itself ships.
        var written = await LoadAndSaveInPlaceAsync(WriteLibrary(Utf8NoBom));

        var (text, encoding) = ModelicaParser.Helpers.ModelicaFileEncoding.ReadAllText(written);
        Assert.Equal(Utf8NoBom.WebName, encoding.WebName);
        Assert.Contains("Krüger", text);
        Assert.DoesNotContain("Ã", text);
    }

    [Fact]
    public async Task SingleByteEncoding_SurvivesLoadAndSave()
    {
        // The case Latin-1 reading was introduced for. It must keep working: the point of detecting
        // per file was to stop trading one broken population for the other.
        var written = await LoadAndSaveInPlaceAsync(WriteLibrary(Encoding.Latin1));

        var (text, encoding) = ModelicaParser.Helpers.ModelicaFileEncoding.ReadAllText(written);
        Assert.Equal(Encoding.Latin1, encoding);
        Assert.Contains("Krüger", text);
    }

    [Fact]
    public async Task Utf8WithBom_SurvivesLoadAndSave()
    {
        var written = await LoadAndSaveInPlaceAsync(WriteLibrary(Utf8NoBom, bom: true));

        var bytes = await File.ReadAllBytesAsync(written);
        Assert.Equal(new UTF8Encoding(true).GetPreamble(), bytes.Take(3));
        Assert.Contains("Krüger", await File.ReadAllTextAsync(written));
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("latin1")]
    public async Task RepeatedSaves_DoNotAccumulateDamage(string kind)
    {
        // The corruption doubled on every pass: 2 bytes, then 4, then 8. Formatting the same
        // library repeatedly is ordinary use, so the byte count has to stop moving.
        var library = WriteLibrary(kind == "utf8" ? Utf8NoBom : Encoding.Latin1);
        var sizes = new List<long>();

        for (var i = 0; i < 3; i++)
            sizes.Add(new FileInfo(await LoadAndSaveInPlaceAsync(library)).Length);

        Assert.Equal(sizes[0], sizes[1]);
        Assert.Equal(sizes[1], sizes[2]);

        var final = Path.Combine(library, "package.mo");
        Assert.Contains("Krüger", ModelicaParser.Helpers.ModelicaFileEncoding.ReadAllTextOnly(final));
    }
}
