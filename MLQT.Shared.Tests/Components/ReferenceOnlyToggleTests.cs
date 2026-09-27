using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Comparison;
using ModelicaParser.SpellChecking;
using Moq;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// B354 — ticking or unticking "Reference only" takes effect in the browser at once.
/// </summary>
/// <remarks>
/// A reference-only repository has no working-copy status shown (B191), and the toggle only set the
/// flag, on the same object the browser holds - so no parameter changed and nothing asked again.
/// Unticking it left Commit and Revert disabled and no change markers until something unrelated
/// refreshed; ticking it left the old markers standing.
/// </remarks>
public class ReferenceOnlyToggleTests : MlqtComponentTestBase
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mlqt-tests", "Toggle");

    [Fact]
    public void UntickingReferenceOnly_ShowsTheRepositorysChanges()
    {
        var graph = new DirectedGraph();
        var path = Path.Combine(Root, "A.mo");
        var fileId = GraphBuilder.GenerateFileId(path);
        var model = new ModelNode("A", "A", "model A end A;");
        graph.AddNode(new FileNode(fileId, path));
        graph.AddNode(model);
        graph.AddFileContainsModel(fileId, model.Id);

        var library = new Mock<ILibraryDataService>();
        library.SetupGet(l => l.CombinedGraph).Returns(graph);
        library.Setup(l => l.GetTopLevelModelsAsync()).ReturnsAsync(new List<ModelNode>());
        library.Setup(l => l.GetChildModelsAsync(It.IsAny<ModelNode>())).ReturnsAsync(new List<ModelNode>());
        library.Setup(l => l.GetModelById(It.IsAny<string>())).Returns<string>(id => graph.GetNode<ModelNode>(id));

        var repositories = new Mock<IRepositoryService>();
        repositories.Setup(r => r.GetWorkingCopyChanges("repo-1"))
            .Returns([new VcsWorkingCopyFile { Path = "A.mo", Status = VcsFileStatus.Modified }]);

        var classifier = new Mock<IModelChangeClassifier>();
        classifier.Setup(c => c.Classify(It.IsAny<Repository>(), It.IsAny<IReadOnlyList<VcsWorkingCopyFile>>()))
            .Returns(new Dictionary<string, ClassChangeKind> { ["A"] = ClassChangeKind.AffectsSimulation });

        Services.AddSingleton(library.Object);
        Services.AddSingleton(repositories.Object);
        Services.AddSingleton(new Mock<IFileMonitoringService>().Object);
        Services.AddSingleton(classifier.Object);
        RenderProviders();

        var repository = new Repository
        {
            Id = "repo-1", Name = "Vendor", LocalPath = Root, VcsRootPath = Root,
            VcsType = RepositoryVcsType.Git, CurrentBranch = "main", CurrentRevision = "a1b2c3",
            IsReferenceOnly = true,
        };

        var browser = Render<LibraryBrowser>(p => p
            .Add(c => c.LibraryOnly, false)
            .Add(c => c.Repository, repository));

        // Reference only: not asked.
        Assert.Equal(0, browser.Instance.CountFor(LibraryBrowser.ChangeFilter.Changed));
        repositories.Verify(r => r.GetWorkingCopyChanges(It.IsAny<string>()), Times.Never);

        // What RepositoryService.SetReferenceOnly does: change the flag on the live object and say so.
        repository.IsReferenceOnly = false;
        repositories.Raise(r => r.OnRepositoriesChanged += null);

        browser.WaitForAssertion(() =>
            Assert.Equal(1, browser.Instance.CountFor(LibraryBrowser.ChangeFilter.Changed)));

        // And back: its markers go with it.
        repository.IsReferenceOnly = true;
        repositories.Raise(r => r.OnRepositoriesChanged += null);

        browser.WaitForAssertion(() =>
            Assert.Equal(0, browser.Instance.CountFor(LibraryBrowser.ChangeFilter.Changed)));
    }

    [Fact]
    public void TheSettingsToggle_GoesThroughTheService()
    {
        // The service is what starts and stops the watch and announces the change; setting the flag
        // here alone was the defect.
        var path = Path.Combine(Path.GetTempPath(), "mlqt-tests", "Vendor");
        var repository = new Repository { Id = "repo-1", Name = "Vendor", LocalPath = path, VcsRootPath = path };
        var project = new ProjectProfile { Name = "Default" };

        var service = new Mock<IRepositoryService>();
        service.SetupGet(s => s.Repositories).Returns([repository]);
        service.Setup(s => s.GetProjects()).Returns([project]);
        service.Setup(s => s.GetActiveProject()).Returns(project);

        var dictionaries = new Mock<IDictionaryManagerService>();
        dictionaries.Setup(d => d.GetAvailableDictionaries()).Returns([]);

        Services.AddSingleton(service.Object);
        Services.AddSingleton(dictionaries.Object);
        Services.AddSingleton(new Mock<ISettingsService>().Object);
        Services.AddSingleton(new Mock<IFilePickerService>().Object);
        RenderProviders();

        var panel = Render<SettingsRepositories>();
        panel.InvokeAsync(() =>
        {
            panel.Instance.OnRepoClick(repository);
            panel.Instance.OnReferenceOnlyChanged(true);
        }).Wait();

        service.Verify(s => s.SetReferenceOnly("repo-1", true), Times.Once);
    }

    /// <summary>
    /// B405: the toggle takes effect at once rather than on Apply, so Cancel has to undo it - and
    /// through the service, which is what stops or starts the watch it started or stopped.
    /// </summary>
    [Fact]
    public void Cancel_UndoesTheToggle_ThroughTheService()
    {
        var (panel, service, repository) = RenderPanel();

        panel.InvokeAsync(() =>
        {
            panel.Instance.OnRepoClick(repository);
            panel.Instance.OnReferenceOnlyChanged(true);
            panel.Instance.CancelChanges();
        }).Wait();

        Assert.False(repository.IsReferenceOnly);
        service.Verify(s => s.SetReferenceOnly("repo-1", false), Times.Once);
    }

    [Fact]
    public void Cancel_WithoutTouchingTheToggle_LeavesItAlone()
    {
        var (panel, service, repository) = RenderPanel();

        panel.InvokeAsync(() =>
        {
            panel.Instance.OnRepoClick(repository);
            panel.Instance.CancelChanges();
        }).Wait();

        service.Verify(s => s.SetReferenceOnly(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    private (IRenderedComponent<SettingsRepositories> Panel, Mock<IRepositoryService> Service, Repository Repository)
        RenderPanel()
    {
        var path = Path.Combine(Path.GetTempPath(), "mlqt-tests", "Vendor");
        var repository = new Repository { Id = "repo-1", Name = "Vendor", LocalPath = path, VcsRootPath = path };
        var project = new ProjectProfile { Name = "Default" };

        var service = new Mock<IRepositoryService>();
        service.SetupGet(s => s.Repositories).Returns([repository]);
        service.Setup(s => s.GetProjects()).Returns([project]);
        service.Setup(s => s.GetActiveProject()).Returns(project);

        var dictionaries = new Mock<IDictionaryManagerService>();
        dictionaries.Setup(d => d.GetAvailableDictionaries()).Returns([]);

        Services.AddSingleton(service.Object);
        Services.AddSingleton(dictionaries.Object);
        Services.AddSingleton(new Mock<ISettingsService>().Object);
        Services.AddSingleton(new Mock<IFilePickerService>().Object);
        RenderProviders();

        return (Render<SettingsRepositories>(), service, repository);
    }
}
