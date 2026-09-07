using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.StyleRules;
using MLQT.Services;
using MLQT.Services.Checking;
using MLQT.Services.Helpers;
using Xunit;
using ModelicaParser.Visitors;

namespace MLQT.Services.Tests.ExternalDocs;

/// <summary>
/// End-to-end tests over a synthetic encrypted library, built on disk in the shape Dymola ships:
/// a <c>package.moe</c> that cannot be read plus a <c>help/</c> directory that describes what is
/// inside it.
///
/// <para>These are the tests that state what the feature is <i>for</i>. Each one sets up the
/// situation that produced a false finding before — a reference into an encrypted namespace, a
/// class inheriting its icon across the boundary — and asserts the finding is gone. They use a
/// fixture rather than an installed Dymola so they run everywhere, on every build.</para>
/// </summary>
public class EncryptedLibraryEndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-encrypted-e2e", Guid.NewGuid().ToString("N"));

    public EncryptedLibraryEndToEndTests() => Directory.CreateDirectory(_root);

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

    /// <summary>
    /// Writes an encrypted library "Vendor 1.2.0" documenting three classes:
    /// <c>Vendor.Icons.Boxed</c> (has an icon), <c>Vendor.Interfaces.Base</c> (extends Boxed, so it
    /// inherits one) and <c>Vendor.Plain</c> (documented as having no icon).
    /// </summary>
    private string WriteEncryptedLibrary()
    {
        var library = Path.Combine(_root, "Vendor 1.2.0");
        var help = Path.Combine(library, "help");
        Directory.CreateDirectory(help);
        File.WriteAllText(Path.Combine(library, "package.moe"), "not readable");

        const string head =
            "<html><head><meta name=\"HTML-Generator\" content=\"Dymola\"></head><body>";

        // The root page, listing the sub-packages with their thumbnails.
        File.WriteAllText(Path.Combine(help, "Vendor.html"), head +
            "<h2><a name=\"Vendor\"></a>Vendor</h2>" +
            "<p><span class=\"ModelicaDescription\">A vendor library</span></p>" +
            "<h3>Package Content</h3>" +
            "<table summary=\"Package Content\" class=\"ModelicaTablePackageContent\">\n" +
            "<tr><th>Name</th><th>Description</th></tr>\n" +
            "<tr><td><img src=\"Vendor.IconsS.png\" alt=\"Vendor.Icons\">&nbsp;" +
            "<a href=\"Vendor_Icons.html#Vendor.Icons\">Icons</a></td><td>Icons</td></tr>\n" +
            "<tr><td><img src=\"Vendor.InterfacesS.png\" alt=\"Vendor.Interfaces\">&nbsp;" +
            "<a href=\"Vendor_Interfaces.html#Vendor.Interfaces\">Interfaces</a></td><td>Interfaces</td></tr>\n" +
            "<tr><td><img src=\"Vendor.PlainS.png\" alt=\"Vendor.Plain\">&nbsp;" +
            "<a href=\"Vendor.html#Vendor.Plain\">Plain</a></td><td>Plain thing</td></tr>\n" +
            "</table>" +
            "<h2><a name=\"Vendor.Plain\"></a>Plain</h2>" +
            "<p><span class=\"ModelicaDescription\">Plain thing</span></p>" +
            "</body></html>");

        File.WriteAllText(Path.Combine(help, "Vendor_Icons.html"), head +
            "<h2><a name=\"Vendor.Icons\"></a>Icons</h2>" +
            "<h2><img src=\"Vendor.Icons.BoxedI.png\" alt=\"Vendor.Icons.Boxed\">" +
            "<a name=\"Vendor.Icons.Boxed\"></a>Boxed</h2>" +
            "<p><span class=\"ModelicaDescription\">Boxed icon</span></p>" +
            "</body></html>");

        File.WriteAllText(Path.Combine(help, "Vendor_Interfaces.html"), head +
            "<h2><a name=\"Vendor.Interfaces\"></a>Interfaces</h2>" +
            "<h2><img src=\"Vendor.Interfaces.BaseI.png\" alt=\"Vendor.Interfaces.Base\">" +
            "<a name=\"Vendor.Interfaces.Base\"></a>Base</h2>" +
            "<p><span class=\"ModelicaDescription\">Base interface</span></p>" +
            "<p><span class=\"ModelicaBaseClass\">Extends from " +
            "<a href=\"Vendor_Icons.html#Vendor.Icons.Boxed\">Vendor.Icons.Boxed</a> (Boxed icon)." +
            "</span></p>" +
            "</body></html>");

        return library;
    }

    /// <summary>Writes a small readable library whose one model leans on the encrypted one.</summary>
    private string WriteUserLibrary(string modelBody)
    {
        var library = Path.Combine(_root, "MyLib");
        Directory.CreateDirectory(library);
        File.WriteAllText(Path.Combine(library, "package.mo"),
            "package MyLib \"My library\"\n" +
            "  annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));\n" +
            "end MyLib;\n");
        File.WriteAllText(Path.Combine(library, "package.order"), "Widget\n");
        File.WriteAllText(Path.Combine(library, "Widget.mo"), modelBody);
        return library;
    }

    private static StyleCheckingSettings CheckSettings() => new()
    {
        ClassHasIcon = true,
        ValidateModelReferences = true
    };

    private static IReadOnlyList<ModelicaParser.DataTypes.Finding> Check(
        DirectedGraph graph, IEnumerable<ModelNode> models, StyleCheckingSettings settings) =>
        LibraryCheckSession.Check(
            graph, models, settings, new CustomDictionaryService(), new DictionaryManagerService());

    #region Discovery and loading

    [Fact]
    public void LibraryDiscovery_FindsAnEncryptedLibrary()
    {
        WriteEncryptedLibrary();
        WriteUserLibrary("within MyLib;\nmodel Widget \"W\"\nend Widget;\n");

        var found = LibraryDiscovery.DiscoverLibraryPaths(_root);

        Assert.Contains(found, path => Path.GetFileName(path) == "Vendor 1.2.0");
        Assert.Contains(found, path => Path.GetFileName(path) == "MyLib");
    }

    [Fact]
    public async Task AddEncryptedLibrary_RecoversClassesAndVersion()
    {
        var service = new LibraryDataService();

        var library = await service.AddEncryptedLibraryFromDirectoryAsync(WriteEncryptedLibrary());

        Assert.Equal("Vendor", library.Name);
        Assert.Equal(
            ["Vendor", "Vendor.Icons", "Vendor.Icons.Boxed", "Vendor.Interfaces",
             "Vendor.Interfaces.Base", "Vendor.Plain"],
            library.ModelIds.OrderBy(id => id, StringComparer.Ordinal));

        // Stamped from the versioned directory name, so the dependency-version check can see it.
        Assert.Equal("1.2.0", service.GetModelById("Vendor")!.Version);
        Assert.All(library.ModelIds, id => Assert.True(service.GetModelById(id)!.IsExternalStub));
    }

    [Fact]
    public async Task AddEncryptedLibrary_WithoutDocumentation_LoadsNoClassesRatherThanAnEmptyLibrary()
    {
        // Claiming the library is empty would turn every reference into it into a fabricated
        // broken-reference finding. Recovering nothing leaves the namespace opaque instead.
        var library = Path.Combine(_root, "Opaque 1.0.0");
        Directory.CreateDirectory(library);
        File.WriteAllText(Path.Combine(library, "package.moe"), "not readable");

        var service = new LibraryDataService();
        var loaded = await service.AddEncryptedLibraryFromDirectoryAsync(library);

        Assert.Empty(loaded.ModelIds);
        Assert.Equal("Opaque", loaded.Name);
    }

    #endregion

    #region The false findings this feature removes

    [Fact]
    public async Task ModelReferenceIntoAnEncryptedLibrary_IsNoLongerReportedAsBroken()
    {
        var service = new LibraryDataService();
        await service.AddEncryptedLibraryFromDirectoryAsync(WriteEncryptedLibrary());
        var userLibrary = await service.AddLibraryFromDirectoryAsync(WriteUserLibrary(
            "within MyLib;\n" +
            "model Widget \"A widget\"\n" +
            "  annotation (\n" +
            "    Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}),\n" +
            "    Documentation(info=\"<html><p>See " +
            "<a href=\\\"modelica://Vendor.Interfaces.Base\\\">Base</a>.</p></html>\"));\n" +
            "end Widget;\n"));

        var models = userLibrary.ModelIds.Select(service.GetModelById!).ToList();
        var findings = Check(service.CombinedGraph, models!, CheckSettings());

        Assert.DoesNotContain(findings, f => f.RuleId == RuleIds.ModelReferences);
    }

    [Fact]
    public async Task ModelReferenceToSomethingNotInTheDocumentation_IsStillReported()
    {
        // The feature must not become a blanket amnesty: a name inside a namespace we do have
        // documentation for, that the documentation does not contain, is still worth reporting.
        var service = new LibraryDataService();
        await service.AddEncryptedLibraryFromDirectoryAsync(WriteEncryptedLibrary());
        var userLibrary = await service.AddLibraryFromDirectoryAsync(WriteUserLibrary(
            "within MyLib;\n" +
            "model Widget \"A widget\"\n" +
            "  annotation (\n" +
            "    Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}),\n" +
            "    Documentation(info=\"<html><p>See " +
            "<a href=\\\"modelica://Vendor.Interfaces.Nonexistent\\\">X</a>.</p></html>\"));\n" +
            "end Widget;\n"));

        var models = userLibrary.ModelIds.Select(service.GetModelById!).ToList();
        var findings = Check(service.CombinedGraph, models!, CheckSettings());

        Assert.Contains(findings, f => f.RuleId == RuleIds.ModelReferences);
    }

    [Fact]
    public async Task IconInheritedFromAnEncryptedBaseClass_SatisfiesTheIconRule()
    {
        // The headline false positive: a class with no Icon annotation of its own, inheriting one
        // from a base MLQT cannot read, used to be reported as missing an icon.
        var service = new LibraryDataService();
        await service.AddEncryptedLibraryFromDirectoryAsync(WriteEncryptedLibrary());
        var userLibrary = await service.AddLibraryFromDirectoryAsync(WriteUserLibrary(
            "within MyLib;\n" +
            "model Widget \"A widget\"\n" +
            "  extends Vendor.Interfaces.Base;\n" +
            "end Widget;\n"));

        var models = userLibrary.ModelIds.Select(service.GetModelById!).ToList();
        var findings = Check(service.CombinedGraph, models!, CheckSettings());

        Assert.DoesNotContain(findings,
            f => f.RuleId == RuleIds.ClassIcon && f.ModelId == "MyLib.Widget");
    }

    [Fact]
    public async Task IconRule_StillFiresWhenTheEncryptedBaseIsDocumentedAsHavingNoIcon()
    {
        // The other direction: documentation that positively says "no icon" is believed, so the
        // rule keeps working across the boundary rather than being silenced by it.
        var service = new LibraryDataService();
        await service.AddEncryptedLibraryFromDirectoryAsync(WriteEncryptedLibrary());
        var userLibrary = await service.AddLibraryFromDirectoryAsync(WriteUserLibrary(
            "within MyLib;\n" +
            "model Widget \"A widget\"\n" +
            "  extends Vendor.Plain;\n" +
            "end Widget;\n"));

        var models = userLibrary.ModelIds.Select(service.GetModelById!).ToList();
        var findings = Check(service.CombinedGraph, models!, CheckSettings());

        Assert.Contains(findings,
            f => f.RuleId == RuleIds.ClassIcon && f.ModelId == "MyLib.Widget");
    }

    [Fact]
    public async Task EncryptedLibraryClasses_AreNeverThemselvesReported()
    {
        var service = new LibraryDataService();
        var vendor = await service.AddEncryptedLibraryFromDirectoryAsync(WriteEncryptedLibrary());

        // Even when handed the stubs directly, the rules must not produce findings about a
        // vendor's library — those are not the user's to fix.
        var stubs = vendor.ModelIds.Select(service.GetModelById!).ToList();
        var findings = Check(service.CombinedGraph, stubs!, CheckSettings());

        Assert.DoesNotContain(findings, f => f.ModelId.StartsWith("Vendor", StringComparison.Ordinal)
                                             && f.RuleId == RuleIds.ClassIcon);
    }

    #endregion

    #region Write-path guard

    [Fact]
    public async Task SavingAnEncryptedLibrary_IsRefused()
    {
        // The worst outcome this feature could have: MLQT rewriting a third-party library it
        // cannot read, replacing it with a reconstruction. The saver refuses rather than skipping,
        // so a caller that assembled the wrong model set fails here instead of on disk.
        var service = new LibraryDataService();
        var vendor = await service.AddEncryptedLibraryFromDirectoryAsync(WriteEncryptedLibrary());

        var output = Path.Combine(_root, "out");
        Directory.CreateDirectory(output);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
                service.CombinedGraph, vendor.ModelIds, output,
                showAnnotations: true, formatting: FormattingOptions.None));

        Assert.Contains("encrypted", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(output, "*", SearchOption.AllDirectories));
    }

    #endregion
}
