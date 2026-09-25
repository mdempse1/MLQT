using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Comparison;
using Moq;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// B358 — with a change filter on, revealing a class opens the tree the user is looking at.
/// </summary>
/// <remarks>
/// <see cref="LibraryBrowser"/>'s reveal (B189) expanded the unfiltered tree, while the view showed
/// the filtered one, whose <c>Expanded</c> flags were fixed when it was built. So clicking a finding
/// on a changed class under <b>Changed</b> left the tree closed.
/// </remarks>
public class LibraryBrowserFilteredRevealTests : MlqtComponentTestBase
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mlqt-tests", "Reveal");

    [Fact]
    public void RevealingAChangedClassUnderAFilter_OpensTheFilteredTree()
    {
        var graph = new DirectedGraph();
        var lib = new ModelNode("Lib", "Lib", "package Lib end Lib;") { ClassType = "package", LibraryId = "lib-1" };
        var pack = new ModelNode("Lib.Pack", "Pack", "package Pack end Pack;")
            { ClassType = "package", ParentModelName = "Lib", LibraryId = "lib-1" };
        var changed = new ModelNode("Lib.Pack.A", "A", "model A end A;")
            { ClassType = "model", ParentModelName = "Lib.Pack", LibraryId = "lib-1" };

        var path = Path.Combine(Root, "Lib", "Pack", "A.mo");
        var fileId = GraphBuilder.GenerateFileId(path);
        graph.AddNode(new FileNode(fileId, path));
        foreach (var model in new[] { lib, pack, changed })
            graph.AddNode(model);
        graph.AddFileContainsModel(fileId, changed.Id);

        var library = new Mock<ILibraryDataService>();
        library.SetupGet(l => l.CombinedGraph).Returns(graph);
        library.Setup(l => l.GetTopLevelModelsAsync()).ReturnsAsync(new List<ModelNode> { lib });
        library.Setup(l => l.GetChildModelsAsync(It.IsAny<ModelNode>())).ReturnsAsync((ModelNode? parent) =>
            parent?.Id switch
            {
                "Lib" => new List<ModelNode> { pack },
                "Lib.Pack" => new List<ModelNode> { changed },
                _ => new List<ModelNode>(),
            });
        library.Setup(l => l.ModelHasChildren(It.IsAny<string>())).Returns<string>(id => id != changed.Id);
        library.Setup(l => l.GetModelById(It.IsAny<string>())).Returns<string>(id => graph.GetNode<ModelNode>(id));
        library.Setup(l => l.ModelsWithDescendantParserErrors()).Returns(new HashSet<string>());

        var repositories = new Mock<IRepositoryService>();
        repositories.Setup(r => r.GetWorkingCopyChanges("repo-1"))
            .Returns([new VcsWorkingCopyFile { Path = "Lib/Pack/A.mo", Status = VcsFileStatus.Modified }]);

        var classifier = new Mock<IModelChangeClassifier>();
        classifier.Setup(c => c.Classify(It.IsAny<Repository>(), It.IsAny<IReadOnlyList<VcsWorkingCopyFile>>()))
            .Returns(new Dictionary<string, ClassChangeKind> { [changed.Id] = ClassChangeKind.AffectsSimulation });

        Services.AddSingleton(library.Object);
        Services.AddSingleton(repositories.Object);
        Services.AddSingleton(new Mock<IFileMonitoringService>().Object);
        Services.AddSingleton(classifier.Object);
        RenderProviders();

        var repository = new Repository
        {
            Id = "repo-1", Name = "Lib", LocalPath = Root, VcsRootPath = Root,
            VcsType = RepositoryVcsType.Git, CurrentBranch = "main", CurrentRevision = "a1b2c3",
            LibraryIds = ["lib-1"],
        };

        var browser = Render<LibraryBrowser>(p => p
            .Add(c => c.LibraryOnly, false)
            .Add(c => c.Repository, repository));
        browser.WaitForAssertion(() =>
            Assert.Equal(1, browser.Instance.CountFor(LibraryBrowser.ChangeFilter.Changed)));

        browser.InvokeAsync(() => browser.Instance.OnChangeFilterChanged(LibraryBrowser.ChangeFilter.Changed)).Wait();

        // Nothing is open yet: the filtered tree opens exactly as far as the user had the tree open.
        var closed = Assert.Single(browser.Instance.ActiveTreeItems);
        Assert.False(closed.Expanded);

        // Opened from somewhere else - a finding, say.
        NavState.ChangeModelID(changed.Id);

        browser.WaitForAssertion(() =>
        {
            var root = Assert.Single(browser.Instance.ActiveTreeItems);
            Assert.True(root.Expanded, "the library was not opened in the filtered tree");
            var package = Assert.Single(root.Children!);
            Assert.True(package.Expanded, "the package holding the class was not opened in the filtered tree");
        });
    }
}
