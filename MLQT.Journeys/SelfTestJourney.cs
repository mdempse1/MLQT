using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// The <c>/selftest</c> conformance route, run against the test host.
/// </summary>
/// <remarks>
/// <para>The same route will be run under MAUI and then under Photino, and the comparison is a diff
/// of the JSON each produces. This journey is what keeps the route itself honest in the meantime: a
/// probe that silently stopped running, or a report that stopped being valid JSON, would make the
/// baseline diff meaningless in exactly the way that is hardest to notice — everything still
/// matching.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class SelfTestJourney(TestHostFixture host)
{
    public sealed record ProbeRow(string Id, string Name, string Status, string Detail);
    public sealed record Report(string Host, string RuntimeVersion, List<ProbeRow> Probes);

    internal static async Task<Report> RunAsync(TestHostFixture host)
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync($"{host.BaseUrl}/selftest");

        // The probes are asynchronous; the page says so itself rather than making the caller guess.
        await page.Locator("#selftest-status:has-text('complete')")
                  .WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });

        var json = await page.Locator("#selftest-result").InnerTextAsync();
        return JsonSerializer.Deserialize<Report>(json,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidOperationException("the self-test produced no report");
    }

    [Fact]
    public async Task TheRouteProducesAReport()
    {
        var report = await RunAsync(host);

        Assert.NotEmpty(report.Probes);
        Assert.Equal("MLQT.TestHost", report.Host);
    }

    [Fact]
    public async Task EveryProbeRan()
    {
        // A probe that threw is recorded as a failure, never dropped - so a report with fewer rows
        // than the route defines means a probe stopped being run at all, which no diff of statuses
        // would show.
        var report = await RunAsync(host);

        Assert.Equal(14, report.Probes.Count);
        Assert.Equal(report.Probes.Count, report.Probes.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public async Task NothingFailsOnThisHost()
    {
        var report = await RunAsync(host);

        var failures = report.Probes.Where(p => p.Status == "Fail").ToList();

        Assert.True(failures.Count == 0,
            "probes failed: " + string.Join("; ", failures.Select(f => $"{f.Id} ({f.Detail})")));
    }

    [Fact]
    public async Task TheProbesThatMatterMostForTheMigration_Pass()
    {
        // Named individually rather than left to the count above, because these four are the ones
        // the design note predicts will break on a new host: static assets, the script globals,
        // Cytoscape under a different engine, and the invariant culture the composition root sets.
        var report = await RunAsync(host);
        var byId = report.Probes.ToDictionary(p => p.Id);

        foreach (var id in new[] { "assets.rcl", "scripts.globals", "cytoscape.init", "cytoscape.layouts", "culture.invariant" })
        {
            Assert.True(byId.ContainsKey(id), $"the route no longer has a probe called {id}");
            Assert.Equal("Pass", byId[id].Status);
        }
    }

    [Fact]
    public async Task TheReportIsStableAcrossRuns()
    {
        // The baseline diff is only meaningful if the same host gives the same answer twice. A probe
        // that flickers would show up as a Photino difference on the day someone happened to re-run
        // it, and be chased as a migration bug.
        var first = await RunAsync(host);
        var second = await RunAsync(host);

        Assert.Equal(
            first.Probes.Select(p => (p.Id, p.Status)),
            second.Probes.Select(p => (p.Id, p.Status)));
    }
}
