using MLQT.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.Services.Tests;

/// <summary>
/// That loading a project by id loads <i>that</i> project, or says it could not.
///
/// <para><b>What broke (B192).</b> Creating a project on the startup screen and choosing Load Project
/// loaded the <i>previously selected</i> project, and the new project appeared to take on that
/// project's repositories. The chain had three links and each one was silent:</para>
/// <list type="number">
/// <item><c>CreateProject</c> does not await its own save, so the project exists in memory before it
/// exists on disk.</item>
/// <item><c>LoadRepositorySettingsAsync</c> re-reads the settings and <b>replaces</b> the in-memory
/// project list with what it finds, so a project not yet written is simply gone.</item>
/// <item>It then resolved the active project with
/// <c>FirstOrDefault(p =&gt; p.Id == activeId) ?? settings.Projects.First()</c> — so an id that named
/// nothing quietly became the first project in the file, along with all of its repositories.</item>
/// </list>
///
/// <para>The fallback is kept for the case it was written for — a saved active id naming a project
/// that has since been deleted — but an <b>explicitly requested</b> id is no longer substituted. The
/// caller either gets the project it named or finds out, through
/// <see cref="IRepositoryService.LastLoadWarnings"/>.</para>
/// </summary>
public class ProjectLoadIdentityTests
{
    private static (RepositoryService Service, InMemorySettingsService Settings) CreateService()
    {
        var settings = new InMemorySettingsService();
        var service = new RepositoryService(
            new LibraryDataService(), settings, new FileMonitoringService());
        return (service, settings);
    }

    /// <summary>
    /// Two saved projects, the first holding a repository, and that first one is active — the shape
    /// the defect needs. The repository points nowhere, which is fine: loading skips a path that does
    /// not exist and still reports the project as the active one, and it is the identity of the
    /// active project that is under test here, not the loading of working copies.
    /// </summary>
    private static async Task<RepositorySettingsCollection> TwoProjectsOneWithRepositories(
        InMemorySettingsService settings)
    {
        var withRepositories = new ProjectProfile
        {
            Name = "Existing",
            Repositories =
            [
                new RepositorySettingsEntry
                {
                    Name = "SomeRepo",
                    LocalPath = Path.Combine(Path.GetTempPath(), "mlqt-b192-does-not-exist"),
                    AutoLoad = true
                }
            ]
        };
        var empty = new ProjectProfile { Name = "Other" };

        var saved = new RepositorySettingsCollection
        {
            Projects = [withRepositories, empty],
            ActiveProjectId = withRepositories.Id
        };
        await settings.SetAsync("Repositories", saved);
        return saved;
    }

    [Fact]
    public async Task AnIdThatNamesNothing_DoesNotSilentlyLoadTheFirstProject()
    {
        // The defect in one assertion. "the-new-project" stands for a project created in memory whose
        // save has not landed; before the fix this loaded "Existing" and its repository.
        var (service, settings) = CreateService();
        await TwoProjectsOneWithRepositories(settings);

        await service.LoadRepositorySettingsAsync("the-new-project");

        Assert.NotEmpty(service.LastLoadWarnings);
        Assert.Contains(service.LastLoadWarnings, w => w.Contains("the-new-project"));
    }

    [Fact]
    public async Task AnIdThatNamesNothing_IsReportedRatherThanGuessedAt()
    {
        // The warning has to name both halves: what was asked for and what was loaded instead. A
        // warning saying only "project not found" leaves the user looking at a populated library tree
        // with no idea whose it is.
        var (service, settings) = CreateService();
        await TwoProjectsOneWithRepositories(settings);

        await service.LoadRepositorySettingsAsync("the-new-project");

        var warning = Assert.Single(service.LastLoadWarnings, w => w.Contains("the-new-project"));
        Assert.Contains("Existing", warning);
    }

    [Fact]
    public async Task AProjectAskedForByIdIsTheOneLoaded()
    {
        // The positive control: naming the *second* project must not get the first.
        var (service, settings) = CreateService();
        var saved = await TwoProjectsOneWithRepositories(settings);
        var second = saved.Projects[1];

        await service.LoadRepositorySettingsAsync(second.Id);

        Assert.Equal(second.Id, service.GetActiveProject()?.Id);
        Assert.Equal("Other", service.GetActiveProject()?.Name);
        Assert.DoesNotContain(service.LastLoadWarnings, w => w.Contains(second.Id));
    }

    [Fact]
    public async Task ASavedActiveIdThatNoLongerExists_StillFallsBack()
    {
        // The case the fallback was written for, and it is deliberately kept: a deleted or renamed
        // project leaves exactly this state, and starting up on some project beats refusing to start.
        // No id was requested here, so nothing was substituted for anything.
        var (service, settings) = CreateService();
        var saved = await TwoProjectsOneWithRepositories(settings);
        saved.ActiveProjectId = "deleted-project";
        await settings.SetAsync("Repositories", saved);

        await service.LoadRepositorySettingsAsync();

        Assert.Equal(saved.Projects[0].Id, service.GetActiveProject()?.Id);
        Assert.DoesNotContain(service.LastLoadWarnings, w => w.Contains("deleted-project"));
    }

    [Fact]
    public async Task ANewlyCreatedProjectIsLoadable_OnceItsSaveIsAwaited()
    {
        // The startup sequence, in order, as MainLayout now performs it. Without the explicit save
        // the load below re-reads settings that do not yet contain the project.
        var (service, settings) = CreateService();
        await TwoProjectsOneWithRepositories(settings);

        await service.LoadRepositorySettingsAsync();
        var created = service.CreateProject("Brand New");
        await service.SaveRepositorySettingsAsync();

        await service.LoadRepositorySettingsAsync(created.Id);

        Assert.Equal(created.Id, service.GetActiveProject()?.Id);
        Assert.Equal("Brand New", service.GetActiveProject()?.Name);
        Assert.Empty(service.LastLoadWarnings);
    }

    [Fact]
    public async Task ANewlyCreatedProjectStartsEmpty()
    {
        // The other half of the report — "the new project appears to take on that project's
        // repositories". It does not: the repositories came with the project that was substituted
        // for it.
        var (service, settings) = CreateService();
        await TwoProjectsOneWithRepositories(settings);

        await service.LoadRepositorySettingsAsync();
        var created = service.CreateProject("Brand New");
        await service.SaveRepositorySettingsAsync();
        await service.LoadRepositorySettingsAsync(created.Id);

        Assert.Empty(service.GetActiveProject()!.Repositories);
        Assert.Empty(service.Repositories);
    }
}
