using MLQT.Services;
using MLQT.Services.DataTypes;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using Xunit;

namespace MLQT.Services.Tests.ExternalDocs;

/// <summary>
/// An encrypted library is never loaded beside readable source for the same library, and when the
/// source wins it wins whole (B268, WP15).
/// </summary>
/// <remarks>
/// <para>The fixture's two copies are deliberately <b>different releases</b>, which is the ordinary
/// case: the tool's encrypted build documents a <c>Gadget</c> that the checkout has since deleted.
/// Merging the copies class by class — which is what happened before — kept <c>Gadget</c> as a stub
/// inside the user's own library, where the tree showed it and a reference to it resolved instead of
/// being reported as broken.</para>
/// </remarks>
public class SourceSupersedesEncryptedTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-supersedes", Guid.NewGuid().ToString("N"));

    public SourceSupersedesEncryptedTests() => Directory.CreateDirectory(_root);

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

    /// <summary>The checkout: the package and its Widget. No Gadget — this release deleted it.</summary>
    private string WriteSource(string name, string parent = "source")
    {
        var lib = Path.Combine(_root, parent, name);
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"),
            $"package {name} \"{name} from source\"\n" +
            "  model Widget \"A widget\"\n  end Widget;\n" +
            $"end {name};\n");
        File.WriteAllText(Path.Combine(lib, "package.order"), "Widget\n");
        return lib;
    }

    /// <summary>The tool's encrypted build of an older release: Widget, and the Gadget since deleted.
    /// <paramref name="classesOf"/> lets the documented classes belong to a different top-level name
    /// than the directory, which is how a test gets two libraries claiming the same ids without the
    /// supersession rule stepping in.</summary>
    private string WriteEncrypted(string name, string parent = "tool", string? classesOf = null)
    {
        var pkg = classesOf ?? name;
        var lib = Path.Combine(_root, parent, $"{name} 2026.1");
        var help = Path.Combine(lib, "help");
        Directory.CreateDirectory(help);
        File.WriteAllText(Path.Combine(lib, "package.moe"), "not readable");

        string Row(string simple) =>
            $"<tr><td><img src=\"{pkg}.{simple}S.png\" alt=\"{pkg}.{simple}\">&nbsp;" +
            $"<a href=\"{pkg}.html#{pkg}.{simple}\">{simple}</a></td><td>{simple}</td></tr>\n";
        string Heading(string simple) =>
            $"<h2><img src=\"{pkg}.{simple}I.png\" alt=\"{pkg}.{simple}\">" +
            $"<a name=\"{pkg}.{simple}\"></a>{simple}</h2>";

        File.WriteAllText(Path.Combine(help, $"{pkg}.html"),
            "<html><head><meta name=\"HTML-Generator\" content=\"Dymola\"></head><body>" +
            $"<h2><a name=\"{pkg}\"></a>{pkg}</h2>" +
            $"<p><span class=\"ModelicaDescription\">{pkg} encrypted</span></p>" +
            "<h3>Package Content</h3>" +
            "<table summary=\"Package Content\" class=\"ModelicaTablePackageContent\">\n" +
            "<tr><th>Name</th><th>Description</th></tr>\n" +
            Row("Widget") + Row("Gadget") +
            "</table>" +
            Heading("Widget") + Heading("Gadget") +
            "</body></html>");
        return lib;
    }

    private async Task<LibraryDataService> LoadBothAsync(bool encryptedFirst)
    {
        var service = new LibraryDataService();
        var encrypted = WriteEncrypted("Claytex");
        var source = WriteSource("Claytex");

        await service.AddLibraryFromPathAsync(encryptedFirst ? encrypted : source);
        await service.AddLibraryFromPathAsync(encryptedFirst ? source : encrypted);
        return service;
    }

    // ---------------------------------------------------------------- the rule on its own

    private static LoadedLibrary Library(string name, bool encrypted) => new()
    {
        Name = name,
        SourcePath = Path.Combine(Path.GetTempPath(), name),
        SourceType = encrypted ? LibrarySourceType.EncryptedDirectory : LibrarySourceType.Directory,
    };

    [Fact]
    public void ReadableSourceArriving_RetiresTheEncryptedCopy()
    {
        var encrypted = Library("Claytex", encrypted: true);
        var other = Library("Suspensions", encrypted: true);

        var retired = SourceSupersedesEncrypted.Retires(Library("Claytex", encrypted: false), [encrypted, other]);

        Assert.Same(encrypted, Assert.Single(retired));
    }

    [Fact]
    public void AnEncryptedCopyArrivingAfterSource_RetiresItself()
    {
        var arriving = Library("Claytex", encrypted: true);

        var retired = SourceSupersedesEncrypted.Retires(arriving, [Library("Claytex", encrypted: false)]);

        Assert.Same(arriving, Assert.Single(retired));
    }

    [Fact]
    public void NothingIsRetiredWithoutASameNamedCounterpart()
    {
        // Every other vendor library in the folder: the ordinary case, and the one that must not
        // lose anything to this rule.
        Assert.Empty(SourceSupersedesEncrypted.Retires(
            Library("Claytex", encrypted: true), [Library("Suspensions", encrypted: false)]));
        Assert.Empty(SourceSupersedesEncrypted.Retires(
            Library("Claytex", encrypted: false), [Library("Suspensions", encrypted: true)]));
    }

    [Fact]
    public void TwoCopiesOfTheSameKind_AreNotThisRulesBusiness()
    {
        Assert.Empty(SourceSupersedesEncrypted.Retires(
            Library("Claytex", encrypted: false), [Library("Claytex", encrypted: false)]));
        Assert.Empty(SourceSupersedesEncrypted.Retires(
            Library("Claytex", encrypted: true), [Library("Claytex", encrypted: true)]));
    }

    [Theory]
    [InlineData("Claytex", "Claytex", true)]
    [InlineData("Claytex", "claytex", false)]   // Modelica names are case-sensitive
    [InlineData("Claytex", "ClaytexExtra", false)]
    [InlineData("", "", false)]                 // an unknown name matches nothing, even itself
    [InlineData(null, null, false)]
    public void SameLibrary_IsTheExactTopLevelName(string? a, string? b, bool same)
    {
        Assert.Equal(same, SourceSupersedesEncrypted.SameLibrary(a, b));
    }

    [Fact]
    public void ReadableSourceFor_NamesWhereTheSourceIs()
    {
        var readable = new[] { ("Suspensions", "/a"), ("Claytex", "/b") };

        Assert.Equal("/b", SourceSupersedesEncrypted.ReadableSourceFor("Claytex", readable));
        Assert.Null(SourceSupersedesEncrypted.ReadableSourceFor("VeSyMA", readable));
        Assert.Null(SourceSupersedesEncrypted.ReadableSourceFor(null, readable));
    }

    // ---------------------------------------------------------------- through LibraryDataService

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyTheSourceLibraryIsLoaded_WhicheverArrivesFirst(bool encryptedFirst)
    {
        var service = await LoadBothAsync(encryptedFirst);

        var only = Assert.Single(service.Libraries);
        Assert.Equal("Claytex", only.Name);
        Assert.NotEqual(LibrarySourceType.EncryptedDirectory, only.SourceType);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AClassTheSourceDeleted_IsNotLeftBehindAsAStub(bool encryptedFirst)
    {
        // The reason the rule is per library and not per class. With the encrypted build loaded
        // first, Gadget was stubbed before the source arrived and nothing ever took it out; with the
        // source first, the stub builder skipped Widget and added Gadget anyway.
        var service = await LoadBothAsync(encryptedFirst);

        Assert.Null(service.GetModelById("Claytex.Gadget"));
        Assert.False(service.GetModelById("Claytex.Widget")!.IsExternalStub);
        Assert.DoesNotContain(service.CombinedGraph.ModelNodes, m => m.IsExternalStub);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheIndexIsTrue(bool encryptedFirst)
    {
        // What B268 was about: the index claiming classes the library does not supply. Every id a
        // library lists is in the graph, is that library's kind of node, and no other library lists it.
        var service = await LoadBothAsync(encryptedFirst);

        var claims = service.Libraries.SelectMany(l => l.ModelIds.Select(id => (Library: l, Id: id))).ToList();
        Assert.Equal(["Claytex", "Claytex.Widget"], claims.Select(c => c.Id).Order(StringComparer.Ordinal));
        Assert.All(claims, c =>
        {
            var node = service.GetModelById(c.Id);
            Assert.NotNull(node);
            Assert.Equal(c.Library.SourceType == LibrarySourceType.EncryptedDirectory, node!.IsExternalStub);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheEncryptedPackageLeavesTheGraphWithIt(bool encryptedFirst)
    {
        // A file node for a vendor's package.moe with nothing in it is one more path every write has
        // to remember not to take at face value.
        var service = await LoadBothAsync(encryptedFirst);

        Assert.DoesNotContain(service.CombinedGraph.FileNodes,
            f => ExternalStubBuilder.IsEncryptedPackageFile(f.FilePath));
    }

    [Fact]
    public async Task AnEncryptedCopyRetiredOnArrival_ComesBackEmpty()
    {
        // The caller gets the library object back either way. Empty is what makes the reference-
        // library loader's "contributed nothing" branch take it, instead of counting it as loaded.
        var service = new LibraryDataService();
        await service.AddLibraryFromPathAsync(WriteSource("Claytex"));

        var encrypted = await service.AddLibraryFromPathAsync(WriteEncrypted("Claytex"));

        Assert.Empty(encrypted.ModelIds);
        Assert.Empty(encrypted.TopLevelModelIds);
        Assert.DoesNotContain(service.Libraries, l => l.Id == encrypted.Id);
    }

    [Fact]
    public async Task AnEncryptedCopyOfLoadedSource_IsNotEvenRead()
    {
        // The CLI's --dependency folder and the MCP server's load_library come straight to the loader,
        // with no discovery pass in front to skip it. The loader asks before reading the help HTML,
        // so a library whose source is checked out costs a directory probe rather than a parse of its
        // documentation and a graph of stubs built to be thrown away.
        var service = new LibraryDataService();
        var source = WriteSource("Claytex");
        await service.AddLibraryFromPathAsync(source);

        var encrypted = await service.AddLibraryFromPathAsync(WriteEncrypted("Claytex"));

        Assert.Null(encrypted.DocumentedClassCount);   // the documentation was never read
        Assert.Equal(source, encrypted.SupersededBy);
        Assert.DoesNotContain(service.Libraries, l => l.Id == encrypted.Id);
        Assert.DoesNotContain(service.CombinedGraph.FileNodes,
            f => ExternalStubBuilder.IsEncryptedPackageFile(f.FilePath));
    }

    [Fact]
    public async Task AnEncryptedLibraryWithNoSourceCopy_IsUntouched()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromPathAsync(WriteSource("Suspensions"));

        var encrypted = await service.AddLibraryFromPathAsync(WriteEncrypted("Claytex"));

        Assert.Contains(service.Libraries, l => l.Id == encrypted.Id);
        Assert.True(service.GetModelById("Claytex.Gadget")!.IsExternalStub);
    }

    [Fact]
    public async Task RemovingALibrary_NeverTakesAnotherLibrarysNodes()
    {
        // The hazard the old RemoveLibrary carried: it removed every id the library listed, and an
        // encrypted library that lost classes to source still listed them — so removing the vendor
        // copy deleted the user's own classes from the graph. Reached here by an encrypted library
        // whose directory is named differently from the classes it documents, which the name rule
        // cannot see, so the two really do both list Claytex.Widget.
        var service = new LibraryDataService();
        var vendor = await service.AddLibraryFromPathAsync(WriteEncrypted("Vendor", classesOf: "Claytex"));
        await service.AddLibraryFromPathAsync(WriteSource("Claytex"));
        Assert.Contains("Claytex.Widget", vendor.ModelIds);

        service.RemoveLibrary(vendor.Id);

        Assert.False(service.GetModelById("Claytex.Widget")!.IsExternalStub);
        Assert.NotNull(service.GetModelById("Claytex"));
        Assert.Null(service.GetModelById("Claytex.Gadget"));   // the vendor's own class does go
    }

    // ---------------------------------------------------------------- through RepositoryService

    private static RepositoryService Repositories(LibraryDataService libraries) =>
        new(libraries, new InMemorySettingsService(), new FileMonitoringService());

    [Fact]
    public async Task AProjectHoldingBothCopies_NeverLoadsTheEncryptedOne()
    {
        // The reported shape: a tool's library folder and the checkout as two repositories of one
        // project, loaded in the same parallel pass. The encrypted build is skipped before it is read,
        // from the names discovery already has, so its load cannot race the source's at all.
        var libraries = new LibraryDataService();
        var repositories = Repositories(libraries);
        var tool = Path.Combine(_root, "tool");
        var checkout = Path.Combine(_root, "checkout");
        WriteEncrypted("Claytex", parent: "tool");
        WriteEncrypted("Suspensions", parent: "tool");
        WriteSource("Claytex", parent: "checkout");

        var toolRepo = (await repositories.AddRepositoryAsync(tool, checkoutPath: null, startMonitoring: false)).Repository!;
        var checkoutRepo = (await repositories.AddRepositoryAsync(checkout, checkoutPath: null, startMonitoring: false)).Repository!;
        await Task.WhenAll(
            repositories.LoadLibrariesAsync(toolRepo.Id),
            repositories.LoadLibrariesAsync(checkoutRepo.Id));

        Assert.Equal(
            [("Claytex", false), ("Suspensions", true)],
            libraries.Libraries
                .Select(l => (l.Name, l.SourceType == LibrarySourceType.EncryptedDirectory))
                .OrderBy(l => l.Name, StringComparer.Ordinal));
        Assert.Null(libraries.GetModelById("Claytex.Gadget"));
    }

    [Fact]
    public async Task SourceAddedMidSession_RetiresTheEncryptedCopyAlreadyLoaded()
    {
        // The other route in: the encrypted build is loaded on its own, and a checkout is added
        // afterwards. Nothing was skipped up front, so it is the registration that has to retire it.
        var libraries = new LibraryDataService();
        var repositories = Repositories(libraries);
        WriteEncrypted("Claytex", parent: "tool");
        var toolRepo = (await repositories.AddRepositoryAsync(
            Path.Combine(_root, "tool"), checkoutPath: null, startMonitoring: false)).Repository!;
        await repositories.LoadLibrariesAsync(toolRepo.Id);
        Assert.True(libraries.GetModelById("Claytex.Gadget")!.IsExternalStub);

        WriteSource("Claytex", parent: "checkout");
        var checkoutRepo = (await repositories.AddRepositoryAsync(
            Path.Combine(_root, "checkout"), checkoutPath: null, startMonitoring: false)).Repository!;
        await repositories.LoadLibrariesAsync(checkoutRepo.Id);

        var only = Assert.Single(libraries.Libraries);
        Assert.NotEqual(LibrarySourceType.EncryptedDirectory, only.SourceType);
        Assert.Null(libraries.GetModelById("Claytex.Gadget"));
    }

    [Fact]
    public async Task RemovingARepository_OffersTheProjectForReloading_UntilItIsLoaded()
    {
        // Removing the checkout does not bring the encrypted build back - it was never loaded, or was
        // retired. The flag is what puts Load project on the active project's row, and loading the
        // project is what clears it.
        var libraries = new LibraryDataService();
        var repositories = Repositories(libraries);
        await repositories.LoadRepositorySettingsAsync();   // an active project to reload
        WriteSource("Claytex", parent: "checkout");
        var checkout = (await repositories.AddRepositoryAsync(
            Path.Combine(_root, "checkout"), checkoutPath: null, startMonitoring: false)).Repository!;
        Assert.False(repositories.RepositoryRemovedSinceProjectLoad);

        repositories.RemoveRepository(checkout.Id);
        Assert.True(repositories.RepositoryRemovedSinceProjectLoad);

        await repositories.SwitchProjectAsync(repositories.GetActiveProject()!.Id);
        Assert.False(repositories.RepositoryRemovedSinceProjectLoad);
    }

    [Fact]
    public async Task ABackedOutAdd_DoesNotOfferAReload()
    {
        // The add dialog's Cancel removes the repository it half-added without unloading anything,
        // so nothing that was standing in for anything has gone.
        var repositories = Repositories(new LibraryDataService());
        WriteSource("Claytex", parent: "checkout");
        var checkout = (await repositories.AddRepositoryAsync(
            Path.Combine(_root, "checkout"), checkoutPath: null, startMonitoring: false)).Repository!;

        repositories.RemoveRepository(checkout.Id, unloadLibraries: false);

        Assert.False(repositories.RepositoryRemovedSinceProjectLoad);
    }
}
