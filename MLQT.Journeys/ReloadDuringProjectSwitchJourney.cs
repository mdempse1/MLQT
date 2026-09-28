using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Models;
using MudBlazor;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// B423 - a window reloaded during a project switch shows the switch's progress, hands over to its
/// own dialog when the switch reaches it, and closes when the switch ends.
/// </summary>
/// <remarks>
/// <para>The row's hand check, performed, in its three parts. A switch reloaded <b>during step 1</b>,
/// before the switch has told any window it happened: the reloaded window shows the switch's step,
/// and when the switch reaches it, its own six-step dialog from formatting onwards, with no second
/// dialog behind it. A switch reloaded <b>later</b>, while the window that started it is running
/// it: the dialog follows it to the end and closes by itself. And <b>a normal switch afterwards</b>,
/// in a window that was itself reloaded, shows only the six-step dialog.</para>
///
/// <para>Step 1 is held at the save that ends it - <c>OnProjectChanged</c> is raised only after
/// the new project's settings are written - through the test host's
/// <see cref="MLQT.TestHost.Services.GatedSettingsService"/>; the later steps at the formatting
/// pass, as for startup (B407).</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class ReloadDuringProjectSwitchJourney(TestHostFixture host) : IAsyncLifetime
{
    private readonly LibraryFixture _first = new();
    private readonly LibraryFixture _second = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await host.ResetRepositoriesAsync();
        _first.Dispose();
        _second.Dispose();
    }

    private IRepositoryService Repositories => host.Services.GetRequiredService<IRepositoryService>();

    /// <summary>The one repository loaded, which says which project the switch ended on.</summary>
    private void AssertOnlyLoaded(LibraryFixture library)
    {
        var loaded = Assert.Single(Repositories.Repositories);
        Assert.Equal(library.RepositoryPath, loaded.VcsRootPath, ignoreCase: true);
        Assert.Null(host.Services.GetRequiredService<AppState>().StartupStep);
    }

    /// <summary>Polls a condition on the host's services, which nothing on the page announces.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 60_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("the host did not reach the expected state in time");
            await Task.Delay(100);
        }
    }

    /// <summary>Settings, Manage Repositories, and the named project's Load project button.</summary>
    private static async Task LoadProjectAsync(IPage page, string name)
    {
        await ShellReadiness.WaitUntilClickableAsync(page);
        await page.Locator(".mud-tab").Nth(4).ClickAsync();

        var manage = page.GetByRole(AriaRole.Tab, new() { Name = "Manage Repositories" });
        await manage.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await ShellReadiness.WaitUntilClickableAsync(page);
        await manage.ClickAsync();

        var panel = page.Locator(".mud-expand-panel").Filter(new LocatorFilterOptions { HasTextString = $"Project: {name}" });
        await RepositoryShell.ButtonWithIcon(page, panel, Icons.Material.Filled.PlayArrow).First.ClickAsync();
    }

    [Fact]
    public async Task AReloadDuringAProjectSwitch_ShowsItsProgress_AndItsOwnDialogWhenTheSwitchReachesIt()
    {
        await host.ResetRepositoriesAsync();
        var first = StartupProgress.ProjectFor(_first, "First");
        var second = StartupProgress.ProjectFor(_second, "Second");
        await StartupProgress.SaveProjectsAsync(host, first, second);

        // Two projects saved, so startup asks which to open - as it does for a user with two.
        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl);
        await page.GetByText("First (1 repositories)").ClickAsync(new() { Timeout = 30_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Load Project" }).ClickAsync();
        await WaitUntilAsync(() => Repositories.Repositories.Count == 1
                                   && host.Services.GetRequiredService<AppState>().StartupStep is null);
        await Assertions.Expect(StartupProgress.SixStep(page)).ToHaveCountAsync(0, new() { Timeout = 60_000 });
        await host.WaitForIdleAsync();
        AssertOnlyLoaded(_first);

        // ---- A reload during step 1: the switch has loaded Second and not yet said so.
        using var saved = host.Settings.HoldWrite((key, value) =>
            key == "Repositories" && value is RepositorySettingsCollection c && c.ActiveProjectId == second.Id);
        using var formatting = host.Formatting.HoldModifiedFiles();

        await LoadProjectAsync(page, "Second");
        await saved.WaitForArrivalAsync();
        await Assertions.Expect(StartupProgress.SixStep(page)).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await page.ReloadAsync();

        var earlier = StartupProgress.EarlierRun(page);
        await Assertions.Expect(earlier).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(earlier).ToContainTextAsync("Loading libraries from repositories");
        await Assertions.Expect(StartupProgress.SixStep(page)).ToHaveCountAsync(0);

        // The switch reaches this window, which runs the rest of it in its own dialog - and only
        // that one, not the earlier run's dialog behind it.
        saved.Release();
        await formatting.WaitForArrivalAsync();
        await Assertions.Expect(StartupProgress.SixStep(page)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(earlier).ToHaveCountAsync(0);

        formatting.Release();
        await Assertions.Expect(StartupProgress.SixStep(page)).ToHaveCountAsync(0, new() { Timeout = 60_000 });
        await Assertions.Expect(earlier).ToHaveCountAsync(0);
        await host.WaitForIdleAsync();
        AssertOnlyLoaded(_second);

        // ---- A reload later in a switch, while the window that started it is running it.
        using var formattingBack = host.Formatting.HoldModifiedFiles();
        await LoadProjectAsync(page, "First");
        await formattingBack.WaitForArrivalAsync();
        await Assertions.Expect(StartupProgress.SixStep(page)).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await page.ReloadAsync();

        await Assertions.Expect(earlier).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(earlier).ToContainTextAsync("Formatting modified files");
        await Assertions.Expect(StartupProgress.SixStep(page)).ToHaveCountAsync(0);

        formattingBack.Release();
        await Assertions.Expect(earlier).ToContainTextAsync("Setting up file system monitors", new() { Timeout = 60_000 });
        await Assertions.Expect(earlier).ToHaveCountAsync(0, new() { Timeout = 30_000 });
        await host.WaitForIdleAsync();
        AssertOnlyLoaded(_first);

        // ---- A normal switch afterwards, in this reloaded window: the six-step dialog alone.
        using var formattingAgain = host.Formatting.HoldModifiedFiles();
        await LoadProjectAsync(page, "Second");
        await formattingAgain.WaitForArrivalAsync();
        await Assertions.Expect(StartupProgress.SixStep(page)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(earlier).ToHaveCountAsync(0);

        formattingAgain.Release();
        await Assertions.Expect(StartupProgress.SixStep(page)).ToHaveCountAsync(0, new() { Timeout = 60_000 });
        await host.WaitForIdleAsync();
        AssertOnlyLoaded(_second);
    }
}
