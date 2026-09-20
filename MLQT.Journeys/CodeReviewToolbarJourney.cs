using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.Interfaces;
using ModelicaParser.DataTypes;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// The controls above and around the findings table, in a real browser.
/// </summary>
/// <remarks>
/// <para><b>Why these are not unit tests.</b> The counting and the wording of the findings heading
/// are a pure function with its own tests (<c>CodeReviewFindingsHeadingTests</c>), and they cannot
/// see the thing that actually went wrong in B247: the heading was computed from one set of findings
/// while the table was filtered from another. A number being right about numbers it was handed says
/// nothing about whether it was handed the right ones. Only rendering the page and narrowing it does.
/// </para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class CodeReviewToolbarJourney(TestHostFixture host) : IDisposable
{
    private readonly LibraryFixture _library = new();

    public void Dispose() => _library.Dispose();

    /// <summary>Forty findings, distinguishable one from another, on the Code Review tab.</summary>
    private async Task<IPage> OpenWithFindingsAsync()
    {
        var libraries = host.Services.GetRequiredService<ILibraryDataService>();
        await libraries.AddLibraryFromDirectoryAsync(_library.LibraryPath);

        var review = host.Services.GetRequiredService<ICodeReviewService>();
        review.ClearLogMessages();
        review.AddLogMessages(Enumerable.Range(1, 40).Select(i => new LogMessage(
            $"Lib.Class{i:D2}", "Style warning", i, $"A finding about Class{i:D2}")));

        await host.WaitForIdleAsync();

        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator(".mud-tab").Nth(4).WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });

        await ShellReadiness.WaitUntilClickableAsync(page);
        await page.Locator(".mud-tab").Nth(0).ClickAsync();

        await Assertions.Expect(page.GetByText(new Regex(@"\d+ Findings to review")).First)
                        .ToBeVisibleAsync(new() { Timeout = 20_000 });
        return page;
    }

    private static ILocator Heading(IPage page) =>
        page.Locator(".mlqt-findings-pane .mud-toolbar .mud-typography").First;

    /// <summary>The findings search box, which is the one inside the findings pane.</summary>
    private static ILocator SearchBox(IPage page) =>
        page.Locator(".mlqt-findings-pane input[placeholder='Search']").First;

    /// <summary>
    /// Types <paramref name="text"/> into the findings search box and commits it.
    ///
    /// <para><b>The blur is not optional.</b> That field is a plain <c>@bind-Value</c> with no
    /// <c>Immediate</c>, so it binds on change rather than on input — filling it and reading the
    /// page straight away shows the heading and the table exactly as they were. The first version
    /// of these tests did that and reported the heading unchanged, which was true and not the
    /// fault under test.
    /// </para>
    /// </summary>
    private static async Task SearchForAsync(IPage page, string text)
    {
        await SearchBox(page).FillAsync(text);
        await SearchBox(page).BlurAsync();
        await page.WaitForTimeoutAsync(600);
    }

    /// <summary>
    /// B249 — one size across the toolbar. Measured, "Go to class" was 32px tall where everything
    /// beside it was 44, which is what "a different size from the rest" turned out to mean; the
    /// whole row then went to <c>Size.Small</c>, which is what CLAUDE.md asks for and what the rest
    /// of the application already uses.
    ///
    /// <para>Asserted as agreement rather than as a number. A test naming 34px would fail on a
    /// MudBlazor upgrade that changed nothing anyone could see, and would say nothing about the
    /// defect, which was one control disagreeing with its neighbours.</para>
    /// </summary>
    [Fact]
    public async Task EveryToolbarButtonIsTheSameHeight()
    {
        var page = await OpenWithFindingsAsync();

        var heights = await page.EvaluateAsync<int[]>(
            @"() => [...document.querySelectorAll('.mlqt-page-fill > .d-flex > .d-flex button')]
                     .map(b => Math.round(b.getBoundingClientRect().height))");

        Assert.True(heights.Length >= 10, $"expected the whole toolbar; found {heights.Length} buttons");

        var labels = await page.EvaluateAsync<string[]>(
            @"() => [...document.querySelectorAll('.mlqt-page-fill > .d-flex > .d-flex button')]
                     .map(b => `${b.getAttribute('aria-label') || b.className.split(' ').slice(0,3).join('.')}`
                               + `=${Math.round(b.getBoundingClientRect().height)}`)");

        Assert.True(heights.Distinct().Count() == 1,
            "the toolbar has more than one button height: " + string.Join(", ", labels));
    }

    /// <summary>
    /// B249 — the navigation arrows are on the toolbar, beside the button whose tooltip names them.
    ///
    /// <para>This overturns B197, which put them beside the class name because the history is one
    /// history and every tab moves the selection. Asserted here so that the decision is written
    /// down somewhere that fails if it is quietly reverted, in either direction.</para>
    /// </summary>
    [Fact]
    public async Task TheBackArrowIsBesideTheButtonThatNamesIt()
    {
        var page = await OpenWithFindingsAsync();

        var back = page.GetByLabel("Back to the previous class").First;
        var uses = page.GetByLabel("Go to a class this one uses").First;

        await Assertions.Expect(back).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var backBox = await back.BoundingBoxAsync();
        var usesBox = await uses.BoundingBoxAsync();
        Assert.NotNull(backBox);
        Assert.NotNull(usesBox);

        // Same row, and close enough together to read as one control. The button group puts them
        // edge to edge, so anything beyond a couple of buttons' width means they have drifted apart
        // again — which is the complaint, not the pixel count.
        Assert.Equal(backBox!.Y, usesBox!.Y, tolerance: 2);
        Assert.True(usesBox.X - backBox.X < 160,
            $"the arrows and the used-classes button should read as one control: {backBox.X} vs {usesBox.X}");
    }

    /// <summary>
    /// B248 — the find-in-code field, its match count and its arrows are one control.
    ///
    /// <para>What separated them was the count: it reserved 84px whether or not it had anything to
    /// say, so the toolbar's resting state — nothing searched for — was a box, a gap, and a pair of
    /// arrows that looked unrelated to either.</para>
    /// </summary>
    [Fact]
    public async Task TheCodeSearchFieldItsCountAndItsArrowsSitTogether()
    {
        var page = await OpenWithFindingsAsync();

        var gap = await page.EvaluateAsync<int>(@"() => {
            const field = document.querySelector('.mlqt-code-search input[placeholder=""Find in code""]');
            const prev = document.querySelector('.mlqt-code-search [aria-label=""Previous match""]');
            const f = field.closest('.mud-input-control').getBoundingClientRect();
            return Math.round(prev.getBoundingClientRect().left - f.right);
        }");

        // With nothing searched for there is no count to show, so the arrows follow the field
        // directly. Before, 84px of reserved-and-empty space sat between them.
        Assert.True(gap < 24, $"the arrows sit {gap}px from the field, which reads as a separate control");
    }

    [Fact]
    public async Task TheHeadingCountsWhatTheSearchLeft()
    {
        var page = await OpenWithFindingsAsync();

        Assert.Equal("40 Findings to review", (await Heading(page).InnerTextAsync()).Trim());

        await SearchForAsync(page, "Class07");

        Assert.Equal("1 of 40 findings", (await Heading(page).InnerTextAsync()).Trim());

        // ...and the number really is the number of rows, which is the whole claim. Asserted at a
        // count small enough that virtualisation cannot be hiding any of them.
        Assert.Equal(1, await page.Locator(".mlqt-findings-pane tbody tr").CountAsync());
    }

    [Fact]
    public async Task ASearchThatMatchesNothingSaysSoRatherThanLookingBroken()
    {
        // The reported case: an empty table under a heading claiming forty findings reads as the
        // table having failed, not as the filter being narrow.
        var page = await OpenWithFindingsAsync();

        await SearchForAsync(page, "nothingmatchesthis");

        Assert.Equal("0 of 40 findings", (await Heading(page).InnerTextAsync()).Trim());
    }

    [Fact]
    public async Task ClearingTheSearchPutsTheHeadingBack()
    {
        // Narrowing has to be reversible in the heading as well as in the table, or the count
        // becomes a thing the user has to distrust once they have used a filter.
        var page = await OpenWithFindingsAsync();

        await SearchForAsync(page, "Class07");
        Assert.Equal("1 of 40 findings", (await Heading(page).InnerTextAsync()).Trim());

        await SearchForAsync(page, "");
        Assert.Equal("40 Findings to review", (await Heading(page).InnerTextAsync()).Trim());
    }
}
