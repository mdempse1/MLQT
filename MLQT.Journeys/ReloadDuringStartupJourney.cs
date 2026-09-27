using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.Interfaces;
using MLQT.Shared.Models;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// B407 - a window reloaded while startup is still running shows that run's progress, and closes it
/// by itself when the run ends.
/// </summary>
/// <remarks>
/// <para>The row's hand check, performed: a project whose startup is held at step 2, the window
/// reloaded, and the reloaded window showing "Loading project repositories, please wait" with the
/// step the run is on, moving on to the file system monitors and closing by itself when the run
/// ends - with one load of the project, not two. A reload in the test host is a new circuit over the
/// same singletons, which is what a reload is in the Photino window: the layout is replaced, the
/// services and the run on them are not (B357).</para>
///
/// <para>The first window is the other half of the check, "a normal start shows only the usual
/// six-step dialog".</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class ReloadDuringStartupJourney(TestHostFixture host) : IAsyncLifetime
{
    private readonly LibraryFixture _library = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await host.ResetRepositoriesAsync();
        _library.Dispose();
    }

    [Fact]
    public async Task AReloadDuringStartup_ShowsTheRunsProgress_UntilItEnds()
    {
        await host.ResetRepositoriesAsync();
        await StartupProgress.SaveProjectsAsync(host, StartupProgress.ProjectFor(_library, "Reloaded"));

        // Startup's step 2, held: the project is loaded and the run is formatting modified files.
        using var gate = host.Formatting.HoldModifiedFiles();
        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl);
        await gate.WaitForArrivalAsync();

        // A normal start: the six-step dialog, and only that.
        await Assertions.Expect(StartupProgress.SixStep(page)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(StartupProgress.EarlierRun(page)).ToHaveCountAsync(0);

        await page.ReloadAsync();

        // The reloaded window did not start again - it shows the run the first one began, on the
        // step it is on, and not a six-step dialog of its own.
        var earlier = StartupProgress.EarlierRun(page);
        await Assertions.Expect(earlier).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(earlier).ToContainTextAsync("Formatting modified files");
        await Assertions.Expect(StartupProgress.SixStep(page)).ToHaveCountAsync(0);

        gate.Release();

        // It follows the run - the monitors are its last step, shown for the two seconds the run
        // waits before it ends - and closes by itself when the run does.
        await Assertions.Expect(earlier).ToContainTextAsync("Setting up file system monitors", new() { Timeout = 60_000 });
        await Assertions.Expect(earlier).ToHaveCountAsync(0, new() { Timeout = 30_000 });
        await Assertions.Expect(StartupProgress.SixStep(page)).ToHaveCountAsync(0);

        // One run: the project's one repository and one library, not a second load beside the first.
        Assert.Null(host.Services.GetRequiredService<AppState>().StartupStep);
        Assert.Single(host.Services.GetRequiredService<IRepositoryService>().Repositories);
        Assert.Single(host.Services.GetRequiredService<ILibraryDataService>().Libraries);

        await host.WaitForIdleAsync();
    }
}
