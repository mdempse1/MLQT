using MLQT.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Helpers;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using Xunit;

namespace MLQT.Services.Tests.ExternalDocs;

/// <summary>
/// A library a host supplies from memory (<see cref="ILibraryDataService.AddLibraryFromSourceAsync"/>):
/// read-only like an encrypted library, ranked between readable source and documentation recovery,
/// and bound to the version of the library actually installed.
/// </summary>
public class SuppliedLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-supplied", Guid.NewGuid().ToString("N"));

    public SuppliedLibraryTests() => Directory.CreateDirectory(_root);

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

    // ---------------------------------------------------------------- fixtures

    /// <summary>A supplied copy of Claytex: the package and its Widget, with Widget's interface.</summary>
    private sealed class Source(string version = "2026.1") : IReadOnlyClassSource
    {
        public int Reads { get; private set; }
        public string LibraryName => "Claytex";
        public string? LibraryVersion => version;
        public ReadOnlySourceKind Kind => ReadOnlySourceKind.Supplied;
        public string? Location => null;
        public string ProvenanceNote => "From a test host - not the vendor's source.";

        public ReadOnlySourceContent Read(CancellationToken cancellationToken)
        {
            Reads++;
            return new ReadOnlySourceContent
            {
                Texts =
                [
                    new SuppliedText("Claytex/package.mo",
                        "within;\npackage Claytex \"Claytex supplied\"\n  model Widget \"A widget\"\n" +
                        "    parameter Real k = 1 \"gain\";\n  end Widget;\nend Claytex;\n")
                ]
            };
        }
    }

    /// <summary>The checkout of the same library.</summary>
    private string WriteSource()
    {
        var lib = Path.Combine(_root, "source", "Claytex");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"),
            "package Claytex \"Claytex from source\"\n  model Widget \"A widget\"\n  end Widget;\nend Claytex;\n");
        return lib;
    }

    /// <summary>The installed encrypted build, version 2026.1: Widget, and a Gadget the supplied copy lacks.</summary>
    private string WriteEncrypted()
    {
        var lib = Path.Combine(_root, "tool", "Claytex 2026.1");
        var help = Path.Combine(lib, "help");
        Directory.CreateDirectory(help);
        File.WriteAllText(Path.Combine(lib, "package.moe"), "not readable");

        static string Row(string simple) =>
            $"<tr><td><img src=\"Claytex.{simple}S.png\" alt=\"Claytex.{simple}\">&nbsp;" +
            $"<a href=\"Claytex.html#Claytex.{simple}\">{simple}</a></td><td>{simple}</td></tr>\n";
        static string Heading(string simple) =>
            $"<h2><img src=\"Claytex.{simple}I.png\" alt=\"Claytex.{simple}\">" +
            $"<a name=\"Claytex.{simple}\"></a>{simple}</h2>";

        File.WriteAllText(Path.Combine(help, "Claytex.html"),
            "<html><head><meta name=\"HTML-Generator\" content=\"Dymola\"></head><body>" +
            "<h2><a name=\"Claytex\"></a>Claytex</h2>" +
            "<p><span class=\"ModelicaDescription\">Claytex encrypted</span></p>" +
            "<h3>Package Content</h3>" +
            "<table summary=\"Package Content\" class=\"ModelicaTablePackageContent\">\n" +
            "<tr><th>Name</th><th>Description</th></tr>\n" +
            Row("Widget") + Row("Gadget") +
            "</table>" +
            Heading("Widget") + Heading("Gadget") +
            "</body></html>");
        return lib;
    }

    // ---------------------------------------------------------------- loading one

    [Fact]
    public async Task ASuppliedLibrary_IsReadOnly_WithItsNameAndVersion()
    {
        var service = new LibraryDataService();

        var library = await service.AddLibraryFromSourceAsync(new Source());

        Assert.Same(library, Assert.Single(service.Libraries));
        Assert.Equal("Claytex", library.Name);
        Assert.Equal("2026.1", library.Version);
        Assert.Equal(LibrarySourceType.Supplied, library.SourceType);
        Assert.Equal(ReadOnlySourceKind.Supplied, library.ReadOnlySource);
        Assert.True(library.IsReadOnly);
        Assert.Equal("mlqt-readonly://Claytex", library.SourcePath);
        Assert.Equal(["Claytex", "Claytex.Widget"], library.ModelIds.Order());
    }

    [Fact]
    public async Task ItsClassesAreStubs_OwnedByIt_InFilesNothingWrites()
    {
        var service = new LibraryDataService();
        var library = await service.AddLibraryFromSourceAsync(new Source());

        var widget = service.GetModelById("Claytex.Widget")!;
        Assert.True(widget.IsExternalStub);
        Assert.StartsWith("// From a test host - not the vendor's source.\n", widget.Definition.ModelicaCode);
        Assert.Same(library, service.GetOwningLibrary("Claytex.Widget"));
        var file = service.CombinedGraph.GetNode<FileNode>(widget.ContainingFileId!)!;
        Assert.True(ReadOnlySources.IsReadOnlyPath(file.FilePath));
        Assert.False(File.Exists(file.FilePath));
    }

    [Fact]
    public async Task RemovingIt_TakesItsInMemoryFilesToo()
    {
        var service = new LibraryDataService();
        var library = await service.AddLibraryFromSourceAsync(new Source());

        service.RemoveLibrary(library.Id);

        Assert.Empty(service.CombinedGraph.ModelNodes);
        Assert.DoesNotContain(service.CombinedGraph.FileNodes, f => ReadOnlySources.IsInMemoryPath(f.FilePath));
    }

    [Fact]
    public async Task ItIsNeverFormatted()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromSourceAsync(new Source());

        Assert.Empty(FormattableLibraries.Select(service.Libraries, new RepositoryService(
            service, new InMemorySettingsService(), new FileMonitoringService()), null));
    }

    [Fact]
    public async Task OnlyASuppliedSource_ComesInThisWay()
    {
        var detected = EncryptedLibraryDetector.Detect(WriteEncrypted())!;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new LibraryDataService().AddLibraryFromSourceAsync(new EncryptedDirectoryClassSource(detected)));
    }

    [Fact]
    public async Task ItsVersion_IsAlsoReadFromAReadableLibrarysAnnotation()
    {
        var lib = Path.Combine(_root, "versioned", "V");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"),
            "package V\n  annotation (version = \"4.1.0\");\nend V;\n");

        var library = await new LibraryDataService().AddLibraryFromDirectoryAsync(lib);

        Assert.Equal("4.1.0", library.Version);
        Assert.False(library.IsReadOnly);
    }

    // ---------------------------------------------------------------- precedence

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadableSource_Outranks_ASuppliedCopy(bool suppliedFirst)
    {
        var service = new LibraryDataService();
        var source = new Source();

        if (suppliedFirst)
            await service.AddLibraryFromSourceAsync(source);
        await service.AddLibraryFromPathAsync(WriteSource());
        var supplied = suppliedFirst ? null : await service.AddLibraryFromSourceAsync(source);

        var only = Assert.Single(service.Libraries);
        Assert.False(only.IsReadOnly);
        Assert.DoesNotContain(service.CombinedGraph.ModelNodes, m => m.IsExternalStub);
        if (supplied is not null)
        {
            // Asked before reading: nothing was decrypted only to be thrown away.
            Assert.Equal(0, source.Reads);
            Assert.Equal(only.SourcePath, supplied.SupersededBy);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ASuppliedCopy_Outranks_ItsInstalledEncryptedBuild(bool suppliedFirst)
    {
        var service = new LibraryDataService();

        if (suppliedFirst)
            await service.AddLibraryFromSourceAsync(new Source());
        var encrypted = await service.AddLibraryFromPathAsync(WriteEncrypted());
        if (!suppliedFirst)
            await service.AddLibraryFromSourceAsync(new Source());

        var only = Assert.Single(service.Libraries);
        Assert.Equal(ReadOnlySourceKind.Supplied, only.ReadOnlySource);
        Assert.Equal(only.SourcePath, encrypted.SupersededBy);
        // Whole, not class by class: the encrypted build's Gadget is not left behind.
        Assert.Null(service.GetModelById("Claytex.Gadget"));
        Assert.Equal(ReadOnlySourceKind.Supplied, ReadOnlySources.KindOf(service.GetModelById("Claytex.Widget")!));
        Assert.DoesNotContain(service.CombinedGraph.FileNodes, f => ExternalStubBuilder.IsEncryptedPackageFile(f.FilePath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ASuppliedCopyOfAnotherRelease_GivesWayToTheInstalledBuild(bool suppliedFirst)
    {
        var service = new LibraryDataService();
        var stale = new Source(version: "2025.2");

        var supplied = suppliedFirst ? await service.AddLibraryFromSourceAsync(stale) : null;
        await service.AddLibraryFromPathAsync(WriteEncrypted());
        supplied ??= await service.AddLibraryFromSourceAsync(stale);

        var only = Assert.Single(service.Libraries);
        Assert.Equal(ReadOnlySourceKind.RecoveredFromDocumentation, only.ReadOnlySource);
        Assert.Equal(only.SourcePath, supplied.SupersededBy);
        Assert.NotNull(service.GetModelById("Claytex.Gadget"));
        Assert.Equal(ReadOnlySourceKind.RecoveredFromDocumentation,
            ReadOnlySources.KindOf(service.GetModelById("Claytex.Widget")!));
    }

    // ---------------------------------------------------------------- the rule on its own

    private static LoadedLibrary Library(ReadOnlySourceKind? kind, string? version = null) => new()
    {
        Name = "Claytex",
        Version = version,
        SourceType = kind switch
        {
            ReadOnlySourceKind.RecoveredFromDocumentation => LibrarySourceType.EncryptedDirectory,
            ReadOnlySourceKind.Supplied => LibrarySourceType.Supplied,
            _ => LibrarySourceType.Directory
        },
        ReadOnlySource = kind
    };

    [Fact]
    public void TheRank_IsSourceThenSuppliedThenRecovered()
    {
        var source = Library(null);
        var supplied = Library(ReadOnlySourceKind.Supplied);
        var recovered = Library(ReadOnlySourceKind.RecoveredFromDocumentation);

        Assert.True(SourceSupersedesEncrypted.Outranks(source, supplied));
        Assert.True(SourceSupersedesEncrypted.Outranks(supplied, recovered));
        Assert.True(SourceSupersedesEncrypted.Outranks(source, recovered));
        Assert.False(SourceSupersedesEncrypted.Outranks(supplied, source));
        Assert.False(SourceSupersedesEncrypted.Outranks(recovered, supplied));
        Assert.False(SourceSupersedesEncrypted.Outranks(supplied, Library(ReadOnlySourceKind.Supplied)));
    }

    [Theory]
    [InlineData("2026.1", "2026.1", true)]
    [InlineData("2025.2", "2026.1", false)]
    [InlineData(null, "2026.1", true)]
    [InlineData("2025.2", null, true)]
    public void ASuppliedCopy_OutranksARecoveredOne_UnlessTheyNameDifferentReleases(
        string? suppliedVersion, string? recoveredVersion, bool suppliedWins)
    {
        var supplied = Library(ReadOnlySourceKind.Supplied, suppliedVersion);
        var recovered = Library(ReadOnlySourceKind.RecoveredFromDocumentation, recoveredVersion);

        Assert.Equal(suppliedWins, SourceSupersedesEncrypted.Outranks(supplied, recovered));
        Assert.Equal(!suppliedWins, SourceSupersedesEncrypted.Outranks(recovered, supplied));
    }

    [Fact]
    public void ReadableSource_OutranksEitherWhateverItsVersion()
    {
        var source = Library(null, "1.0");

        Assert.True(SourceSupersedesEncrypted.Outranks(source, Library(ReadOnlySourceKind.Supplied, "2.0")));
        Assert.True(SourceSupersedesEncrypted.Outranks(source, Library(ReadOnlySourceKind.RecoveredFromDocumentation, "2.0")));
    }

    [Fact]
    public void AnEncryptedDirectory_IsRecovered_WithoutBeingTold()
    {
        var library = new LoadedLibrary { SourceType = LibrarySourceType.EncryptedDirectory };

        Assert.Equal(ReadOnlySourceKind.RecoveredFromDocumentation, library.ReadOnlySource);
        Assert.True(library.IsReadOnly);
        Assert.False(new LoadedLibrary { SourceType = LibrarySourceType.Git }.IsReadOnly);
    }
}
