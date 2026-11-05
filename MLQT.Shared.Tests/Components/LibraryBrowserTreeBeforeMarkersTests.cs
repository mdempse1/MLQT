using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using MLQT.Shared.Helpers;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Comparison;
using Moq;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// The tree is drawn before the change markers are worked out, never after.
/// </summary>
/// <remarks>
/// Working out the markers classifies every changed file against its committed version. With
/// 17,372 changed files in an SVN checkout that took eighteen minutes, and because the browser asked
/// for the markers first, the repository's libraries - loaded, and logged as loaded - did not appear
/// until it finished.
/// </remarks>
public class LibraryBrowserTreeBeforeMarkersTests : MlqtComponentTestBase
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mlqt-tests", "TreeBeforeMarkers");

    [Fact]
    public async Task TheTreeIsShown_WhileTheChangesAreStillBeingClassified()
    {
        var graph = new DirectedGraph();
        var path = Path.Combine(Root, "Lib", "package.mo");
        var fileId = GraphBuilder.GenerateFileId(path);
        var library = new ModelNode("Lib", "Lib", "package Lib end Lib;") { LibraryId = "lib-1", ClassType = "package" };
        graph.AddNode(new FileNode(fileId, path));
        graph.AddNode(library);
        graph.AddFileContainsModel(fileId, library.Id);

        var libraries = new Mock<ILibraryDataService>();
        libraries.SetupGet(l => l.CombinedGraph).Returns(graph);
        libraries.Setup(l => l.GetTopLevelModelsAsync()).ReturnsAsync(new List<ModelNode> { library });
        libraries.Setup(l => l.GetChildModelsAsync(It.IsAny<ModelNode>())).ReturnsAsync(new List<ModelNode>());
        libraries.Setup(l => l.GetModelById(It.IsAny<string>())).Returns<string>(id => graph.GetNode<ModelNode>(id));
        libraries.Setup(l => l.ModelsWithDescendantParserErrors()).Returns(new HashSet<string>());

        var repositories = new Mock<IRepositoryService>();
        repositories.Setup(r => r.GetWorkingCopyChanges("repo-1"))
            .Returns([new VcsWorkingCopyFile { Path = "Lib/package.mo", Status = VcsFileStatus.Modified }]);

        // The classification is held until the tree has been looked at.
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var classifier = new Mock<IModelChangeClassifier>();
        classifier.Setup(c => c.Classify(It.IsAny<Repository>(), It.IsAny<IReadOnlyList<VcsWorkingCopyFile>>()))
            .Returns(() =>
            {
                started.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                return new Dictionary<string, ClassChangeKind> { ["Lib"] = ClassChangeKind.AffectsSimulation };
            });

        Services.AddSingleton(libraries.Object);
        Services.AddSingleton(repositories.Object);
        Services.AddSingleton(new Mock<IFileMonitoringService>().Object);
        Services.AddSingleton(classifier.Object);
        RenderProviders();

        var repository = new Repository
        {
            Id = "repo-1",
            Name = "Repo",
            LocalPath = Root,
            VcsRootPath = Root,
            VcsType = RepositoryVcsType.SVN,
            LibraryIds = ["lib-1"],
        };

        var browser = Render<LibraryBrowser>(p => p
            .Add(c => c.LibraryOnly, false)
            .Add(c => c.Repository, repository));

        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken),
                "the changes were never classified");

            // Shown while the classification is still running.
            browser.WaitForAssertion(() =>
                Assert.Contains(browser.Instance.ActiveTreeItems, item => item.Value?.Id == "Lib"));
            Assert.Equal(ChangeMarker.None, browser.Instance.MarkerFor(library));
        }
        finally
        {
            release.Set();
        }

        // And the marker follows once it has finished.
        browser.WaitForAssertion(() => Assert.NotEqual(ChangeMarker.None, browser.Instance.MarkerFor(library)));
        await browser.InvokeAsync(() => { });
    }
}
