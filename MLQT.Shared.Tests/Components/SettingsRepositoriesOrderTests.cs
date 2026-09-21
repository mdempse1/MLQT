using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using ModelicaParser.SpellChecking;
using Moq;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// The Manage Repositories panel's reorder arrows (B188): when they are offered, and what a click
/// on one obliges.
/// </summary>
/// <remarks>
/// <para>Two things are worth holding here rather than in the service. The arrows are disabled at
/// the ends of the list, and the panel has <b>no Save button</b> — the Settings page hides its
/// action buttons for this tab — so a move that is not persisted where it is made is a move that
/// lasts until the window closes.</para>
///
/// <para>Rendered rather than constructed, because the list the arrows read is the one
/// <c>OnInitialized</c> builds from the service: a test that assigned it directly would pass
/// against a panel that never asked the service for anything.</para>
/// </remarks>
public class SettingsRepositoriesOrderTests : MlqtComponentTestBase
{
    private static Repository Repo(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "mlqt-tests", name);
        return new Repository { Id = name, Name = name, LocalPath = path, VcsRootPath = path };
    }

    private (IRenderedComponent<SettingsRepositories> Panel, Mock<IRepositoryService> Service) RenderPanel(
        params string[] names)
    {
        var repositories = names.Select(Repo).ToList();
        var project = new ProjectProfile { Name = "Default" };

        var service = new Mock<IRepositoryService>();
        service.SetupGet(s => s.Repositories).Returns(repositories);
        service.Setup(s => s.GetProjects()).Returns([project]);
        service.Setup(s => s.GetActiveProject()).Returns(project);

        var dictionaries = new Mock<IDictionaryManagerService>();
        dictionaries.Setup(d => d.GetAvailableDictionaries()).Returns([]);

        Services.AddSingleton(service.Object);
        Services.AddSingleton(dictionaries.Object);
        Services.AddSingleton(new Mock<ISettingsService>().Object);
        Services.AddSingleton(new Mock<IFilePickerService>().Object);

        RenderProviders();
        return (Render<SettingsRepositories>(), service);
    }

    [Fact]
    public void TheFirstRepositoryCannotMoveUp()
    {
        var (panel, _) = RenderPanel("Alpha", "Beta", "Gamma");

        Assert.False(panel.Instance.CanMove(panel.Instance._repositories[0], -1));
        Assert.True(panel.Instance.CanMove(panel.Instance._repositories[0], 1));
    }

    [Fact]
    public void TheLastRepositoryCannotMoveDown()
    {
        var (panel, _) = RenderPanel("Alpha", "Beta", "Gamma");

        Assert.False(panel.Instance.CanMove(panel.Instance._repositories[2], 1));
        Assert.True(panel.Instance.CanMove(panel.Instance._repositories[2], -1));
    }

    [Fact]
    public void AMiddleRepositoryCanMoveEitherWay()
    {
        var (panel, _) = RenderPanel("Alpha", "Beta", "Gamma");

        Assert.True(panel.Instance.CanMove(panel.Instance._repositories[1], -1));
        Assert.True(panel.Instance.CanMove(panel.Instance._repositories[1], 1));
    }

    /// <summary>A project with one repository offers neither arrow.</summary>
    [Fact]
    public void ALoneRepositoryCanMoveNeitherWay()
    {
        var (panel, _) = RenderPanel("Alpha");

        Assert.False(panel.Instance.CanMove(panel.Instance._repositories[0], -1));
        Assert.False(panel.Instance.CanMove(panel.Instance._repositories[0], 1));
    }

    [Fact]
    public void ARepositoryThatIsNotInTheListCannotMove()
    {
        var (panel, _) = RenderPanel("Alpha", "Beta");

        Assert.False(panel.Instance.CanMove(Repo("Elsewhere"), -1));
    }

    /// <summary>
    /// Both arrows are rendered for every repository — disabled at the ends rather than absent, so
    /// the column does not change width as a repository moves through the list.
    /// </summary>
    [Fact]
    public void EveryRepositoryRowOffersBothArrows()
    {
        var (panel, _) = RenderPanel("Alpha", "Beta", "Gamma");

        Assert.Equal(3, panel.FindAll("button[aria-label$=' up']").Count);
        Assert.Equal(3, panel.FindAll("button[aria-label$=' down']").Count);
    }

    [Fact]
    public void TheArrowsAtTheEndsOfTheListAreDisabled()
    {
        var (panel, _) = RenderPanel("Alpha", "Beta", "Gamma");

        Assert.True(panel.Find("button[aria-label='Move Alpha up']").HasAttribute("disabled"));
        Assert.False(panel.Find("button[aria-label='Move Alpha down']").HasAttribute("disabled"));
        Assert.True(panel.Find("button[aria-label='Move Gamma down']").HasAttribute("disabled"));
        Assert.False(panel.Find("button[aria-label='Move Gamma up']").HasAttribute("disabled"));
    }

    /// <summary>
    /// The tab has no Save button, so a move has to be written out where it is made.
    /// </summary>
    [Fact]
    public async Task AMoveIsPersistedImmediately()
    {
        var (panel, service) = RenderPanel("Alpha", "Beta");
        service.Setup(s => s.MoveRepository("Beta", -1)).Returns(true);

        await panel.InvokeAsync(() => panel.Instance.MoveRepositoryAsync(panel.Instance._repositories[1], -1));

        service.Verify(s => s.SaveRepositorySettingsAsync(), Times.Once);
    }

    /// <summary>
    /// A refused move writes nothing. Saving repository settings rewrites every repository's
    /// <c>.mlqt/settings.json</c>, which is not a thing to do because an arrow was clicked at the
    /// end of the list.
    /// </summary>
    [Fact]
    public async Task ARefusedMoveSavesNothing()
    {
        var (panel, service) = RenderPanel("Alpha", "Beta");
        service.Setup(s => s.MoveRepository(It.IsAny<string>(), It.IsAny<int>())).Returns(false);

        await panel.InvokeAsync(() => panel.Instance.MoveRepositoryAsync(panel.Instance._repositories[0], -1));

        service.Verify(s => s.SaveRepositorySettingsAsync(), Times.Never);
    }

    /// <summary>
    /// Clicking an arrow reorders rather than opening the repository's edit dialog — the row's own
    /// click handler does that, and the arrows sit inside the row. The row click below is the
    /// control: without it, a test that the dialog stayed shut would pass against a panel that
    /// never opened it at all.
    /// </summary>
    [Fact]
    public void ClickingAnArrowReordersWithoutOpeningTheEditDialog()
    {
        var (panel, service) = RenderPanel("Alpha", "Beta");
        service.Setup(s => s.MoveRepository("Alpha", 1)).Returns(true);

        panel.Find("button[aria-label='Move Alpha down']").Click();

        service.Verify(s => s.MoveRepository("Alpha", 1), Times.Once);
        Assert.False(panel.Instance._editRepository);
    }

    [Fact]
    public void ClickingTheRowItselfStillOpensTheEditDialog()
    {
        var (panel, _) = RenderPanel("Alpha", "Beta");

        panel.FindAll("tbody tr")[0].Click();

        Assert.True(panel.Instance._editRepository);
    }
}
