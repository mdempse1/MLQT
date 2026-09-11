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

    /// <summary>
    /// Where a journey's Playwright trace is written, or null when nobody asked for one.
    /// </summary>
    /// <remarks>
    /// <para>Backlog B148, and 7a asked for it in as many words: "journey failures on a headless
    /// Linux runner are otherwise near-undebuggable". A trx gives an assertion message and a stack;
    /// a trace gives the DOM at each step, screenshots, the console and the network log, which is
    /// what actually answers "why did that selector not match".</para>
    ///
    /// <para>Recorded when this is set rather than always, because tracing costs time and disk on
    /// every journey - and <b>kept</b> by the workflow only when the job failed. Deciding in the
    /// upload rather than in the code means nothing here has to know how the test ended, which xUnit
    /// does not offer a fixture cleanly anyway.</para>
    /// </remarks>
    public static string? TraceDirectory =>
        Environment.GetEnvironmentVariable("MLQT_JOURNEY_TRACE") is { Length: > 0 } dir ? dir : null;

    private readonly List<(IBrowserContext Context, string Name)> _traced = [];
    private int _traceCounter;

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

        if (TraceDirectory is not null)
        {
            await context.Tracing.StartAsync(new TracingStartOptions
            {
                Screenshots = true,
                Snapshots = true,
                Sources = true,
                Title = TestContext.Current.Test?.TestDisplayName,
            });

            // Named after the test where xUnit offers it, so a directory of traces can be read
            // without opening them. The counter keeps two pages in one test apart.
            var name = TestContext.Current.Test?.TestDisplayName ?? "journey";
            _traced.Add((context, $"{Sanitise(name)}-{Interlocked.Increment(ref _traceCounter)}"));
        }

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

    /// <summary>A test display name that a file system will accept.</summary>
    private static string Sanitise(string name)
    {
        var clean = new string([.. name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)]);
        return clean.Length <= 120 ? clean : clean[..120];
    }

    public async ValueTask DisposeAsync()
    {
        // Written before the browser closes, which takes the contexts with it. One file per journey:
        // Playwright's viewer opens them individually, and a single merged trace would be unreadable.
        if (_traced.Count > 0)
            Directory.CreateDirectory(TraceDirectory!);

        foreach (var (context, name) in _traced)
        {
            try
            {
                await context.Tracing.StopAsync(new TracingStopOptions
                {
                    Path = Path.Combine(TraceDirectory!, $"{name}.zip"),
                });
            }
            catch (Exception ex)
            {
                // A trace that cannot be written must not fail a run that otherwise passed - it is
                // evidence about a failure, not a result in itself.
                Console.WriteLine($"[trace] could not write {name}: {ex.Message}");
            }
        }

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
