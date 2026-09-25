using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using MLQT.Shared.Models;
using Moq;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// B360 — choosing a UI theme saves the syntax colours it re-derives, not only the UI.
/// </summary>
/// <remarks>
/// A UI preset switches the syntax theme to its dark or light variant in memory. Code Review reads
/// <c>"SyntaxHighlighting"</c> from disk when its tab opens, so picking Dark while only
/// <c>"UI"</c> was saved showed light syntax colours on a dark page, and after a restart the two
/// settings disagreed.
/// </remarks>
public class SettingsUIThemeSaveTests : MlqtComponentTestBase
{
    [Fact]
    public void ChoosingDark_SavesTheDarkSyntaxColours()
    {
        var light = SyntaxHighlightingSettings.GetLightTheme();
        light.ThemeName = "VSCode";

        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetAsync("UI", It.IsAny<UISettings>())).ReturnsAsync(new UISettings { Theme = Theme.Light });
        settings.Setup(s => s.GetAsync("SyntaxHighlighting", It.IsAny<SyntaxHighlightingSettings>())).ReturnsAsync(light);

        SyntaxHighlightingSettings? saved = null;
        settings.Setup(s => s.SetAsync("SyntaxHighlighting", It.IsAny<SyntaxHighlightingSettings>()))
            .Callback<string, SyntaxHighlightingSettings>((_, value) => saved = value)
            .Returns(Task.CompletedTask);

        Services.AddSingleton(settings.Object);
        var panel = Render<SettingsUI>();

        panel.InvokeAsync(() => panel.Instance.ApplyPresetUITheme("Dark")).Wait();

        settings.Verify(s => s.SetAsync("UI", It.Is<UISettings>(ui => ui.Theme == Theme.Dark)), Times.Once);
        Assert.NotNull(saved);
        Assert.Equal(SyntaxHighlightingSettings.GetDarkTheme().BackgroundColor, saved!.BackgroundColor);
        Assert.NotEqual(light.BackgroundColor, saved.BackgroundColor);   // the fixture does change
    }
}
