using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using ModelicaGraph;
using ModelicaParser.Helpers;
using Moq;
using MudBlazor;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// The tools whose built-ins <c>MLQT.Reference.Undeclared</c> accepts: a checkbox per tool, offered
/// only while the rule is on, written to <c>StyleCheckingSettings.AcceptedToolBuiltIns</c>.
/// </summary>
public class SettingsRepositoriesToolBuiltInsTests : MlqtComponentTestBase
{
    private const string Heading = "Accept the built-in functions and flags of:";
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mlqt-tests", "Tools");
    private IRenderedComponent<MudDialogProvider> _provider = null!;

    private (IRenderedComponent<SettingsRepositories> Panel, StyleCheckingSettings Settings) Open(bool ruleOn)
    {
        var settings = new StyleCheckingSettings { CheckUndeclaredNames = ruleOn, AcceptedToolBuiltIns = ["openmodelica"] };
        var repository = new Repository { Id = "repo", Name = "Lib", LocalPath = Root, VcsRootPath = Root, StyleSettings = settings };
        var project = new ProjectProfile { Name = "Default" };
        var service = new Mock<IRepositoryService>();
        service.SetupGet(s => s.Repositories).Returns([repository]);
        service.Setup(s => s.GetProjects()).Returns([project]);
        service.Setup(s => s.GetActiveProject()).Returns(project);
        var dictionaries = new Mock<IDictionaryManagerService>();
        dictionaries.Setup(d => d.GetAvailableDictionaries()).Returns([]);

        Services.AddSingleton(service.Object);
        Services.AddSingleton(dictionaries.Object);
        Services.AddSingleton(new Mock<ISettingsService>().Object);
        Services.AddSingleton(new Mock<IFilePickerService>().Object);
        Render<MudPopoverProvider>();
        _provider = Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();

        var panel = Render<SettingsRepositories>();
        panel.InvokeAsync(() => panel.Instance.OnRepoClick(repository));
        _provider.WaitForAssertion(() => Assert.Contains("Edit Repository Details", _provider.Markup));
        return (panel, settings);
    }

    [Fact]
    public void TheToolsAreOffered_OnlyWhileTheRuleIsOn()
    {
        Open(ruleOn: false);
        Assert.DoesNotContain(Heading, _provider.Markup);
    }

    [Fact]
    public void TickingATool_AcceptsItsBuiltIns_AndUntickingOneRemovesIt()
    {
        var (panel, settings) = Open(ruleOn: true);
        Assert.Contains(Heading, _provider.Markup);

        // A tool written by hand in another case is still the one ticked.
        Assert.True(panel.Instance.IsToolAccepted(ToolBuiltIns.OpenModelica));
        Assert.False(panel.Instance.IsToolAccepted(ToolBuiltIns.Dymola));

        var dymola = _provider.FindAll("label").First(l => l.TextContent.Trim() == ToolBuiltIns.Dymola).QuerySelector("input")!;
        dymola.Change(true);
        Assert.Contains(ToolBuiltIns.Dymola, settings.AcceptedToolBuiltIns);

        panel.InvokeAsync(() => panel.Instance.SetToolAccepted(ToolBuiltIns.OpenModelica, false));
        Assert.Equal([ToolBuiltIns.Dymola], settings.AcceptedToolBuiltIns);
    }
}
