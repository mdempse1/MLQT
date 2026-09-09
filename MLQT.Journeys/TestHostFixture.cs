using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.TestHost;

namespace MLQT.Journeys;

/// <summary>
/// Starts <c>MLQT.TestHost</c> on a real socket and a browser to drive it.
/// </summary>
/// <remarks>
/// <para>Kestrel on an ephemeral port rather than <c>WebApplicationFactory</c>: its
/// <c>TestServer</c> has no socket, so a browser cannot reach it, and a browser is the whole point.
/// Port 0 lets the OS choose, which is what makes two of these safe to run at once.</para>
///
/// <para>One host and one browser per collection. Starting either is measured in seconds, and a
/// journey suite that pays that per test is one nobody runs.</para>
///
/// <para><b>Which browser is a choice, not a constant</b> — see <see cref="BrowserName"/>. Chromium
/// by default, because that is what WebView2 is and what the desktop host runs on Windows; WebKit on
/// demand, because that is the nearest thing to WebKitGTK that can be driven from a test.</para>
/// </remarks>
public sealed class TestHostFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private IPlaywright? _playwright;

    /// <summary>Which Playwright browser the journeys drive.</summary>
    /// <remarks>
    /// <para>Phase 7b-6, and the 7a note's "optional WebKit rehearsal" made real. Playwright's
    /// <c>webkit</c> is <b>not</b> WebKitGTK — it is the same WebKit core behind a different
    /// embedding — but it is far closer to the engine the Linux desktop host runs than Chromium is,
    /// and it surfaces the CSS and JS-feature differences that would otherwise only be found by a
    /// person opening the Photino window.</para>
    ///
    /// <para>An environment variable rather than a parameter, because the whole suite has to move
    /// together: one host and one browser per collection, and the journeys say nothing about which.
    /// Chromium stays the default so the PR gate is unchanged; the nightly job sets this.</para>
    ///
    /// <para>An unrecognised value fails loudly. Falling back to Chromium would mean a typo in the
    /// nightly job produces a green run that tested nothing, which is the failure this job exists to
    /// avoid rather than one it can afford.</para>
    /// </remarks>
    public static string BrowserName =>
        Environment.GetEnvironmentVariable("MLQT_JOURNEY_BROWSER") is { Length: > 0 } name
            ? name.ToLowerInvariant()
            : "chromium";

    public string BaseUrl { get; private set; } = "";
    public IBrowser Browser { get; private set; } = null!;

    /// <summary>The host's service provider, for a journey that drives a service directly.</summary>
    public IServiceProvider Services => _app!.Services;

    public async ValueTask InitializeAsync()
    {
        _app = TestHostFactory.Build();
        _app.Urls.Add("http://127.0.0.1:0");
        await _app.StartAsync();

        BaseUrl = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        _playwright = await Playwright.CreateAsync();

        var type = BrowserName switch
        {
            "chromium" => _playwright.Chromium,
            "webkit" => _playwright.Webkit,
            "firefox" => _playwright.Firefox,
            var other => throw new InvalidOperationException(
                $"MLQT_JOURNEY_BROWSER={other} is not a browser Playwright ships; use chromium, webkit or firefox"),
        };

        Browser = await type.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    /// <summary>A page with the console wired to the test output, and a sane default timeout.</summary>
    public async Task<IPage> NewPageAsync()
    {
        var context = await Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        // The failure mode this catches: an interop call that throws leaves the UI looking merely
        // empty, and without the console there is nothing to read.
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                Console.WriteLine($"[browser console] {message.Text}");
        };
        page.PageError += (_, error) => Console.WriteLine($"[browser error] {error}");

        return page;
    }

    /// <summary>Blocks until MLQT's analysis pipeline has gone quiet. See PipelineQuiescence.</summary>
    public async Task WaitForIdleAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var response = await http.GetAsync($"{BaseUrl}/testapi/idle");
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
            await Browser.CloseAsync();
        _playwright?.Dispose();

        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}

/// <summary>The collection every journey belongs to, so they share one host and one browser.</summary>
[CollectionDefinition(Name)]
public sealed class JourneyCollection : ICollectionFixture<TestHostFixture>
{
    public const string Name = "journeys";
}
