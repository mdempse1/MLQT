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
/// B382 - a rebase left in progress can be continued or aborted from the rebase dialog.
/// </summary>
/// <remarks>
/// <para>The dialog only ever started a rebase. Closed in its conflict phase, it left the rebase
/// stopped with HEAD detached, and opened again it offered to start another one - which the Git
/// layer refuses on a detached HEAD, as it refuses Commit, Merge and Push (B327). Nothing in MLQT
/// could finish the rebase or undo it.</para>
/// </remarks>
public class GitRebaseResumeTests : MlqtComponentTestBase
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mlqt-tests", "Lib");
    private static readonly string Conflicted = Path.Combine(Root, "package.mo");

    private readonly Mock<IRepositoryService> _repositories = new(MockBehavior.Loose);
    private readonly Mock<IFileMonitoringService> _monitor = new();
    private readonly VcsDialogOutcome _outcome = new();

    public GitRebaseResumeTests()
    {
        _repositories.Setup(r => r.GetRepository("repo")).Returns(new Repository
        {
            Id = "repo",
            Name = "Lib",
            LocalPath = Root,
            VcsRootPath = Root,
            VcsType = RepositoryVcsType.Git,
            CurrentBranch = null,
        });
        _repositories.Setup(r => r.GetWorkingCopyChanges("repo")).Returns([]);
        _repositories.Setup(r => r.GetRepositoriesSharingWorkingCopy("repo"))
            .Returns(() => [_repositories.Object.GetRepository("repo")!]);
        _monitor.Setup(m => m.IsMonitoringRepository(It.IsAny<string>())).Returns(true);

        Services.AddSingleton(_repositories.Object);
        Services.AddSingleton(_monitor.Object);
        Services.AddSingleton(new Mock<ILibraryDataService>().Object);
    }

    private void RebaseLeftInProgress(params string[] conflicted) =>
        _repositories.Setup(r => r.GetRebaseInProgressAsync("repo"))
            .ReturnsAsync(new VcsRebaseInProgress("feature", conflicted));

    private DialogParameters Parameters() => new() { { "RepositoryId", "repo" }, { "Outcome", _outcome } };

    [Fact]
    public async Task ARebaseLeftInProgress_OpensOnItsConflicts_WithContinueOnlyOnceTheyAreResolved()
    {
        RebaseLeftInProgress(Conflicted);
        _repositories.Setup(r => r.ResolveConflictAsync("repo", Conflicted, ConflictResolutionChoice.KeepMine))
            .ReturnsAsync(new VcsOperationResult { Success = true });

        var (provider, _) = await ShowDialogAsync<GitRebaseDialog>(Parameters());

        provider.WaitForAssertion(() => Assert.NotNull(provider.Find("[data-testid=rebase-left-in-progress]")));
        Assert.Contains("feature", provider.Find("[data-testid=rebase-left-in-progress]").TextContent);
        Assert.Contains("package.mo", provider.Markup);
        Assert.True(Button(provider, "Continue Rebase").HasAttribute("disabled"), "Continue was offered over an unresolved conflict");
        Assert.False(Button(provider, "Abort Rebase").HasAttribute("disabled"));

        // No branch selector: there is nothing to start while a rebase is stopped.
        Assert.Empty(provider.FindAll(".mud-treeview-item-content"));

        // The monitor is held off while the conflicts are resolved, as when the rebase started here.
        _monitor.Verify(m => m.StopMonitoring("repo"), Times.Once);

        await ClickButton(provider, "Keep Mine");
        provider.WaitForAssertion(() => Assert.False(Button(provider, "Continue Rebase").HasAttribute("disabled")));
    }

    [Fact]
    public async Task ARebaseLeftInProgress_WithNothingInConflict_CanBeContinued_AndSaysTheWorkingCopyChanged()
    {
        // Resolved in another tool before the dialog was opened again: the list is empty, and that
        // must not leave Continue off for good as an empty merge list does.
        RebaseLeftInProgress();
        _repositories.Setup(r => r.ContinueRebaseAsync("repo")).ReturnsAsync(new VcsMergeResult { Success = true, HasChanges = true });

        var (provider, _) = await ShowDialogAsync<GitRebaseDialog>(Parameters());

        await ClickButton(provider, "Continue Rebase");

        provider.WaitForAssertion(() => Assert.Contains("Rebase complete", provider.Markup));
        _repositories.Verify(r => r.ContinueRebaseAsync("repo"), Times.Once);
        _repositories.Verify(r => r.RebaseAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.True(_outcome.WorkingCopyChanged, "the rebase finished and rewrote the working copy, and the outcome did not say so");
        Assert.False(_outcome.LeftInProgress);
        _monitor.Verify(m => m.StartMonitoring("repo", Root), Times.Once);
    }

    [Fact]
    public async Task ARebaseLeftInProgress_Aborted_SendsTheBrowserToReload()
    {
        // Unlike an abort of a rebase started in the same dialog, the libraries were reloaded from
        // the half-rebased working copy when it was left, so putting it back changes what they show.
        RebaseLeftInProgress(Conflicted);
        _repositories.Setup(r => r.AbortRebaseAsync("repo")).ReturnsAsync(new VcsOperationResult { Success = true });

        var (provider, dialog) = await ShowDialogAsync<GitRebaseDialog>(Parameters());

        await ClickButton(provider, "Abort Rebase");
        await dialog.Result;

        Assert.True(_outcome.WorkingCopyChanged, "the abort rewrote the half-rebased files the libraries were loaded from");
        Assert.False(_outcome.LeftInProgress);
        _monitor.Verify(m => m.StartMonitoring("repo", Root), Times.Once);
    }

    [Fact]
    public async Task ARebaseLeftInProgress_ClosedAgain_IsStillInProgress_AndChangedNothing()
    {
        RebaseLeftInProgress(Conflicted);

        var (provider, dialog) = await ShowDialogAsync<GitRebaseDialog>(Parameters());

        await ClickButton(provider, "Cancel");
        await dialog.Result;

        Assert.True(_outcome.LeftInProgress);
        Assert.False(_outcome.WorkingCopyChanged);
        provider.WaitForAssertion(() => _monitor.Verify(m => m.StartMonitoring("repo", Root), Times.Once));
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<MudDialogProvider> provider, string label) =>
        provider.FindAll("button").First(b => b.TextContent.Trim().Equals(label, StringComparison.OrdinalIgnoreCase));

    private static async Task ClickButton(IRenderedComponent<MudDialogProvider> provider, string label)
    {
        bool Matches(AngleSharp.Dom.IElement b) =>
            !b.HasAttribute("disabled") && b.TextContent.Trim().Equals(label, StringComparison.OrdinalIgnoreCase);

        provider.WaitForAssertion(() => Assert.Contains(provider.FindAll("button"), Matches));
        await provider.InvokeAsync(() => provider.FindAll("button").First(Matches).Click());
    }
}
