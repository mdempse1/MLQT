using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Pages;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Moq;
using MudBlazor;
using RevisionControl;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// B376 - Split into files holds the monitor off the way every other write does: a
/// <c>MonitorPause</c> over every repository in the working copy.
///
/// <para>It stopped and started the monitor by hand, for its own repository only, so a second
/// library checked out in the same tree kept its watcher and heard the split as external changes
/// (B301), and the restart was one more hand-written one of the kind B296 took away.</para>
/// </summary>
public class CodeReviewSplitMonitorTests : CodeReviewTestBase
{
    private readonly Mock<IFileMonitoringService> _monitor = new();
    private readonly Repository _sibling = new()
    {
        Id = "repo-2",
        Name = "Other",
        LocalPath = Path.Combine(Root, "Other"),
        VcsRootPath = Root,
        VcsType = RepositoryVcsType.Git,
    };

    public CodeReviewSplitMonitorTests()
    {
        Services.AddSingleton(_monitor.Object);
        _monitor.Setup(m => m.IsMonitoringRepository(It.IsAny<string>())).Returns(true);

        var own = Repositories.Object.GetRepository(RepositoryId)!;
        Repositories.Setup(r => r.GetRepositoriesSharingWorkingCopy(RepositoryId)).Returns([own, _sibling]);

        // A library that is a directory, so the page does not refuse the split before it starts (B306).
        var library = Libraries.Object.Libraries[0];
        library.SourceType = LibrarySourceType.Git;
        library.SourcePath = Root;
    }

    /// <summary>
    /// Presses Split into files on a finding for a package the splitter declines - nothing on disk
    /// is touched - and confirms the dialog. What the page does with the monitor around it is the
    /// same either way.
    /// </summary>
    private void PressSplitAndConfirm()
    {
        // A package with nothing that could have a file of its own: PackageSplitter refuses it
        // without writing anything, so the test never needs the working copy to exist.
        LoadFile("Lib/package.mo", """
            package Lib "lib"
              constant Real c = 1;
            end Lib;
            """);
        Findings.AddLogMessage(new LogMessage("Lib", "Style warning", 1, "Lib is stored as a single file")
        {
            RuleId = RuleIds.SingleFilePackage,
            Source = LogMessage.StyleCheckingSource,
        });

        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
        var page = Render<CodeReview>();

        Eventually(page, () => Assert.NotNull(page.Find("button[aria-label='Split this package into files']")));
        page.Find("button[aria-label='Split this package into files']").Click();

        dialogs.WaitForAssertion(() => Assert.Contains(dialogs.FindAll("button"), b => b.TextContent.Trim() == "Split"),
            Patience);
        dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Split").Click();
    }

    [Fact]
    public void EveryRepositoryInTheWorkingCopyIsPausedAndStartedAgain()
    {
        Libraries.Setup(l => l.UpdateChangedFilesAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>()))
            .ReturnsAsync(new HashSet<string>());

        PressSplitAndConfirm();

        WaitUntil(() => _monitor.Invocations.Count(i => i.Method.Name == nameof(IFileMonitoringService.StartMonitoring)) == 2);
        _monitor.Verify(m => m.StopMonitoring(RepositoryId), Times.Once);
        _monitor.Verify(m => m.StopMonitoring(_sibling.Id), Times.Once);
        _monitor.Verify(m => m.StartMonitoring(RepositoryId, Root), Times.Once);
        _monitor.Verify(m => m.StartMonitoring(_sibling.Id, Root), Times.Once);
    }

    [Fact]
    public void AReloadThatThrowsStillStartsTheMonitorAgain()
    {
        Libraries.Setup(l => l.UpdateChangedFilesAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("the disk went away"));

        PressSplitAndConfirm();

        WaitUntil(() => _monitor.Invocations.Count(i => i.Method.Name == nameof(IFileMonitoringService.StartMonitoring)) == 2);
        _monitor.Verify(m => m.StartMonitoring(RepositoryId, Root), Times.Once);
        _monitor.Verify(m => m.StartMonitoring(_sibling.Id, Root), Times.Once);
    }

    [Fact]
    public void ARepositoryThatWasNotBeingWatchedIsNotStartedByTheSplit()
    {
        // A reference-only neighbour is never monitored, and a restart by hand would start one.
        _monitor.Setup(m => m.IsMonitoringRepository(_sibling.Id)).Returns(false);
        Libraries.Setup(l => l.UpdateChangedFilesAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>()))
            .ReturnsAsync(new HashSet<string>());

        PressSplitAndConfirm();

        WaitUntil(() => _monitor.Invocations.Any(i => i.Method.Name == nameof(IFileMonitoringService.StartMonitoring)));
        _monitor.Verify(m => m.StartMonitoring(_sibling.Id, It.IsAny<string>()), Times.Never);
        _monitor.Verify(m => m.StopMonitoring(_sibling.Id), Times.Never);
    }

    /// <summary>
    /// Polled rather than asked of bUnit: the split's work runs past the dialog on the renderer's
    /// dispatcher, and the monitor calls render nothing that a wait could re-check on.
    /// </summary>
    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("The split did not finish with the monitor.");
            Thread.Sleep(10);
        }
    }
}
