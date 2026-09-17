using MLQT.Services;
using MLQT.Services.Interfaces;

namespace MLQT.Services.Tests;

/// <summary>
/// That adding a repository loads the library in it, for the layout B198 was reported against: a
/// repository holding a <b>single library with <c>package.mo</c> at the top level</b>, rather than
/// libraries in subdirectories.
///
/// <para>That layout is the one shape where the library's path relative to the repository is the
/// <b>empty string</b>, and an empty string is the kind of value that gets treated as "none" by
/// something along the way. It is threaded through
/// <c>DiscoveredLibraries</c> (as a dictionary key), the selected-libraries set the add dialog
/// builds, and <c>LoadLibrariesAsync</c>, which has to turn it back into the repository's own path
/// rather than combining it onto the end.</para>
///
/// <para>These reproduce the service half of the add path exactly as <c>AddRepositoryDialog</c>
/// performs it — add, then load the discovered libraries — so that the layout hypothesis in B198 is
/// settled by a test rather than by reading.</para>
/// </summary>
public class AddRepositoryLoadsItsLibraryTests : IDisposable
{
    private readonly string _root;

    public AddRepositoryLoadsItsLibraryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mlqt-b198-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>A library whose package.mo sits at the repository root — B198's layout.</summary>
    private string SingleLibraryAtTopLevel()
    {
        var path = Path.Combine(_root, "TopLevel");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "package.mo"), """
            within ;
            package MyLib "test library"

              model Class1 "test class"
                Real x;
              end Class1;

            end MyLib;
            """);
        File.WriteAllText(Path.Combine(path, "package.order"), "Class1\n");
        return path;
    }

    /// <summary>The ordinary layout: libraries one level down. The control for the tests above.</summary>
    private string LibraryInASubdirectory()
    {
        var path = Path.Combine(_root, "Nested");
        var library = Path.Combine(path, "MyLib");
        Directory.CreateDirectory(library);
        File.WriteAllText(Path.Combine(library, "package.mo"), """
            within ;
            package MyLib "test library"

              model Class1 "test class"
                Real x;
              end Class1;

            end MyLib;
            """);
        File.WriteAllText(Path.Combine(library, "package.order"), "Class1\n");
        return path;
    }

    private static (RepositoryService Service, LibraryDataService Libraries) CreateService()
    {
        var libraries = new LibraryDataService();
        var service = new RepositoryService(
            libraries, new InMemorySettingsService(), new FileMonitoringService());
        return (service, libraries);
    }

    /// <summary>
    /// Add, then load the discovered libraries — the two calls <c>AddRepositoryDialog</c> makes, in
    /// its order, including how it builds the selected set from the discovery result.
    /// </summary>
    private static async Task<string> AddAsTheDialogDoes(RepositoryService service, string path)
    {
        var result = await service.AddRepositoryAsync(path, startMonitoring: false);
        Assert.True(result.Success, result.ErrorMessage);

        var selected = new HashSet<string>(result.DiscoveredLibraries.Select(d => d.RelativePath));
        await service.LoadLibrariesAsync(result.Repository!.Id, selected);
        return result.Repository.Id;
    }

    [Fact]
    public async Task ATopLevelLibraryIsDiscovered()
    {
        var (service, _) = CreateService();

        var result = await service.AddRepositoryAsync(SingleLibraryAtTopLevel(), startMonitoring: false);

        var discovered = Assert.Single(result.DiscoveredLibraries);
        Assert.Equal("", discovered.RelativePath);      // the value the whole layout turns on
        Assert.Equal("MyLib", discovered.LibraryName);
    }

    [Fact]
    public async Task ATopLevelLibraryIsLoaded()
    {
        var (service, libraries) = CreateService();

        var repositoryId = await AddAsTheDialogDoes(service, SingleLibraryAtTopLevel());

        var loaded = Assert.Single(libraries.Libraries);
        Assert.Equal("MyLib", loaded.Name);
        Assert.Equal(repositoryId, loaded.RepositoryId);
        Assert.NotEmpty(loaded.ModelIds);
    }

    [Fact]
    public async Task ATopLevelLibraryIsRecordedOnTheRepository()
    {
        // LibraryIds is what the add dialog's own cancel path reads to decide whether anything was
        // loaded, and what the settings persist.
        var (service, _) = CreateService();

        var repositoryId = await AddAsTheDialogDoes(service, SingleLibraryAtTopLevel());

        Assert.NotEmpty(service.GetRepository(repositoryId)!.LibraryIds);
    }

    [Fact]
    public async Task ATopLevelLibraryIsVisibleToTheCallerThatRunsTheAnalysis()
    {
        // MainLayout picks the models to analyse with
        // `Libraries.Where(l => l.RepositoryId == id).SelectMany(l => l.ModelIds)`, and does nothing
        // at all when that set is empty — no analysis, and no reason for the tree to change. So this
        // query returning nothing is indistinguishable from "the library did not load".
        var (service, libraries) = CreateService();

        var repositoryId = await AddAsTheDialogDoes(service, SingleLibraryAtTopLevel());

        var modelIds = libraries.Libraries
            .Where(l => l.RepositoryId == repositoryId)
            .SelectMany(l => l.ModelIds)
            .ToHashSet();

        Assert.NotEmpty(modelIds);
        Assert.Contains("MyLib.Class1", modelIds);
    }

    [Fact]
    public async Task ALibraryInASubdirectoryIsLoadedToo()
    {
        // The control. If this passed while the top-level tests failed, the layout would be the
        // cause; both passing is what says it is not.
        var (service, libraries) = CreateService();

        var repositoryId = await AddAsTheDialogDoes(service, LibraryInASubdirectory());

        var loaded = Assert.Single(libraries.Libraries);
        Assert.Equal("MyLib", loaded.Name);
        Assert.Equal(repositoryId, loaded.RepositoryId);
        Assert.NotEmpty(loaded.ModelIds);
    }

    [Fact]
    public async Task TheDiscoveredNameIsTheLibrarysOwn_NotTheFoldersName()
    {
        // B204, found while settling B198's layout question. `ExtractLibraryName` looked for the
        // outermost class with `ParentModelName == null`, and that is the empty string rather than
        // null — for a file with a `within` clause and for one without alike. So it returned null on
        // every call and every caller fell back to the folder name.
        //
        // It hid because the two are usually the same word: a library called Modelica lives in a
        // folder called Modelica. The folder here is deliberately named something else, which is the
        // ordinary case for a repository checked out under its own name — and B198 was reported
        // against exactly such a repository, `ModelicaEditorTestsGit`.
        var (service, _) = CreateService();

        var result = await service.AddRepositoryAsync(SingleLibraryAtTopLevel(), startMonitoring: false);

        var discovered = Assert.Single(result.DiscoveredLibraries);
        Assert.Equal("MyLib", discovered.LibraryName);
        Assert.NotEqual("TopLevel", discovered.LibraryName);
    }

    [Fact]
    public async Task TheDiscoveredNameMatchesWhatTheLibraryIsActuallyLoadedAs()
    {
        // The two names are read by different code — discovery parses package.mo itself, loading goes
        // through AddLibraryFromPathAsync — and the user sees both: the discovered name in the add
        // dialog, the loaded name in the tree. They disagreeing is what makes a repository look like
        // it added something other than what it added.
        var (service, libraries) = CreateService();

        var result = await service.AddRepositoryAsync(SingleLibraryAtTopLevel(), startMonitoring: false);
        var selected = new HashSet<string>(result.DiscoveredLibraries.Select(d => d.RelativePath));
        await service.LoadLibrariesAsync(result.Repository!.Id, selected);

        Assert.Equal(
            Assert.Single(result.DiscoveredLibraries).LibraryName,
            Assert.Single(libraries.Libraries).Name);
    }

    [Fact]
    public async Task TheTreeIsToldThatLibrariesChanged()
    {
        // The other candidate for "added the repository but loaded no library": the library is in the
        // graph and the browser was never told, which looks identical from the outside and would also
        // be cured by a restart.
        var (service, libraries) = CreateService();
        var announced = 0;
        libraries.OnLibrariesChanged += () => announced++;

        await AddAsTheDialogDoes(service, SingleLibraryAtTopLevel());

        Assert.True(announced > 0, "nothing announced that the set of loaded libraries had changed");
    }
}
