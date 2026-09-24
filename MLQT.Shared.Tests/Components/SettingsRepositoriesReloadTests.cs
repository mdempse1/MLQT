using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using Moq;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// <b>Load project</b> for the project that is already active, offered once a repository has been
/// removed from it (B268).
/// </summary>
/// <remarks>
/// Removing a repository can leave a library unloaded that it was standing in for: an encrypted
/// build that was never loaded because the removed repository held its source. Nothing brings that
/// back by itself, and loading the project again is an existing path that does — so the button the
/// other projects already have is offered on the active one while the service says a reload could
/// give a different answer.
/// </remarks>
public class SettingsRepositoriesReloadTests : MlqtComponentTestBase
{
    private const string ReloadLabel = "Load project again";

    private (IRenderedComponent<SettingsRepositories> Panel, Mock<IRepositoryService> Service, ProjectProfile Project)
        RenderPanel(bool repositoryRemoved)
    {
        var project = new ProjectProfile { Name = "Default" };

        var service = new Mock<IRepositoryService>();
        service.SetupGet(s => s.Repositories).Returns([]);
        service.Setup(s => s.GetProjects()).Returns([project]);
        service.Setup(s => s.GetActiveProject()).Returns(project);
        service.SetupGet(s => s.RepositoryRemovedSinceProjectLoad).Returns(repositoryRemoved);

        var dictionaries = new Mock<IDictionaryManagerService>();
        dictionaries.Setup(d => d.GetAvailableDictionaries()).Returns([]);

        Services.AddSingleton(service.Object);
        Services.AddSingleton(dictionaries.Object);
        Services.AddSingleton(new Mock<ISettingsService>().Object);
        Services.AddSingleton(new Mock<IFilePickerService>().Object);

        RenderProviders();
        return (Render<SettingsRepositories>(), service, project);
    }

    [Fact]
    public void TheActiveProjectIsNotOfferedForReloading_ByDefault()
    {
        var (panel, _, _) = RenderPanel(repositoryRemoved: false);

        Assert.Empty(panel.FindAll($"button[aria-label='{ReloadLabel}']"));
    }

    [Fact]
    public void OnceARepositoryIsRemoved_TheActiveProjectCanBeLoadedAgain()
    {
        var (panel, service, project) = RenderPanel(repositoryRemoved: true);

        panel.Find($"button[aria-label='{ReloadLabel}']").Click();

        service.Verify(s => s.SwitchProjectAsync(project.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
