using MLQT.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.Services.Tests;

/// <summary>
/// Creating a project on the startup screen must not open the previous session's repositories
/// (B192, second attempt).
///
/// <para><b>What the first attempt missed.</b> It fixed the two silent fallbacks that pick a
/// <i>different</i> project when the named one is not found, which were real. But the repositories
/// were never arriving through project selection at all. <c>MainLayout</c> called
/// <c>LoadRepositorySettingsAsync()</c> with no argument, to get the project list into memory so the
/// new project could be appended to it — and that method also opens every repository of the
/// <b>currently active</b> project and loads their libraries. By the time the new project was
/// selected, the old project's work was already done and nothing unloads it:
/// <c>LoadRepositorySettingsAsync</c> never clears <c>_repositories</c> or the graph, and only
/// <c>SwitchProjectAsync</c> does.</para>
///
/// <para>That accounts for the whole report — the previous session's repositories appearing under a
/// project that should be empty, the startup progress dialog never showing (the new project really
/// does have nothing to load, so that branch returns early), the UI staying busy afterwards (the old
/// project's loading and the analysis it triggers carry on behind it), and the title not updating (a
/// later handler on that loading path writes the name of the project it was loading).</para>
///
/// <para><see cref="IRepositoryService.CreateAndSelectProjectAsync"/> is the missing primitive:
/// create a project in the persisted settings and make it active, touching no in-memory state and
/// loading nothing. It deliberately leaves <c>_projects</c> and <c>_repositories</c> alone, because
/// <c>SaveRepositorySettingsAsync</c> writes the active project's repository list from
/// <c>_repositories</c> — so a half-populated pair is not a state it is safe to save from.</para>
/// </summary>
public class NewProjectAtStartupTests : IDisposable
{
    private readonly string _root;

    public NewProjectAtStartupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mlqt-b192-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>A real library on disk, so loading it is observable rather than skipped.</summary>
    private string ALibrary(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "package.mo"), $$"""
            within ;
            package {{name}} "a library"

              model Class1 "a class"
                Real x;
              end Class1;

            end {{name}};
            """);
        File.WriteAllText(Path.Combine(path, "package.order"), "Class1\n");
        return path;
    }

    private static (RepositoryService Service, LibraryDataService Libraries, InMemorySettingsService Settings) CreateService()
    {
        var libraries = new LibraryDataService();
        var settings = new InMemorySettingsService();
        return (new RepositoryService(libraries, settings, new FileMonitoringService()), libraries, settings);
    }

    /// <summary>
    /// The state a previous session leaves: an active project holding a repository, and one other
    /// project, which is what makes the startup selector appear at all.
    /// </summary>
    private async Task<RepositorySettingsCollection> APreviousSession(InMemorySettingsService settings)
    {
        var previous = new ProjectProfile
        {
            Name = "Previous",
            Repositories =
            [
                new RepositorySettingsEntry
                {
                    Name = "WorkLib",
                    LocalPath = ALibrary("WorkLib"),
                    VcsType = "Local",
                    AutoLoad = true
                }
            ]
        };
        var other = new ProjectProfile { Name = "Other" };

        var saved = new RepositorySettingsCollection
        {
            Projects = [previous, other],
            ActiveProjectId = previous.Id
        };
        await settings.SetAsync("Repositories", saved);
        return saved;
    }

    [Fact]
    public async Task CreatingAProjectLoadsNoLibraries()
    {
        // The defect in one assertion. Creating a project is a settings edit; nothing about it should
        // open a repository.
        var (service, libraries, settings) = CreateService();
        await APreviousSession(settings);

        await service.CreateAndSelectProjectAsync("Brand New");

        Assert.Empty(libraries.Libraries);
        Assert.Empty(service.Repositories);
    }

    [Fact]
    public async Task TheNewProjectIsTheActiveOneAndIsEmpty()
    {
        var (service, libraries, settings) = CreateService();
        await APreviousSession(settings);

        var created = await service.CreateAndSelectProjectAsync("Brand New");
        await service.LoadRepositorySettingsAsync(created.Id);

        Assert.Equal(created.Id, service.GetActiveProject()?.Id);
        Assert.Equal("Brand New", service.GetActiveProject()?.Name);
        Assert.Empty(service.GetActiveProject()!.Repositories);
        Assert.Empty(libraries.Libraries);
        Assert.Empty(service.Repositories);
    }

    [Fact]
    public async Task TheProjectThatWasActiveKeepsItsRepositories()
    {
        // The other half of the original report - "the new project appears to take on that project's
        // repositories". They must stay where they were, and not follow the user into the new one.
        var (service, _, settings) = CreateService();
        var before = await APreviousSession(settings);

        var created = await service.CreateAndSelectProjectAsync("Brand New");

        var after = await settings.GetAsync("Repositories", new RepositorySettingsCollection());
        var previous = Assert.Single(after.Projects, p => p.Id == before.Projects[0].Id);
        var repository = Assert.Single(previous.Repositories);
        Assert.Equal("WorkLib", repository.Name);

        var brandNew = Assert.Single(after.Projects, p => p.Id == created.Id);
        Assert.Empty(brandNew.Repositories);
    }

    [Fact]
    public async Task EveryExistingProjectSurvives()
    {
        var (service, _, settings) = CreateService();
        var before = await APreviousSession(settings);

        // Snapshotted rather than counted afterwards, which is simply the clearer way to write it.
        // It used to be necessary: the double stored objects by reference, so `before` and what was
        // read back were one instance and counting after the call counted the new project too. B205
        // replaced it with one that round-trips like the real service, so this no longer has to be
        // defensive - it is kept because a snapshot says what it means.
        var idsBefore = before.Projects.Select(p => p.Id).ToList();

        await service.CreateAndSelectProjectAsync("Brand New");

        var after = await settings.GetAsync("Repositories", new RepositorySettingsCollection());
        Assert.Equal(idsBefore.Count + 1, after.Projects.Count);
        foreach (var id in idsBefore)
            Assert.Contains(after.Projects, p => p.Id == id);
    }

    [Fact]
    public async Task TheNewProjectIsRecordedAsActive()
    {
        // So that a restart before anything else is saved comes back to the project the user chose,
        // rather than to whichever one is first in the file.
        var (service, _, settings) = CreateService();
        await APreviousSession(settings);

        var created = await service.CreateAndSelectProjectAsync("Brand New");

        var after = await settings.GetAsync("Repositories", new RepositorySettingsCollection());
        Assert.Equal(created.Id, after.ActiveProjectId);
    }

    [Fact]
    public async Task ASaveAfterwardsDoesNotWriteTheOldRepositoriesIntoTheNewProject()
    {
        // The trap that makes this worth a primitive rather than a few lines in the caller.
        // SaveRepositorySettingsAsync writes the active project's repository list from the loaded
        // _repositories, so creating a project while another one's repositories are loaded and then
        // saving would copy them across. Nothing is loaded here, so there is nothing to copy.
        var (service, _, settings) = CreateService();
        var before = await APreviousSession(settings);

        var created = await service.CreateAndSelectProjectAsync("Brand New");
        await service.LoadRepositorySettingsAsync(created.Id);
        await service.SaveRepositorySettingsAsync();

        var after = await settings.GetAsync("Repositories", new RepositorySettingsCollection());
        Assert.Empty(Assert.Single(after.Projects, p => p.Id == created.Id).Repositories);
        Assert.Single(Assert.Single(after.Projects, p => p.Id == before.Projects[0].Id).Repositories);
    }

    [Fact]
    public async Task LoadingWithNoProjectNamedStillOpensTheActiveProject()
    {
        // The behaviour MainLayout was leaning on, asserted so that it is not mistaken for a defect
        // later: LoadRepositorySettingsAsync() really does load repositories. That is correct for
        // ordinary startup; it was simply the wrong way to obtain a project list.
        var (service, libraries, settings) = CreateService();
        await APreviousSession(settings);

        await service.LoadRepositorySettingsAsync();

        Assert.Single(service.Repositories);
        Assert.NotEmpty(libraries.Libraries);
    }
}
