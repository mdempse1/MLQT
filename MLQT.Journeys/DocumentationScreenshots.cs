using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.Interfaces;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// Generates the screenshots in <c>Documentation/Images</c>, from the real UI.
/// </summary>
/// <remarks>
/// <para>Off unless <c>MLQT_DOC_SCREENSHOTS</c> names a directory, so an ordinary run does not write
/// to the repository. Run it with:</para>
/// <code>
/// MLQT_DOC_SCREENSHOTS=Documentation/Images dotnet test MLQT.Journeys --filter DocumentationScreenshots
/// </code>
///
/// <para><b>Why this is possible at all:</b> the journeys already drive MLQT's real components in a
/// real browser against a real library, through <c>MLQT.TestHost</c>. A screenshot is the same
/// navigation with one more call at the end, so the pictures come from the same apparatus that proves
/// the pages work — reproducible, free of personal data and local paths, and regenerable when the UI
/// changes rather than re-taken by hand.</para>
///
/// <para><b>The state has to be set up the way a user sets it up.</b> Loading a library through
/// <c>ILibraryDataService</c> alone leaves the left-hand browser blank while every other page shows
/// the library, and that is not a defect: in repository mode <c>MainLayout</c> renders one
/// <c>LibraryBrowser</c> per repository, so with no repository registered it renders none. The
/// screenshots therefore add a repository, which is what a user does, and which also gives the tree
/// its VCS status indicators.</para>
///
/// <para><b>What it cannot do.</b> The test host renders <c>MLQT.Shared</c> under Blazor Server in
/// Chromium, so these show the application's content exactly as it ships and the *window* not at all:
/// no native title bar, no taskbar, no menu. Anything about the window itself — the icon, the window
/// size, a native file dialog — needs a photograph of the real Photino host, because nothing can
/// drive that automatically (7a says why: a CDP-driven WebView2 is work that cannot be carried to
/// WebKitGTK).</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class DocumentationScreenshots(TestHostFixture host) : IDisposable
{
    /// <summary>
    /// The fixture library, built only if the generator is actually going to run.
    /// </summary>
    /// <remarks>
    /// Lazy because building it initialises a git repository and commits to it, and xUnit constructs
    /// a test class once per test - so an eager field made every ordinary journey run pay for a
    /// repository that was then thrown away unused.
    /// </remarks>
    private LibraryFixture? _library;

    public void Dispose() => _library?.Dispose();

    /// <summary>Where to write them, or null when nobody asked.</summary>
    private static string? OutputDirectory =>
        Environment.GetEnvironmentVariable("MLQT_DOC_SCREENSHOTS") is { Length: > 0 } dir ? dir : null;

    /// <summary>
    /// The window the documentation is shot at.
    /// </summary>
    /// <remarks>
    /// The size MLQT opens at (<c>WindowGeometry.PreferredWidth/Height</c>) less the window chrome,
    /// so a reader sees the layout the application actually gives them rather than one stretched to
    /// whatever the runner's display happens to be. Fixed, so two regenerations are comparable.
    /// </remarks>
    private const int Width = 1200;
    private const int Height = 860;

    [Fact]
    public async Task EveryTab()
    {
        if (OutputDirectory is null)
            return;   // an ordinary run: the journeys are the test, these are a tool

        Directory.CreateDirectory(OutputDirectory);
        _library = new LibraryFixture();

        var page = await host.NewPageAsync();
        await page.SetViewportSizeAsync(Width, Height);
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        await OpenTheLibraryAsync(page);
        await SelectAClassAsync(page);

        foreach (var (index, name) in new[]
                 {
                     (0, "code-review"),
                     (1, "dependencies"),
                     (2, "external-resources"),
                     (3, "metrics"),
                     (4, "settings"),
                 })
        {
            await ShellReadiness.WaitUntilClickableAsync(page);
            await page.Locator(".mud-tab").Nth(index).ClickAsync();
            await SettleAsync(page);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(OutputDirectory, $"tab-{name}.png") });
        }
    }

    /// <summary>
    /// Adds the fixture repository and waits for the application to finish reacting to it.
    /// </summary>
    /// <remarks>
    /// Through <c>IRepositoryService</c> rather than by driving the Add Repository dialog: the dialog
    /// opens a native folder picker, which is the one thing the test host fakes. Everything after the
    /// picker — discovery, loading, the tree, the analysis pipeline — is the real path.
    /// </remarks>
    private async Task OpenTheLibraryAsync(IPage page)
    {
        EnableSomeRules();

        var repositories = host.Services.GetRequiredService<IRepositoryService>();

        if (repositories.GetActiveProject() is null)
            repositories.CreateProject("Documentation");

        var added = await repositories.AddRepositoryAsync(_library!.RepositoryPath, name: "MyLibrary", startMonitoring: false);
        Assert.True(added.Success, added.ErrorMessage);

        // Adding a repository *discovers* its libraries; loading them is a second step, because the
        // Add Repository dialog lets the user choose which of them to open. Null means all of them,
        // which is what a user with one library in a repository chooses.
        await repositories.LoadLibrariesAsync(added.Repository!.Id);

        // And check what was loaded. Adding a repository through the dialog raises the progress
        // dialog and runs this; adding it through the service does not, so the page would otherwise
        // show an empty findings list - the one state a reader does not need a picture of. The
        // findings are the application's own: the worker raises them and MainLayout collects them,
        // exactly as it does when a user adds a repository.
        var libraries = host.Services.GetRequiredService<ILibraryDataService>();
        var graph = libraries.CombinedGraph;
        await host.Services.GetRequiredService<IStyleCheckingService>()
                  .CheckModelsAsync(graph.ModelNodes.Select(m => m.Id).ToList(), graph);

        // The dependency edges the Dependencies tab draws. Idempotent, and the one supported way to
        // ask for them.
        await libraries.EnsureDependenciesAnalyzedAsync();

        // And then the resources - in that order, and not the other way round. The resource *edges*
        // are built by the dependency pass, while the parse trees are still in hand; this service
        // indexes what that pass left in the graph. Run first it indexes an empty graph, and the
        // External Resources page is then correct and empty, which is a picture of nothing.
        await host.Services.GetRequiredService<IExternalResourceService>()
                  .AnalyzeResourcesAsync(graph);

        await host.WaitForIdleAsync();
        await SettleAsync(page);
    }

    /// <summary>
    /// Writes the repository's style settings, so the application finds something to report.
    /// </summary>
    /// <remarks>
    /// <para><b>MLQT ships with every style rule off</b>, so a library in a repository nobody has
    /// configured reports nothing - and a screenshot of the Code Review page would be a screenshot of
    /// an empty list, which is the one state a reader does not need a picture of.</para>
    ///
    /// <para>Written as <c>.mlqt/settings.json</c> before the repository is added, which is where
    /// <c>RepositoryService</c> reads it from. The findings in the picture are then the application's
    /// own, produced by its own pipeline from its own configuration - not a list this class handed
    /// it. Three rules, chosen because the fixture library breaks all three and because they are the
    /// ones a new user turns on first.</para>
    /// </remarks>
    private void EnableSomeRules()
    {
        var mlqt = Path.Combine(_library!.RepositoryPath, ".mlqt");
        Directory.CreateDirectory(mlqt);

        File.WriteAllText(Path.Combine(mlqt, "settings.json"), """
            {
              "RuleSeverities": {
                "MLQT.Doc.ClassDescription": "Warning",
                "MLQT.Doc.ParameterDescription": "Error",
                "MLQT.Naming.Convention": "Warning"
              }
            }
            """);
    }

    /// <summary>
    /// Opens the library in the tree and selects a class, so the pages have a subject.
    /// </summary>
    /// <remarks>
    /// Driven through the tree rather than by setting state, because selecting a class is what makes
    /// the code viewer, the findings list and the dependency graph show anything - and doing it the
    /// way a user does is what keeps the picture honest about the number of clicks involved.
    /// </remarks>
    private static async Task SelectAClassAsync(IPage page, string className = "Modified")
    {
        // Expanding and selecting are different gestures, and the tree is lazy: clicking the node's
        // label selects it and leaves it closed, so the children a later step wants are not in the
        // DOM at all. The arrow is what loads them.
        await ExpandAsync(page, "Lib");

        var target = page.Locator(".mud-treeview-item-content",
                                  new PageLocatorOptions { HasTextString = className }).First;

        Assert.True(await target.CountAsync() > 0, $"{className} is not in the tree");
        await target.ClickAsync();
        await page.WaitForTimeoutAsync(1500);
    }

    /// <summary>Opens one tree node by its arrow, and waits for its children to arrive.</summary>
    private static async Task ExpandAsync(IPage page, string nodeText)
    {
        var node = page.Locator(".mud-treeview-item-content",
                                new PageLocatorOptions { HasTextString = nodeText }).First;

        await node.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        var arrow = node.Locator("xpath=preceding-sibling::*[contains(@class,'mud-treeview-item-arrow')]")
                        .Or(node.Locator(".mud-treeview-item-arrow button"))
                        .Or(node.Locator("button").First)
                        .First;

        if (await arrow.CountAsync() > 0)
            await arrow.ClickAsync();
        else
            await node.DblClickAsync();

        // Server-side children: the node's own click returns before they are fetched.
        await page.WaitForTimeoutAsync(1500);
    }

    /// <summary>
    /// Waits for the page to stop moving, and clears what is only passing through.
    /// </summary>
    /// <remarks>
    /// Two things spoil a screenshot taken the moment a click lands. The tooltip the click raised
    /// sits over the toolbar, which reads as part of the interface rather than as a hover; and the
    /// analysis pipeline's snackbars stack up the right-hand side for a few seconds after a library
    /// opens. Both are dismissed rather than waited out, because waiting for a snackbar to expire is
    /// a race that is lost on a slow runner.
    /// </remarks>
    private static async Task SettleAsync(IPage page)
    {
        // The pages do JS interop on first render; a shot taken mid-render shows a half-drawn panel,
        // which is worse than none because it looks like a defect.
        await page.WaitForTimeoutAsync(1500);

        // Off the toolbar, so no tooltip is showing.
        await page.Mouse.MoveAsync(Width / 2, Height - 4);

        // Until there are none left, rather than once: the analysis pipeline raises them in sequence,
        // so a single sweep followed by a wait just let the next one appear in the gap. Bounded,
        // because a page that produced them for ever is a different problem from an untidy picture.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var open = await page.Locator(".mud-snackbar .mud-snackbar-close-button").AllAsync();
            if (open.Count == 0)
                break;

            foreach (var close in open)
            {
                try
                {
                    await close.ClickAsync(new LocatorClickOptions { Timeout = 1000 });
                }
                catch (Exception e) when (e is PlaywrightException or TimeoutException)
                {
                    // It closed itself between being found and being clicked, which is ordinary -
                    // and which of the two exceptions that surfaces as depends on whether it went
                    // before or during the click. Catching only one made the generator fail about
                    // one run in three, on a snackbar that was already gone.
                }
            }

            await page.WaitForTimeoutAsync(500);
        }
    }
}
