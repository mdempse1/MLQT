using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.Interfaces;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using MudBlazor;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// <c>MLQT.Resource.Missing</c> in the desktop app, end to end: the application's own pipeline reports
/// the fixture's missing file, and putting the file back clears the finding from the refresh button.
/// </summary>
/// <remarks>
/// <para>The second half is B521. Whether a resource exists was read once, when its graph node was
/// made, and the resource watchers queued every change where nothing read it - so a restored file
/// went on being reported until the project was reloaded. The rule's decision reads the disk when
/// it runs, the watcher's changes now count on the refresh button with the library's own file
/// changes, and a refresh re-checks the classes that reference them. That wiring is in
/// <c>MainLayout</c>, which only a journey reaches.</para>
///
/// <para>The fixture's <c>Lib.Components.Profile</c> loads <c>calibration.txt</c>, which the library
/// deliberately does not ship.</para>
///
/// <para>Run alone it passed from the first try; run after the other journeys it failed, because
/// they had loaded libraries called <c>Lib</c> too and removing a class left its resource
/// references behind, pointing into their deleted checkouts (B522). The shared host is what made
/// this a test of something the product does: open another checkout of a library.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class MissingResourceJourney(TestHostFixture host) : IAsyncLifetime
{
    private const string RepositoryName = "Resources";
    private const string ReferringClass = "Lib.Components.Profile";

    private readonly LibraryFixture _library = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await host.ResetRepositoriesAsync();
        _library.Dispose();
    }

    private List<LogMessage> CalibrationFindings() =>
        host.Services.GetRequiredService<ICodeReviewService>().LogMessages
            .Where(m => m.RuleId == RuleIds.MissingResource && m.Summary.Contains("calibration.txt", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public async Task AMissingFileIsAFinding_AndPuttingItBackClearsIt_FromTheRefreshButton()
    {
        await host.ResetRepositoriesAsync();
        _library.EnableRules(RuleIds.MissingResource);
        await RepositoryShell.AddAsync(host, _library, RepositoryName);

        // Open first: the checker raises its findings and MainLayout is what collects them, so a check
        // run with no page open reports to nobody.
        var page = await RepositoryShell.OpenAsync(host);

        // What the Add Repository dialog's progress runs after adding: the check, then the resource
        // analysis and its watchers. Adding through the service, as the shell does, runs none of it
        // (DocumentationScreenshots does the same, for the same reason). The watchers matter here:
        // they are what hears the file come back.
        var libraries = host.Services.GetRequiredService<ILibraryDataService>();
        var graph = libraries.CombinedGraph;
        await host.Services.GetRequiredService<IStyleCheckingService>()
                  .CheckModelsAsync(graph.ModelNodes.Select(m => m.Id).ToList(), graph);
        await libraries.EnsureDependenciesAnalyzedAsync();
        var resources = host.Services.GetRequiredService<IExternalResourceService>();
        await resources.AnalyzeResourcesAsync(graph);
        resources.StartMonitoringResources();
        await host.WaitForIdleAsync();

        // Reported by the application's own pipeline, against the class that names the file.
        await HostPolling.UntilAsync(() => CalibrationFindings().Count > 0, what: "the missing calibration.txt to be reported");
        var finding = Assert.Single(CalibrationFindings());
        Assert.Equal(ReferringClass, finding.ModelName);

        // Put the file back, from outside MLQT.
        File.WriteAllText(Path.Combine(_library.ResourcesPath, "Data", "calibration.txt"), "1.0 1.0\n");

        // The resource watcher hears it, and the change is counted on the refresh button, as a library
        // file's is. Waited for in that order: a badge another journey's changes left behind would
        // otherwise let the click go before this change had arrived.
        await HostPolling.UntilAsync(() => resources.PendingResourceChangeCount > 0, 30_000,
            "the resource watcher to queue the restored file");
        await page.Locator(".mud-badge").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

        var refresh = RepositoryShell.ButtonWithIcon(page, Icons.Material.Filled.Refresh).First;
        await Assertions.Expect(refresh).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await refresh.ClickAsync();
        await HostPolling.UntilAsync(() => resources.PendingResourceChangeCount == 0, 30_000,
            "the refresh to take the resource change");

        await HostPolling.UntilAsync(() => CalibrationFindings().Count == 0, what: "the restored calibration.txt to stop being reported");
        await host.WaitForIdleAsync();
        resources.StopMonitoringResources();
    }
}
