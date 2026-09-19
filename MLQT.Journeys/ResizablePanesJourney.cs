using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.Interfaces;
using ModelicaParser.DataTypes;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// The two panes a user could not resize, and the findings they could not reach (B186, B173).
/// </summary>
/// <remarks>
/// <para>Both pages divided their space with fixed numbers — Code Review with
/// <c>height: calc(100vh - 405px)</c> over a <c>221px</c> findings pane, External Resources with a
/// 4/8 grid — so a long resource path was cut off with no way to widen its column, and the findings
/// table showed the same five rows whatever the screen. <b>Paging was the other half of that</b>: a
/// pager shows a fixed number of rows however much room the table is given, so even resizing the
/// pane would only have moved which rows were cut off.</para>
///
/// <para>These assert what a user can do rather than what the markup says, because that is the claim
/// — the splitter exists and can be dragged, and every finding is reachable without one.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class ResizablePanesJourney(TestHostFixture host) : IDisposable
{
    private readonly LibraryFixture _library = new();

    public void Dispose() => _library.Dispose();

    private async Task<IPage> OpenAsync(int tabIndex, bool withResources = false, bool withFindings = false)
    {
        var libraries = host.Services.GetRequiredService<ILibraryDataService>();
        await libraries.AddLibraryFromDirectoryAsync(_library.LibraryPath);

        if (withFindings)
        {
            // Findings put straight into the service the page reads, rather than produced by the
            // check. That is deliberate: these tests are about a table scrolling, and getting the
            // application to raise findings of its own needs a repository, a settings file turning
            // rules on, and a check run — three steps, minutes of pipeline, and none of it about
            // scrolling. What the table must not do is depend on where its rows came from.
            var review = host.Services.GetRequiredService<ICodeReviewService>();
            review.ClearLogMessages();
            review.AddLogMessages(Enumerable.Range(1, 40).Select(i => new LogMessage(
                $"Lib.Class{i:D2}", "Style warning", i, $"A finding about Class{i:D2}")));
        }

        if (withResources)
        {
            // The External Resources page draws its two panes only when it has something to put in
            // them, so there is nothing to resize until the resources are indexed. Dependencies
            // first: that pass builds the resource edges while the parse trees are still in hand,
            // and this service indexes what it left behind.
            await libraries.EnsureDependenciesAnalyzedAsync();
            await host.Services.GetRequiredService<IExternalResourceService>()
                      .AnalyzeResourcesAsync(libraries.CombinedGraph);
        }

        await host.WaitForIdleAsync();

        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        // The host is shared, so this page may be starting up behind another journey's analysis run.
        await page.Locator(".mud-tab").Nth(4).WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });

        await ShellReadiness.WaitUntilClickableAsync(page);
        await page.Locator(".mud-tab").Nth(tabIndex).ClickAsync();
        return page;
    }

    /// <summary>
    /// The splitter of the innermost panel containing <paramref name="pane"/>.
    ///
    /// <para><b>Not <c>.First</c>, and not by a class on the panel.</b> MainLayout splits the tree
    /// from the tabs with this same control, so every page carries at least two — nested, since the
    /// page's own panel is inside MainLayout's. A page-wide locator picks the outer one, which drags
    /// perfectly well and resizes something else entirely; the first version of this journey did
    /// that and reported the splitter broken when the test was looking at the wrong element. Putting
    /// a class on <c>MudExSplitPanel</c> does not help either: it lands on a wrapper rather than on
    /// the element carrying <c>mud-ex-split-panel</c>. Anchoring on a pane we do own, and taking the
    /// innermost match, says what is meant.</para>
    /// </summary>
    private static ILocator Splitter(IPage page, string pane) =>
        page.Locator($".mud-ex-split-panel:has(.{pane}) > .mud-ex-splitter").Last;

    /// <summary>Drags <paramref name="splitter"/> by (dx, dy) and lets the layout settle.</summary>
    private static async Task DragAsync(IPage page, ILocator splitter, int dx, int dy)
    {
        var box = await splitter.BoundingBoxAsync();
        Assert.NotNull(box);

        await page.Mouse.MoveAsync(box!.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width / 2 + dx, box.Y + box.Height / 2 + dy,
            new MouseMoveOptions { Steps = 10 });
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(500);
    }

    [Fact]
    public async Task TheCodeReviewPaneDividerCanBeDragged()
    {
        var page = await OpenAsync(0);
        await Assertions.Expect(page.GetByText(new Regex("Findings")).First)
                        .ToBeVisibleAsync(new() { Timeout = 20_000 });

        var splitter = Splitter(page, "mlqt-findings-pane");
        await Assertions.Expect(splitter).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // That the pane moved is the claim; a particular height would only assert the drag
        // arithmetic back at itself.
        var findings = page.Locator(".mlqt-findings-pane").First;
        var before = await findings.BoundingBoxAsync();

        await DragAsync(page, splitter, dx: 0, dy: -120);

        var after = await findings.BoundingBoxAsync();
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.True(after!.Height > before!.Height + 20,
            $"dragging the splitter up should have made the findings pane taller: {before.Height} -> {after.Height}");
    }

    [Fact]
    public async Task EveryFindingIsReachableWithoutAPager()
    {
        var page = await OpenAsync(0, withFindings: true);

        var heading = page.GetByText(new Regex(@"\d+ Findings to review")).First;
        await Assertions.Expect(heading).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // A pager is what made a finding unreachable, by showing a fixed number of rows however much
        // room the table had.
        Assert.Equal(0, await page.Locator(".mud-table-pagination").CountAsync());

        var reported = int.Parse(Regex.Match(await heading.InnerTextAsync(), @"\d+").Value);
        Assert.True(reported > 1, "this needs more findings than fit on screen or it proves nothing");

        // Reaching the last one is the claim, and it cannot be made by counting rows: the table is
        // virtualised, so the DOM holds the rows on screen and a handful either side, never all of
        // them. Counting was this test's first mistake — it compared a rendered window against a
        // total and happened to agree only while every row fitted.
        await page.Locator(".mud-table-container").First
                  .EvaluateAsync("e => e.scrollTop = e.scrollHeight");
        await page.WaitForTimeoutAsync(600);

        await Assertions.Expect(page.GetByText($"A finding about Class{reported:D2}").First)
                        .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// One scrollbar, on the rows. <c>MudExSplitPanelItem</c> gives its content
    /// <c>overflow: auto</c>, so when the toolbar and the table together outgrew the pane the pane
    /// scrolled as well — carrying the heading, the filters and the search box out of view along
    /// with the rows, and leaving the user two scrollbars of which the obvious one moved the wrong
    /// thing.
    /// </summary>
    [Fact]
    public async Task OnlyTheRowsScroll_NotTheWholeFindingsPane()
    {
        var page = await OpenAsync(0, withFindings: true);
        await Assertions.Expect(page.GetByText(new Regex(@"\d+ Findings to review")).First)
                        .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Squeeze the pane until the rows cannot possibly fit, which is the state the defect needed.
        await DragAsync(page, Splitter(page, "mlqt-findings-pane"), dx: 0, dy: 260);

        var measured = await page.EvaluateAsync<int[]>(@"() => {
            const pane = document.querySelector('.mlqt-findings-pane');
            const rows = pane.querySelector('.mud-table-container');
            return [pane.scrollHeight - pane.clientHeight, rows.scrollHeight - rows.clientHeight];
        }");

        Assert.True(measured[1] > 0,
            $"the rows should have more to scroll than fits: overflow was {measured[1]}px");
        Assert.Equal(0, measured[0]);
    }

    [Fact]
    public async Task TheResourceTreeColumnCanBeWidened()
    {
        var page = await OpenAsync(2, withResources: true);
        await Assertions.Expect(page.GetByText(new Regex("Filter by type:")).First)
                        .ToBeVisibleAsync(new() { Timeout = 20_000 });

        var splitter = Splitter(page, "mlqt-resource-tree-pane");
        await Assertions.Expect(splitter).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var tree = page.Locator(".mlqt-resource-tree-pane").First;
        var before = await tree.BoundingBoxAsync();

        await DragAsync(page, splitter, dx: 150, dy: 0);

        var after = await tree.BoundingBoxAsync();
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.True(after!.Width > before!.Width + 20,
            $"dragging the splitter right should have widened the tree: {before.Width} -> {after.Width}");
    }
}
