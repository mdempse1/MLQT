using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using MLQT.Shared.Dialogs;
using Moq;
using MudBlazor;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// B425 - the Edit Repository dialog cannot be closed other than by its buttons, which would leave
/// the edits in place without <c>CancelChanges</c>.
/// </summary>
/// <remarks>
/// The dialog is an inline <c>&lt;MudDialog @bind-Visible&gt;</c>. Its options once set neither
/// <c>BackdropClick</c> nor <c>CloseOnEscapeKey</c>, so both fell back to the provider it is shown
/// through, and MainLayout's provider turning both off was the only protection. These render it
/// through a provider that allows both, so what holds it open is the dialog's own options. The
/// control shows the same provider, backdrop click and Escape do close a dialog that does not refuse
/// them, so the first two are not passing because the gesture reached nothing.
/// </remarks>
public class SettingsRepositoriesEditDialogCloseTests : MlqtComponentTestBase
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mlqt-tests", "Lib");
    private readonly Repository _repository = new() { Id = "repo", Name = "Original", LocalPath = Root, VcsRootPath = Root };
    private IRenderedComponent<MudDialogProvider> _provider = null!;

    /// <summary>Providers and services, with a dialog provider that lets a backdrop click and Escape close a dialog.</summary>
    private void RenderPermissiveProviders()
    {
        var project = new ProjectProfile { Name = "Default" };
        var service = new Mock<IRepositoryService>();
        service.SetupGet(s => s.Repositories).Returns([_repository]);
        service.Setup(s => s.GetProjects()).Returns([project]);
        service.Setup(s => s.GetActiveProject()).Returns(project);

        var dictionaries = new Mock<IDictionaryManagerService>();
        dictionaries.Setup(d => d.GetAvailableDictionaries()).Returns([]);

        Services.AddSingleton(service.Object);
        Services.AddSingleton(dictionaries.Object);
        Services.AddSingleton(new Mock<ISettingsService>().Object);
        Services.AddSingleton(new Mock<IFilePickerService>().Object);

        Render<MudPopoverProvider>();
        _provider = Render<MudDialogProvider>(p => p.Add(x => x.BackdropClick, true).Add(x => x.CloseOnEscapeKey, true));
        Render<MudSnackbarProvider>();
    }

    private IRenderedComponent<SettingsRepositories> OpenEditDialog()
    {
        RenderPermissiveProviders();

        var panel = Render<SettingsRepositories>();
        panel.InvokeAsync(() => panel.Instance.OnRepoClick(_repository));
        _provider.WaitForAssertion(() => Assert.Contains("Edit Repository Details", _provider.Markup));

        // An edit the user has not applied.
        _repository.Name = "Edited";
        return panel;
    }

    private void ClickBackdrop() => _provider.Find(".mud-overlay").Click();

    private void PressEscape()
    {
        // What the key interceptor calls when Escape is pressed in the dialog; a headless renderer
        // has no key interceptor of its own to press it through.
        var container = _provider.FindComponent<MudDialogContainer>().Instance;
        var handler = typeof(MudDialogContainer).GetMethod("HandleEscapeAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                      ?? throw new MissingMethodException("MudDialogContainer.HandleEscapeAsync is gone - find what Escape calls now");
        _provider.InvokeAsync(() => (Task)handler.Invoke(container, null)!).GetAwaiter().GetResult();
    }

    [Fact]
    public void ABackdropClickDoesNotCloseTheDialog_WhateverTheProviderAllows()
    {
        var panel = OpenEditDialog();

        ClickBackdrop();

        Assert.True(panel.Instance._editRepository);
        Assert.Contains("Edit Repository Details", _provider.Markup);
    }

    [Fact]
    public void EscapeDoesNotCloseTheDialog_WhateverTheProviderAllows()
    {
        var panel = OpenEditDialog();

        PressEscape();

        Assert.True(panel.Instance._editRepository);
        Assert.Contains("Edit Repository Details", _provider.Markup);
    }

    /// <summary>
    /// The control: through the same provider, a dialog that does not refuse them is closed by a
    /// backdrop click and by Escape - so the gestures above reached the dialog.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThroughTheSameProvider_ADialogThatDoesNotRefuseThemIsClosed(bool byBackdrop)
    {
        RenderPermissiveProviders();
        var dialogService = Services.GetRequiredService<IDialogService>();
        await _provider.InvokeAsync(async () => await dialogService.ShowAsync<ConfirmDeleteProjectDialog>(
            "Delete Project", new DialogParameters<ConfirmDeleteProjectDialog> { { x => x.ProjectName, "Default" } }));
        _provider.WaitForAssertion(() => Assert.Contains("Are you sure", _provider.Markup));

        if (byBackdrop)
            ClickBackdrop();
        else
            PressEscape();

        _provider.WaitForAssertion(() => Assert.DoesNotContain("Are you sure", _provider.Markup));
    }
}
