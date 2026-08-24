using ModelicaGraph;
using MLQT.Services;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using Xunit;

namespace MLQT.Services.Tests.ExternalDocs;

/// <summary>
/// An encrypted library sitting inside the repository being checked — a vendor library vendored
/// into the checkout, alongside the source that uses it.
///
/// <para>This is the path the desktop app takes, and it is a different one from the CLI's: the app
/// loads a repository's libraries through <see cref="RepositoryService"/>, which used to hand every
/// discovered library to the ordinary source loader. An encrypted library has no <c>.mo</c> files,
/// so that loader found nothing and produced an empty library — invisible in the tree, and silently
/// absent from reference resolution, which made every reference into it look broken. The CLI, going
/// through its own loader, resolved them fine, so the two disagreed on the finding count.</para>
/// </summary>
public class EncryptedLibraryInRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-encrypted-repo", Guid.NewGuid().ToString("N"));

    public EncryptedLibraryInRepositoryTests() => Directory.CreateDirectory(_root);

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
    /// A repository holding a readable library that extends into an encrypted one shipped beside it.
    /// </summary>
    private string WriteRepository()
    {
        var vendor = Path.Combine(_root, "Vendor 1.2.0");
        var help = Path.Combine(vendor, "help");
        Directory.CreateDirectory(help);
        File.WriteAllText(Path.Combine(vendor, "package.moe"), "not readable");

        const string head =
            "<html><head><meta name=\"HTML-Generator\" content=\"Dymola\"></head><body>";
        File.WriteAllText(Path.Combine(help, "Vendor.html"), head +
            "<h2><a name=\"Vendor\"></a>Vendor</h2>" +
            "<p><span class=\"ModelicaDescription\">A vendor library</span></p>" +
            "<h3>Package Content</h3>" +
            "<table summary=\"Package Content\" class=\"ModelicaTablePackageContent\">\n" +
            "<tr><th>Name</th><th>Description</th></tr>\n" +
            "<tr><td><img src=\"Vendor.BaseS.png\" alt=\"Vendor.Base\">&nbsp;" +
            "<a href=\"Vendor.html#Vendor.Base\">Base</a></td><td>Base</td></tr>\n" +
            "</table>" +
            "<h2><img src=\"Vendor.BaseI.png\" alt=\"Vendor.Base\">" +
            "<a name=\"Vendor.Base\"></a>Base</h2>" +
            "<p><span class=\"ModelicaDescription\">Base with an icon</span></p>" +
            "</body></html>");

        var mine = Path.Combine(_root, "MyLib");
        Directory.CreateDirectory(mine);
        File.WriteAllText(Path.Combine(mine, "package.mo"),
            "package MyLib \"My library\"\n" +
            "  annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));\n" +
            "end MyLib;\n");
        File.WriteAllText(Path.Combine(mine, "package.order"), "Widget\n");
        File.WriteAllText(Path.Combine(mine, "Widget.mo"),
            "within MyLib;\n" +
            "model Widget \"A widget\"\n" +
            "  extends Vendor.Base;\n" +
            "end Widget;\n");

        return _root;
    }

    private static async Task<(LibraryDataService Libraries, RepositoryService Repositories)>
        LoadRepositoryAsync(string path)
    {
        var libraries = new LibraryDataService();
        var repositories = new RepositoryService(
            libraries, new InMemorySettingsService(), new FileMonitoringService());

        var result = await repositories.AddRepositoryAsync(path, checkoutPath: null, startMonitoring: false);
        Assert.True(result.Success, result.ErrorMessage);
        await repositories.LoadLibrariesAsync(result.Repository!.Id);

        return (libraries, repositories);
    }

    [Fact]
    public async Task EncryptedLibraryInARepository_IsLoadedFromItsDocumentation()
    {
        var (libraries, _) = await LoadRepositoryAsync(WriteRepository());

        var vendor = libraries.Libraries.Single(l => l.Name == "Vendor");
        Assert.Equal(["Vendor", "Vendor.Base"], vendor.ModelIds.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task EncryptedLibraryInARepository_KeepsItsNameWithoutTheVersionSuffix()
    {
        var (libraries, _) = await LoadRepositoryAsync(WriteRepository());

        Assert.Contains(libraries.Libraries, l => l.Name == "Vendor");
        Assert.DoesNotContain(libraries.Libraries, l => l.Name == "Vendor 1.2.0");
    }

    [Fact]
    public async Task EncryptedLibraryInARepository_StaysReadOnly()
    {
        // The source type is what the formatter and the "format all files" path key off. Being
        // inside a Git or SVN checkout must not make a library MLQT cannot read look writable.
        var (libraries, _) = await LoadRepositoryAsync(WriteRepository());

        var vendor = libraries.Libraries.Single(l => l.Name == "Vendor");
        Assert.Equal(LibrarySourceType.EncryptedDirectory, vendor.SourceType);
        Assert.All(vendor.ModelIds, id => Assert.True(libraries.GetModelById(id)!.IsExternalStub));
    }

    [Fact]
    public async Task ReferencesIntoAnEncryptedLibraryInTheSameRepository_Resolve()
    {
        // The symptom that exposed this: the app reported more issues than the CLI for the same
        // repository, because the classes the code extends from were never loaded.
        var (libraries, _) = await LoadRepositoryAsync(WriteRepository());

        var settings = new StyleCheckingSettings { ClassHasIcon = true, ValidateModelReferences = true };
        var mine = libraries.Libraries.Single(l => l.Name == "MyLib");
        var models = mine.ModelIds.Select(libraries.GetModelById!).ToList();

        var findings = LibraryCheckSession.Check(
            libraries.CombinedGraph, models!, settings,
            new CustomDictionaryService(), new DictionaryManagerService());

        // Widget has no icon of its own; it inherits one from Vendor.Base.
        Assert.DoesNotContain(findings, f => f.ModelId == "MyLib.Widget");
    }

    [Fact]
    public async Task EncryptedLibraryClasses_AreNotStyleCheckedByThePerModelRunner()
    {
        // The desktop app's background workers call StyleCheckRunner directly rather than going
        // through LibraryCheckSession, so the guard has to sit in the per-model primitive.
        var (libraries, _) = await LoadRepositoryAsync(WriteRepository());

        var settings = new StyleCheckingSettings { ClassHasIcon = true, ClassHasDescription = true };
        var context = StyleCheckContext.Build(
            settings, libraries.CombinedGraph,
            new CustomDictionaryService(), new DictionaryManagerService());

        var vendor = libraries.Libraries.Single(l => l.Name == "Vendor");
        foreach (var id in vendor.ModelIds)
        {
            var node = libraries.GetModelById(id)!;
            Assert.Empty(StyleCheckRunner.Run(node, settings, context));
            Assert.Empty(StyleCheckRunner.RunFindings(node, settings, context));
        }
    }
}
