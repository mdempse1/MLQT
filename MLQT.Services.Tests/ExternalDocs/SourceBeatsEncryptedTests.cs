using ModelicaGraph;
using ModelicaGraph.DataTypes;
using MLQT.Services;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using Xunit;

namespace MLQT.Services.Tests.ExternalDocs;

/// <summary>
/// The same library loaded twice: once as the source the user is working on, once as the encrypted
/// build that ships in a tool's library folder alongside every other vendor library.
///
/// <para>This is not a corner case — it is what happens the moment a library vendor points MLQT at
/// their own Dymola installation while working on their own library. Both copies land in the one
/// graph under the same class ids, and the source copy has to win.</para>
///
/// <para>It failed in a way that was almost invisible. The graph's collision rule preferred a
/// standalone class over a non-standalone one, and a stub is never standalone — so a stub colliding
/// with a *nested* class, which is not standalone either, matched no case and the stub stayed. Those
/// classes then had no source to check, so every rule went quiet on them while the standalone
/// classes beside them were checked normally. The symptom was a finding count that differed from the
/// CLI's by a few hundred, spread evenly across every rule and confined to one library.</para>
/// </summary>
public class SourceBeatsEncryptedTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-source-wins", Guid.NewGuid().ToString("N"));

    public SourceBeatsEncryptedTests() => Directory.CreateDirectory(_root);

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

    // Standalone (Widget) and nested non-standalone (BaseProperties, a redeclare) side by side —
    // the collision rule used to handle only the first.
    private const string SourcePackage = """
        within;
        package Claytex "Claytex"
          package Media "Media"
            redeclare model BaseProperties
              Real d;
            equation
              d = 1;
            end BaseProperties;
          end Media;
        end Claytex;
        """;

    private string WriteSource()
    {
        var lib = Path.Combine(_root, "src", "Claytex");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"), SourcePackage);
        File.WriteAllText(Path.Combine(lib, "package.order"), "Media\n");
        return lib;
    }

    private string WriteEncrypted()
    {
        var lib = Path.Combine(_root, "tool", "Claytex 2026.1");
        var help = Path.Combine(lib, "help");
        Directory.CreateDirectory(help);
        File.WriteAllText(Path.Combine(lib, "package.moe"), "not readable");

        const string head =
            "<html><head><meta name=\"HTML-Generator\" content=\"Dymola\"></head><body>";
        File.WriteAllText(Path.Combine(help, "Claytex.html"), head +
            "<h2><a name=\"Claytex\"></a>Claytex</h2>" +
            "<h3>Package Content</h3>" +
            "<table summary=\"Package Content\" class=\"ModelicaTablePackageContent\">\n" +
            "<tr><th>Name</th><th>Description</th></tr>\n" +
            "<tr><td><img src=\"Claytex.MediaS.png\" alt=\"Claytex.Media\">&nbsp;" +
            "<a href=\"Claytex_Media.html#Claytex.Media\">Media</a></td><td>Media</td></tr>\n" +
            "</table></body></html>");
        File.WriteAllText(Path.Combine(help, "Claytex_Media.html"), head +
            "<h2><a name=\"Claytex.Media\"></a>Media</h2>" +
            "<h3>Package Content</h3>" +
            "<table summary=\"Package Content\" class=\"ModelicaTablePackageContent\">\n" +
            "<tr><th>Name</th><th>Description</th></tr>\n" +
            "<tr><td><img src=\"Claytex.Media.BasePropertiesS.png\" alt=\"Claytex.Media.BaseProperties\">&nbsp;" +
            "<a href=\"Claytex_Media.html#Claytex.Media.BaseProperties\">BaseProperties</a></td>" +
            "<td>Base properties</td></tr>\n" +
            "</table>" +
            "<h2><img src=\"Claytex.Media.BasePropertiesI.png\" alt=\"Claytex.Media.BaseProperties\">" +
            "<a name=\"Claytex.Media.BaseProperties\"></a>BaseProperties</h2>" +
            "<p><span class=\"ModelicaDescription\">Base properties</span></p>" +
            "</body></html>");
        return lib;
    }

    private const string NestedClass = "Claytex.Media.BaseProperties";

    [Theory]
    [InlineData(true)]   // encrypted arrives first — the order the app used to load in
    [InlineData(false)]  // source arrives first
    public async Task SourceWinsWhicheverArrivesFirst(bool encryptedFirst)
    {
        var source = WriteSource();
        var encrypted = WriteEncrypted();
        var service = new LibraryDataService();

        if (encryptedFirst)
        {
            await service.AddLibraryFromPathAsync(encrypted);
            await service.AddLibraryFromPathAsync(source);
        }
        else
        {
            await service.AddLibraryFromPathAsync(source);
            await service.AddLibraryFromPathAsync(encrypted);
        }

        var nested = service.GetModelById(NestedClass);
        Assert.NotNull(nested);
        Assert.False(nested!.IsExternalStub,
            "the nested class kept the documentation stub instead of the real source");
        Assert.Contains("Real d;", nested.Definition.ModelicaCode);
    }

    [Theory]
    [InlineData(true)]   // encrypted arrives first
    [InlineData(false)]  // source arrives first — the order the app loads in, and the one that broke
    public async Task TheClassKeepsTheFileItsSourceIsIn(bool encryptedFirst)
    {
        // Winning the node is not enough. Registering the encrypted package's containment afterwards
        // repointed the real class at package.moe, and everything that asks a class where it lives
        // believed it: correcting a spelling in a checked-out class read, and spent minutes trying to
        // parse, a vendor's encrypted blob — then reported that the word was not in it.
        var source = WriteSource();
        var encrypted = WriteEncrypted();
        var service = new LibraryDataService();

        if (encryptedFirst)
        {
            await service.AddLibraryFromPathAsync(encrypted);
            await service.AddLibraryFromPathAsync(source);
        }
        else
        {
            await service.AddLibraryFromPathAsync(source);
            await service.AddLibraryFromPathAsync(encrypted);
        }

        var nested = service.GetModelById(NestedClass);
        Assert.NotNull(nested);
        var file = service.CombinedGraph.GetNode<ModelicaGraph.DataTypes.FileNode>(nested!.ContainingFileId!);

        Assert.NotNull(file);
        Assert.DoesNotContain(".moe", file!.FilePath);
        Assert.EndsWith("package.mo", file.FilePath);
    }

    [Fact]
    public async Task ANestedClassShadowedByAStub_IsStillChecked()
    {
        // The visible symptom: a class that lost to a stub is skipped by every rule, so it silently
        // contributes nothing to the finding count.
        var service = new LibraryDataService();
        await service.AddLibraryFromPathAsync(WriteEncrypted());
        await service.AddLibraryFromPathAsync(WriteSource());

        var settings = new StyleCheckingSettings { ClassHasDescription = true };
        var nested = service.GetModelById(NestedClass)!;

        var findings = LibraryCheckSession.Check(
            service.CombinedGraph, [nested], settings,
            new CustomDictionaryService(), new DictionaryManagerService());

        // BaseProperties has no description string in the source, so the rule must fire.
        Assert.Contains(findings, f => f.ModelId == NestedClass);
    }

    [Fact]
    public void AStubNeverReplacesRealSource()
    {
        // Stated directly against the graph, since this is the primitive everything else relies on.
        var graph = new DirectedGraph();
        var real = new ModelNode("Lib.Thing", "Thing", "within Lib;\nmodel Thing\nend Thing;\n")
        {
            CanBeStoredStandalone = false
        };
        graph.AddNode(real);

        var stub = new ModelNode("Lib.Thing", "Thing", "within Lib;\nmodel Thing\nend Thing;\n")
        {
            IsExternalStub = true,
            CanBeStoredStandalone = false
        };
        graph.AddNode(stub);

        Assert.False(graph.GetNode<ModelNode>("Lib.Thing")!.IsExternalStub);
    }
}
