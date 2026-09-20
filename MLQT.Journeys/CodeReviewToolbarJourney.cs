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
