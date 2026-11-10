using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using MLQT.Shared.Models;
using Moq;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// The Reference Libraries tab loads and unloads a folder's libraries as the list is edited, and
/// refuses to while anything else is using the graph.
/// </summary>
/// <remarks>
/// Removing a folder used to change only the saved list: its classes stayed in the graph until the
/// next project load, so a check of every class still accepted their names. Changing the graph at
/// once makes the edit unsafe under a load, a check, an analysis or a VCS operation - which is why the
/// tab now says no to it then.
/// </remarks>
public class SettingsReferenceLibrariesTests : MlqtComponentTestBase
{
    private readonly Mock<IReferenceLibraryLoader> _loader = new();
    private readonly Mock<IStyleCheckingService> _styleChecking = new();
    private readonly Mock<ILibraryDataService> _libraries = new();
    private readonly Mock<IFilePickerService> _picker = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly Mock<IRepositoryService> _repositories = new();
    private readonly List<string> _calls = [];

    private const string First = "first-folder";
    private const string Second = "second-folder";

    private IRenderedComponent<SettingsReferenceLibraries> RenderTab()
    {
        _settings.Setup(s => s.GetAsync("ReferenceLibraries", It.IsAny<ReferenceLibrarySettings>()))
            .ReturnsAsync(new ReferenceLibrarySettings { Paths = [First, Second] });
        _loader.Setup(l => l.LoadAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<bool>()))
            .Callback(() => _calls.Add("load folders"))
            .ReturnsAsync(new ReferenceLibraryLoadSummary());
        _loader.Setup(l => l.UnloadFolder(It.IsAny<string>()))
            .Callback(() => _calls.Add("unload folder"))
            .Returns(1);
        _repositories.Setup(r => r.LoadLibrariesNoLongerProvidedAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("restore repositories"))
            .ReturnsAsync(0);
        Services.AddSingleton(_repositories.Object);

        Services.AddSingleton(_loader.Object);
        Services.AddSingleton(_styleChecking.Object);
        Services.AddSingleton(_libraries.Object);
        Services.AddSingleton(_picker.Object);
        Services.AddSingleton(_settings.Object);

        RenderProviders();
        return Render<SettingsReferenceLibraries>();
    }

    private static void ClickRemove(IRenderedComponent<SettingsReferenceLibraries> tab, int row) =>
        tab.FindAll("tbody button")[row].Click();

    [Fact]
    public void RemovingAFolder_UnloadsIt_AndLoadsTheFoldersThatRemain()
    {
        var tab = RenderTab();

        ClickRemove(tab, 0);

        _loader.Verify(l => l.UnloadFolder(First), Times.Once);
        _loader.Verify(l => l.LoadAsync(
            It.Is<IReadOnlyList<string>>(p => p.SequenceEqual(new[] { Second })), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void RemovingAFolder_RestoresTheRepositoriesCopies_BeforeTheOtherFoldersAreAsked()
    {
        // A repository's copy of a library outranks a reference folder's. Asked the other way round,
        // a remaining folder would load its copy first and the repository's would then be skipped.
        var tab = RenderTab();

        ClickRemove(tab, 0);

        Assert.Equal(["unload folder", "restore repositories", "load folders"], _calls);
    }

    [Fact]
    public void AddingAFolder_LoadsIt()
    {
        var tab = RenderTab();
        _picker.Setup(p => p.PickFolderAsync(It.IsAny<string>())).ReturnsAsync("third-folder");

        tab.FindAll("button").Single(b => b.TextContent.Contains("Add folder")).Click();

        _loader.Verify(l => l.LoadAsync(
            It.Is<IReadOnlyList<string>>(p => p.Contains("third-folder")), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void WhileAStyleCheckRuns_RemovingAFolderIsRefused_AndTheListKeepsIt()
    {
        var tab = RenderTab();
        _styleChecking.Setup(s => s.IsRunning).Returns(true);

        ClickRemove(tab, 0);

        _loader.Verify(l => l.UnloadFolder(It.IsAny<string>()), Times.Never);
        Assert.Contains(First, tab.Markup);
        _settings.Verify(s => s.SetAsync("ReferenceLibraries", It.IsAny<ReferenceLibrarySettings>()), Times.Never);
    }

    [Fact]
    public void WhileAVcsOperationRuns_AddingAFolderIsRefused_BeforeThePickerOpens()
    {
        var tab = RenderTab();
        using var work = NavState.BeginVcsWork();

        tab.FindAll("button").Single(b => b.TextContent.Contains("Add folder")).Click();

        _picker.Verify(p => p.PickFolderAsync(It.IsAny<string>()), Times.Never);
        _loader.Verify(l => l.LoadAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void EachThingUsingTheGraph_IsNamed_AndNothingIsWhenIdle()
    {
        var tab = RenderTab();
        Assert.Null(tab.Instance.BusyWith());

        _libraries.Setup(l => l.IsDependencyAnalysisRunning).Returns(true);
        Assert.Equal("dependency analysis is running", tab.Instance.BusyWith());

        _styleChecking.Setup(s => s.IsRunning).Returns(true);
        Assert.Equal("a style check is running", tab.Instance.BusyWith());

        using (NavState.BeginVcsWork())
            Assert.Equal("a version control operation is running", tab.Instance.BusyWith());

        NavState.StartupProgress("Loading libraries from repositories");
        Assert.Equal("the project is loading", tab.Instance.BusyWith());
    }
}
