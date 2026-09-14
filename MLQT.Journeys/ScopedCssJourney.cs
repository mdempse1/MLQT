using Microsoft.Playwright;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// The scoped CSS in <c>MLQT.Shared</c>'s <c>.razor.css</c> files reaches the browser.
/// </summary>
/// <remarks>
/// <para>A component's scoped stylesheet is delivered through the *host's* bundle, which is named
/// after the host assembly and so cannot be listed in <see cref="MLQT.Shared.HostAssetManifest"/> —
/// each host links its own. <c>HostAssetManifestTests</c> holds the Photino page to that by reading
/// the file, and cannot hold this host to it: the test host's page is generated Razor rather than a
/// <c>wwwroot/index.html</c>, so it is not in that test's list. It was the one that was missing the
/// link.</para>
///
/// <para><b>The failure is invisible until you look at a picture.</b> The page renders, MudBlazor is
/// styled, and the code viewer's syntax colours are right — <c>CodeReview</c> injects those as a
/// runtime <c>&lt;style&gt;</c> block from the user's settings, so the one part of the code display
/// that is <i>not</i> scoped goes on working. What disappears is every rule in a <c>.razor.css</c>:
/// <c>DiffViewer</c> loses <c>display: flex</c> on <c>.diff-side-by-side</c> and stacks the Original
/// and Modified panes vertically, the added/removed line colours go, and the rule rows in settings
/// lose their spacing. That reached <c>Documentation/Images</c>, because the screenshots are
/// generated through this host.</para>
///
/// <para>So this asks the browser rather than the file: are the scoped rules actually loaded and
/// parsed? Checked by selector text rather than by computed style, so it needs no particular page to
/// be open and does not depend on the generated scope hash, which changes whenever the stylesheet
/// does.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class ScopedCssJourney(TestHostFixture host)
{
    /// <summary>
    /// Every selector the page has loaded, following <c>@import</c> — which is how the host bundle
    /// reaches a referenced library's, and therefore the only way MLQT.Shared's rules arrive.
    /// </summary>
    private const string SelectorsInLoadedStylesheets = """
        () => {
            const selectors = [];
            const walk = sheet => {
                let rules;
                try { rules = sheet.cssRules; } catch { return; }   // a cross-origin sheet
                for (const rule of rules) {
                    if (rule.styleSheet) walk(rule.styleSheet);      // @import
                    else if (rule.selectorText) selectors.push(rule.selectorText);
                }
            };
            for (const sheet of document.styleSheets) walk(sheet);
            return selectors;
        }
        """;

    [Fact]
    public async Task TheHostPageLinksItsScopedCssBundle()
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var hrefs = await page.EvalOnSelectorAllAsync<string[]>(
            "link[rel=stylesheet]", "links => links.map(l => l.getAttribute('href'))");

        Assert.Contains(hrefs, h => h is not null && h.EndsWith(".styles.css", StringComparison.Ordinal));
    }

    [Theory]
    // One selector from each of MLQT.Shared's scoped stylesheets, so a bundle that stops being
    // linked fails here rather than in a screenshot nobody diffs.
    //
    // CodeViewer is deliberately not among them, and that is the whole shape of this defect:
    // every selector it scopes is also written by CodeReview's runtime <style> block, so it goes on
    // looking right with no scoped CSS at all and would have passed this test throughout.
    [InlineData(".diff-side-by-side")]   // DiffViewer      - the panes that stacked
    [InlineData(".diff-line-added")]     // DiffViewer      - the green/red that went with them
    [InlineData(".mlqt-rule-row")]       // RuleSeverityRow
    [InlineData(".mlqt-trend-chart")]    // MetricsDashboard
    public async Task AScopedRuleIsLoaded(string selector)
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var selectors = await page.EvaluateAsync<string[]>(SelectorsInLoadedStylesheets);

        Assert.Contains(selectors, s => s.Contains(selector, StringComparison.Ordinal));
    }
}
