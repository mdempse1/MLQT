using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Dialogs;
using Moq;
using MudBlazor;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Dialogs;

/// <summary>
/// B296 — a merge or rebase dialog closed in its conflict phase starts the file monitor again, and
/// says what it left behind.
/// </summary>
/// <remarks>
/// <para>Both stopped the monitor when the operation started and started it again on the paths that
/// finish it - continue, abort, commit. Cancel was not one of them, nor was Escape: the repository
/// went unwatched for the rest of the session. And the cancelled result told the browser nothing
/// had happened, so the libraries it showed were the ones from before the merge.</para>
/// </remarks>
public class VcsDialogMonitorTests : MlqtComponentTestBase
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mlqt-tests", "Lib");
    private static readonly string Conflicted = Path.Combine(Root, "package.mo");

    private readonly Mock<IRepositoryService> _repositories = new(MockBehavior.Loose);
    private readonly Mock<IFileMonitoringService> _monitor = new();
    private readonly VcsDialogOutcome _outcome = new();

    public VcsDialogMonitorTests()
    {
        _repositories.Setup(r => r.GetRepository("repo")).Returns(new Repository
        {
            Id = "repo",
            Name = "Lib",
            LocalPath = Root,
            VcsRootPath = Root,
            VcsType = RepositoryVcsType.Git,
            CurrentBranch = "main",
        });
        _repositories.Setup(r => r.GetWorkingCopyChanges("repo")).Returns([]);
        _repositories.Setup(r => r.GetRepositoriesSharingWorkingCopy("repo"))
            .Returns(() => [_repositories.Object.GetRepository("repo")!]);
        _monitor.Setup(m => m.IsMonitoringRepository(It.IsAny<string>())).Returns(true);
        _repositories.Setup(r => r.GetBranches("repo", It.IsAny<bool>())).Returns(
        [
            new VcsBranchInfo { Name = "main", IsCurrent = true },
            new VcsBranchInfo { Name = "develop" },
        ]);

        var conflicts = new VcsMergeResult { HasConflicts = true, HasChanges = true, ConflictedFiles = [Conflicted] };
        _repositories.Setup(r => r.RebaseAsync("repo", "develop")).ReturnsAsync(conflicts);
        _repositories.Setup(r => r.MergeBranchAsync("repo", "develop")).ReturnsAsync(conflicts);

        Services.AddSingleton(_repositories.Object);
        Services.AddSingleton(_monitor.Object);

        // The commit dialog's change list asks for these; nothing here is about them.
        Services.AddSingleton(new Mock<ILibraryDataService>().Object);
    }

    private DialogParameters Parameters() => new() { { "RepositoryId", "repo" }, { "Outcome", _outcome } };

    [Fact]
    public async Task ARebaseCancelledInItsConflicts_StartsTheMonitorAgain_AndSaysItIsUnfinished()
    {
        var (provider, dialog) = await ShowDialogAsync<GitRebaseDialog>(Parameters());

        await SelectBranch(provider, "develop");
        await ClickButton(provider, "Rebase");
        provider.WaitForAssertion(() => Assert.Contains("Abort Rebase", provider.Markup));

        // The premise: the monitor is held off while the conflicts are resolved.
        _monitor.Verify(m => m.StopMonitoring("repo"), Times.Once);
        _monitor.Verify(m => m.StartMonitoring(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        await ClickButton(provider, "Cancel");
        var result = await dialog.Result;

        Assert.True(result!.Canceled);
        provider.WaitForAssertion(() => _monitor.Verify(m => m.StartMonitoring("repo", Root), Times.Once));
        Assert.True(_outcome.WorkingCopyChanged, "the rebase rewrote the working copy, and the outcome did not say so");
        Assert.True(_outcome.LeftInProgress, "the rebase is unfinished, and the outcome did not say so");
    }

    [Fact]
    public async Task AMergeCancelledInItsConflicts_StartsTheMonitorAgain_AndSaysItIsUnfinished()
    {
        var (provider, dialog) = await ShowDialogAsync<GitMergeBranchDialog>(Parameters());

        await SelectBranch(provider, "develop");
        await ClickButton(provider, "Merge");
        provider.WaitForAssertion(() => Assert.Contains("resolved", provider.Markup));

        _monitor.Verify(m => m.StopMonitoring("repo"), Times.Once);
        _monitor.Verify(m => m.StartMonitoring(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        await ClickButton(provider, "Cancel");
        var result = await dialog.Result;

        Assert.True(result!.Canceled);
        provider.WaitForAssertion(() => _monitor.Verify(m => m.StartMonitoring("repo", Root), Times.Once));
        Assert.True(_outcome.WorkingCopyChanged);
        Assert.True(_outcome.LeftInProgress);
    }

    [Fact]
    public async Task ARebaseAborted_LeavesNothingToReload()
    {
        // The control for the two above: an abort puts the working copy back as it was, and the
        // libraries were never reloaded from the half-rebased one, so the browser has nothing to do.
        _repositories.Setup(r => r.AbortRebaseAsync("repo")).ReturnsAsync(new VcsOperationResult { Success = true });

        var (provider, dialog) = await ShowDialogAsync<GitRebaseDialog>(Parameters());

        await SelectBranch(provider, "develop");
        await ClickButton(provider, "Rebase");
        await ClickButton(provider, "Abort Rebase");
        await dialog.Result;

        _monitor.Verify(m => m.StartMonitoring("repo", Root), Times.Once);
        Assert.False(_outcome.WorkingCopyChanged);
        Assert.False(_outcome.LeftInProgress);
    }

    [Fact]
    public async Task SwitchingBranch_HoldsTheMonitorOffForTheCheckout_AndStartsItAgain()
    {
        // The one VCS operation that used to let the monitor report every file it rewrote (B296).
        var monitorWasOffDuringTheSwitch = false;
        _repositories.Setup(r => r.SwitchBranchAsync("repo", "develop"))
            .Callback(() => monitorWasOffDuringTheSwitch = MonitorStoppedAndNotYetStarted())
            .ReturnsAsync(new VcsOperationResult { Success = true });

        var (provider, dialog) = await ShowDialogAsync<SwitchBranchDialog>(new DialogParameters { { "RepositoryId", "repo" } });

        await SelectBranch(provider, "develop");
        await ClickButton(provider, "Switch");
        var result = await dialog.Result;

        Assert.False(result!.Canceled);
        Assert.Equal("develop", result.Data);
        Assert.True(monitorWasOffDuringTheSwitch, "the checkout ran with the monitor still watching");
        _monitor.Verify(m => m.StartMonitoring("repo", Root), Times.Once);
    }

    [Fact]
    public async Task ACommitThatHadToUpdateFirst_SaysTheWorkingCopyChanged_AndHoldsTheMonitorOffForTheUpdate()
    {
        // An out-of-date SVN working copy is updated before the commit is tried again. That rewrites
        // files the libraries were loaded from, so the caller has to reload - and used to be told
        // nothing, leaving the graph describing the files from before the update (B296).
        _repositories.Setup(r => r.GetWorkingCopyChanges("repo"))
            .Returns([new VcsWorkingCopyFile { Path = "package.mo", Status = VcsFileStatus.Modified }]);
        _repositories.SetupSequence(r => r.CommitAsync("repo", It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IProgress<string>>()))
            .ReturnsAsync(new VcsCommitResult { IsOutOfDate = true, ErrorMessage = "out of date" })
            .ReturnsAsync(new VcsCommitResult { Success = true, NewRevision = "42" });

        var monitorWasOffDuringTheUpdate = false;
        _repositories.Setup(r => r.UpdateRepositoryAsync("repo", It.IsAny<CancellationToken>()))
            .Callback(() => monitorWasOffDuringTheUpdate = MonitorStoppedAndNotYetStarted())
            .ReturnsAsync(new VcsUpdateResult { Success = true, HasChanges = true, NewRevision = "41" });

        var (provider, dialog) = await ShowDialogAsync<CommitChangesDialog>(new DialogParameters
        {
            { "RepositoryId", "repo" }, { "Outcome", _outcome }, { "InitialCommitMessage", "Fix the thing" },
        });

        await ClickButton(provider, "Commit (1 files)");
        var result = await dialog.Result;

        Assert.False(result!.Canceled);
        Assert.Equal("42", result.Data);
        Assert.True(_outcome.WorkingCopyChanged, "the update rewrote the working copy, and the outcome did not say so");
        Assert.True(monitorWasOffDuringTheUpdate, "the update ran with the monitor still watching");
        _monitor.Verify(m => m.StartMonitoring("repo", Root), Times.Once);
    }

    [Fact]
    public async Task ACommitThatDidNotNeedToUpdate_LeavesTheWorkingCopyAlone()
    {
        // The control: a commit changes no file, so the caller must not be sent to reload.
        _repositories.Setup(r => r.GetWorkingCopyChanges("repo"))
            .Returns([new VcsWorkingCopyFile { Path = "package.mo", Status = VcsFileStatus.Modified }]);
        _repositories.Setup(r => r.CommitAsync("repo", It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IProgress<string>>()))
            .ReturnsAsync(new VcsCommitResult { Success = true, NewRevision = "42" });

        var (provider, dialog) = await ShowDialogAsync<CommitChangesDialog>(new DialogParameters
        {
            { "RepositoryId", "repo" }, { "Outcome", _outcome }, { "InitialCommitMessage", "Fix the thing" },
        });

        await ClickButton(provider, "Commit (1 files)");
        await dialog.Result;

        Assert.False(_outcome.WorkingCopyChanged);
        _monitor.Verify(m => m.StopMonitoring(It.IsAny<string>()), Times.Never);
    }

    /// <summary>Whether the monitor has been stopped and not yet started again - asked mid-operation.</summary>
    private bool MonitorStoppedAndNotYetStarted()
    {
        var stopped = _monitor.Invocations.Any(i => i.Method.Name == nameof(IFileMonitoringService.StopMonitoring));
        var started = _monitor.Invocations.Any(i => i.Method.Name == nameof(IFileMonitoringService.StartMonitoring));
        return stopped && !started;
    }

    private static async Task SelectBranch(IRenderedComponent<MudDialogProvider> provider, string branch)
    {
        provider.WaitForAssertion(() => Assert.Contains(
            provider.FindAll(".mud-treeview-item-content"), item => item.TextContent.Contains(branch)));

        await provider.InvokeAsync(() => provider.FindAll(".mud-treeview-item-content")
            .First(item => item.TextContent.Contains(branch))
            .Click());
    }

    private static async Task ClickButton(IRenderedComponent<MudDialogProvider> provider, string label)
    {
        // Enabled, and the label exactly: "Rebase" is also the start of "Abort Rebase".
        bool Matches(AngleSharp.Dom.IElement b) =>
            !b.HasAttribute("disabled") && b.TextContent.Trim().Equals(label, StringComparison.OrdinalIgnoreCase);

        provider.WaitForAssertion(() => Assert.Contains(provider.FindAll("button"), Matches));
        await provider.InvokeAsync(() => provider.FindAll("button").First(Matches).Click());
    }
}
