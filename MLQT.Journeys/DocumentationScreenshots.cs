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
/// $env:MLQT_DOC_SCREENSHOTS = "Documentation/Images"
/// MLQT.Journeys/bin/Release/net10.0/MLQT.Journeys.exe --filter DocumentationScreenshots
/// </code>
///
/// <para><b>Run it on its own, by that filter.</b> The journeys share one host, so a full-suite run
/// reaches this class with libraries another journey loaded and settings another journey wrote —
/// which is fine for a test and wrong for a picture that is supposed to show a user's own project.
/// </para>
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
/// <para><b>The names are the documentation's names.</b> Each image is written as the file the
/// markdown already links to, so regenerating replaces the picture and touches no text. The caption
/// in the markdown is the specification for the shot — where the two disagree, one of them is wrong
/// and it is usually the picture.</para>
///
/// <para><b>What it cannot do.</b> The test host renders <c>MLQT.Shared</c> under Blazor Server in
/// Chromium, so these show the application's content exactly as it ships and the *window* not at all:
/// no native title bar, no taskbar, no menu. Anything about the window itself — the icon, the window
/// size, a native file dialog — needs a photograph of the real Photino host, because nothing can
/// drive that automatically (7a says why: a CDP-driven WebView2 is work that cannot be carried to
/// WebKitGTK). Nor can it produce the SVN pictures (no server to talk to) or the Dymola check
/// progress (no Dymola). Those stay photographs; <c>Design/roadmap.md</c> B152 lists them.</para>
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

    /// <summary>The tabs, by the position they are addressed at. They carry icons and no text.</summary>
    private const int CodeTab = 0;
    private const int DependenciesTab = 1;
    private const int ResourcesTab = 2;
    private const int MetricsTab = 3;
    private const int SettingsTab = 4;

    /// <summary>
    /// Where the fixture repository goes when it is going to be photographed.
    /// </summary>
    /// <remarks>
    /// MLQT prints the full path of what it is describing - in the resource detail panel, the
    /// repository list and the Edit Repository dialog - so the ordinary
    /// <c>%TEMP%/mlqt-journey-&lt;guid&gt;</c> would put a developer's user name and a GUID into the
    /// manual. The shared documents folder is somewhere a reader recognises and anyone can write to;
    /// where there is no such folder (Linux) the temp path is neutral enough.
    /// </remarks>
    private static string RepositoryPathForPictures()
    {
        var shared = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
        var root = string.IsNullOrEmpty(shared) ? Path.GetTempPath() : shared;
        return Path.Combine(root, "MLQT", "MyLibrary");
    }

    [Fact]
    public async Task TheDocumentationImages()
    {
        if (OutputDirectory is null)
            return;   // an ordinary run: the journeys are the test, these are a tool

        Directory.CreateDirectory(OutputDirectory);
        _library = new LibraryFixture(RepositoryPathForPictures());

        var page = await host.NewPageAsync();
        await page.SetViewportSizeAsync(Width, Height);
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        await FirstLaunchAsync(page);

        await OpenTheLibraryAsync(page);
        await SelectAClassAsync(page);

        await CodeReviewAsync(page);
        await TheLibraryBrowserAsync(page);
        await DependenciesAsync(page);
        await ExternalResourcesAsync(page);
        await SettingsAsync(page);
        await RepositorySettingsAsync(page);
        await GitOperationsAsync(page);
        await AddingARepositoryAsync(page);
    }

    // ---------------------------------------------------------------- the scenes

    /// <summary>Before a repository is added: the layout a first-time user meets.</summary>
    private async Task FirstLaunchAsync(IPage page)
    {
        await SettleAsync(page);

        await ShotAsync(page, "getting-started-1");

        // The left-hand toolbar: Add Repository, the library/repository toggle, and Refresh. Taken as
        // an element rather than cropped from the page, so it stays right when the layout moves.
        await ShotAroundAsync(page, LeftToolbar(page), "getting-started-2");
    }

    /// <summary>The Code tab: the whole page, the view-mode buttons, and a diff.</summary>
    private async Task CodeReviewAsync(IPage page)
    {
        await OpenTabAsync(page, CodeTab);

        await ShotAsync(page, "code-review-1");

        // The four view-mode buttons. The fixture leaves Modified uncommitted, so the three that need
        // a HEAD to compare against are enabled - which is the state the caption describes.
        var viewModes = Right(page).Locator(".mud-button-group-root").First;
        await ShotAroundAsync(page, viewModes, "code-review-2");

        // Side-by-side diff: the second button of that group.
        await ShellReadiness.WaitUntilClickableAsync(page);
        await viewModes.Locator("button").Nth(1).ClickAsync();
        await SettleAsync(page);
        await ShotAsync(page, "code-review-3");

        // Back to the plain view for everything after this.
        await ShellReadiness.WaitUntilClickableAsync(page);
        await viewModes.Locator("button").First.ClickAsync();
        await SettleAsync(page);

        // The formatting-exclusion toggle, in the second group beside the annotations button.
        await ShotAroundAsync(page, Right(page).Locator(".mud-button-group-root").Nth(1), "settings-reference-5");

        // code-review-5, the Finding Details dialog, is not taken here and cannot be: it opens only
        // for a finding that carries Details, and a style rule does not produce one - the dialog's
        // subject is the check log from Dymola or OpenModelica. It stays a photograph, with
        // code-review-4 and the SVN set.
    }

    /// <summary>The left panel: the tree, its status chips, and the repository header.</summary>
    private async Task TheLibraryBrowserAsync(IPage page)
    {
        await OpenTabAsync(page, CodeTab);

        // The repository header row - branch name and the branch actions.
        var branchRow = RowContaining(page, "Current branch:");
        await ShotAroundAsync(page, branchRow, "library-browser-3");

        // The commit row beneath it: the short commit id, the info tooltip, and update/commit/revert.
        await ShotAroundAsync(page, RowContaining(page, "Commit:"), "library-browser-5");

        // The More actions popover: rebase, merge, push, create pull request.
        await ShellReadiness.WaitUntilClickableAsync(page);
        var more = branchRow.Locator("button").Last;
        await more.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        // The one with the buttons in it. A MudTooltip is a popover too, and the tooltip on the
        // button that was just clicked matches first - the shot came out as the words "More
        // actions ..." on their own.
        var popover = page.Locator(".mud-popover-open").Filter(new LocatorFilterOptions
        {
            Has = page.Locator("button"),
        }).First;

        if (await popover.CountAsync() > 0)
            await ShotOfAsync(popover, "library-browser-4");

        // Closed by the button that opened it. This popover is not a menu: LibraryBrowser holds it
        // open with a bool that only that button toggles, so neither Escape nor a click elsewhere
        // shuts it - it sat over the tab strip and the next click waited ten seconds for it to go.
        await more.ClickAsync();
        await page.Mouse.MoveAsync(Width / 2, Height - 4);
        await page.WaitForTimeoutAsync(800);

        // The tree itself, with the M chip on the class the fixture leaves uncommitted and the dot
        // that carries it up to the package.
        await ShotOfAsync(page.Locator(".mud-treeview").First, "library-browser-2");

        // The whole left panel, which is what a user sees once a repository is in: the repository as
        // an expansion header, its VCS row, and the packages below it.
        await ShotOfAsync(page.Locator(".mud-expand-panel").First, "getting-started-7");

        // And the other view of the same thing. The middle button of the left toolbar swaps the tree
        // between repository view - libraries grouped under the repository that holds them - and
        // library view, a flat list of packages with no repository headers at all.
        await ShellReadiness.WaitUntilClickableAsync(page);
        await LeftToolbar(page).Locator("button").Nth(1).ClickAsync();
        await SettleAsync(page);

        await ShotOfAsync(page.Locator(".mud-treeview").First, "library-browser-1");
        await ShotAsync(page, "getting-started-9");

        // Back to repository view, which is what every later picture assumes. The tree is rebuilt
        // closed by the switch, so it has to be opened again.
        await ShellReadiness.WaitUntilClickableAsync(page);
        await LeftToolbar(page).Locator("button").Nth(1).ClickAsync();
        await SettleAsync(page);
        await SelectAClassAsync(page);
    }

    /// <summary>The Dependencies tab: the tree's checkboxes, and the network they produce.</summary>
    private async Task DependenciesAsync(IPage page)
    {
        await OpenTabAsync(page, DependenciesTab);

        // Impact analysis is over a *selection*, and the tab is what puts the checkboxes in the tree.
        // Pin is the connector everything else in the fixture uses, so checking it gives the graph
        // every component and the example above them - which is what an impact analysis is for.
        await ExpandAsync(page, "Interfaces");
        await CheckInTheTreeAsync(page, "Pin");
        await SettleAsync(page);

        await ShotOfAsync(page.Locator(".mud-treeview").First, "dependency-analysis-2");
        await ShotAsync(page, "dependency-analysis-1");

        // The impacted-models table below the graph, with a row selected.
        var table = page.Locator(".mud-table").Last;
        if (await table.CountAsync() > 0)
        {
            await ShellReadiness.WaitUntilClickableAsync(page);
            var row = table.Locator("tbody tr").First;
            if (await row.CountAsync() > 0)
                await row.ClickAsync();

            await page.WaitForTimeoutAsync(800);
            await ShotOfAsync(table, "dependency-analysis-4");
        }

        // One node picked out of the network: its neighbours stay lit and the rest dim, and the
        // panel names it. Clicked through the graph itself, which is the gesture the page is about.
        var node = page.Locator("canvas").First;
        if (await node.CountAsync() > 0 && await node.BoundingBoxAsync() is { } canvas)
        {
            // Cytoscape draws to a canvas, so there is no element to address - only a position. The
            // layout is deterministic for a graph this small, and the shot is checked by eye when it
            // is regenerated; a wrong position costs a picture of an unhighlighted graph, not a
            // wrong one.
            //
            // Hovered, not clicked, and photographed without settling first: the highlight and the
            // panel naming the node are what the pointer being *there* produces, and SettleAsync's
            // first act is to move the pointer away.
            await page.Mouse.MoveAsync(canvas.X + canvas.Width * 0.62f, canvas.Y + canvas.Height * 0.55f,
                                       new MouseMoveOptions { Steps = 10 });
            await page.WaitForTimeoutAsync(1500);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = PathFor("dependency-analysis-3") });
        }

        await UncheckInTheTreeAsync(page, "Pin");
    }

    /// <summary>The External Resources tab: the tree, a missing file, and an annotated directory.</summary>
    private async Task ExternalResourcesAsync(IPage page)
    {
        await OpenTabAsync(page, ResourcesTab);

        // Every type, rather than the default set: the fixture's image is a resource too, and a
        // filter chip that hides it makes the picture disagree with the list beside it.
        await ShellReadiness.WaitUntilClickableAsync(page);
        var all = page.GetByText("All", new PageGetByTextOptions { Exact = true }).First;
        if (await all.CountAsync() > 0)
            await all.ClickAsync();

        await OpenEveryResourceFolderAsync(page);
        await SettleAsync(page);

        // A file with referencing models: the data file the profile model reads.
        await ClickResourceAsync(page, "profile.txt");
        await ShotAsync(page, "external-resources-1");

        // The one the library does not ship, which is what the warning alert is for.
        if (await ClickResourceAsync(page, "calibration.txt"))
            await ShotAroundAsync(page, page.Locator(".mud-paper").Last, "external-resources-2", maxHeight: 260);

        // An annotated directory, which carries a chip saying what the annotation made it.
        if (await ClickResourceAsync(page, "Include"))
            await ShotAroundAsync(page, page.Locator(".mud-paper").Last, "external-resources-3", maxHeight: 260);
    }

    /// <summary>The Settings tab and its panels.</summary>
    private async Task SettingsAsync(IPage page)
    {
        await OpenTabAsync(page, SettingsTab);
        await ShotAsync(page, "settings-reference-1");

        await OpenSettingsPanelAsync(page, "External Tools");
        await ShotAsync(page, "external-tools-1");

        await OpenSettingsPanelAsync(page, "Manage Repositories");
        await ShotAsync(page, "getting-started-3");
    }

    /// <summary>
    /// The Add Repository dialog, in both of its two ways of naming a repository.
    /// </summary>
    /// <remarks>
    /// Left until last because it is the one dialog that can change what every other picture shows:
    /// it is cancelled rather than confirmed, but a mistake here would add a second repository to the
    /// tree. The folder picker behind the Browse button is the host's, and the test host fakes it -
    /// so the path is typed, which is what the dialog supports anyway.
    /// </remarks>
    private async Task AddingARepositoryAsync(IPage page)
    {
        await OpenTabAsync(page, CodeTab);

        await ShellReadiness.WaitUntilClickableAsync(page);
        await LeftToolbar(page).Locator("button").First.ClickAsync();
        await page.WaitForTimeoutAsync(1500);

        var dialog = page.Locator(".mud-dialog").Last;
        Assert.True(await dialog.CountAsync() > 0, "the Add Repository dialog did not open");

        // A path it can recognise: the fixture's own working copy, which is a git repository, so the
        // dialog shows what it detected rather than an empty form.
        await dialog.GetByLabel("Repository Path").FillAsync(_library!.RepositoryPath);
        await page.WaitForTimeoutAsync(2500);
        await ShotOfAsync(dialog, "getting-started-5");

        await dialog.GetByRole(AriaRole.Tab, new() { Name = "Download Remote Repository" }).ClickAsync();
        await page.WaitForTimeoutAsync(800);

        await dialog.GetByLabel("Remote Repository Address").FillAsync("https://github.com/modelica/ModelicaStandardLibrary.git");
        await dialog.GetByLabel("Checkout Directory").FillAsync(Path.Combine(
            Path.GetDirectoryName(_library.RepositoryPath)!, "ModelicaStandardLibrary"));
        await page.WaitForTimeoutAsync(2500);
        await ShotOfAsync(dialog, "getting-started-6");

        await CloseTheDialogAsync(page);
    }

    /// <summary>
    /// The VCS dialogs, each opened from the button a user opens it from.
    /// </summary>
    /// <remarks>
    /// <para>The working copy has one uncommitted file and a second branch nobody is on, which is
    /// what makes these dialogs show anything: the commit and revert dialogs list the change, and the
    /// switch-branch dialog has somewhere to switch to.</para>
    ///
    /// <para>The buttons carry an icon and a tooltip and no accessible name, so they are addressed by
    /// their position in the row they live in - which is why each one says which row and which
    /// position, and why a changed row order shows up as a picture of the wrong dialog rather than as
    /// an error. Each shot asserts the dialog opened, so that failure is loud.</para>
    ///
    /// <para><b>git-operations-6 and -10 are not here</b>: the ready-to-merge phase of the merge
    /// dialog needs a clean working copy, which would cost the uncommitted change every other picture
    /// uses, and the revision diff needs two revisions of a file the fixture only has one of.</para>
    /// </remarks>
    private async Task GitOperationsAsync(IPage page)
    {
        await OpenTabAsync(page, CodeTab);

        var commitRow = RowContaining(page, "Commit:");
        var branchRow = RowContaining(page, "Current branch:");

        // Update, Commit, Revert - in that order, after the revision text and the info icon.
        await DialogShotAsync(page, commitRow.Locator("button").Nth(1), "git-operations-1");
        await DialogShotAsync(page, commitRow.Locator("button").Nth(2), "git-operations-2");

        // Switch branch, create branch, and then the popover with the rest.
        await DialogShotAsync(page, branchRow.Locator("button").First, "git-operations-3");
        await DialogShotAsync(page, branchRow.Locator("button").Nth(1), "git-operations-4");

        await ShellReadiness.WaitUntilClickableAsync(page);
        var more = branchRow.Locator("button").Last;
        var actions = await OpenActionsPopoverAsync(page, more);

        // Rebase, merge, push, create pull request. Merge with a dirty working copy is the phase that
        // offers to commit or revert first, which is the one a user meets.
        // Not waiting for the shell here, and that is the point: the popover these buttons live in
        // is deliberately open, and the readiness wait is a wait for every popover to be gone.
        await DialogShotAsync(page, actions.Locator("button").Nth(1), "git-operations-5", waitForShell: false);

        // Opened again, because it closed with the dialog it raised.
        actions = await OpenActionsPopoverAsync(page, more);
        await DialogShotAsync(page, actions.Locator("button").Nth(3), "git-operations-7", waitForShell: false);

        // The history dialog, from the button on the repository's own header.
        var history = RowContaining(page, "MyLibrary").Locator("button").Last;
        await ShellReadiness.WaitUntilClickableAsync(page);
        await history.ClickAsync();
        await page.WaitForTimeoutAsync(2500);

        var dialog = page.Locator(".mud-dialog").First;
        if (await dialog.CountAsync() > 0)
        {
            await ShotOfAsync(dialog, "git-operations-8");

            // A commit, clicked: the popover of files it changed. By its message rather than by
            // position, and specifically the fixture's second commit, because that one *modifies* a
            // file - the first adds every file there is, and a list of additions has no diff behind
            // it.
            var row = dialog.Locator("tbody tr").Filter(new LocatorFilterOptions
            {
                HasTextString = "Report the rate of change",
            }).First;
            if (await row.CountAsync() > 0)
            {
                await row.ClickAsync();
                await page.WaitForTimeoutAsync(1500);
                await ShotAsync(page, "git-operations-9");

                // The diff is taken from the *first* commit instead. There is no Diff button,
                // whatever git-operations.md says - the file in the popover is the control - and
                // what it opens is a comparison against the working copy, so asking it about the
                // newest revision of a file nobody has touched since produces a dialog that says
                // "File is identical", correctly and uselessly.
                await dialog.Locator("tbody tr").Filter(new LocatorFilterOptions
                {
                    HasTextString = "before the journey edits it",
                }).First.ClickAsync();
                await page.WaitForTimeoutAsync(1500);

                var diff = page.Locator(".mud-list-item").Filter(new LocatorFilterOptions
                {
                    HasTextString = "Documented.mo",
                }).First;

                if (await diff.CountAsync() > 0)
                {
                    await diff.ClickAsync();
                    await page.WaitForTimeoutAsync(2500);

                    var diffDialog = page.Locator(".mud-dialog").Last;
                    if (await diffDialog.CountAsync() > 0)
                    {
                        await ShotOfAsync(diffDialog, "git-operations-10");
                        await CloseTheDialogAsync(page);
                    }
                }
            }

            await CloseTheDialogAsync(page);

            // The same mouseleave the dialog swallowed - DialogShotAsync does this for the dialogs
            // it opens itself, and this one is opened by hand.
            await history.HoverAsync();
            await page.Mouse.MoveAsync(0, 0, new MouseMoveOptions { Steps = 8 });
        }
    }

    /// <summary>
    /// Opens the "More actions" popover and returns it, having waited for its buttons.
    /// </summary>
    /// <remarks>
    /// The wait is for the popover that *has buttons in it*, because the tooltip on the button that
    /// opens it is a popover too. And it is a wait rather than a pause: the first version paused
    /// 800ms and then addressed the popover's second button, which on a slow run was a 30-second
    /// timeout on a locator that named the right thing.
    /// </remarks>
    private static async Task<ILocator> OpenActionsPopoverAsync(IPage page, ILocator more)
    {
        var actions = page.Locator(".mud-popover-open").Filter(new LocatorFilterOptions
        {
            Has = page.Locator("button"),
        }).First;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await more.ClickAsync();

            try
            {
                await actions.Locator("button").Nth(3).WaitForAsync(
                    new LocatorWaitForOptions { Timeout = 5_000 });

                // Off the button that opened it, and wait for its tooltip to go: the tooltip is
                // drawn directly below that button, which is directly over the popover's own
                // buttons, so it intercepts the click meant for the one underneath it.
                await page.Mouse.MoveAsync(0, 0, new MouseMoveOptions { Steps = 8 });
                await page.Locator(".mud-popover-open.mud-tooltip").First.WaitForAsync(
                    new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

                return actions;
            }
            catch (Exception e) when (e is PlaywrightException or TimeoutException)
            {
                // The click toggled it shut, or landed before the row had settled. Try once more.
                await page.WaitForTimeoutAsync(1000);
            }
        }

        Assert.Fail("the More actions popover did not open");
        return actions;
    }

    /// <summary>Opens a dialog, photographs it, and closes it again.</summary>
    private static async Task DialogShotAsync(IPage page, ILocator opener, string name,
                                              bool waitForShell = true)
    {
        if (waitForShell)
            await ShellReadiness.WaitUntilClickableAsync(page);
        await opener.ClickAsync();
        await page.WaitForTimeoutAsync(2000);

        var dialog = page.Locator(".mud-dialog").First;
        Assert.True(await dialog.CountAsync() > 0, $"{name}: nothing opened");

        await ShotOfAsync(dialog, name);
        await CloseTheDialogAsync(page);

        // Take the pointer back over the button and off it again. The dialog opened underneath the
        // pointer, so MudBlazor's tooltip on the button that opened it never got its mouseleave: it
        // stayed open over the row for the rest of the run, and the next step waited ten seconds for
        // a popover that was never going to close. Jumping the mouse away is not enough - the
        // element has to see the pointer arrive and leave.
        //
        // Only if the button is still there. The ones inside the More actions popover are not: the
        // popover closes with the dialog it opened, and hovering what is no longer in the page is a
        // 30-second timeout reported against the *next* picture's name.
        if (await opener.CountAsync() > 0)
        {
            await opener.HoverAsync();
            await page.Mouse.MoveAsync(0, 0, new MouseMoveOptions { Steps = 8 });
        }
    }

    /// <summary>Manage Repositories, and the Edit Repository Details dialog it opens.</summary>
    private async Task RepositorySettingsAsync(IPage page)
    {
        await OpenTabAsync(page, SettingsTab);
        await OpenSettingsPanelAsync(page, "Manage Repositories");

        // Naming a new project: the field, with its confirm and cancel buttons.
        await ShellReadiness.WaitUntilClickableAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "New Project" }).First.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var nameField = page.GetByLabel("Project Name").First;
        if (await nameField.CountAsync() > 0)
        {
            await nameField.FillAsync("Building Simulation");
            await ShotAroundAsync(page, RowContaining(page, "New Project"), "getting-started-4", margin: 16);
        }

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(500);

        // Everything a repository can be told, on one dialog: the paths, the commit requirements,
        // and every rule group.
        await ShellReadiness.WaitUntilClickableAsync(page);
        await page.GetByText("MyLibrary").Last.ClickAsync();
        await page.WaitForTimeoutAsync(1500);

        var dialog = page.Locator(".mud-dialog").First;
        Assert.True(await dialog.CountAsync() > 0, "the Edit Repository Details dialog did not open");

        await ShotOfAsync(dialog, "getting-started-8");

        await SectionShotAsync(page, "Commit requirements", "settings-reference-3", height: 220);
        await SectionShotAsync(page, "Spell checking", "settings-reference-6", height: 320);

        // The naming panel proper - the preset and the per-element styles - rather than the severity
        // row above it. It is only rendered when the naming rule is on, which the repository's own
        // settings turned on before it was added.
        var naming = page.GetByText("Naming Convention Rules").First;
        if (await naming.CountAsync() > 0)
        {
            await naming.ScrollIntoViewIfNeededAsync();
            await page.WaitForTimeoutAsync(600);
            // The element rather than a clip: the panel is taller than the window, and an element
            // screenshot captures all of it while a clip would stop at the bottom of the dialog.
            await ShotOfAsync(naming.Locator("xpath=ancestor::div[contains(@class,'mud-paper')][1]"),
                              "naming-conventions-1");
        }

        await CloseTheDialogAsync(page);
    }

    /// <summary>
    /// A section of a long scrolling dialog: from its heading down, across the dialog's full width.
    /// </summary>
    /// <remarks>
    /// The width comes from the dialog rather than from the heading, which is as wide as its own
    /// words - a clip sized to the heading cut off the severity buttons on the right-hand side, and
    /// those are the setting the section is about.
    /// </remarks>
    private static async Task SectionShotAsync(IPage page, string heading, string name, int height)
    {
        var title = page.GetByText(heading).First;
        if (await title.CountAsync() == 0)
            return;

        await title.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(600);

        var box = await title.BoundingBoxAsync();
        var dialog = await page.Locator(".mud-dialog").First.BoundingBoxAsync();
        if (box is null || dialog is null)
            return;

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = PathFor(name),
            Clip = new Clip
            {
                X = dialog.X,
                Y = Math.Max(0, box.Y - 16),
                Width = dialog.Width,
                Height = Math.Min(height, dialog.Y + dialog.Height - box.Y),
            },
        });
    }

    // ---------------------------------------------------------------- the apparatus

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

        var target = NodeByText(page, className);

        Assert.True(await target.CountAsync() > 0, $"{className} is not in the tree");
        await target.ClickAsync();
        await page.WaitForTimeoutAsync(1500);
    }

    /// <summary>
    /// The row a piece of text sits in — its nearest enclosing stack.
    /// </summary>
    /// <remarks>
    /// <para>From the text outwards rather than from a container inwards: asking for "the container
    /// holding this text" finds a nested one that is often zero-sized, and a zero-sized element
    /// cannot be photographed - it fails as a 30-second timeout naming a selector that is, in fact,
    /// matching.</para>
    ///
    /// <para><b>A <c>MudStack</c> has no <c>mud-stack</c> class.</b> It renders
    /// <c>&lt;div role="group" class="d-flex flex-row ..."&gt;</c>, so the role is the anchor and the
    /// classes are layout, which change. Guessing the class cost two runs; reading the rendered HTML
    /// settled it in one.</para>
    /// </remarks>
    private static ILocator RowContaining(IPage page, string text) =>
        page.GetByText(text).First.Locator("xpath=ancestor::div[@role='group'][1]");

    /// <summary>
    /// The right-hand half of the shell — everything inside the tab panels.
    /// </summary>
    /// <remarks>
    /// Every locator that means "the page the tab is showing" has to say so. The left panel has a
    /// button group of its own, and an unscoped <c>.mud-button-group-root</c> finds *that* one: the
    /// first version of this class clicked what it thought was the side-by-side diff button and
    /// actually flipped the tree into library view, which is a picture of the wrong thing that still
    /// looks plausible.
    /// </remarks>
    private static ILocator Right(IPage page) => page.Locator(".mud-tabs-panels").First;

    /// <summary>The three buttons above the tree: Add Repository, the view toggle, and Refresh.</summary>
    private static ILocator LeftToolbar(IPage page) => page.Locator(".mud-button-group-root").First;

    /// <summary>One tree node, addressed by the text on it.</summary>
    private static ILocator NodeByText(IPage page, string text) =>
        page.Locator(".mud-treeview-item-content", new PageLocatorOptions { HasTextString = text }).First;

    /// <summary>
    /// Opens one tree node by its arrow, and waits for its children to arrive.
    /// </summary>
    /// <remarks>
    /// Idempotent, because the arrow is a toggle and several of these scenes pass the same node
    /// twice - switching the tree to library view and back rebuilds it closed, and a second
    /// unconditional click would have shut what the first one opened. MudBlazor marks an open
    /// node's arrow with <c>mud-transform</c>, which is the only way to ask.
    /// </remarks>
    private static async Task ExpandAsync(IPage page, string nodeText)
    {
        var node = NodeByText(page, nodeText);
        await node.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        var icon = node.Locator(".mud-treeview-item-arrow-expand").First;
        if (await icon.CountAsync() > 0 &&
            (await icon.GetAttributeAsync("class"))?.Contains("mud-transform") == true)
            return;

        var arrow = node.Locator(".mud-treeview-item-arrow button").First;

        if (await arrow.CountAsync() > 0)
            await arrow.ClickAsync();
        else
            await node.DblClickAsync();

        // Server-side children: the node's own click returns before they are fetched.
        await page.WaitForTimeoutAsync(1500);
    }

    /// <summary>Ticks a tree node's impact-analysis checkbox.</summary>
    private static async Task CheckInTheTreeAsync(IPage page, string nodeText)
    {
        var box = NodeByText(page, nodeText).Locator("input[type=checkbox]").First;
        await box.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await box.CheckAsync(new LocatorCheckOptions { Force = true });
        await page.WaitForTimeoutAsync(1500);
    }

    private static async Task UncheckInTheTreeAsync(IPage page, string nodeText)
    {
        var box = NodeByText(page, nodeText).Locator("input[type=checkbox]").First;
        if (await box.CountAsync() > 0)
            await box.UncheckAsync(new LocatorUncheckOptions { Force = true });
    }

    /// <summary>Opens every folder in the resource tree, so the files below them are on screen.</summary>
    private static async Task OpenEveryResourceFolderAsync(IPage page)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            var arrows = await page.Locator(".mud-treeview-item-arrow button").AllAsync();
            if (arrows.Count == 0)
                break;

            foreach (var arrow in arrows)
            {
                try
                {
                    await arrow.ClickAsync(new LocatorClickOptions { Timeout = 2000 });
                }
                catch (Exception e) when (e is PlaywrightException or TimeoutException)
                {
                    // Already open, or gone: neither is worth stopping for.
                }
            }

            await page.WaitForTimeoutAsync(800);
        }
    }

    /// <summary>Selects a resource in the tree by name; false when it is not there.</summary>
    private static async Task<bool> ClickResourceAsync(IPage page, string name)
    {
        var node = NodeByText(page, name);
        if (await node.CountAsync() == 0)
            return false;

        await ShellReadiness.WaitUntilClickableAsync(page);
        await node.ClickAsync();
        await page.WaitForTimeoutAsync(1000);
        return true;
    }

    /// <summary>Opens one of the Settings page's own sub-tabs, by its name.</summary>
    private static async Task OpenSettingsPanelAsync(IPage page, string name)
    {
        await ShellReadiness.WaitUntilClickableAsync(page);
        await page.GetByRole(AriaRole.Tab, new() { Name = name }).ClickAsync();
        await SettleAsync(page);
    }

    /// <summary>Moves to a top-level tab and lets it settle.</summary>
    private static async Task OpenTabAsync(IPage page, int index)
    {
        await ShellReadiness.WaitUntilClickableAsync(page);
        await page.Locator(".mud-tab").Nth(index).ClickAsync();
        await SettleAsync(page);
    }

    /// <summary>
    /// Closes whatever dialog is open, and waits until it has really gone.
    /// </summary>
    /// <remarks>
    /// <b>Scoped to the dialog, and verified.</b> Looking for a button named "Close" anywhere on the
    /// page also finds a snackbar's close button and anything else with that label, so the click
    /// landed somewhere harmless and the dialog stayed up - which surfaced a minute later as the next
    /// step waiting for a scrim that was never going to go. MLQT's dialogs are declared with
    /// <c>BackdropClick="false"</c> and <c>CloseOnEscapeKey="false"</c>, so a button is the only way
    /// out and there is no fallback worth writing: if this cannot close it, the run should say so.
    /// </remarks>
    private static async Task CloseTheDialogAsync(IPage page)
    {
        // The topmost one, not the first: the revision diff opens on top of the history dialog, and
        // aiming at the one underneath means clicking a button another dialog is covering - which
        // Playwright waits thirty seconds to be allowed to do before giving up.
        var dialogs = page.Locator(".mud-dialog");
        var open = await dialogs.CountAsync();
        var dialog = dialogs.Last;

        foreach (var label in new[] { "Cancel", "Close", "Done" })
        {
            var button = dialog.GetByRole(AriaRole.Button, new() { Name = label });
            if (await button.CountAsync() == 0)
                continue;

            await button.First.ClickAsync();

            // One fewer dialog, rather than no scrim: closing the diff leaves the history dialog it
            // opened from, and its scrim is still there and should be.
            for (var waited = 0; waited < 15_000 && await dialogs.CountAsync() >= open; waited += 250)
                await page.WaitForTimeoutAsync(250);

            Assert.True(await dialogs.CountAsync() < open, $"the {label} button did not close the dialog");
            return;
        }

        Assert.Fail("the dialog offers no Cancel, Close or Done button, so nothing can close it");
    }

    /// <summary>Writes the whole page as the named documentation image.</summary>
    private static async Task ShotAsync(IPage page, string name)
    {
        await SettleAsync(page);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = PathFor(name) });
    }

    /// <summary>Writes one element as the named documentation image — the close-up shots.</summary>
    private static async Task ShotOfAsync(ILocator locator, string name)
    {
        await locator.ScreenshotAsync(new LocatorScreenshotOptions { Path = PathFor(name) });
    }

    /// <summary>
    /// Writes a close-up: the element, a margin around it, and no more of its height than asked for.
    /// </summary>
    /// <remarks>
    /// An element screenshot is exactly the element, which for a one-line row is a strip a reader
    /// cannot place, and for a full-height detail panel is mostly empty white below the content. A
    /// clip taken from the element's own box keeps the locator honest and the picture readable.
    /// </remarks>
    private static async Task ShotAroundAsync(IPage page, ILocator locator, string name,
                                              int margin = 12, int? maxHeight = null)
    {
        var box = await locator.BoundingBoxAsync()
                  ?? throw new InvalidOperationException($"{name}: the element has no box to photograph");

        var height = box.Height + margin * 2;

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = PathFor(name),
            Clip = new Clip
            {
                X = Math.Max(0, box.X - margin),
                Y = Math.Max(0, box.Y - margin),
                Width = box.Width + margin * 2,
                Height = maxHeight is null ? height : Math.Min(height, maxHeight.Value),
            },
        });
    }

    private static string PathFor(string name) => Path.Combine(OutputDirectory!, name + ".png");

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
