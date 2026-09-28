using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Shared.Models;
using MudBlazor;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// B385 - Format All Files counts as version-control work, so nothing that changes the working copy
/// can be started while it rewrites every file.
/// </summary>
/// <remarks>
/// <para>The row's hand check, performed: start Format All Files, and the Library Browser's VCS
/// buttons and the Refresh button stay disabled until it finishes. The queue that counts it is
/// unit-tested; the wiring in <c>MainLayout</c> from the settings event to that queue had no harness,
/// which is why the row waited for a person.</para>
///
/// <para>On the fixture library the full pass is over in milliseconds, so it is held part-way at the
/// test host's <see cref="MLQT.TestHost.Services.GatedFormattingPipeline"/> - the application's own
/// pipeline behind a door - and the buttons are read while it waits.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class FormatAllIsVcsWorkJourney(TestHostFixture host) : IAsyncLifetime
{
    private const string RepositoryName = "Formatting";

    private readonly LibraryFixture _library = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await host.ResetRepositoriesAsync();
        _library.Dispose();
    }

    /// <summary>Everything in the shell that starts version-control work or re-reads the files.</summary>
    private static IEnumerable<(string Name, ILocator Button)> VcsControls(IPage page) =>
    [
        ("Update", page.Locator("button[aria-label='Update repository']")),
        ("Commit", page.Locator("button[aria-label='Commit changes']")),
        ("Revert", page.Locator("button[aria-label='Revert changes']")),
        ("Switch branch", RepositoryShell.ButtonWithIcon(page, Icons.Material.Outlined.CallSplit)),
        ("Create branch", page.Locator("button[aria-label='Create new branch']")),
        ("More actions", page.Locator("button[aria-label='More actions']")),
        ("Refresh", RepositoryShell.ButtonWithIcon(page, Icons.Material.Filled.Refresh)),
    ];

    /// <summary>Every control in <see cref="VcsControls"/> on or off, naming the one that is not.</summary>
    private static async Task ExpectAllAsync(IPage page, bool enabled, int timeoutMs)
    {
        foreach (var (name, button) in VcsControls(page))
        {
            try
            {
                if (enabled)
                    await Assertions.Expect(button.First).ToBeEnabledAsync(new() { Timeout = timeoutMs });
                else
                    await Assertions.Expect(button.First).ToBeDisabledAsync(new() { Timeout = timeoutMs });
            }
            catch (PlaywrightException e)
            {
                Assert.Fail($"{name} should be {(enabled ? "enabled" : "disabled")}: {e.Message}");
            }
        }
    }

    [Fact]
    public async Task WhileFormatAllFilesRuns_TheVcsButtonsAndRefreshAreDisabled_AndComeBackWhenItEnds()
    {
        await host.ResetRepositoriesAsync();
        _library.EnableFormatting();
        await RepositoryShell.AddAsync(host, _library, RepositoryName);
        var page = await RepositoryShell.OpenAsync(host);

        // The positive control: every one of them is on before the format, so "off" below is the
        // format's doing and not the fixture's. The working copy has an uncommitted edit, which is
        // what turns Commit and Revert on.
        await ExpectAllAsync(page, enabled: true, timeoutMs: 15_000);

        var before = File.ReadAllText(_library.ModifiedFile);
        using var gate = host.Formatting.HoldAllFiles();
        await RepositoryShell.FormatAllFilesAsync(page, RepositoryName);
        await gate.WaitForArrivalAsync();

        // Part-way through the rewrite: counted as VCS work, and every control that would start some
        // more is off.
        await Assertions.Expect(page.GetByText("Formatting all files").First).ToBeVisibleAsync();
        await ExpectAllAsync(page, enabled: false, timeoutMs: 10_000);
        Assert.True(host.Services.GetRequiredService<AppState>().IsVcsWorkInProgress,
            "Format All Files is running and is not counted as VCS work");

        gate.Release();

        // Finished: the files were rewritten, and every control is back.
        await Assertions.Expect(page.GetByText("Formatting all files").First).ToBeHiddenAsync(new() { Timeout = 60_000 });
        await ExpectAllAsync(page, enabled: true, timeoutMs: 60_000);
        Assert.NotEqual(before, File.ReadAllText(_library.ModifiedFile));

        await host.WaitForIdleAsync();
    }
}
