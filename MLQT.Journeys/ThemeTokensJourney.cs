using Microsoft.Playwright;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// The theme's <c>--mlqt-*</c> tokens resolve in a real page to the sizes they stand for, and the
/// scrollbar rule built on them still does the one thing it is there for.
/// </summary>
/// <remarks>
/// <para><c>ThemeCssContractTests</c> holds that every token used is defined, which a reading of the
/// files can say. What only a browser can say is that the value arrives: a token that resolves to
/// another (<c>--mlqt-font-size</c> is MudBlazor's <c>--mud-typography-body1-size</c>) depends on
/// MudThemeProvider having written that one, and a <c>var()</c> that fails is not an error, only a
/// property at its initial value.</para>
///
/// <para>The scrollbar is the case worth a journey of its own. Its width is the B140 fix: any
/// <c>::-webkit-scrollbar</c> rule turns WebKitGTK's overlay scrollbars, which lay over the library
/// browser's buttons and took their clicks, into classic ones that take layout width. Nothing had
/// measured that it still did since the fix, and a custom property inside a scrollbar pseudo-element
/// is exactly where an engine could differ. So this measures the gutter a scrolling box reserves, and
/// is worth running with <c>MLQT_JOURNEY_BROWSER=webkit</c> after any change to that rule.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class ThemeTokensJourney(TestHostFixture host)
{
    private async Task<IPage> OpenAsync()
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        return page;
    }

    [Fact]
    public async Task AScrollingBox_ReservesTheThemesScrollbarWidth()
    {
        // Headless Chromium draws no scrollbars at all - Playwright launches it hiding them - so a box
        // there reserves 0 whatever the stylesheet says, and the measurement means nothing. WebKit
        // draws them, and is the engine the rule exists for. Measured when the rule was tokenised:
        // 4 under WebKit, 0 under headless Chromium.
        if (TestHostFixture.BrowserName != "webkit")
            Assert.Skip("headless Chromium hides scrollbars; run with MLQT_JOURNEY_BROWSER=webkit to measure this");

        var page = await OpenAsync();

        var gutter = await page.EvaluateAsync<int>("""
            () => {
                const box = document.createElement('div');
                box.style.cssText = 'position:absolute; left:0; top:0; width:100px; height:50px; overflow:scroll';
                box.innerHTML = '<div style="width:300px; height:300px"></div>';
                document.body.appendChild(box);
                const reserved = box.offsetWidth - box.clientWidth;
                box.remove();
                return reserved;
            }
            """);

        // 0 is the overlay scrollbar B140 was about; 15-17 is an engine's own, which means the rule
        // is not applying at all.
        Assert.Equal(4, gutter);
    }

    [Theory]
    // The text sizes are MlqtTheme's typography through MudBlazor's variables: 0.75rem and 0.625rem.
    [InlineData("body1-table", "font-size", "12px")]
    [InlineData("compact-table", "font-size", "10px")]
    // The spacing scale.
    [InlineData("body1-table", "padding-top", "4px")]
    [InlineData("compact-icon-button", "padding-left", "4px")]
    [InlineData("mud-table-toolbar", "padding-top", "8px")]
    [InlineData("mud-toolbar-gutters", "padding-right", "16px")]
    // Sizes and colours.
    [InlineData("mud-tab", "min-height", "32px")]
    [InlineData("mud-icon-size-medium", "font-size", "20px")]
    // MudBlazor's --mud-palette-lines-default, which in the light palette is 0.118, not 0.12.
    [InlineData("mlqt-split-divider", "background-color", "rgba(0, 0, 0, 0.118)")]
    public async Task ATokenisedRule_ComesOutAtTheThemesValue(string className, string property, string expected)
    {
        var page = await OpenAsync();

        var actual = await page.EvaluateAsync<string>("""
            ([className, property]) => {
                const probe = document.createElement('div');
                probe.className = className;
                document.body.appendChild(probe);
                const value = getComputedStyle(probe).getPropertyValue(property);
                probe.remove();
                return value;
            }
            """, new[] { className, property });

        Assert.Equal(expected, actual);
    }
}
