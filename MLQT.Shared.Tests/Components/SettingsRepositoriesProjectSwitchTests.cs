using System.Text.RegularExpressions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using Moq;
using MudBlazor;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// B435 - a project switch that ends without <c>OnProjectChanged</c> must not leave its progress
/// published.
/// </summary>
/// <remarks>
/// <para><c>ProjectSwitchStarting</c> has MainLayout open its six-step dialog and publish its first
/// step on <see cref="Models.AppState.StartupStep"/>, and only the handler of <c>OnProjectChanged</c>
/// closed either. A switch that threw before raising it, or returned early because the project was
/// not found, left both for the rest of the session - and since B422/B423 a reload afterwards showed
/// the non-closable earlier-run dialog for ever and skipped startup.</para>
///
/// <para>MainLayout cannot be rendered in this suite, so its part is played by a handler that
/// publishes the step as it does; its own closing of the dialog is held by reading its source.</para>
/// </remarks>
public class SettingsRepositoriesProjectSwitchTests : MlqtComponentTestBase
{
    private const string FirstStep = "Loading libraries from repositories";

    private readonly ProjectProfile _project = new() { Name = "Default" };
    private readonly Mock<IRepositoryService> _service = new();
    private int _abandoned;
    private IRenderedComponent<MudDialogProvider> _dialogs = null!;

    private IRenderedComponent<SettingsRepositories> RenderPanel(params ProjectProfile[] projects)
    {
        _service.SetupGet(s => s.Repositories).Returns([]);
        _service.Setup(s => s.GetProjects()).Returns(projects.Length > 0 ? projects : [_project]);
        _service.Setup(s => s.GetActiveProject()).Returns(_project);
        _service.SetupGet(s => s.RepositoryRemovedSinceProjectLoad).Returns(true);

        var dictionaries = new Mock<IDictionaryManagerService>();
        dictionaries.Setup(d => d.GetAvailableDictionaries()).Returns([]);

        Services.AddSingleton(_service.Object);
        Services.AddSingleton(dictionaries.Object);
        Services.AddSingleton(new Mock<ISettingsService>().Object);
        Services.AddSingleton(new Mock<IFilePickerService>().Object);

        // MainLayout's part: publish the switch's first step when it is announced.
        NavState.OnProjectSwitchStarting += () => NavState.StartupProgress(FirstStep);
        NavState.OnProjectSwitchAbandoned += () => _abandoned++;

        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
        return Render<SettingsRepositories>();
    }

    private ISnackbar Snackbar => Services.GetRequiredService<ISnackbar>();

    /// <summary><b>Load project again</b>, which reaches <c>LoadProject</c>.</summary>
    private static void LoadProjectAgain(IRenderedComponent<SettingsRepositories> panel)
        => panel.Find("button[aria-label='Load project again']").Click();

    [Fact]
    public void ASwitchThatThrows_ClearsItsProgressAndSaysWhatFailed()
    {
        _service.Setup(s => s.SwitchProjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("the settings file is read-only"));
        var panel = RenderPanel();

        LoadProjectAgain(panel);

        panel.WaitForAssertion(() => Assert.Null(NavState.StartupStep));
        Assert.Equal(1, _abandoned);
        var message = Assert.Single(Snackbar.ShownSnackbars);
        Assert.Equal(Severity.Error, message.Severity);
        Assert.Contains("the settings file is read-only", message.Message);
    }

    [Fact]
    public void ASwitchToAProjectThatIsNotFound_ClearsItsProgressAndSaysSo()
    {
        // SwitchProjectAsync returns early without raising OnProjectChanged.
        _service.Setup(s => s.SwitchProjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var panel = RenderPanel();

        LoadProjectAgain(panel);

        panel.WaitForAssertion(() => Assert.Null(NavState.StartupStep));
        Assert.Equal(1, _abandoned);
        var message = Assert.Single(Snackbar.ShownSnackbars);
        Assert.Equal(Severity.Error, message.Severity);
        Assert.Contains("not found", message.Message);
    }

    [Fact]
    public async Task ASwitchThatHappens_IsNotAbandoned()
    {
        // The step is MainLayout's OnProjectChanged handler's to clear, not this panel's.
        _service.Setup(s => s.SwitchProjectAsync(_project.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Raises(s => s.OnProjectChanged += null, _project.Id);
        var panel = RenderPanel();

        var switched = await panel.InvokeAsync(() => panel.Instance.SwitchToProjectAsync(_project.Id));

        Assert.True(switched);
        Assert.Equal(0, _abandoned);
        Assert.Equal(FirstStep, NavState.StartupStep);
        Assert.Empty(Snackbar.ShownSnackbars);
    }

    [Fact]
    public async Task TheHelperStopsWatchingForTheChangeOnceTheSwitchEnds()
    {
        _service.Setup(s => s.SwitchProjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var panel = RenderPanel();

        await panel.InvokeAsync(() => panel.Instance.SwitchToProjectAsync(_project.Id));

        _service.VerifyAdd(s => s.OnProjectChanged += It.IsAny<Action<string>>(), Times.Once);
        _service.VerifyRemove(s => s.OnProjectChanged -= It.IsAny<Action<string>>(), Times.Once);
    }

    /// <summary>
    /// Both switches on the panel - creating a project and loading one - go through the one helper;
    /// a third that called the service directly would bring B435 back. Deleting is no longer a
    /// switch: only an inactive project can be deleted (B442).
    /// </summary>
    [Fact]
    public void EverySwitchOnThePanelGoesThroughTheHelper()
    {
        var source = Source(Path.Combine("MLQT.Shared", "Components", "SettingsRepositories.razor.cs"));

        Assert.Single(Regex.Matches(source, @"RepositoryService\.SwitchProjectAsync\("));
        Assert.Single(Regex.Matches(source, @"NavState\.ProjectSwitchStarting\(\)"));
        Assert.Equal(2, Regex.Matches(source, @"await SwitchToProjectAsync\(").Count);
    }

    /// <summary>
    /// B442 - the active project is not offered for deletion; another one is. The service refuses
    /// the active one too, so a saved active id cannot name a deleted project.
    /// </summary>
    [Fact]
    public void OnlyAnInactiveProjectOffersDelete()
    {
        var other = new ProjectProfile { Name = "Other" };
        var panel = RenderPanel(_project, other);

        Assert.Single(panel.FindAll("button[aria-label='Delete project']"));
    }

    [Fact]
    public void DeletingAProjectThatFailsToSave_SaysSo_AndDoesNotClaimItWasDeleted()
    {
        var other = new ProjectProfile { Name = "Other" };
        _service.Setup(s => s.DeleteProjectAsync(other.Id))
            .ThrowsAsync(new IOException("the settings file is read-only"));
        var panel = RenderPanel(_project, other);

        DeleteTheInactiveProject(panel);

        panel.WaitForAssertion(() => Assert.Single(Snackbar.ShownSnackbars));
        var message = Snackbar.ShownSnackbars.Single();
        Assert.Equal(Severity.Error, message.Severity);
        Assert.Contains("could not be deleted", message.Message);
        Assert.Contains("the settings file is read-only", message.Message);
        _service.Verify(s => s.SwitchProjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void DeletingAProject_AwaitsTheServiceAndSwitchesNothing()
    {
        var other = new ProjectProfile { Name = "Other" };
        _service.Setup(s => s.DeleteProjectAsync(other.Id)).ReturnsAsync(true);
        var panel = RenderPanel(_project, other);

        DeleteTheInactiveProject(panel);

        panel.WaitForAssertion(() => Assert.Equal("Project deleted", Assert.Single(Snackbar.ShownSnackbars).Message));
        _service.Verify(s => s.DeleteProjectAsync(other.Id), Times.Once);
        _service.Verify(s => s.SwitchProjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(NavState.StartupStep);
    }

    /// <summary>Clicks the one Delete offered, then Delete in the confirmation.</summary>
    private void DeleteTheInactiveProject(IRenderedComponent<SettingsRepositories> panel)
    {
        panel.Find("button[aria-label='Delete project']").Click();
        _dialogs.WaitForAssertion(() => Assert.Contains("Are you sure", _dialogs.Markup));
        _dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Delete").Click();
    }

    /// <summary>
    /// B447 - a new project is switched to only once its save has landed, so the create's save
    /// cannot land after the switch's and put the previous project back as the active one.
    /// </summary>
    [Fact]
    public void CreatingAProject_SwitchesToItOnlyOnceItsSaveHasLanded()
    {
        var created = new ProjectProfile { Name = "Fresh" };
        var saving = new TaskCompletionSource<ProjectProfile>();
        _service.Setup(s => s.CreateProjectAsync("Fresh")).Returns(saving.Task);
        _service.Setup(s => s.SwitchProjectAsync(created.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Raises(s => s.OnProjectChanged += null, created.Id);
        var panel = RenderPanel();

        NameANewProject(panel, "Fresh");

        _service.Verify(s => s.SwitchProjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        saving.SetResult(created);
        panel.WaitForAssertion(() =>
            _service.Verify(s => s.SwitchProjectAsync(created.Id, It.IsAny<CancellationToken>()), Times.Once));
    }

    [Fact]
    public void CreatingAProjectThatFailsToSave_SaysSo_AndSwitchesNothing()
    {
        _service.Setup(s => s.CreateProjectAsync("Fresh"))
            .ThrowsAsync(new IOException("the settings file is read-only"));
        var panel = RenderPanel();

        NameANewProject(panel, "Fresh");

        panel.WaitForAssertion(() => Assert.Single(Snackbar.ShownSnackbars));
        var message = Snackbar.ShownSnackbars.Single();
        Assert.Equal(Severity.Error, message.Severity);
        Assert.Contains("could not be created", message.Message);
        Assert.Contains("the settings file is read-only", message.Message);
        _service.Verify(s => s.SwitchProjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(NavState.StartupStep);
    }

    [Fact]
    public void RenamingAProject_AwaitsTheService()
    {
        _service.Setup(s => s.RenameProjectAsync(_project.Id, "Renamed")).Returns(Task.CompletedTask);
        var panel = RenderPanel();

        RenameTheProject(panel, "Renamed");

        panel.WaitForAssertion(() => _service.Verify(s => s.RenameProjectAsync(_project.Id, "Renamed"), Times.Once));
        Assert.Empty(Snackbar.ShownSnackbars);
    }

    [Fact]
    public void RenamingAProjectThatFailsToSave_SaysSo()
    {
        _service.Setup(s => s.RenameProjectAsync(_project.Id, "Renamed"))
            .ThrowsAsync(new IOException("the settings file is read-only"));
        var panel = RenderPanel();

        RenameTheProject(panel, "Renamed");

        panel.WaitForAssertion(() => Assert.Single(Snackbar.ShownSnackbars));
        var message = Snackbar.ShownSnackbars.Single();
        Assert.Equal(Severity.Error, message.Severity);
        Assert.Contains("could not be renamed", message.Message);
        Assert.Contains("the settings file is read-only", message.Message);
    }

    /// <summary>Clicks New Project, types the name, and confirms it.</summary>
    private static void NameANewProject(IRenderedComponent<SettingsRepositories> panel, string name)
    {
        panel.FindAll("button").Single(b => b.TextContent.Trim() == "New Project").Click();
        panel.WaitForAssertion(() => panel.Find("button[aria-label='Confirm new project name']"));
        panel.Find("input").Input(name);
        panel.Find("button[aria-label='Confirm new project name']").Click();
    }

    /// <summary>Clicks Rename on the one project, types the name, and confirms it.</summary>
    private static void RenameTheProject(IRenderedComponent<SettingsRepositories> panel, string name)
    {
        panel.Find("button[aria-label='Rename project']").Click();
        panel.WaitForAssertion(() => panel.Find("button[aria-label='Confirm rename']"));
        panel.Find("input").Input(name);
        panel.Find("button[aria-label='Confirm rename']").Click();
    }

    /// <summary>MainLayout closes its six-step dialog for a switch that was abandoned.</summary>
    [Fact]
    public void MainLayoutClosesItsDialogForAnAbandonedSwitch()
    {
        var source = Regex.Replace(Source(Path.Combine("MLQT.Shared", "Layout", "MainLayout.razor.cs")), @"\s+", " ");

        Assert.Contains("NavState.OnProjectSwitchAbandoned += OnProjectSwitchAbandoned;", source);
        Assert.Contains("NavState.OnProjectSwitchAbandoned -= OnProjectSwitchAbandoned;", source);
        Assert.Contains(
            "private void OnProjectSwitchAbandoned() { ResetStartupSteps(); _startupProcessRunning = false; _ = InvokeAsync(StateHasChanged); }",
            source);
    }

    private static string Source(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"{relativePath} was not found above the test binary");
    }
}
