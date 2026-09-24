using Bunit;
using DymolaInterface;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.Helpers;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using Moq;
using OpenModelicaInterface;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// The check time limit each tool now has in the External Tools tab (B263): shown in seconds, kept
/// in milliseconds, and labelled with the name a timed-out check tells the user to look for.
/// </summary>
public class SettingsExternalToolsTests : MlqtComponentTestBase
{
    private IRenderedComponent<SettingsExternalTools> RenderPanel(int dymolaMs, int omcMs)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetAsync("Dymola", It.IsAny<DymolaSettings>()))
            .ReturnsAsync(new DymolaSettings { CommandTimeoutMs = dymolaMs });
        settings.Setup(s => s.GetAsync("OpenModelica", It.IsAny<OpenModelicaSettings>()))
            .ReturnsAsync(new OpenModelicaSettings { CommandTimeoutMs = omcMs });

        Services.AddSingleton(settings.Object);
        Services.AddSingleton(new Mock<IFilePickerService>().Object);

        RenderProviders();
        return Render<SettingsExternalTools>();
    }

    [Fact]
    public void EachToolsLimitIsShownInSeconds()
    {
        var panel = RenderPanel(dymolaMs: 300_000, omcMs: 60_000);

        Assert.Equal(300, panel.Instance.DymolaTimeLimitSeconds);
        Assert.Equal(60, panel.Instance.OpenModelicaTimeLimitSeconds);
    }

    [Fact]
    public void ALimitEnteredInSecondsIsKeptInMilliseconds()
    {
        var panel = RenderPanel(300_000, 60_000);

        panel.Instance.DymolaTimeLimitSeconds = 900;
        panel.Instance.OpenModelicaTimeLimitSeconds = 0;   // no limit

        Assert.Equal(900, panel.Instance.DymolaTimeLimitSeconds);
        Assert.Equal(0, panel.Instance.OpenModelicaTimeLimitSeconds);
    }

    [Fact]
    public void ANegativeLimitIsNoLimitRatherThanAnError()
    {
        // The field's minimum is zero, but a value typed past it must not reach a setting the
        // interfaces would refuse - a refused Dymola timeout throws, and that reads as every
        // command failing.
        var panel = RenderPanel(300_000, 60_000);

        panel.Instance.DymolaTimeLimitSeconds = -5;

        Assert.Equal(0, panel.Instance.DymolaTimeLimitSeconds);
    }

    [Fact]
    public void BothToolsHaveTheFieldATimedOutCheckPointsTo()
    {
        // The message a timed-out check shows names the setting by this text; a field labelled
        // anything else would send the user looking for something that is not there.
        var panel = RenderPanel(300_000, 60_000);

        var labels = panel.FindAll("label").Select(l => l.TextContent.Trim()).ToList();

        Assert.Equal(2, labels.Count(l => l.StartsWith(ToolTimeLimit.SettingName, StringComparison.Ordinal)));
    }
}
