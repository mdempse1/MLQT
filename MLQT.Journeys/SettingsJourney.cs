using Microsoft.Playwright;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// Journey 7 — the settings tabs, which no test of any kind reached before.
/// </summary>
/// <remarks>
/// <para>Phase 7b-A. <c>SettingsUI</c> (82 coverable lines), <c>SettingsExternalTools</c> (56) and
/// <c>SettingsReferenceLibraries</c> (43) were all at <b>0%</b>: reachable only by rendering, and
/// nothing rendered them. The coverage ledger said exactly that, honestly, for each of them.</para>
///
/// <para>Written before the Photino port rather than after, because a journey written afterwards
/// proves nothing about the migration — there would be no MAUI build left to have proved it passed
/// on first. This runs against the test host today and against Photino in 7b-5.</para>
///
/// <para><b>On selectors.</b> The five top-level tabs carry an icon and a tooltip and no text, so
/// they can only be addressed positionally; the settings tabs inside them have labels and are
/// addressed by role and name. Clicking the top-level Settings tab is therefore
/// <c>.mud-tab</c> index 4 — and it has to be taken before the click, because opening the panel adds
/// four more tabs to the same selector and "the last tab" stops meaning what it did.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class SettingsJourney(TestHostFixture host)
{
    /// <summary>The top-level tabs, in the order MainLayout declares them.</summary>
    private const int SettingsTabIndex = 4;

    private async Task<IPage> OpenSettingsAsync()
    {
        // The host is shared by the whole collection, so an earlier journey may have left a library
        // loaded and the analysis pipeline still running - which delays this page's own startup and
        // was enough to blow a 30-second wait on a CI runner while passing locally every time.
        // Waiting for idle first is what the other journeys do and what this one should have.
        await host.WaitForIdleAsync();

        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var tabs = page.Locator(".mud-tab");
        await tabs.Nth(SettingsTabIndex).WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await tabs.Nth(SettingsTabIndex).ClickAsync();

        await page.GetByRole(AriaRole.Tab, new() { Name = "UI Settings" })
                  .WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

        return page;
    }

    private static Task OpenTabAsync(IPage page, string name) =>
        page.GetByRole(AriaRole.Tab, new() { Name = name }).ClickAsync();

    [Fact]
    public async Task TheSettingsTab_ShowsItsFourSections()
    {
        var page = await OpenSettingsAsync();

        foreach (var tab in new[] { "UI Settings", "External Tools", "Reference Libraries", "Manage Repositories" })
            await Assertions.Expect(page.GetByRole(AriaRole.Tab, new() { Name = tab })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task EverySettingsTab_RendersWithoutError()
    {
        // The point of the journey. Each of these is a page of MudBlazor controls bound to a settings
        // object, and none had ever been rendered by a test - so a binding that throws on first
        // render was something only a user would find, on the tab they happened to open.
        var page = await OpenSettingsAsync();

        var errors = new List<string>();
        page.PageError += (_, e) => errors.Add(e);
        page.Console += (_, m) => { if (m.Type == "error") errors.Add(m.Text); };

        // Each panel is checked for its own content rather than the strip being clicked through and
        // one thing asserted at the end: an exception during render leaves that panel empty and the
        // tab strip perfectly intact, which looks like a working page.
        foreach (var (tab, landmark) in new[]
                 {
                     ("External Tools", "Dymola"),
                     ("Reference Libraries", "Library folders"),
                     ("Manage Repositories", "Project and repository settings"),
                     ("UI Settings", "UI Theme"),
                 })
        {
            await OpenTabAsync(page, tab);

            await Assertions.Expect(page.GetByText(landmark).First)
                            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        }

        Assert.True(errors.Count == 0, "the settings tabs raised: " + string.Join("; ", errors));
    }

    [Fact]
    public async Task ChoosingTheCustomTheme_RevealsThePaletteEditor()
    {
        // The behaviour B107 and B108 both lived in, driven the way a user drives it. The presets
        // were correct as functions while the page disagreed with them - so this asserts the thing a
        // unit test of ThemePresets cannot: that clicking the button shows the editor.
        var page = await OpenSettingsAsync();

        await Assertions.Expect(page.GetByText("Custom UI Theme")).ToBeHiddenAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Custom" }).First.ClickAsync();

        await Assertions.Expect(page.GetByText("Custom UI Theme")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task SwitchingBetweenLightAndDark_KeepsThePageWorking()
    {
        // Changing the UI theme re-applies the syntax theme underneath (that coupling is what B108
        // was), and it raises OnThemeChanged, which MainLayout handles by rebuilding the MudTheme.
        // A journey is the only place that whole chain runs.
        var page = await OpenSettingsAsync();

        var errors = new List<string>();
        page.PageError += (_, e) => errors.Add(e);

        foreach (var theme in new[] { "Dark", "Light", "Dark" })
        {
            await page.GetByRole(AriaRole.Button, new() { Name = theme }).First.ClickAsync();
            await page.WaitForTimeoutAsync(300);
        }

        Assert.True(errors.Count == 0, "switching themes raised: " + string.Join("; ", errors));
        await Assertions.Expect(page.GetByText("UI Theme")).ToBeVisibleAsync();
    }
}
