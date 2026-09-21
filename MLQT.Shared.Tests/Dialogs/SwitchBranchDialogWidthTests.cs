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
/// B193 — the Switch Branch dialog keeps its size when a tag is selected.
/// </summary>
/// <remarks>
/// <para>Reported from using it: clicking a tag made the dialog jump to the width of the screen. The
/// content had a <c>min-width</c> and no width of its own, MudDialog sizes to its content, and the
/// provider's default cap is <c>MaxWidth.Large</c> — so the paragraph explaining detached HEAD,
/// which appears on the click, stretched the dialog underneath the pointer that caused it.</para>
///
/// <para><b>What a headless render can and cannot see.</b> It cannot measure a layout: the widths
/// here are CSS a browser applies. What it can hold is that the declaration is still there and that
/// the alert still appears — the two things a later edit would drop, and between them the reason the
/// dialog used to resize.</para>
/// </remarks>
public class SwitchBranchDialogWidthTests : MlqtComponentTestBase
{
    private const string RepositoryId = "repo-1";

    private void GivenRepositoryWith(params VcsBranchInfo[] refs)
    {
        var repositories = new Mock<IRepositoryService>();
        repositories.Setup(r => r.GetBranches(RepositoryId, It.IsAny<bool>())).Returns(refs.ToList());
        repositories.Setup(r => r.GetRepository(RepositoryId))
                    .Returns(new Repository { Id = RepositoryId, Name = "ExternData", CurrentBranch = "main" });
        repositories.Setup(r => r.GetWorkingCopyChanges(RepositoryId)).Returns([]);

        Services.AddSingleton(repositories.Object);
    }

    private async Task<IRenderedComponent<MudDialogProvider>> ShowAsync()
    {
        var (provider, _) = await ShowDialogAsync<SwitchBranchDialog>(
            new DialogParameters { { nameof(SwitchBranchDialog.RepositoryId), RepositoryId } });

        return provider;
    }

    [Fact]
    public async Task TheContentHasAWidthOfItsOwn()
    {
        // Not a min-width. That is what let the content decide how wide the dialog was.
        GivenRepositoryWith(
            new VcsBranchInfo { Name = "main", IsCurrent = true },
            new VcsBranchInfo { Name = "v2.0.0", IsTag = true });

        var provider = await ShowAsync();

        provider.WaitForAssertion(() => Assert.Contains("width: 460px", provider.Markup));
    }

    [Fact]
    public async Task SelectingATagExplainsTheDetachedHead()
    {
        // The alert that caused the resize is still wanted - the fix is that it no longer changes
        // the dialog's width, not that it went away.
        GivenRepositoryWith(
            new VcsBranchInfo { Name = "main", IsCurrent = true },
            new VcsBranchInfo { Name = "v2.0.0", IsTag = true });

        var provider = await ShowAsync();
        provider.WaitForAssertion(() => Assert.Contains("v2.0.0", provider.Markup));

        var tag = provider.FindAll("p").First(e => e.TextContent.Trim() == "v2.0.0");
        await provider.InvokeAsync(() => tag.Click());

        provider.WaitForAssertion(() => Assert.Contains("detached HEAD", provider.Markup));

        // And the width is the same declaration it started with, with the alert on screen.
        Assert.Contains("width: 460px", provider.Markup);
    }

    [Fact]
    public async Task SelectingABranchSaysNothingAboutDetachedHeads()
    {
        GivenRepositoryWith(
            new VcsBranchInfo { Name = "main", IsCurrent = true },
            new VcsBranchInfo { Name = "release", IsTag = false });

        var provider = await ShowAsync();
        provider.WaitForAssertion(() => Assert.Contains("release", provider.Markup));

        var branch = provider.FindAll("p").First(e => e.TextContent.Trim() == "release");
        await provider.InvokeAsync(() => branch.Click());

        Assert.DoesNotContain("detached HEAD", provider.Markup);
    }
}
