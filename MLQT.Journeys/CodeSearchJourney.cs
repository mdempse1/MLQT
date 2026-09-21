using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.Interfaces;
using ModelicaParser.DataTypes;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// "Find in code" on the Code Review tab, in a real browser (B176, B237).
/// </summary>
/// <remarks>
/// <para><b>Why this cannot be a unit test.</b> Both halves already are: which lines match is
/// <c>CodeReviewCodeSearchTests</c>, and what a tinted line looks like is
/// <c>CodeViewerHtmlTests</c>. What neither can see is the wiring between them — the term travels
/// from the field through <c>CodeReview</c> to <c>CodeViewer</c>, which rebuilds its markup only
/// when it notices the parameter changed. A component that never re-rendered would pass both unit
/// tests and show the user nothing.</para>
///
/// <para><b>Why it was removed once, and what it needed (B237).</b> It could not be made reliable in
/// the shared host: searching needs a class open, and the class never rendered once other journeys
/// had run. The cause was not in this journey. Every journey builds its own <c>LibraryFixture</c>
/// under a fresh temp path and every one is called <c>Lib</c>, so they all produce the same class
/// ids; nothing took a library back out of the shared service, and <c>AddNode</c> keeps the copy
/// that arrived first. So <c>Lib.Modified</c> resolved to the first journey's node, whose file had
/// been deleted with its fixture. <c>TestHostFixture.ResetLibrariesAsync</c> is the fix, and it is
/// why this journey is back.</para>
///
/// <para>The class is opened by clicking an injected finding rather than through the tree:
/// registering a repository starts the analysis pipeline and leaves it registered, which is what
/// timed out <c>TabNavigationJourney</c> five minutes later the first time this was tried.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class CodeSearchJourney(TestHostFixture host) : IDisposable
{
    private readonly LibraryFixture _library = new();

    public void Dispose() => _library.Dispose();

    /// <summary>The word to search for, and the class that has it. Both come from the fixture.</summary>
    private const string ClassId = "Lib.Modified";
    private const string Term = "Real";

    /// <summary>The Code Review tab with <c>Lib.Modified</c> open in the viewer.</summary>
    private async Task<IPage> OpenAClassAsync()
    {
        var libraries = host.Services.GetRequiredService<ILibraryDataService>();
        await host.ResetLibrariesAsync();
        await libraries.AddLibraryFromDirectoryAsync(_library.LibraryPath);

        // One finding, whose row is how the class gets opened.
        var review = host.Services.GetRequiredService<ICodeReviewService>();
        review.ClearLogMessages();
        review.AddLogMessages([new LogMessage(ClassId, "Style warning", 1, "A finding to click")]);

        await host.WaitForIdleAsync();

        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator(".mud-tab").Nth(4).WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });

        await ShellReadiness.WaitUntilClickableAsync(page);
        await page.Locator(".mud-tab").Nth(0).ClickAsync();

        await page.Locator(".mlqt-findings-pane tbody tr").First
                  .WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await page.Locator(".mlqt-findings-pane tbody tr").First.ClickAsync();

        // The assertion that B237 was about: the class actually opens. Everything below depends on
        // it, so it is waited for explicitly rather than left to fail as a confusing search result.
        await Assertions.Expect(page.Locator(".code-line").First)
                        .ToBeVisibleAsync(new() { Timeout = 30_000 });
        return page;
    }

    private static ILocator SearchBox(IPage page) =>
        page.Locator(".mlqt-code-search input").First;

    private static async Task SearchForAsync(IPage page, string text)
    {
        // Immediate="true" on this field, unlike the findings search - so no blur is needed, but
        // the re-render still has to land before anything is read back.
        await SearchBox(page).FillAsync(text);
        await page.WaitForTimeoutAsync(600);
    }

    [Fact]
    public async Task TypingATermTintsTheMatchesInTheCode()
    {
        var page = await OpenAClassAsync();

        Assert.Equal(0, await page.Locator(".code-search-match").CountAsync());

        await SearchForAsync(page, Term);

        // The wiring: the term reached CodeViewer and it rebuilt its markup.
        Assert.True(await page.Locator(".code-search-match").CountAsync() > 0,
            $"no occurrence of '{Term}' was tinted; the term did not reach CodeViewer");
    }

    [Fact]
    public async Task TheMatchCountSaysHowManyLinesMatched()
    {
        var page = await OpenAClassAsync();

        await SearchForAsync(page, Term);

        var status = (await page.Locator(".mlqt-match-count").First.TextContentAsync() ?? "").Trim();
        Assert.False(string.IsNullOrEmpty(status), "the match count stayed empty after a search that matched");
    }

    /// <summary>
    /// Clearing the box puts the code back. The two halves are separate: the page finds the lines
    /// and the viewer tints them, so either could be left holding the last search.
    /// </summary>
    [Fact]
    public async Task ClearingTheTermTakesTheTintingAwayAgain()
    {
        var page = await OpenAClassAsync();
        await SearchForAsync(page, Term);
        Assert.True(await page.Locator(".code-search-match").CountAsync() > 0);

        await SearchForAsync(page, "");

        Assert.Equal(0, await page.Locator(".code-search-match").CountAsync());
        Assert.True(await page.Locator(".code-line").CountAsync() > 0, "the code went away with the search term");
    }

    /// <summary>
    /// A term that is in no line tints nothing and leaves the code alone — the case that would
    /// otherwise look identical to the wiring being broken.
    /// </summary>
    [Fact]
    public async Task ATermThatMatchesNothingLeavesTheCodeAsItWas()
    {
        var page = await OpenAClassAsync();
        var lines = await page.Locator(".code-line").CountAsync();

        await SearchForAsync(page, "notinthisclassanywhere");

        Assert.Equal(0, await page.Locator(".code-search-match").CountAsync());
        Assert.Equal(lines, await page.Locator(".code-line").CountAsync());
    }
}
