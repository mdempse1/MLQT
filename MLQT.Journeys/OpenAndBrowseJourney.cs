using Microsoft.Playwright;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// Journey 1 — the application starts, renders, and its scripts load.
/// </summary>
/// <remarks>
/// <para>The least interesting journey to read and the most important one to have: it is the only
/// thing that says the whole stack stands up. Every other journey assumes it.</para>
///
/// <para>It is also the journey the migration will break first, and not in the component tree — the
/// composition root, the host page, the static-asset pipeline and the webview engine are all
/// exercised here, and all four are what phase 7b changes.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class OpenAndBrowseJourney(TestHostFixture host)
{
    [Fact]
    public async Task TheApplicationShell_Renders()
    {
        var page = await host.NewPageAsync();

        var response = await page.GotoAsync(host.BaseUrl);

        Assert.NotNull(response);
        Assert.True(response!.Ok, $"the page returned {response.Status}");

        // MudBlazor's own markup: if this is absent the component tree did not render, whatever the
        // status code said.
        await page.Locator(".mud-layout, .mud-appbar, .mud-drawer").First
                  .WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
    }

    [Fact]
    public async Task EveryAssetTheHostPageAsksFor_Loads()
    {
        // A 404 on a library script is silent in a browser. It surfaces much later as a feature that
        // renders an empty box, at which point the question looks like "is Cytoscape broken under
        // this engine" rather than "did the file arrive".
        //
        // **Every same-origin request, not just _content/ ones.** The narrow filter is what hid B120:
        // the page asks for the host's own app.css, this host had none, and every page load 404ed on
        // it unseen for a phase - in the one place a real missing asset would have shown up. Anything
        // the page fetches from this server is something it expected to get.
        var failures = new List<string>();
        var page = await host.NewPageAsync();
        page.Response += (_, response) =>
        {
            if (!response.Ok && response.Url.StartsWith(host.BaseUrl, StringComparison.OrdinalIgnoreCase))
                failures.Add($"{response.Status} {response.Url}");
        };

        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.True(failures.Count == 0, "the page asked for these and did not get them: " + string.Join(", ", failures));
    }

    [Fact]
    public async Task TheLibraryScriptsDefineTheirGlobals()
    {
        // Loading is not the same as working: a script that arrives but throws while evaluating
        // leaves the page looking fine and the global missing. This is probe 3 of the /selftest
        // route in 7a-7, run here first because the test host can answer it today.
        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        foreach (var (global, script) in new[]
                 {
                     ("cytoscape", "cytoscape.min.js"),
                     ("dagre", "dagre.min.js"),
                     ("cytoscapeGraph", "cytoscapeGraph.js"),
                     ("diffViewer", "diffViewer.js"),
                     ("spellCheck", "spellCheck.js"),
                     ("getDimensions", "the inline helper in the host page"),
                 })
        {
            var defined = await page.EvaluateAsync<bool>($"() => typeof window['{global}'] !== 'undefined'");
            Assert.True(defined, $"window.{global} is undefined - {script} did not evaluate");
        }
    }

    [Fact]
    public async Task ThePipelineReportsItselfIdle_WithNothingLoaded()
    {
        // The signal every later journey waits on. With no library open it must return immediately
        // rather than hanging, or the first journey to use it hangs the suite instead of failing it.
        await host.WaitForIdleAsync();
    }
}
