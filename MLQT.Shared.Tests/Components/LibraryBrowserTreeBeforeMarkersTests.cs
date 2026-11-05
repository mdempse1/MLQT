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

    /// <summary>
    /// A browser over one changed library whose classification is held until <c>Release</c> is set.
    /// </summary>
    private sealed record Held(
        IRenderedComponent<LibraryBrowser> Browser, ModelNode Library, Repository Repository,
        ManualResetEventSlim Started, ManualResetEventSlim Release);

    private Held RenderWithClassificationHeld()
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

        return new Held(browser, library, repository, started, release);
    }

    [Fact]
    public async Task TheTreeIsShown_WhileTheChangesAreStillBeingClassified()
    {
        var (browser, library, _, started, release) = RenderWithClassificationHeld();

        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken),
                "the changes were never classified");

            // Shown while the classification is still running - and marked modified already, from the
            // status alone, without saying how.
            browser.WaitForAssertion(() =>
                Assert.Contains(browser.Instance.ActiveTreeItems, item => item.Value?.Id == "Lib"));
            browser.WaitForAssertion(() =>
                Assert.Equal(ChangeMarker.UnknownTooltip, browser.Instance.MarkerFor(library).Tooltip));
        }
        finally
        {
            release.Set();
        }

        // And the kind follows once it has finished.
        browser.WaitForAssertion(() =>
            Assert.Equal(ChangeMarker.For(VcsFileStatus.Modified, ClassChangeKind.AffectsSimulation, ClassChangeKind.Unchanged),
                browser.Instance.MarkerFor(library)));
        await browser.InvokeAsync(() => { });
    }

    [Fact]
    public async Task AVcsOperation_DoesNotWaitForTheClassification()
    {
        // Every VCS operation refreshes the markers while it holds the VCS lock. Waiting for the
        // classification kept every VCS button disabled for as long as the comparison took - for an
        // answer none of the operations uses.
        var (browser, _, repository, started, release) = RenderWithClassificationHeld();
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken));

            // A commit dialog closed without touching the working copy: a status refresh.
            var refresh = browser.InvokeAsync(() =>
                browser.Instance.AfterVcsDialogAsync(repository, new MLQT.Shared.Dialogs.VcsDialogOutcome(), "commit"));

            var finished = await Task.WhenAny(refresh, Task.Delay(TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken));
            Assert.Same(refresh, finished);
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>
    /// A browser over one library in a changed file, with a classifier answering through
    /// <paramref name="classify"/> (given how many times it has been asked) and the working copy's
    /// changes read from <paramref name="changes"/> each time.
    /// </summary>
    private (IRenderedComponent<LibraryBrowser> Browser, ModelNode Library, Mock<ILibraryDataService> Libraries,
        Mock<IModelChangeClassifier> Classifier)
        RenderWith(Func<int, IReadOnlyDictionary<string, ClassChangeKind>> classify, List<VcsWorkingCopyFile> changes)
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
        repositories.Setup(r => r.GetWorkingCopyChanges("repo-1")).Returns(() => changes.ToList());

        var asked = 0;
        var classifier = new Mock<IModelChangeClassifier>();
        classifier.Setup(c => c.Classify(It.IsAny<Repository>(), It.IsAny<IReadOnlyList<VcsWorkingCopyFile>>()))
            .Returns(() => classify(Interlocked.Increment(ref asked)));

        Services.AddSingleton(libraries.Object);
        Services.AddSingleton(repositories.Object);
        Services.AddSingleton(new Mock<IFileMonitoringService>().Object);
        Services.AddSingleton(classifier.Object);
        RenderProviders();

        var browser = Render<LibraryBrowser>(p => p
            .Add(c => c.LibraryOnly, false)
            .Add(c => c.Repository, new Repository
            {
                Id = "repo-1", Name = "Repo", LocalPath = Root, VcsRootPath = Root,
                VcsType = RepositoryVcsType.SVN, LibraryIds = ["lib-1"],
            }));
        return (browser, library, libraries, classifier);
    }

    private static readonly IReadOnlyDictionary<string, ClassChangeKind> LibAffectsSimulation =
        new Dictionary<string, ClassChangeKind> { ["Lib"] = ClassChangeKind.AffectsSimulation };

    [Fact]
    public async Task AnOvertakenAnswer_DoesNotTakeAwayTheKindsTheTreeHas()
    {
        // An overtaken classification answers "nothing", and applied as an answer it took every kind
        // away - every chip back to a plain M, the Simulation and Cosmetic counts to 0 - until the
        // next refresh. Here every request after the first is overtaken, retries included.
        // Signalled by the classifier, not waited for by render: an overtaken answer is not applied,
        // so the last calls draw nothing, and a wait that re-checks on render never saw them under a
        // loaded machine.
        var allAsked = new ManualResetEventSlim();
        var (browser, library, libraries, classifier) = RenderWith(
            asked =>
            {
                if (asked == 5)
                    allAsked.Set();
                return asked == 1 ? LibAffectsSimulation : IModelChangeClassifier.Overtaken;
            },
            [new VcsWorkingCopyFile { Path = "Lib/package.mo", Status = VcsFileStatus.Modified }]);
        var classified = ChangeMarker.For(VcsFileStatus.Modified, ClassChangeKind.AffectsSimulation, ClassChangeKind.Unchanged);
        browser.WaitForAssertion(() => Assert.Equal(classified, browser.Instance.MarkerFor(library)));

        libraries.Raise(l => l.OnTreeDataChanged += null);

        // The first answer, the refresh's own, and its three retries.
        Assert.True(allAsked.Wait(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken),
            "the refresh did not ask the classifier, and retry, as expected");
        classifier.Verify(
            c => c.Classify(It.IsAny<Repository>(), It.IsAny<IReadOnlyList<VcsWorkingCopyFile>>()), Times.Exactly(5));
        await browser.InvokeAsync(() => { });
        Assert.Equal(classified, browser.Instance.MarkerFor(library));
        Assert.Equal(1, browser.Instance.CountFor(LibraryBrowser.ChangeFilter.AffectsSimulation));
    }

    [Fact]
    public void ARefreshFindingNoChanges_StillTellsTheClassifier()
    {
        // So a classification still running for the changes there were - before a commit - is
        // overtaken and stops, rather than holding the checkout's gate to the end.
        var (browser, _, _, classifier) = RenderWith(_ => new Dictionary<string, ClassChangeKind>(), []);

        browser.WaitForAssertion(() => classifier.Verify(
            c => c.Classify(It.IsAny<Repository>(), It.Is<IReadOnlyList<VcsWorkingCopyFile>>(l => l.Count == 0)),
            Times.Once));
    }

    [Fact]
    public void TheSuppressDialog_SaysWhenAnEnclosingClassIsAnFmuImport()
    {
        // A record inside an FMU model has no findings and no list of its own; the dialog says why.
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("P", "P", "model P\n  annotation(__Dymola_FMUImportPath=\"a.fmu\");\nend P;"));
        graph.AddNode(new ModelNode("P.R", "R", "record R\nend R;"));

        var notes = MLQT.Shared.Pages.CodeReview.EnclosingSuppressionNotes(graph, "P.R");

        Assert.Equal([MLQT.Shared.Pages.CodeReview.GeneratedImportNote("P")], notes);
    }

    [Fact]
    public void TheUnknownTooltip_NamesTheClassifiersLimit()
    {
        Assert.Contains(
            MLQT.Services.ModelChangeClassifier.DefaultMaxFilesToClassify.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
            ChangeMarker.UnknownTooltip);
    }

    // ---- what a class shows between the status and the classification ------------------------

    [Fact]
    public void AClassStillChanged_KeepsTheKindItHad()
    {
        var kept = LibraryBrowser.KindsStillChanged(
            new Dictionary<string, ClassChangeKind> { ["A"] = ClassChangeKind.Cosmetic },
            new Dictionary<string, VcsFileStatus> { ["A"] = VcsFileStatus.Modified });

        Assert.Equal(ClassChangeKind.Cosmetic, kept["A"]);
    }

    [Fact]
    public void AClassNoLongerChanged_LosesItsKindAtOnce()
    {
        // What a commit or a revert does: the class leaves the status, and its chip has to go with
        // it rather than wait for the next classification.
        var kept = LibraryBrowser.KindsStillChanged(
            new Dictionary<string, ClassChangeKind> { ["A"] = ClassChangeKind.Cosmetic, ["B"] = ClassChangeKind.AffectsSimulation },
            new Dictionary<string, VcsFileStatus> { ["B"] = VcsFileStatus.Modified });

        Assert.DoesNotContain("A", kept);
        Assert.Equal(ClassChangeKind.AffectsSimulation, kept["B"]);
    }
}
