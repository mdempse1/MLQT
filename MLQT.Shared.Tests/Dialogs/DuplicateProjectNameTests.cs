using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Dialogs;
using Moq;
using Xunit;

namespace MLQT.Shared.Tests.Dialogs;

/// <summary>
/// The startup project selector refuses a name another project already has.
///
/// <para>Both screens that name a project share one rule (<c>ProjectNameRules</c>) and the service
/// refuses a name that reaches it anyway. What is tested here is the half the user meets: the reason
/// shown while typing, and the confirm button being unavailable until the name is usable.</para>
///
/// <para>This dialog is worth rendering rather than calling: the name being composed and the id of
/// the selected radio are the <b>same field</b>, so whether the check is even asked depends on which
/// mode the dialog is in.</para>
///
/// <para><b>They assert the button, not the message.</b> MudTextField renders its <c>ErrorText</c> on
/// the render <i>after</i> the value changes, while the button's <c>Disabled</c> is evaluated in the
/// same one — so a single synthetic input shows the button correctly and the message not yet. A real
/// keyboard produces a render per keystroke and the message appears from the second character. The
/// wording is asserted in <c>ProjectNameRulesTests</c>, and that the field is wired to show it at all
/// by <c>SharedUiConventionTests</c>.</para>
/// </summary>
public class DuplicateProjectNameTests : MlqtComponentTestBase
{
    private const string ExistingName = "Existing Project";

    private void ArrangeSettings()
    {
        var settings = new RepositorySettingsCollection
        {
            Projects =
            [
                new ProjectProfile { Id = "p1", Name = ExistingName },
                new ProjectProfile { Id = "p2", Name = "Another Project" }
            ],
            ActiveProjectId = "p1"
        };

        var service = new Mock<ISettingsService>();
        service.Setup(s => s.GetAsync("Repositories", It.IsAny<RepositorySettingsCollection>()))
               .ReturnsAsync(settings);
        Services.AddSingleton(service.Object);
    }

    /// <summary>
    /// Opens the dialog, puts it into new-project mode, and types <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    /// The field is looked up immediately before it is typed into: each keystroke re-renders the
    /// dialog, and an element captured beforehand is stale by then.
    /// </remarks>
    private async Task<IRenderedComponent<MudBlazor.MudDialogProvider>> TypeANewProjectNameAsync(string name)
    {
        ArrangeSettings();
        var (provider, _) = await ShowDialogAsync<ProjectSelectionDialog>();

        var newProject = provider.FindAll("button").First(b => b.TextContent.Contains("New Project"));
        await provider.InvokeAsync(() => newProject.Click());

        var field = provider.Find("input[type=text]");
        await provider.InvokeAsync(() => field.Input(name));

        return provider;
    }

    /// <summary>
    /// The green tick beside the new-project field, found by the colour MudBlazor puts in its class
    /// rather than by its icon: an icon button does not carry the icon's path data in its own markup.
    /// The other icon button in that row is the red cancel.
    /// </summary>
    private static bool ConfirmIsDisabled(IRenderedComponent<MudBlazor.MudDialogProvider> provider) =>
        provider.FindAll("button")
                .First(b => b.ClassName?.Contains("mud-button-outlined-success") == true)
                .HasAttribute("disabled");

    [Fact]
    public async Task ANameAnotherProjectHasIsRefused()
    {
        var provider = await TypeANewProjectNameAsync(ExistingName);

        Assert.True(ConfirmIsDisabled(provider));
    }

    [Fact]
    public async Task TheComparisonIgnoresCaseAndSurroundingSpace()
    {
        var provider = await TypeANewProjectNameAsync("  existing project  ");

        Assert.True(ConfirmIsDisabled(provider));
    }

    [Fact]
    public async Task AFreshNameIsAccepted()
    {
        // The control: the guard must not block an ordinary new project.
        var provider = await TypeANewProjectNameAsync("Something New");

        Assert.False(ConfirmIsDisabled(provider));
    }

    [Fact]
    public async Task AnEmptyNameStillCannotBeConfirmed()
    {
        // Behaviour that existed before the duplicate check and has to survive it, since the same
        // expression now drives the button.
        var provider = await TypeANewProjectNameAsync("   ");

        Assert.True(ConfirmIsDisabled(provider));
    }
}
