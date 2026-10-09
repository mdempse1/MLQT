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
    /// <summary>
    /// <paramref name="version"/> is what the source claims; <paramref name="annotated"/>, when set, is
    /// the <c>version</c> annotation its top-level package carries, which is what counts.
    /// </summary>
    private sealed class Source(
        string? version = "2026.1", string? annotated = null, string? resourceRoot = null,
        Action? onRead = null) : IReadOnlyClassSource
    {
        public int Reads { get; private set; }
        public string LibraryName => "Claytex";
        public string? LibraryVersion => version;
        public ReadOnlySourceKind Kind => ReadOnlySourceKind.Supplied;
        public string? Location => null;
        public string ProvenanceNote => "From a test host - not the vendor's source.";
        public string? ResourceRoot => resourceRoot;

        public ReadOnlySourceContent Read(CancellationToken cancellationToken)
        {
            Reads++;
            onRead?.Invoke();
            var annotation = annotated is null ? "" : $"  annotation (version = \"{annotated}\");\n";
            return new ReadOnlySourceContent
            {
                Texts =
                [
                    new SuppliedText("Claytex/package.mo",
                        "within;\npackage Claytex \"Claytex supplied\"\n  model Widget \"A widget\"\n" +
                        "    parameter Real k = 1 \"gain\";\n  end Widget;\n" + annotation + "end Claytex;\n")
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

    private static ModelNode Model(LibraryDataService service, string id)
    {
        var node = service.GetModelById(id);
        Assert.NotNull(node);
        return node;
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

        var widget = Model(service, "Claytex.Widget");
        Assert.True(widget.IsExternalStub);
        Assert.StartsWith("// From a test host - not the vendor's source.\n", widget.Definition.ModelicaCode);
        Assert.Same(library, service.GetOwningLibrary("Claytex.Widget"));
        Assert.NotNull(widget.ContainingFileId);
        var file = service.CombinedGraph.GetNode<FileNode>(widget.ContainingFileId);
        Assert.NotNull(file);
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
        var detected = EncryptedLibraryDetector.Detect(WriteEncrypted());
        Assert.NotNull(detected);

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

    [Fact]
    public async Task ACancelledLoad_LeavesNothingBehind()
    {
        // Cancelled while the source is being read: the load stops before its first class reaches
        // the graph, so nothing is left there that no library owns.
        using var cancellation = new CancellationTokenSource();
        var service = new LibraryDataService();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.AddLibraryFromSourceAsync(new Source(onRead: cancellation.Cancel), cancellation.Token));

        Assert.Empty(service.Libraries);
        Assert.Empty(service.CombinedGraph.ModelNodes);
        Assert.Empty(service.CombinedGraph.FileNodes);
    }

    [Fact]
    public async Task ItsResources_ResolveUnderTheResourceRootItNames()
    {
        var installed = Path.Combine(_root, "installed", "Claytex 2026.1");
        Directory.CreateDirectory(Path.Combine(installed, "Resources"));
        var data = Path.Combine(installed, "Resources", "data.txt");
        File.WriteAllText(data, "1 2 3");

        var withRoot = await ResolvedResourceAsync(new Source(resourceRoot: installed));
        var without = await ResolvedResourceAsync(new Source());

        Assert.Equal(Path.GetFullPath(data), withRoot.ResolvedPath);
        Assert.True(withRoot.FileExists);
        // The control: with no root the URI points into memory, where nothing can exist.
        Assert.False(without.FileExists);
    }

    private async Task<ResourceFileNode> ResolvedResourceAsync(Source source)
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromSourceAsync(source);

        var user = Path.Combine(_root, "user-" + Guid.NewGuid().ToString("N"), "MyLib");
        Directory.CreateDirectory(user);
        File.WriteAllText(Path.Combine(user, "package.mo"),
            "package MyLib\n  model Uses\n" +
            "    parameter String f = Modelica.Utilities.Files.loadResource(\"modelica://Claytex/Resources/data.txt\");\n" +
            "  end Uses;\nend MyLib;\n");
        await service.AddLibraryFromDirectoryAsync(user);
        await service.EnsureDependenciesAnalyzedAsync();

        return Assert.Single(service.CombinedGraph.ResourceFileNodes);
    }

    // ---------------------------------------------------------------- the version

    [Fact]
    public async Task TheTopLevelPackagesAnnotation_IsItsVersion_WhateverTheSourceClaims()
    {
        var library = await new LibraryDataService().AddLibraryFromSourceAsync(new Source(version: "2025.2", annotated: "2026.1"));

        Assert.Equal("2026.1", library.Version);
    }

    [Fact]
    public async Task WithNoAnnotation_TheSourcesClaimStands()
    {
        var library = await new LibraryDataService().AddLibraryFromSourceAsync(new Source(version: "2025.2"));

        Assert.Equal("2025.2", library.Version);
    }

    [Fact]
    public async Task AReadableLibrarysAnnotation_OutranksTheVersionInItsDirectoryName()
    {
        var annotated = Path.Combine(_root, "readable", "V 4.0.0");
        Directory.CreateDirectory(annotated);
        File.WriteAllText(Path.Combine(annotated, "package.mo"), "package V\n  annotation (version = \"4.1.0\");\nend V;\n");
        var plain = Path.Combine(_root, "readable", "W 2.0.0");
        Directory.CreateDirectory(plain);
        File.WriteAllText(Path.Combine(plain, "package.mo"), "package W\nend W;\n");

        var service = new LibraryDataService();

        Assert.Equal("4.1.0", (await service.AddLibraryFromDirectoryAsync(annotated)).Version);
        Assert.Equal("2.0.0", (await service.AddLibraryFromDirectoryAsync(plain)).Version);
    }

    [Fact]
    public async Task AnEncryptedLibrarysVersion_IsItsDirectoryName()
    {
        // It has no annotation anyone can read, so what the directory says is all there is.
        var library = await new LibraryDataService().AddLibraryFromPathAsync(WriteEncrypted());

        Assert.Equal("2026.1", library.Version);
    }

    [Fact]
    public void Resolve_PrefersTheAnnotation_AndSaysWhenTheClaimDisagrees()
    {
        Assert.Equal(("4.1.0", (string?)null), LibraryVersion.Resolve("4.1.0", "4.1.0"));
        Assert.Equal(("4.1.0", (string?)null), LibraryVersion.Resolve("4.1.0", null));
        Assert.Equal(("4.0.0", (string?)null), LibraryVersion.Resolve(null, "4.0.0"));
        Assert.Equal(((string?)null, (string?)null), LibraryVersion.Resolve(" ", null));

        var (version, conflict) = LibraryVersion.Resolve("4.1.0", "4.0.0");
        Assert.Equal("4.1.0", version);
        Assert.NotNull(conflict);
        Assert.Contains("4.0.0", conflict);
    }

    [Theory]
    [InlineData("Modelica 4.0.0", "4.0.0")]
    [InlineData("Battery 2.9.0.mo", "2.9.0")]
    [InlineData("My Library", null)]
    [InlineData("Modelica", null)]
    [InlineData("", null)]
    public void FromPathName_ReadsTheVersionedNameConvention(string name, string? version) =>
        Assert.Equal(version, LibraryVersion.FromPathName(name.Length == 0 ? name : Path.Combine(_rootStatic, name)));

    private static readonly string _rootStatic = Path.GetTempPath();

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
        Assert.Equal(ReadOnlySourceKind.Supplied, ReadOnlySources.KindOf(Model(service, "Claytex.Widget")));
        Assert.DoesNotContain(service.CombinedGraph.FileNodes, f => ExternalStubBuilder.IsEncryptedPackageFile(f.FilePath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ASuppliedCopyOfAnotherRelease_GivesWayToTheInstalledBuild(bool suppliedFirst)
    {
        var service = new LibraryDataService();
        var stale = new Source(version: "2025.2", annotated: "2025.2");

        var supplied = suppliedFirst ? await service.AddLibraryFromSourceAsync(stale) : null;
        await service.AddLibraryFromPathAsync(WriteEncrypted());
        supplied ??= await service.AddLibraryFromSourceAsync(stale);

        var only = Assert.Single(service.Libraries);
        Assert.Equal(ReadOnlySourceKind.RecoveredFromDocumentation, only.ReadOnlySource);
        Assert.Equal(only.SourcePath, supplied.SupersededBy);
        Assert.NotNull(service.GetModelById("Claytex.Gadget"));
        Assert.Equal(ReadOnlySourceKind.RecoveredFromDocumentation,
            ReadOnlySources.KindOf(Model(service, "Claytex.Widget")));
    }

    [Fact]
    public async Task TheVersionThatDecides_IsTheAnnotation_NotTheClaim()
    {
        // Claims another release, but its package says it is the one installed - and that is what
        // counts, so it is used.
        var service = new LibraryDataService();
        await service.AddLibraryFromPathAsync(WriteEncrypted());

        await service.AddLibraryFromSourceAsync(new Source(version: "2025.2", annotated: "2026.1"));

        Assert.Equal(ReadOnlySourceKind.Supplied, Assert.Single(service.Libraries).ReadOnlySource);
    }

    [Fact]
    public async Task ACopyAnnotatedForAnotherRelease_IsTurnedAwayBeforeAnyClassLoads()
    {
        // Claims the installed release, but its package says otherwise. It is read - that is the only
        // way to learn its version - and nothing of it reaches the graph: the installed build's
        // classes are all still there, Gadget included.
        var service = new LibraryDataService();
        await service.AddLibraryFromPathAsync(WriteEncrypted());
        var stale = new Source(version: "2026.1", annotated: "2025.2");

        var supplied = await service.AddLibraryFromSourceAsync(stale);

        Assert.Equal(1, stale.Reads);
        Assert.NotNull(supplied.SupersededBy);
        Assert.Equal(ReadOnlySourceKind.RecoveredFromDocumentation, Assert.Single(service.Libraries).ReadOnlySource);
        Assert.Equal(ReadOnlySourceKind.RecoveredFromDocumentation, ReadOnlySources.KindOf(Model(service, "Claytex.Widget")));
        Assert.NotNull(service.GetModelById("Claytex.Gadget"));
        Assert.DoesNotContain(service.CombinedGraph.FileNodes, f => ReadOnlySources.IsInMemoryPath(f.FilePath));
    }

    [Fact]
    public async Task ASecondSuppliedCopy_IsNotLoaded_AndTheFirstKeepsItsFiles()
    {
        var service = new LibraryDataService();
        var first = await service.AddLibraryFromSourceAsync(new Source());
        var secondSource = new Source();

        var second = await service.AddLibraryFromSourceAsync(secondSource);

        Assert.Same(first, Assert.Single(service.Libraries));
        Assert.Equal(first.SourcePath, second.SupersededBy);
        Assert.Equal(0, secondSource.Reads);
        Assert.Contains(service.CombinedGraph.FileNodes, f => ReadOnlySources.IsInMemoryPath(f.FilePath));
    }

    [Fact]
    public void AnEncryptedReferenceLibrary_BesideASuppliedCopy_IsLeftToPrecedence()
    {
        // Not "already loaded": a supplied copy describing another release gives way to the build
        // installed, and only the precedence rule knows the versions.
        var supplied = Library(ReadOnlySourceKind.Supplied, "2025.2");
        supplied.SourcePath = "mlqt-readonly://Claytex";
        var recovered = Library(ReadOnlySourceKind.RecoveredFromDocumentation, "2026.1");
        recovered.SourcePath = Path.Combine(_root, "elsewhere", "Claytex");
        var candidate = Path.Combine(_root, "tool", "Claytex 2026.1");

        Assert.Null(ReferenceLibraryRules.ReasonToSkip(candidate, isEncrypted: true, "Claytex", [supplied], true));
        // The control: beside another encrypted copy it is a second copy of something present.
        Assert.NotNull(ReferenceLibraryRules.ReasonToSkip(candidate, isEncrypted: true, "Claytex", [recovered], true));
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
