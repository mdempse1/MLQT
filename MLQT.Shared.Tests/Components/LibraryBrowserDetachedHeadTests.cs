using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using Moq;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// B193 — what the browser says when there is no branch to name.
/// </summary>
/// <remarks>
/// <para>Checking out a tag leaves Git with a detached HEAD, so <c>CurrentBranch</c> is correctly
/// null and the browser showed the words "Detached HEAD" and nothing else. That is the state a user
/// reaches by doing the thing this item added — switching to a released version — so leaving them to
/// work out <i>which</i> version they are looking at would make the feature worse than not having
/// it.</para>
/// </remarks>
public class LibraryBrowserDetachedHeadTests : MlqtComponentTestBase
{
    private static Repository Repo(string? branch, string? detachedLabel) =>
        new()
        {
            Id = "repo-1",
            Name = "ExternData",
            LocalPath = Path.Combine(Path.GetTempPath(), "mlqt-tests", "ExternData"),
            VcsType = RepositoryVcsType.Git,
            CurrentRevision = "a1b2c3d4e5f6",
            CurrentBranch = branch,
            DetachedHeadLabel = detachedLabel,
        };

    private IRenderedComponent<LibraryBrowser> RenderBrowser(Repository repository)
    {
        var library = new Mock<ILibraryDataService>();
        library.SetupGet(l => l.CombinedGraph).Returns(new DirectedGraph());
        library.Setup(l => l.GetTopLevelModelsAsync()).ReturnsAsync(new List<ModelNode>());
        library.Setup(l => l.GetChildModelsAsync(It.IsAny<ModelNode>()))
               .ReturnsAsync(new List<ModelNode>());

        Services.AddSingleton(library.Object);
        Services.AddSingleton(new Mock<IRepositoryService>().Object);
        Services.AddSingleton(new Mock<IFileMonitoringService>().Object);

        // The browser asks this what kind of change each model carries (B191). Nothing here has
        // working-copy changes, so the stand-in is never called - it just has to be resolvable.
        Services.AddSingleton(new Mock<IModelChangeClassifier>().Object);

        RenderProviders();

        return Render<LibraryBrowser>(p => p
            .Add(c => c.LibraryOnly, false)
            .Add(c => c.Repository, repository));
    }

    [Fact]
    public void ATagIsNamedWhereTheBranchNameWouldBe()
    {
        var browser = RenderBrowser(Repo(branch: null, detachedLabel: "v2.0.0"));

        Assert.Contains("Detached HEAD at", browser.Markup);
        Assert.Contains("v2.0.0", browser.Markup);
    }

    [Fact]
    public void WithNothingToNameItStillSaysDetachedHead()
    {
        // A revision checked out directly, or a repository MLQT could not ask. The words on their
        // own are still better than a blank, which is what this replaced.
        var browser = RenderBrowser(Repo(branch: null, detachedLabel: null));

        Assert.Contains("Detached HEAD", browser.Markup);
        Assert.DoesNotContain("Detached HEAD at", browser.Markup);
    }

    [Fact]
    public void OnABranchTheBranchIsNamedAndNothingIsDetached()
    {
        var browser = RenderBrowser(Repo(branch: "main", detachedLabel: null));

        Assert.Contains("Current branch", browser.Markup);
        Assert.Contains("main", browser.Markup);
        Assert.DoesNotContain("Detached HEAD", browser.Markup);
    }
}
