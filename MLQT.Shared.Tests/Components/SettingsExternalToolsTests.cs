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
    private readonly Mock<IFilePickerService> _picker = new();

    private IRenderedComponent<SettingsExternalTools> RenderPanel(int dymolaMs, int omcMs)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetAsync("Dymola", It.IsAny<DymolaSettings>()))
            .ReturnsAsync(new DymolaSettings { CommandTimeoutMs = dymolaMs });
        settings.Setup(s => s.GetAsync("OpenModelica", It.IsAny<OpenModelicaSettings>()))
            .ReturnsAsync(new OpenModelicaSettings { CommandTimeoutMs = omcMs });

        Services.AddSingleton(settings.Object);
        Services.AddSingleton(_picker.Object);

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

    // ---- Browsing for omc (B338) --------------------------------------------------------------
    //
    // On Linux omc is one file among many in /usr/bin, so the user chooses it; on Windows every
    // installer puts it under bin in its own folder, so the user chooses that folder.

    [Fact]
    public async Task OnLinux_TheChosenFileIsTheCompiler_AsChosen()
    {
        var omc = Path.Combine(Path.GetTempPath(), "mlqt-tests", Guid.NewGuid().ToString("N"), "omc");
        _picker.Setup(p => p.PickExecutableAsync(It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync(omc);
        var panel = RenderPanel(300_000, 60_000);

        await panel.InvokeAsync(() => panel.Instance.BrowseForOpenModelica(windows: false));

        Assert.Equal(omc, panel.Instance.OmcPath);
        _picker.Verify(p => p.PickFolderAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task OnWindows_TheChosenFolderIsTheInstallation_AndOmcIsTakenUnderBin()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mlqt-tests", Guid.NewGuid().ToString("N"));
        _picker.Setup(p => p.PickFolderAsync(It.IsAny<string>())).ReturnsAsync(folder);
        var panel = RenderPanel(300_000, 60_000);

        await panel.InvokeAsync(() => panel.Instance.BrowseForOpenModelica(windows: true));

        Assert.Equal(OpenModelicaSettings.OmcUnder(folder), panel.Instance.OmcPath);
        Assert.StartsWith(Path.Combine(folder, "bin"), panel.Instance.OmcPath);
        _picker.Verify(p => p.PickExecutableAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ACancelledPickerLeavesThePathAlone(bool windows)
    {
        var panel = RenderPanel(300_000, 60_000);
        var before = panel.Instance.OmcPath;

        await panel.InvokeAsync(() => panel.Instance.BrowseForOpenModelica(windows));

        Assert.Equal(before, panel.Instance.OmcPath);
    }

    // ---- Auto-detect ---------------------------------------------------------------------------

    [Fact]
    public void AutoDetect_FillsThePathWithWhatItFinds_WhateverWasThere()
    {
        var found = Path.Combine(Path.GetTempPath(), "mlqt-tests", "found", "omc");
        var panel = RenderPanel(300_000, 60_000);
        panel.Instance.FindInstalledOmc = () => found;

        panel.Find("button[aria-label='Auto-detect OpenModelica']").Click();

        Assert.Equal(found, panel.Instance.OmcPath);
    }

    [Fact]
    public async Task AutoDetect_BlanksThePathWhenNothingIsFound()
    {
        // A path left in place after a search that could not find it would read as confirmed.
        var chosen = Path.Combine(Path.GetTempPath(), "mlqt-tests", Guid.NewGuid().ToString("N"), "omc");
        _picker.Setup(p => p.PickExecutableAsync(It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync(chosen);
        var panel = RenderPanel(300_000, 60_000);
        await panel.InvokeAsync(() => panel.Instance.BrowseForOpenModelica(windows: false));
        Assert.Equal(chosen, panel.Instance.OmcPath);
        panel.Instance.FindInstalledOmc = () => "";

        panel.Find("button[aria-label='Auto-detect OpenModelica']").Click();

        Assert.Equal("", panel.Instance.OmcPath);
        Assert.DoesNotContain("Could not find OpenModelica", panel.Markup);
    }
}
