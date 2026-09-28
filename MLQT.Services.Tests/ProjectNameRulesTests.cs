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
    public void TheRefusalForAnEmptyNameSaysWhatToDo()
    {
        // The message is the reason Validate returns a string rather than a bool: the screen showing
        // it and the service refusing have to say the same thing. Asserting only that it is non-null
        // let the text be emptied without any test noticing - found by mutation testing (B212).
        var refusal = ProjectNameRules.Validate("   ", Projects("Work"));

        Assert.NotNull(refusal);
        Assert.Contains("name", refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsAvailableAgreesWithValidate()
    {
        // IsAvailable had no test at all: inverting it to `is not null` killed nothing. A helper
        // that returns the exact opposite of its name is the kind of thing a suite should not be
        // able to miss (B212).
        var projects = Projects("Work", "Archive");

        Assert.True(ProjectNameRules.IsAvailable("Research", projects));
        Assert.False(ProjectNameRules.IsAvailable("Work", projects));
        Assert.False(ProjectNameRules.IsAvailable("   ", projects));
    }

    [Fact]
    public void IsAvailableHonoursTheIgnoredProject()
    {
        var projects = Projects("Work", "Archive");
        var work = projects[0];

        Assert.True(ProjectNameRules.IsAvailable("Work", projects, ignoringProjectId: work.Id));
        Assert.False(ProjectNameRules.IsAvailable("Archive", projects, ignoringProjectId: work.Id));
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
    public async Task CreateProject_RefusesANameAlreadyTaken()
    {
        var (service, _) = CreateService();
        await service.CreateProjectAsync("Work");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateProjectAsync("work"));
        Assert.Contains("Work", ex.Message);
    }

    [Fact]
    public async Task CreateProject_StillAcceptsADistinctName()
    {
        var (service, _) = CreateService();
        await service.CreateProjectAsync("Work");

        var second = await service.CreateProjectAsync("Archive");

        Assert.Equal("Archive", second.Name);
        Assert.Equal(2, service.GetProjects().Count);
    }

    [Fact]
    public async Task CreateProject_StoresTheTrimmedName()
    {
        var (service, _) = CreateService();

        var project = await service.CreateProjectAsync("  Work  ");

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
    public async Task RenameProject_RefusesAnotherProjectsName()
    {
        var (service, _) = CreateService();
        var work = await service.CreateProjectAsync("Work");
        await service.CreateProjectAsync("Archive");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenameProjectAsync(work.Id, "Archive"));
        Assert.Equal("Work", service.GetProjects().Single(p => p.Id == work.Id).Name);
    }

    [Fact]
    public async Task RenameProject_AllowsAProjectToKeepItsOwnName()
    {
        var (service, _) = CreateService();
        var work = await service.CreateProjectAsync("Work");

        await service.RenameProjectAsync(work.Id, "Work");

        Assert.Equal("Work", service.GetProjects().Single(p => p.Id == work.Id).Name);
    }

    [Fact]
    public async Task RenameProject_AllowsAnOrdinaryRename()
    {
        var (service, _) = CreateService();
        var work = await service.CreateProjectAsync("Work");
        await service.CreateProjectAsync("Archive");

        await service.RenameProjectAsync(work.Id, "Current Work");

        Assert.Equal("Current Work", service.GetProjects().Single(p => p.Id == work.Id).Name);
    }
}
