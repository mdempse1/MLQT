using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.Interfaces;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// B382 - a rebase left in progress is found when MLQT opens the repository, and can be continued or
/// aborted from the Rebase dialog.
/// </summary>
/// <remarks>
/// <para>The row's hand check, performed: a rebase stopped on a conflict by git itself, outside the
/// dialog; the repository opened; the header naming the rebase, and Rebase - inside More actions -
/// highlighted and opening on the conflict list; the conflict resolved by editing the file and
/// marking it resolved; and the rebase continued to the end, or aborted.</para>
///
/// <para>The parts are unit-tested - the Git layer finding the rebase, the service carrying it, the
/// dialog's phases over a mocked service, the header over a mocked repository. What none of them can
/// see is the chain from a stopped rebase on disk to a button a user can press, which is what B382
/// was: every part was there except the one that noticed.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class RebaseInProgressJourney(TestHostFixture host) : IAsyncLifetime
{
    private readonly LibraryFixture _library = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await host.ResetRepositoriesAsync();
        _library.Dispose();
    }

    /// <summary>The shell, with the fixture repository open part-way through a rebase.</summary>
    private async Task<(IPage Page, string BranchTip, string Conflicted)> OpenARepositoryMidRebaseAsync()
    {
        await host.ResetRepositoriesAsync();
        var (tip, conflicted) = _library.StopARebaseOnAConflict();

        // Through the service, as DocumentationScreenshots does: the Add Repository dialog opens a
        // native folder picker, the one thing this host fakes. What happens from there on is real.
        var repositories = host.Services.GetRequiredService<IRepositoryService>();
        var added = await repositories.AddRepositoryAsync(_library.RepositoryPath, name: "Rebasing", startMonitoring: false);
        Assert.True(added.Success, added.ErrorMessage);
        await repositories.LoadLibrariesAsync(added.Repository!.Id);
        await host.WaitForIdleAsync();

        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator(".mud-tab").Nth(4).WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
        await ShellReadiness.WaitUntilClickableAsync(page);
        return (page, tip, conflicted);
    }

    private static ILocator Header(IPage page) => page.Locator("[data-testid=rebase-in-progress]");

    private static ILocator MoreActions(IPage page) => page.Locator("button[aria-label='More actions']");

    private static ILocator RebaseDialog(IPage page) =>
        page.Locator(".mud-dialog").Filter(new LocatorFilterOptions { HasTextString = "Rebase Branch" });

    private static ILocator DialogButton(IPage page, string name) =>
        RebaseDialog(page).GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    /// <summary>More actions, then Rebase - which is how a user reaches the dialog.</summary>
    private static async Task OpenTheRebaseDialogAsync(IPage page)
    {
        await MoreActions(page).ClickAsync();
        var rebase = page.Locator(".mud-popover-open button[aria-label='Rebase current branch']");
        await rebase.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        // The rebase is the way out of this state, so the button that starts it is highlighted and,
        // unlike Merge and Push beside it, not disabled by the detached HEAD the rebase leaves.
        await Assertions.Expect(rebase).ToBeEnabledAsync();
        await Assertions.Expect(rebase).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("filled"));

        // Off the button first: its tooltip opens over the popover's row and takes the click.
        await page.Mouse.MoveAsync(0, 0);
        await rebase.ClickAsync();

        await Assertions.Expect(RebaseDialog(page).Locator("[data-testid=rebase-left-in-progress]"))
                        .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task ARebaseLeftInProgress_IsNamedInTheHeader_AndCanBeContinuedOnceItsConflictIsResolved()
    {
        var (page, tip, conflicted) = await OpenARepositoryMidRebaseAsync();

        // The header names the rebase and its branch, rather than a bare detached HEAD.
        await Assertions.Expect(Header(page)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(Header(page)).ToContainTextAsync(LibraryFixture.RebasingBranch);
        await Assertions.Expect(MoreActions(page)).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("filled"));

        await OpenTheRebaseDialogAsync(page);

        // Opened on the rebase that is there, not on a new one: its branch, its conflict, and no
        // Continue while the conflict is unresolved.
        var dialog = RebaseDialog(page);
        await Assertions.Expect(dialog.Locator("[data-testid=rebase-left-in-progress]"))
                        .ToContainTextAsync(LibraryFixture.RebasingBranch);
        await Assertions.Expect(dialog).ToContainTextAsync("Documented.mo");
        await Assertions.Expect(DialogButton(page, "Continue Rebase")).ToBeDisabledAsync();

        // Edit Externally, as a user with their own merge tool would: the file is put right on disk
        // and then marked resolved.
        await DialogButton(page, "Edit Externally").ClickAsync();
        File.WriteAllText(conflicted, """
            within Lib;
            model Documented "Described on both branches"
              Real x "The state";
              Real dx "Its rate of change";
            equation
              der(x) = -x;
              dx = der(x);
              annotation(Documentation(info="<html><p>Nothing to report here.</p></html>"));
            end Documented;
            """.ReplaceLineEndings("\n"));
        await DialogButton(page, "Mark as Resolved").ClickAsync();

        await Assertions.Expect(DialogButton(page, "Continue Rebase")).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await DialogButton(page, "Continue Rebase").ClickAsync();

        // Finished: the push prompt, which this repository has nowhere to push to.
        await Assertions.Expect(dialog).ToContainTextAsync("Rebase complete", new() { Timeout = 30_000 });
        await DialogButton(page, "Skip").ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync(new() { Timeout = 15_000 });

        // Git agrees: no rebase, back on the branch, and the branch replayed onto the other one.
        using (var repo = new Repository(_library.RepositoryPath))
        {
            Assert.Equal(CurrentOperation.None, repo.Info.CurrentOperation);
            Assert.Equal(LibraryFixture.RebasingBranch, repo.Head.FriendlyName);
            Assert.NotEqual(tip, repo.Head.Tip.Sha);
            Assert.Contains("Described on both branches", File.ReadAllText(conflicted));
        }

        // And so does the header, once the browser has re-read the repository.
        await Assertions.Expect(Header(page)).ToBeHiddenAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(page.GetByText("Current branch:").First).ToContainTextAsync(LibraryFixture.RebasingBranch);
        await host.WaitForIdleAsync();
    }

    [Fact]
    public async Task ARebaseLeftInProgress_CanBeAborted_PuttingTheBranchBackAsItWas()
    {
        var (page, tip, _) = await OpenARepositoryMidRebaseAsync();

        await Assertions.Expect(Header(page)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await OpenTheRebaseDialogAsync(page);

        // Abort is there before anything is resolved - it is the way out that asks nothing.
        await Assertions.Expect(DialogButton(page, "Abort Rebase")).ToBeEnabledAsync();
        await DialogButton(page, "Abort Rebase").ClickAsync();
        await Assertions.Expect(RebaseDialog(page)).ToBeHiddenAsync(new() { Timeout = 30_000 });

        using (var repo = new Repository(_library.RepositoryPath))
        {
            Assert.Equal(CurrentOperation.None, repo.Info.CurrentOperation);
            Assert.Equal(LibraryFixture.RebasingBranch, repo.Head.FriendlyName);
            Assert.Equal(tip, repo.Head.Tip.Sha);
        }

        await Assertions.Expect(Header(page)).ToBeHiddenAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(page.GetByText("Current branch:").First).ToContainTextAsync(LibraryFixture.RebasingBranch);
        await host.WaitForIdleAsync();
    }
}
