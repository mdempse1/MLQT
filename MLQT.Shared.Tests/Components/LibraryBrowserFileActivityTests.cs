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
/// What the browser does when the file monitor reports activity in its repository: the cached
/// working-copy status is dropped every time, and only asking again is throttled.
/// </summary>
/// <remarks>
/// Both used to be throttled together, so a second file saved within two seconds of the first left
/// the cache as the first event had refilled it. The commit dialog reads that cache rather than
/// asking SVN again, and did not list the second file.
/// </remarks>
public class LibraryBrowserFileActivityTests : MlqtComponentTestBase
{
    private readonly Mock<IRepositoryService> _repositories = new();
    private readonly Mock<IFileMonitoringService> _monitor = new();

    private static readonly Repository Repo = new()
    {
        Id = "repo-1",
        Name = "Lib",
        LocalPath = Path.Combine(Path.GetTempPath(), "mlqt-tests", "Lib"),
        VcsType = RepositoryVcsType.SVN,
        CurrentBranch = "trunk",
    };

    private void RenderBrowser()
    {
        var library = new Mock<ILibraryDataService>();
        library.SetupGet(l => l.CombinedGraph).Returns(new DirectedGraph());
        library.Setup(l => l.GetTopLevelModelsAsync()).ReturnsAsync(new List<ModelNode>());
        library.Setup(l => l.GetChildModelsAsync(It.IsAny<ModelNode>())).ReturnsAsync(new List<ModelNode>());

        var classifier = new Mock<IModelChangeClassifier>();
        classifier.Setup(c => c.Classify(It.IsAny<Repository>(), It.IsAny<IReadOnlyList<VcsWorkingCopyFile>>()))
                  .Returns(new Dictionary<string, ClassChangeKind>());

        Services.AddSingleton(library.Object);
        Services.AddSingleton(_repositories.Object);
        Services.AddSingleton(_monitor.Object);
        Services.AddSingleton(classifier.Object);

        Render<MudBlazor.MudPopoverProvider>();
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudSnackbarProvider>();
        Render<LibraryBrowser>(p => p.Add(c => c.LibraryOnly, false).Add(c => c.Repository, Repo));
    }

    [Fact]
    public void EveryFileEvent_DropsTheCachedStatus_EvenInsideTheThrottle()
    {
        RenderBrowser();
        _repositories.Invocations.Clear();

        _monitor.Raise(m => m.OnRepositoryFileActivity += null, "repo-1");
        _monitor.Raise(m => m.OnRepositoryFileActivity += null, "repo-1");
        _monitor.Raise(m => m.OnRepositoryFileActivity += null, "repo-1");

        _repositories.Verify(r => r.InvalidateWorkingCopyCache("repo-1"), Times.Exactly(3));
    }

    [Fact]
    public void AnotherRepositorysEvent_LeavesThisOnesStatusAlone()
    {
        RenderBrowser();
        _repositories.Invocations.Clear();

        _monitor.Raise(m => m.OnRepositoryFileActivity += null, "another-repo");

        _repositories.Verify(r => r.InvalidateWorkingCopyCache(It.IsAny<string>()), Times.Never);
    }
}
