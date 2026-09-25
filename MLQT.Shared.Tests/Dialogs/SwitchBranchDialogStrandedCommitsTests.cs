using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Dialogs;
using MudBlazor;
using Moq;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Dialogs;

/// <summary>
/// B327 — switching away from a detached HEAD says what it leaves behind.
/// </summary>
/// <remarks>
/// A commit made on a detached HEAD - by another tool, or by MLQT before it refused to - is held by
/// nothing but HEAD, and Git leaves it to the reflog on the next switch without a word.
/// </remarks>
public class SwitchBranchDialogStrandedCommitsTests : MlqtComponentTestBase
{
    private const string RepositoryId = "repo-1";

    private async Task<IRenderedComponent<MudDialogProvider>> ShowWith(int commitsOnNoBranch)
    {
        var repositories = new Mock<IRepositoryService>();
        repositories.Setup(r => r.GetBranches(RepositoryId, It.IsAny<bool>()))
                    .Returns([new VcsBranchInfo { Name = "main" }]);
        repositories.Setup(r => r.GetRepository(RepositoryId))
                    .Returns(new Repository { Id = RepositoryId, Name = "ExternData", VcsType = RepositoryVcsType.Git });
        repositories.Setup(r => r.GetWorkingCopyChanges(RepositoryId)).Returns([]);
        repositories.Setup(r => r.CountCommitsOnNoBranch(RepositoryId)).Returns(commitsOnNoBranch);

        Services.AddSingleton(repositories.Object);
        Services.AddSingleton(new Mock<IFileMonitoringService>().Object);

        var (provider, _) = await ShowDialogAsync<SwitchBranchDialog>(
            new DialogParameters { { nameof(SwitchBranchDialog.RepositoryId), RepositoryId } });
        return provider;
    }

    [Fact]
    public async Task CommitsOnNoBranch_AreCountedBeforeTheSwitch()
    {
        var provider = await ShowWith(commitsOnNoBranch: 2);

        provider.WaitForAssertion(() =>
        {
            var warning = provider.Find(".mlqt-orphaned-commits");
            Assert.Contains("2 commits", warning.TextContent);
            Assert.Contains("create a branch here", warning.TextContent);
        });
    }

    [Fact]
    public async Task WithNothingStranded_NothingIsSaid()
    {
        var provider = await ShowWith(commitsOnNoBranch: 0);

        provider.WaitForAssertion(() => Assert.Contains("Select a branch or tag", provider.Markup));
        Assert.Empty(provider.FindAll(".mlqt-orphaned-commits"));
    }
}
