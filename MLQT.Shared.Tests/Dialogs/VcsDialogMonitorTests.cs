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

    /// <summary>
    /// Creating a branch and switching to it runs with the monitor held off, as every switch does.
    /// Watched, each file svn touched was reported as the user's change and dropped the cached
    /// working-copy status, so the next commit had to ask SVN all over again.
    /// </summary>
    [Fact]
    public async Task CreatingABranchAndSwitching_HoldsTheMonitorOff_AndReportsWhatTheSwitchChanged()
    {
        var monitorWasOffDuringTheSwitch = false;
        _repositories.Setup(r => r.CreateBranchAsync("repo", "feature", true))
            .Callback(() => monitorWasOffDuringTheSwitch = MonitorStoppedAndNotYetStarted())
            .ReturnsAsync(new VcsOperationResult { Success = true, WorkingCopyChanged = true });

        var (provider, dialog) = await ShowDialogAsync<CreateBranchDialog>(Parameters());
        await TypeBranchName(provider, "feature");
        await ClickButton(provider, "Create Branch");
        var result = await dialog.Result.WaitAsync(DialogCloses, Xunit.TestContext.Current.CancellationToken);

        Assert.False(result!.Canceled);
        Assert.True(monitorWasOffDuringTheSwitch, "the switch ran with the monitor still watching");
        _monitor.Verify(m => m.StartMonitoring("repo", Root), Times.Once);
        Assert.True(_outcome.WorkingCopyChanged, "the switch rewrote files, and the outcome did not say so");
    }

    [Fact]
    public async Task CreatingABranchWhoseSwitchChangedNothing_LeavesNothingToReload()
    {
        _repositories.Setup(r => r.CreateBranchAsync("repo", "feature", true))
            .ReturnsAsync(new VcsOperationResult { Success = true });

        var (provider, dialog) = await ShowDialogAsync<CreateBranchDialog>(Parameters());
        await TypeBranchName(provider, "feature");
        await ClickButton(provider, "Create Branch");
        await dialog.Result.WaitAsync(DialogCloses, Xunit.TestContext.Current.CancellationToken);

        Assert.False(_outcome.WorkingCopyChanged);
    }

    /// <summary>
    /// How long a dialog that should close is given. Bounded: a dialog that stays open - a refused
    /// create shows its error and waits - otherwise hangs the whole run rather than failing a test.
    /// </summary>
    private static readonly TimeSpan DialogCloses = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task CreatingABranchWithoutSwitching_LeavesTheMonitorAlone()
    {
        _repositories.Setup(r => r.CreateBranchAsync("repo", "feature", false))
            .ReturnsAsync(new VcsOperationResult { Success = true });

        var (provider, dialog) = await ShowDialogAsync<CreateBranchDialog>(Parameters());
        await TypeBranchName(provider, "feature");
        await provider.InvokeAsync(() => provider.Find("input[type=checkbox]").Change(false));
        await ClickButton(provider, "Create Branch");
        await dialog.Result.WaitAsync(DialogCloses, Xunit.TestContext.Current.CancellationToken);

        _repositories.Verify(r => r.CreateBranchAsync("repo", "feature", false), Times.Once);
        _monitor.Verify(m => m.StopMonitoring(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// A revert runs with the monitor held off. Watched, every reverted file was reported as the
    /// user's change - 17,000 for a large revert - though the browser reloads exactly those files.
    /// </summary>
    [Fact]
    public async Task Reverting_HoldsTheMonitorOffForTheRevert_AndStartsItAgain()
    {
        _repositories.Setup(r => r.GetWorkingCopyChanges("repo"))
            .Returns([new VcsWorkingCopyFile { Path = "package.mo", Status = VcsFileStatus.Modified }]);
        var monitorWasOffDuringTheRevert = false;
        _repositories.Setup(r => r.RevertFilesAsync("repo", It.IsAny<IEnumerable<string>>()))
            .Callback(() => monitorWasOffDuringTheRevert = MonitorStoppedAndNotYetStarted())
            .ReturnsAsync(new VcsOperationResult { Success = true });

        var (provider, dialog) = await ShowDialogAsync<RevertFilesDialog>(new DialogParameters { { "RepositoryId", "repo" } });
        await ClickButton(provider, "Revert (1 files)");
        var result = await dialog.Result.WaitAsync(DialogCloses, Xunit.TestContext.Current.CancellationToken);

        Assert.False(result!.Canceled);
        Assert.True(monitorWasOffDuringTheRevert, "the revert ran with the monitor still watching");
        _monitor.Verify(m => m.StartMonitoring("repo", Root), Times.Once);
    }

    [Fact]
    public async Task ARefusedRevert_StartsTheMonitorAgain_AndStaysOpen()
    {
        _repositories.Setup(r => r.GetWorkingCopyChanges("repo"))
            .Returns([new VcsWorkingCopyFile { Path = "package.mo", Status = VcsFileStatus.Modified }]);
        _repositories.Setup(r => r.RevertFilesAsync("repo", It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new VcsOperationResult { ErrorMessage = "Could not revert 1 file(s): locked" });

        var (provider, dialog) = await ShowDialogAsync<RevertFilesDialog>(Parameters());
        await ClickButton(provider, "Revert (1 files)");

        provider.WaitForAssertion(() => Assert.Contains("Could not revert 1 file(s): locked", provider.Markup));
        Assert.False(dialog.Result.IsCompleted);
        _monitor.Verify(m => m.StartMonitoring("repo", Root), Times.Once);

        // A revert that failed part way has still changed files: the caller is told which it was
        // asked to change, and reloads them however the dialog closes.
        Assert.Equal(["package.mo"], _outcome.FilesTouched);
    }

    private static async Task TypeBranchName(IRenderedComponent<MudDialogProvider> provider, string name)
    {
        provider.WaitForAssertion(() => Assert.NotEmpty(provider.FindAll("input:not([type=checkbox])")));
        await provider.InvokeAsync(() => provider.Find("input:not([type=checkbox])").Input(name));
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
