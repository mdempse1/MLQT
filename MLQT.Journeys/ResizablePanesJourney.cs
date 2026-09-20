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

    /// <summary>
    /// A resource name stays on one line and the pane scrolls sideways to reach the rest of it,
    /// rather than wrapping to whatever width the splitter is at.
    ///
    /// <para>Three things have to agree or the picture is unchanged: the label wraps because it is
    /// <c>white-space: normal</c>, the row clips because <c>.mud-treeview-item-content</c> is
    /// <c>overflow: hidden</c>, and the tree can never be wider than the pane because every level of
    /// it sizes to its parent — so there is nothing for the pane's <c>overflow-x</c> to scroll.</para>
    /// </summary>
    [Fact]
    public async Task ALongResourceNameKeepsToOneLineAndScrolls()
    {
        var page = await OpenAsync(2, withResources: true);
        await Assertions.Expect(page.GetByText(new Regex("Filter by type:")).First)
                        .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The fixture's own resource names are all short enough to fit, so the case being tested has
        // to be made rather than waited for. The name is the only thing borrowed; everything the
        // assertions look at is the page's own layout.
        await page.EvaluateAsync(@"() => {
            const label = document.querySelector('.mlqt-resource-tree-pane .mud-treeview-item-label');
            label.textContent = 'SaturatingInductor_LossyRepresentationOfTheThing.png (1)';
        }");
        await page.WaitForTimeoutAsync(300);

        var measured = await page.EvaluateAsync<string[]>(@"() => {
            const pane = document.querySelector('.mlqt-resource-tree-pane');
            const label = pane.querySelector('.mud-treeview-item-label');
            return [getComputedStyle(label).whiteSpace,
                    String(pane.scrollWidth - pane.clientWidth),
                    String(Math.round(label.getBoundingClientRect().height))];
        }");

        Assert.Equal("nowrap", measured[0]);
        Assert.True(int.Parse(measured[1]) > 0,
            $"the pane should have something to scroll to; overflow was {measured[1]}px");

        // One line. Wrapped, this row was two, which is what the report was about.
        Assert.True(int.Parse(measured[2]) < 40,
            $"the name should be on one line; the label was {measured[2]}px tall");
    }

    /// <summary>
    /// B250 — the page itself does not scroll. The Code Review tab is built from regions that
    /// declare their own scrolling, so a scrollbar on the document means something above them has
    /// asked for more room than there is, and the first thing it carries out of view is the
    /// current-class box that tells you what you are looking at.
    /// </summary>
    [Fact]
    public async Task ThePageItselfNeverScrolls()
    {
        var page = await OpenAsync(0, withFindings: true);
        await Assertions.Expect(page.GetByText(new Regex(@"\d+ Findings to review")).First)
                        .ToBeVisibleAsync(new() { Timeout = 20_000 });

        Assert.Equal(0, await PageOverflowAsync(page));
    }

    /// <summary>
    /// The same claim after the splitter has been dragged, which is the state the report came from.
    /// A height computed from the viewport rather than from the space actually left over is only
    /// wrong once something above it changes size, so the resting layout can be right while every
    /// layout the user produces is not.
    /// </summary>
    [Fact]
    public async Task ThePageStillDoesNotScrollAfterTheSplitterMoves()
    {
        var page = await OpenAsync(0, withFindings: true);
        await Assertions.Expect(page.GetByText(new Regex(@"\d+ Findings to review")).First)
                        .ToBeVisibleAsync(new() { Timeout = 20_000 });

        foreach (var dy in new[] { -200, 260 })
        {
            await DragAsync(page, Splitter(page, "mlqt-findings-pane"), dx: 0, dy: dy);
            Assert.Equal(0, await PageOverflowAsync(page));
        }
    }

    /// <summary>
    /// How far the document can scroll, and — when that is not zero — what is tall enough to
    /// explain it. The diagnostic is in the assertion because the number on its own says only that
    /// the page is too tall, and the answer is always which element made it so.
    /// </summary>
    private static async Task<int> PageOverflowAsync(IPage page)
    {
        var report = await page.EvaluateAsync<string>(@"() => {
            const doc = document.documentElement;
            const overflow = doc.scrollHeight - doc.clientHeight;
            if (overflow <= 0) return '0';

            const tall = [...document.querySelectorAll('body *')]
                .filter(e => e.getBoundingClientRect().height > doc.clientHeight)
                .slice(0, 6)
                .map(e => `${e.tagName.toLowerCase()}.${(e.className || '').toString().split(' ')[0]}`
                          + ` ${Math.round(e.getBoundingClientRect().height)}px`
                          + (e.getAttribute('style') ? ` [${e.getAttribute('style')}]` : ''));

            return `${overflow}|viewport ${doc.clientHeight}px|` + tall.join(' / ');
        }");

        if (report == "0")
            return 0;

        var parts = report.Split('|');
        Assert.Fail($"the page scrolls by {parts[0]}px ({parts[1]}), which takes the current-class "
                    + $"box out of view. Taller than the viewport: {parts[2]}");
        return 0;
    }

    /// <summary>
    /// The page tolerates something new above the viewer. This is the case the report came from and
    /// the one a resting-state assertion cannot see: the layout was right to within 12px, so it
    /// looked correct until a class with a syntax error put a 34px alert above the splitter.
    ///
    /// <para><b>The alert is inserted rather than provoked.</b> Rendering the real one needs a class
    /// that fails to parse, selected in the tree — three moving parts, none of them about layout,
    /// and the claim here is only that the page absorbs a taller header. What is inserted is the
    /// element the page itself renders, into the position the page renders it, so what is measured
    /// is this page's flex chain and not a contrivance.</para>
    /// </summary>
    [Fact]
    public async Task ThePageAbsorbsAnAlertAboveTheViewer()
    {
        var page = await OpenAsync(0, withFindings: true);
        await Assertions.Expect(page.GetByText(new Regex(@"\d+ Findings to review")).First)
                        .ToBeVisibleAsync(new() { Timeout = 20_000 });

        var grew = await page.EvaluateAsync<int>(@"() => {
            const panels = [...document.querySelectorAll('.mud-ex-split-panel:has(.mlqt-findings-pane)')];
            const splitter = panels[panels.length - 1].closest('.mud-ex-split-panel-grid') ?? panels[panels.length - 1];
            const before = splitter.getBoundingClientRect().height;

            const alert = document.createElement('div');
            alert.className = 'mud-alert mud-alert-filled-warning mt-1 mb-0 py-1 mlqt-test-alert';
            alert.style.height = '34px';
            alert.textContent = 'This model has 3 parser errors.';
            splitter.parentElement.insertBefore(alert, splitter);
            return Math.round(before);
        }");

        Assert.True(grew > 0, "the splitter should have had a height to begin with");
        await page.WaitForTimeoutAsync(400);

        Assert.Equal(0, await PageOverflowAsync(page));

        // And the alert really is taking room from the viewer rather than being ignored.
        var after = await page.EvaluateAsync<int>(@"() => {
            const panels = [...document.querySelectorAll('.mud-ex-split-panel:has(.mlqt-findings-pane)')];
            const splitter = panels[panels.length - 1].closest('.mud-ex-split-panel-grid') ?? panels[panels.length - 1];
            return Math.round(splitter.getBoundingClientRect().height);
        }");

        Assert.True(after < grew,
            $"the viewer should have given up the alert's height, not pushed the page down: {grew} -> {after}");
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
