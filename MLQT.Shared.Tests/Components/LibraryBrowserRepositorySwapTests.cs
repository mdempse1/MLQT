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
/// B353 — a browser's state belongs to one repository, and stays with it when the list is reordered.
/// </summary>
/// <remarks>
/// <para>MainLayout rendered one <c>LibraryBrowser</c> per repository with no <c>@key</c>, so after
/// a repository was moved up or down (B188) Blazor reused the browsers by position and handed each a
/// different <c>Repository</c>. Nothing in the browser resets its change filter, busy flag, loading
/// flag or expansion for that, so all of it followed the position rather than the repository.</para>
///
/// <para>And a working-copy status query in flight across the swap wrote the first repository's
/// changes into the second's browser when it came back - B299's shape, in a sibling that was not
/// fixed with it.</para>
/// </remarks>
public class LibraryBrowserRepositorySwapTests : MlqtComponentTestBase
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mlqt-tests", "Swap");

    private static Repository Repo(string id) =>
        new()
        {
            Id = id,
            Name = id,
            LocalPath = Root,
            VcsRootPath = Root,
            VcsType = RepositoryVcsType.Git,
            CurrentBranch = "main",
            CurrentRevision = "a1b2c3d4e5f6",
        };

    [Fact]
    public void AStatusQueryThatReturnsAfterTheRepositoryChanged_IsDiscarded()
    {
        // One class, in a file repo-a reports as modified and repo-b does not.
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

        // repo-a's query is held until repo-b's browser has been shown.
        var release = new ManualResetEventSlim();
        var repositories = new Mock<IRepositoryService>();
        repositories.Setup(r => r.GetWorkingCopyChanges("repo-a")).Returns(() =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return [new VcsWorkingCopyFile { Path = "A.mo", Status = VcsFileStatus.Modified }];
        });
        repositories.Setup(r => r.GetWorkingCopyChanges("repo-b")).Returns(new List<VcsWorkingCopyFile>());

        var classified = new ManualResetEventSlim();
        var classifier = new Mock<IModelChangeClassifier>();
        classifier.Setup(c => c.Classify(It.IsAny<Repository>(), It.IsAny<IReadOnlyList<VcsWorkingCopyFile>>()))
            .Returns(() =>
            {
                classified.Set();
                return new Dictionary<string, ClassChangeKind> { ["A"] = ClassChangeKind.AffectsSimulation };
            });

        Services.AddSingleton(library.Object);
        Services.AddSingleton(repositories.Object);
        Services.AddSingleton(new Mock<IFileMonitoringService>().Object);
        Services.AddSingleton(classifier.Object);
        RenderProviders();

        var browser = Render<LibraryBrowser>(p => p
            .Add(c => c.LibraryOnly, false)
            .Add(c => c.Repository, Repo("repo-a")));

        // The same component, given the other repository - which is what a reorder did.
        browser.Render(p => p.Add(c => c.Repository, Repo("repo-b")));
        browser.WaitForAssertion(() => repositories.Verify(r => r.GetWorkingCopyChanges("repo-b"), Times.Once));

        release.Set();
        Assert.True(classified.Wait(TimeSpan.FromSeconds(10)), "repo-a's query never came back");

        // Let the late answer reach the dispatcher, where it used to be applied.
        Thread.Sleep(200);
        browser.InvokeAsync(() => { }).Wait();

        Assert.Equal(0, browser.Instance.CountFor(LibraryBrowser.ChangeFilter.Changed));
        Assert.Equal(ChangeMarker.None, browser.Instance.MarkerFor(model));
    }

    /// <summary>
    /// The browsers are keyed by repository, so a reorder moves each browser - and everything it
    /// holds - with its repository instead of handing it to whichever one now sits in its place.
    /// </summary>
    [Fact]
    public void TheRepositoryBrowsersAreKeyedByRepository()
    {
        var markup = File.ReadAllText(Path.Combine(SharedDirectory(), "Layout", "MainLayout.razor"));

        var browsers = markup.Split('\n')
            .Where(line => line.Contains("<LibraryBrowser", StringComparison.Ordinal)
                           && line.Contains("Repository=", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(browsers);
        Assert.All(browsers, line => Assert.Contains("@key=\"repo.Id\"", line));
    }

    private static string SharedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "MLQT.Shared");
            if (File.Exists(Path.Combine(candidate, "_Imports.razor")))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("MLQT.Shared sources not found from " + AppContext.BaseDirectory);
    }
}
