using Microsoft.Playwright;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// <c>mudBlazorWorkarounds.js</c> takes MudBlazor's <c>tr:not(:has(>*))</c> selector out of the page,
/// and only that.
/// </summary>
/// <remarks>
/// <para>MudBlazor 9.9.0 added the selector to its sticky-header table rules. Under WebKit its
/// presence alone makes every child inserted anywhere in the document re-check it, and recolouring a
/// 5,000-line class went from 3-4 s to 75-110 s - <see cref="LargeClassLineLimitJourney"/> timing
/// out in the nightly WebKit run is how it was found. That journey is the measure; this one says
/// whether the workaround is still doing anything, which no timing can say under Chromium.</para>
///
/// <para>When it fails because nothing was removed, MudBlazor has stopped shipping the selector:
/// check the nightly WebKit run stays green without the script, and delete it.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class MudBlazorWorkaroundsJourney(TestHostFixture host)
{
    /// <summary>Every selector in the page's stylesheets, nested rules included.</summary>
    private const string AllSelectors = """
        () => {
            const out = [];
            const walk = rules => { for (const r of rules) {
                if (r.selectorText !== undefined) out.push(r.selectorText);
                else if (r.cssRules) walk(r.cssRules); } };
            for (const s of document.styleSheets) { try { walk(s.cssRules); } catch (e) {} }
            return out;
        }
        """;

    [Fact]
    public async Task TheSlowSelector_IsGone_AndTheRestOfItsRuleIsKept()
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var removed = await page.EvaluateAsync<int>("() => window.mudBlazorWorkarounds.removed");
        var selectors = await page.EvaluateAsync<string[]>(AllSelectors);

        Assert.True(removed > 0,
            "no MudBlazor rule carried the :has(>*) selector - if a MudBlazor upgrade dropped it, delete mudBlazorWorkarounds.js");
        Assert.DoesNotContain(selectors, s => s.Replace(" ", "").Contains(":has(>*)", StringComparison.Ordinal));

        // The selector shared its rule with the plain loading-bar one, which must still be there.
        Assert.Contains(selectors, s => s.Contains(".mud-table-loading-row:first-child", StringComparison.Ordinal));
    }
}
