using ModelicaGraph;
using ModelicaGraph.DataTypes;
using MLQT.Services;
using MLQT.Services.Checking;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// The Reference Libraries setting, loaded and unloaded as it is edited.
///
/// <para>Removing a folder used to change nothing until the next project load: its classes stayed in
/// the graph, went on resolving references and counting as known words, so checking every class again
/// reported what it had with the folder configured - and a restart reported something else.</para>
/// </summary>
public class ReferenceLibraryLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-reference-loader", Guid.NewGuid().ToString("N"));

    public ReferenceLibraryLoaderTests() => Directory.CreateDirectory(_root);

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

    /// <summary>A folder holding library <c>RefLib</c>, whose one class is named <c>Zorblat</c>.</summary>
    private string WriteReferenceFolder(string name)
    {
        var lib = Path.Combine(_root, name, "RefLib");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"), "package RefLib \"Reference\"\nend RefLib;\n");
        File.WriteAllText(Path.Combine(lib, "package.order"), "Zorblat\n");
        File.WriteAllText(Path.Combine(lib, "Zorblat.mo"), "within RefLib;\nmodel Zorblat \"A reference class\"\nend Zorblat;\n");
        return Path.Combine(_root, name);
    }

    /// <summary>
    /// The user's library, whose description names the reference class. It declares it uses RefLib:
    /// only a declared library's class names count as words (KnownClassNames).
    /// </summary>
    private string WriteUserLibrary()
    {
        var lib = Path.Combine(_root, "mine", "MyLib");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"),
            "package MyLib \"Built on Zorblat\"\n  annotation (uses(RefLib(version=\"1.0\")));\nend MyLib;\n");
        return lib;
    }

    private static List<ModelicaParser.DataTypes.Finding> SpellingFindings(LibraryDataService libraries)
    {
        var settings = new StyleCheckingSettings
        {
            SpellCheckDescription = true,
            SpellCheckLanguages = ["en_US"],
        };
        var mine = libraries.Libraries.Single(l => l.Name == "MyLib");
        var models = mine.ModelIds.Select(libraries.GetModelById).OfType<ModelNode>().ToList();

        return LibraryCheckSession.Check(
                libraries.CombinedGraph, models, settings, new CustomDictionaryService(), new DictionaryManagerService())
            .Where(f => f.Message.Contains("Zorblat", StringComparison.Ordinal))
            .ToList();
    }

    [Fact]
    public async Task RemovingAFolder_ReportsWhatItHadBeenAccepting_WithoutARestart()
    {
        var libraries = new LibraryDataService();
        var loader = new ReferenceLibraryLoader(libraries);
        await libraries.AddLibraryFromPathAsync(WriteUserLibrary());
        var folder = WriteReferenceFolder("tool");

        await loader.LoadAsync([folder], useEncryptedDocumentation: true);
        Assert.Empty(SpellingFindings(libraries));     // a class of that name is loaded, so the word is known

        Assert.Equal(1, loader.UnloadFolder(folder));

        Assert.NotEmpty(SpellingFindings(libraries));  // what a restart without the folder reports
    }

    [Fact]
    public async Task RemovingAFolder_TakesItsClassesAndFilesOutOfTheGraph_AndAsksForNewEdges()
    {
        var libraries = new LibraryDataService();
        var loader = new ReferenceLibraryLoader(libraries);
        var folder = WriteReferenceFolder("tool");
        await loader.LoadAsync([folder], useEncryptedDocumentation: true);
        await libraries.EnsureDependenciesAnalyzedAsync();
        Assert.NotNull(libraries.GetModelById("RefLib.Zorblat"));

        loader.UnloadFolder(folder);

        Assert.DoesNotContain(libraries.Libraries, l => l.Name == "RefLib");
        Assert.Null(libraries.GetModelById("RefLib.Zorblat"));
        Assert.Null(libraries.GetModelById("RefLib"));
        Assert.DoesNotContain(libraries.CombinedGraph.FileNodes,
            f => f.FilePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
        Assert.False(libraries.CombinedGraph.DependenciesAnalyzed);
    }

    [Fact]
    public async Task RemovingAFolder_LeavesALibraryFromARepositoryUnderIt_Alone()
    {
        // The setting and a repository can name the same place. The repository's library is the
        // user's, whatever folder it sits in.
        var libraries = new LibraryDataService();
        var loader = new ReferenceLibraryLoader(libraries);
        var folder = WriteReferenceFolder("tool");
        var fromRepository = await libraries.AddLibraryFromPathAsync(Path.Combine(folder, "RefLib"));
        fromRepository.RepositoryId = "a repository";

        Assert.Equal(0, loader.UnloadFolder(folder));
        Assert.NotNull(libraries.GetModelById("RefLib.Zorblat"));
    }

    [Fact]
    public async Task RemovingAFolder_BringsInTheCopyItHadBeenStandingInFor()
    {
        // Two folders holding the same library: the second was skipped as a copy of the first. With
        // the first removed and the rest loaded again, the second's copy is what is loaded.
        var libraries = new LibraryDataService();
        var loader = new ReferenceLibraryLoader(libraries);
        var first = WriteReferenceFolder("first");
        var second = WriteReferenceFolder("second");

        var summary = await loader.LoadAsync([first, second], useEncryptedDocumentation: true);
        Assert.Equal(1, summary.Loaded);
        Assert.Equal(1, summary.Skipped);

        loader.UnloadFolder(first);
        await loader.LoadAsync([second], useEncryptedDocumentation: true);

        var lib = Assert.Single(libraries.Libraries, l => l.Name == "RefLib");
        Assert.StartsWith(second, lib.SourcePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(lib.IsReferenceOnly);
    }

    [Fact]
    public async Task LoadingAgain_LoadsOnlyWhatIsNew_AndNamesAMissingFolder()
    {
        var libraries = new LibraryDataService();
        var loader = new ReferenceLibraryLoader(libraries);
        var folder = WriteReferenceFolder("tool");
        var missing = Path.Combine(_root, "not-there");

        await loader.LoadAsync([folder], useEncryptedDocumentation: true);
        var again = await loader.LoadAsync([folder, missing], useEncryptedDocumentation: true);

        Assert.Equal(0, again.Loaded);
        Assert.Equal([missing], again.MissingFolders);
        Assert.Single(libraries.Libraries, l => l.Name == "RefLib");
    }
}
