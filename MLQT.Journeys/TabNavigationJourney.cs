using Microsoft.Playwright;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// Journey 8 — every top-level tab renders.
/// </summary>
/// <remarks>
/// <para>Phase 7b-A. The application is a single page with five tabs, and until now only the first
/// one was ever rendered by a test. <c>Dependencies</c> (112 coverable lines) sat at 0%,
/// <c>ExternalResources</c> (339) at 9% and <c>MetricsDashboard</c> (360) at 4% — each one a page a
/// user reaches in a single click, and none of them reached by anything.</para>
///
/// <para>The assertion is narrow on purpose: <b>that the tab renders its own content and raises
/// nothing</b>. What each page does with a library loaded is other journeys' work, and pinning the
/// empty-state wording would make this a test of copy. What it catches is the failure that costs
/// most and is easiest to ship: a component that throws during its first render, which leaves the
/// tab strip intact and the panel blank — a page that looks like it is still loading.</para>
///
/// <para>It is also the shape most likely to break under a different webview engine, which is why it
/// is written before the port rather than after: every one of these pages does JS interop on first
/// render, and 7a-6 found that MLQT's components issue interop during initial render at all.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class TabNavigationJourney(TestHostFixture host)
{
    /// <summary>The five tabs MainLayout declares, in order, with something each one must show.</summary>
    /// <remarks>
    /// They carry an icon and a tooltip and no text, so they can only be addressed positionally.
    /// The landmarks are the empty states, because the host starts with no library open — which is
    /// itself the state a user meets on first launch and the one least often looked at.
    /// </remarks>
    public static TheoryData<int, string, string> Tabs() => new()
    {
        { 0, "Code Review",        "Findings" },
        { 1, "Dependencies",       "Dependency Network" },
        { 2, "External Resources", "No external resources detected" },
        { 3, "Metrics",            "Scope" },
        { 4, "Settings",           "UI Theme" },
    };

    private async Task<IPage> OpenAsync()
    {
        // The host is shared by the whole collection, so an earlier journey may have left a library
        // loaded and the analysis pipeline still running - which delays this page's own startup and
        // was enough to blow a 30-second wait on a CI runner while passing locally every time.
        // Waiting for idle first is what the other journeys do and what this one should have.
        await host.WaitForIdleAsync();

        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator(".mud-tab").Nth(4).WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        return page;
    }

    [Fact]
    public async Task TheShellOffersFiveTabs()
    {
        // Guards the positional addressing every other test here depends on. A tab added or removed
        // silently shifts the indices, and the symptom would be a journey asserting the wrong page's
        // content - which would still pass if the two happened to share a word.
        var page = await OpenAsync();

        Assert.Equal(5, await page.Locator(".mud-tab").CountAsync());
    }

    [Theory]
    [MemberData(nameof(Tabs))]
    public async Task EachTabRendersItsOwnContent(int index, string name, string landmark)
    {
        var page = await OpenAsync();

        var errors = new List<string>();
        page.PageError += (_, e) => errors.Add(e);

        await page.Locator(".mud-tab").Nth(index).ClickAsync();

        await Assertions.Expect(page.GetByText(landmark).First)
                        .ToBeVisibleAsync(new() { Timeout = 20_000 });

        Assert.True(errors.Count == 0, $"the {name} tab raised: " + string.Join("; ", errors));
    }

    [Fact]
    public async Task MovingThroughEveryTabAndBack_RaisesNothing()
    {
        // Tabs are disposed and recreated as the user moves between them, and MudTabs renders only
        // the active panel. That makes this the path where an unsubscribed event handler shows up -
        // the failure SharedUiConventionTests guards statically, seen here dynamically. A component
        // that leaks a subscription raises on the *second* visit, not the first, which is why this
        // goes round twice.
        var page = await OpenAsync();

        var errors = new List<string>();
        page.PageError += (_, e) => errors.Add(e);
        page.Console += (_, m) => { if (m.Type == "error") errors.Add(m.Text); };

        for (var pass = 0; pass < 2; pass++)
        {
            for (var index = 0; index < 5; index++)
            {
                await page.Locator(".mud-tab").Nth(index).ClickAsync();
                await page.WaitForTimeoutAsync(200);
            }
        }

        Assert.True(errors.Count == 0, "moving between tabs raised: " + string.Join("; ", errors));
    }
}
