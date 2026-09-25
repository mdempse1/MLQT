using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.Interfaces;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// The Metrics tab's coverage grid is as tall as what is in it.
/// </summary>
/// <remarks>
/// <para>Reported 2026-09-24 as a large blank space between the coverage bars and the burndown
/// chart. MainLayout's inline stylesheet restated MudBlazor's <c>.mud-grid</c> rule with
/// <c>height: 100%</c> added, from the initial commit, so every MudGrid took the height of whatever
/// contained it. Everywhere else that height is automatic and 100% of it is nothing; the Metrics page
/// sits in the tab host's flex column (B250), which gives it a definite height, so its grid filled
/// the whole page and pushed everything after it down by the difference.</para>
///
/// <para>Only a browser can see this — the markup is right, and it is the cascade that is not.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class MetricsLayoutJourney(TestHostFixture host) : IDisposable
{
    private readonly LibraryFixture _library = new();

    public void Dispose() => _library.Dispose();

    [Fact]
    public async Task TheCoverageGridIsNoTallerThanItsContent()
    {
        var libraries = host.Services.GetRequiredService<ILibraryDataService>();
        // Every journey's library is called Lib, so they share class ids; start from an empty graph.
        await host.ResetLibrariesAsync();
        await libraries.AddLibraryFromDirectoryAsync(_library.LibraryPath);
        await host.WaitForIdleAsync();

        var page = await host.NewPageAsync();
        // Tall, because how far the grid stretches depends on the window's height: at the journeys'
        // usual size this fixture's small library shows no gap at all, and the test passed with the
        // bug in place until the window was made taller.
        await page.SetViewportSizeAsync(1200, 2200);
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator(".mud-tab").Nth(4).WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });

        await ShellReadiness.WaitUntilClickableAsync(page);
        await page.Locator(".mud-tab").Nth(3).ClickAsync();

        // The page computes on arrival when it can; the button is there when it has not. Asked for
        // until the grid is there rather than once: counting the button and then clicking it failed
        // on CI, because the arrival compute finished in between and relabelled it Refresh, and the
        // click waited its whole 30 seconds for a "Compute" that no longer existed.
        await ShellReadiness.WaitUntilClickableAsync(page);
        var compute = page.GetByRole(AriaRole.Button, new() { Name = "Compute", Exact = true });
        var grid = page.Locator(".mud-tab-panel-active .mud-grid").First;

        var giveUp = DateTime.UtcNow.AddSeconds(60);
        while (!await grid.IsVisibleAsync())
        {
            Assert.True(DateTime.UtcNow < giveUp, "the Metrics tab never showed its coverage grid");

            if (await compute.IsVisibleAsync() && await compute.IsEnabledAsync())
            {
                try
                {
                    await compute.ClickAsync(new LocatorClickOptions { Timeout = 2_000 });
                }
                catch (TimeoutException)
                {
                    // Relabelled or disabled between the check and the click - the loop looks again.
                }
            }

            await page.WaitForTimeoutAsync(200);
        }

        // How far the grid runs past the lowest thing in any of its columns.
        var slack = await grid.EvaluateAsync<double>(@"grid => {
            const bottom = Math.max(...[...grid.children].flatMap(item =>
                [...item.children].map(c => c.getBoundingClientRect().bottom)));
            return grid.getBoundingClientRect().bottom - bottom;
        }");

        Assert.True(slack < 40,
            $"the coverage grid runs {slack:0}px past its content, which shows as a blank space above the trend");
    }
}
