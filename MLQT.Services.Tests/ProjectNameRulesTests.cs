using MLQT.Services;
using MLQT.Services.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// The rule that two projects may not share a name, and the service refusing to break it.
///
/// <para>A project can be named from the startup selector and from Settings → Manage Repositories,
/// and the second can also rename one. Three ways in, one rule — kept in
/// <see cref="ProjectNameRules"/> rather than written out at each screen, which is the shape behind
/// B106, B109 and B110.</para>
/// </summary>
public class ProjectNameRulesTests
{
    private static List<ProjectProfile> Projects(params string[] names) =>
        names.Select(n => new ProjectProfile { Name = n }).ToList();

    [Fact]
    public void AFreshNameIsAccepted()
    {
        Assert.Null(ProjectNameRules.Validate("Research", Projects("Work", "Archive")));
    }

    [Fact]
    public void ANameAlreadyTakenIsRefused()
    {
        var refusal = ProjectNameRules.Validate("Work", Projects("Work", "Archive"));

        Assert.NotNull(refusal);
        Assert.Contains("Work", refusal);
    }

    [Theory]
    [InlineData("work")]
    [InlineData("WORK")]
    [InlineData("WoRk")]
    public void CaseDoesNotMakeANameDifferent(string candidate)
    {
        // Two entries differing only in case are two entries nobody can tell apart in a list.
        Assert.NotNull(ProjectNameRules.Validate(candidate, Projects("Work")));
    }

    [Theory]
    [InlineData("  Work")]
    [InlineData("Work  ")]
    [InlineData("  Work  ")]
    public void SurroundingSpaceDoesNotMakeANameDifferent(string candidate)
    {
        Assert.NotNull(ProjectNameRules.Validate(candidate, Projects("Work")));
    }

    [Fact]
    public void SpaceAroundAnExistingNameIsAlsoIgnored()
    {
        // The stored name is compared trimmed as well, so a project saved with a stray space does not
        // quietly allow a duplicate of itself.
        Assert.NotNull(ProjectNameRules.Validate("Work", Projects(" Work ")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyNameIsRefused(string? candidate)
    {
        Assert.NotNull(ProjectNameRules.Validate(candidate, Projects("Work")));
    }

    [Fact]
    public void ARenameThatChangesNothingIsAllowed()
    {
        // Confirming a rename without having edited the name must not report a clash with itself.
        var projects = Projects("Work", "Archive");
        var work = projects[0];

        Assert.Null(ProjectNameRules.Validate("Work", projects, ignoringProjectId: work.Id));
    }

    [Fact]
    public void ARenameToAnotherProjectsNameIsRefused()
    {
        // The hole a create-only rule would leave: two projects made indistinguishable afterwards.
        var projects = Projects("Work", "Archive");
        var work = projects[0];

        Assert.NotNull(ProjectNameRules.Validate("Archive", projects, ignoringProjectId: work.Id));
    }

    [Fact]
    public void ARenameThatOnlyChangesCaseIsAllowed()
    {
        // Correcting the capitalisation of a project's own name is not a clash.
        var projects = Projects("work");
        var work = projects[0];

        Assert.Null(ProjectNameRules.Validate("Work", projects, ignoringProjectId: work.Id));
    }

    [Fact]
    public void NormaliseTrims()
    {
        Assert.Equal("Work", ProjectNameRules.Normalise("  Work  "));
        Assert.Equal("", ProjectNameRules.Normalise(null));
    }
}

/// <summary>
/// The same rule where it is enforced rather than merely offered: the screens check first and show
/// the reason, and these refuse a name that got past them.
/// </summary>
public class DuplicateProjectNameIsRefusedTests
{
    private static (RepositoryService Service, InMemorySettingsService Settings) CreateService()
    {
        var settings = new InMemorySettingsService();
        return (new RepositoryService(new LibraryDataService(), settings, new FileMonitoringService()), settings);
    }

    [Fact]
    public void CreateProject_RefusesANameAlreadyTaken()
    {
        var (service, _) = CreateService();
        service.CreateProject("Work");

        var ex = Assert.Throws<InvalidOperationException>(() => service.CreateProject("work"));
        Assert.Contains("Work", ex.Message);
    }

    [Fact]
    public void CreateProject_StillAcceptsADistinctName()
    {
        var (service, _) = CreateService();
        service.CreateProject("Work");

        var second = service.CreateProject("Archive");

        Assert.Equal("Archive", second.Name);
        Assert.Equal(2, service.GetProjects().Count);
    }

    [Fact]
    public void CreateProject_StoresTheTrimmedName()
    {
        var (service, _) = CreateService();

        var project = service.CreateProject("  Work  ");

        Assert.Equal("Work", project.Name);
    }

    [Fact]
    public async Task CreateAndSelectProjectAsync_RefusesANameAlreadyTaken()
    {
        var (service, settings) = CreateService();
        await settings.SetAsync("Repositories", new RepositorySettingsCollection
        {
            Projects = [new ProjectProfile { Name = "Work" }]
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAndSelectProjectAsync("WORK"));
        Assert.Contains("Work", ex.Message);
    }

    [Fact]
    public async Task CreateAndSelectProjectAsync_ChecksWhatIsSavedRatherThanWhatIsLoaded()
    {
        // This path runs before anything is loaded, so the in-memory project list is empty and
        // checking it would accept every name.
        var (service, settings) = CreateService();
        await settings.SetAsync("Repositories", new RepositorySettingsCollection
        {
            Projects = [new ProjectProfile { Name = "Work" }]
        });

        Assert.Empty(service.GetProjects());     // nothing loaded, which is the point

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAndSelectProjectAsync("Work"));
    }

    [Fact]
    public void RenameProject_RefusesAnotherProjectsName()
    {
        var (service, _) = CreateService();
        var work = service.CreateProject("Work");
        service.CreateProject("Archive");

        Assert.Throws<InvalidOperationException>(() => service.RenameProject(work.Id, "Archive"));
        Assert.Equal("Work", service.GetProjects().Single(p => p.Id == work.Id).Name);
    }

    [Fact]
    public void RenameProject_AllowsAProjectToKeepItsOwnName()
    {
        var (service, _) = CreateService();
        var work = service.CreateProject("Work");

        service.RenameProject(work.Id, "Work");

        Assert.Equal("Work", service.GetProjects().Single(p => p.Id == work.Id).Name);
    }

    [Fact]
    public void RenameProject_AllowsAnOrdinaryRename()
    {
        var (service, _) = CreateService();
        var work = service.CreateProject("Work");
        service.CreateProject("Archive");

        service.RenameProject(work.Id, "Current Work");

        Assert.Equal("Current Work", service.GetProjects().Single(p => p.Id == work.Id).Name);
    }
}
