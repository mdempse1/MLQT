using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Dialogs;
using Moq;
using Xunit;

namespace MLQT.Shared.Tests.Dialogs;

/// <summary>
/// The startup project picker, and what a click on one of its rows does.
///
/// <para><b>What broke (B194).</b> Each row was a bare <c>MudRadio</c>, so clicking one moved the
/// selection and nothing else — the only way out of the dialog was the Load Project button. On the
/// first screen a new user sees, a list whose rows do not respond reads as a broken list rather than
/// as a modal waiting for a button.</para>
///
/// <para>These need the dialog provider: what the dialog hands back reaches its caller through the
/// cascaded dialog instance, which only exists inside one. <see cref="MlqtComponentTestBase"/>'s
/// remarks explain why a directly rendered dialog would make every Close a silent no-op.</para>
/// </summary>
public class ProjectSelectionDialogTests : MlqtComponentTestBase
{
    private static RepositorySettingsCollection TwoProjects() => new()
    {
        ActiveProjectId = "first",
        Projects =
        [
            new ProjectProfile { Id = "first", Name = "First Project" },
            new ProjectProfile { Id = "second", Name = "Second Project" }
        ]
    };

    private void ArrangeSettings(RepositorySettingsCollection settings)
    {
        var service = new Mock<ISettingsService>();
        service.Setup(s => s.GetAsync("Repositories", It.IsAny<RepositorySettingsCollection>()))
               .ReturnsAsync(settings);
        Services.AddSingleton(service.Object);
    }

    /// <summary>
    /// Clicks the row carrying <paramref name="projectName"/> — the row element, not the radio
    /// inside it, which is the distinction the defect was about.
    /// </summary>
    private static async Task ClickRowAsync(IRenderedComponent<MudBlazor.MudDialogProvider> provider, string projectName)
    {
        var row = provider.FindAll("div.project-row")
                          .First(r => r.TextContent.Contains(projectName));
        await provider.InvokeAsync(() => row.Click());
    }

    [Fact]
    public async Task ClickingARow_LoadsThatProject()
    {
        // The defect, stated as an assertion.
        ArrangeSettings(TwoProjects());

        var (provider, dialog) = await ShowDialogAsync<ProjectSelectionDialog>();

        await ClickRowAsync(provider, "Second Project");

        var result = await dialog.Result;

        Assert.False(result!.Canceled);
        Assert.Equal("second", result.Data);
    }

    [Fact]
    public async Task ClickingARow_LoadsTheRowClicked_NotThePreselectedOne()
    {
        // The preselection is the active project, so a click that closed with the *selected* value
        // without moving the selection first would return "first" and look almost right. This is the
        // assertion that tells the fix from that near miss — and it is the same confusion B192 is
        // about one layer up.
        ArrangeSettings(TwoProjects());

        var (provider, dialog) = await ShowDialogAsync<ProjectSelectionDialog>();

        await ClickRowAsync(provider, "Second Project");

        var result = await dialog.Result;

        Assert.Equal("second", result!.Data);
        Assert.NotEqual("first", result.Data);
    }

    [Fact]
    public async Task TheLoadProjectButtonStillWorks()
    {
        // Kept, per the item: the button is the keyboard path, since arrowing through a radio group
        // moves the selection without committing it.
        ArrangeSettings(TwoProjects());

        var (provider, dialog) = await ShowDialogAsync<ProjectSelectionDialog>();

        var button = provider.FindAll("button").First(b => b.TextContent.Contains("Load Project"));
        await provider.InvokeAsync(() => button.Click());

        var result = await dialog.Result;

        Assert.False(result!.Canceled);
        Assert.Equal("first", result.Data);      // the preselected active project
    }

    [Fact]
    public async Task EveryProjectGetsAClickableRow()
    {
        // Not one row wired up and the rest left as radios.
        ArrangeSettings(TwoProjects());

        var (provider, _) = await ShowDialogAsync<ProjectSelectionDialog>();

        Assert.Equal(2, provider.FindAll("div.project-row").Count);
    }
}
