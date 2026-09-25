using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using MLQT.Shared.Dialogs;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using Moq;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// B294 — what Update leaves behind when it fails, and what it lets the user start while it runs.
/// </summary>
/// <remarks>
/// <para>Update stops the file monitor before it touches the working copy, and only the analysis
/// pipeline it hands over to at the end starts it again. Anything that threw or hung before the hand
/// over left the repository unwatched for the rest of the session, with nothing on screen to say
/// so.</para>
///
/// <para>And nothing stopped a second VCS operation starting over the first: on MSL a Revert was
/// still reloading its files when Update began removing and reloading every library in the same
/// repository.</para>
/// </remarks>
public class LibraryBrowserUpdateTests : MlqtComponentTestBase
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mlqt-tests", "MSL");

    private readonly Mock<IRepositoryService> _repositories = new();
    private readonly Mock<IFileMonitoringService> _monitor = new();

    private static Repository Repo() =>
        new()
        {
            Id = "repo-1",
            Name = "MSL",
            LocalPath = Root,
            VcsRootPath = Root,
            VcsType = RepositoryVcsType.Git,
            CurrentBranch = "main",
            CurrentRevision = "a1b2c3d4e5f6",
        };

    private IRenderedComponent<LibraryBrowser> RenderBrowser()
    {
        var library = new Mock<ILibraryDataService>();
        library.SetupGet(l => l.CombinedGraph).Returns(new DirectedGraph());
        library.Setup(l => l.GetTopLevelModelsAsync()).ReturnsAsync(new List<ModelNode>());
        library.Setup(l => l.GetChildModelsAsync(It.IsAny<ModelNode>())).ReturnsAsync(new List<ModelNode>());

        _repositories.Setup(r => r.GetWorkingCopyChanges(It.IsAny<string>())).Returns(new List<VcsWorkingCopyFile>());

        Services.AddSingleton(library.Object);
        Services.AddSingleton(_repositories.Object);
        Services.AddSingleton(_monitor.Object);
        Services.AddSingleton(new Mock<IModelChangeClassifier>().Object);

        RenderProviders();

        return Render<LibraryBrowser>(p => p
            .Add(c => c.LibraryOnly, false)
            .Add(c => c.Repository, Repo()));
    }

    private static AngleSharp.Dom.IElement UpdateButton(IRenderedComponent<LibraryBrowser> browser) =>
        browser.Find("button[aria-label='Update repository']");

    [Fact]
    public void AnUpdateThatFails_StartsTheMonitorAgain()
    {
        _repositories.Setup(r => r.UpdateRepositoryAsync("repo-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VcsUpdateResult { Success = true, HasChanges = false });
        _repositories.Setup(r => r.RefreshRepositoryAsync("repo-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("a library could not be read"));

        var browser = RenderBrowser();
        UpdateButton(browser).Click();

        browser.WaitForAssertion(() =>
            _monitor.Verify(m => m.StartMonitoring("repo-1", Root), Times.Once));
        _monitor.Verify(m => m.StopMonitoring("repo-1"), Times.Once);
    }

    [Fact]
    public void AnUpdateThatSucceeds_LeavesTheRestartToTheAnalysisPipeline()
    {
        // The control. Restarting here as well would watch the files the pipeline is about to format.
        _repositories.Setup(r => r.UpdateRepositoryAsync("repo-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VcsUpdateResult { Success = true, HasChanges = false });
        _repositories.Setup(r => r.RefreshRepositoryAsync("repo-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var browser = RenderBrowser();

        var handedOver = 0;
        NavState.OnVcsFilesChanged += _ => handedOver++;
        UpdateButton(browser).Click();

        browser.WaitForAssertion(() => Assert.Equal(1, handedOver));
        _monitor.Verify(m => m.StartMonitoring(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void WhileAnUpdateRuns_NoOtherOperationCanStart()
    {
        var update = new TaskCompletionSource<VcsUpdateResult>();
        _repositories.Setup(r => r.UpdateRepositoryAsync("repo-1", It.IsAny<CancellationToken>()))
            .Returns(update.Task);
        _repositories.Setup(r => r.RefreshRepositoryAsync("repo-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var browser = RenderBrowser();
        UpdateButton(browser).Click();

        browser.WaitForAssertion(() => Assert.True(UpdateButton(browser).HasAttribute("disabled")));

        // The click that arrives before the render that disables the button is refused as well.
        UpdateButton(browser).Click();
        _repositories.Verify(r => r.UpdateRepositoryAsync("repo-1", It.IsAny<CancellationToken>()), Times.Once);

        update.SetResult(new VcsUpdateResult { Success = true, HasChanges = false });
        browser.WaitForAssertion(() => Assert.False(UpdateButton(browser).HasAttribute("disabled")));
    }

    // ---- After a VCS dialog (B296) ------------------------------------------------------------
    //
    // The merge and rebase dialogs used to start the analysis pipeline while still open, and the
    // browser then removed and reloaded every library once they closed - the analysis ran over a
    // graph being rebuilt under it. A dialog now only records what it did; the browser reloads, and
    // then starts the pipeline.

    private List<string> RecordReloadAndAnalysis()
    {
        var sequence = new List<string>();
        _repositories.Setup(r => r.RefreshRepositoryAsync("repo-1", It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("reload"))
            .Returns(Task.CompletedTask);
        NavState.OnVcsFilesChanged += _ => sequence.Add("analyse");
        return sequence;
    }

    [Fact]
    public async Task ADialogThatRewroteTheWorkingCopy_IsReloadedBeforeItIsAnalysed()
    {
        var browser = RenderBrowser();
        var sequence = RecordReloadAndAnalysis();

        await browser.InvokeAsync(() => browser.Instance.AfterVcsDialogAsync(
            Repo(), new VcsDialogOutcome { WorkingCopyChanged = true }, "merge"));

        Assert.Equal(["reload", "analyse"], sequence);
    }

    [Fact]
    public async Task ADialogLeftWithConflicts_IsReloadedButNotFormatted()
    {
        // The pipeline formats every changed file, and a file with conflict markers in it is not
        // Modelica.
        var browser = RenderBrowser();
        var sequence = RecordReloadAndAnalysis();

        await browser.InvokeAsync(() => browser.Instance.AfterVcsDialogAsync(
            Repo(), new VcsDialogOutcome { WorkingCopyChanged = true, LeftInProgress = true }, "rebase"));

        Assert.Equal(["reload"], sequence);
    }

    [Fact]
    public async Task ADialogThatChangedNothing_ReloadsNothing()
    {
        // A merge with nothing to merge, a commit, a dialog opened and closed: the libraries are
        // still the working copy's, and reloading MSL's is twenty seconds of nothing.
        var browser = RenderBrowser();
        var sequence = RecordReloadAndAnalysis();

        await browser.InvokeAsync(() => browser.Instance.AfterVcsDialogAsync(
            Repo(), new VcsDialogOutcome(), "commit"));

        Assert.Empty(sequence);
    }
}
