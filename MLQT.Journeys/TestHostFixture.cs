using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.Interfaces;
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

    /// <summary>The page handed out by the last <see cref="NewPageAsync"/>. See its remarks.</summary>
    private IPage? _lastPage;

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
    /// <remarks>
    /// <para><b>The page handed out last is closed first, and that is not tidiness (B237).</b> Every
    /// open page is a live Blazor circuit, and <c>AppState</c> is a <i>singleton</i> shared by all of
    /// them in this host — so a page left open goes on reacting to events raised by whatever page
    /// came after it. <c>CodeReview.OnModelSelected</c> then runs on the raising circuit's
    /// dispatcher rather than its own and the render call throws <c>The current thread is not
    /// associated with the Dispatcher</c>, killing circuits that no test is looking at and taking
    /// the current one's code viewer down with them.</para>
    ///
    /// <para><b>The desktop host has exactly one circuit, so none of this is reachable in the
    /// product</b> — it is an artefact of a harness that opened 71 pages and closed none of them,
    /// and the fix belongs here rather than in a component being made to tolerate it. Journeys hold
    /// one page at a time and always have, so closing the previous one costs nothing; a journey that
    /// ever needs two at once will have to say so here.</para>
    /// </remarks>
    public async Task<IPage> NewPageAsync()
    {
        if (_lastPage is { IsClosed: false } previous)
        {
            try
            {
                await previous.CloseAsync();
            }
            catch (PlaywrightException)
            {
                // Already gone with its context, or the browser is shutting down. Either way there
                // is no circuit left to leak.
            }
        }

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

        return _lastPage = page;
    }

    /// <summary>
    /// Unloads every library a previous journey left behind, so this one starts on an empty graph.
    /// </summary>
    /// <remarks>
    /// <para><b>The journeys share one host, and nothing used to take a library out of it (B237).</b>
    /// Each journey builds its own <c>LibraryFixture</c> under a fresh temp path, and every one of
    /// them is called <c>Lib</c> — so they all produce the same class ids. <c>LibraryFixture.Dispose</c>
    /// deletes the directory and leaves the library registered, and
    /// <c>DirectedGraph.AddNode</c> keeps the copy that arrived first. After the first journey,
    /// <c>Lib.Modified</c> therefore resolves to a node whose file has been deleted.</para>
    ///
    /// <para>That is why a journey needing a class open passes on its own and fails in a full run:
    /// the code viewer has nothing to read. It cost B237 its journey, and
    /// <c>ResizablePanesJourney</c> would have met it the moment it needed a class.</para>
    ///
    /// <para><b>Called at the start of a journey rather than at the end</b>, because a journey that
    /// fails half way through does not get to clean up, and the next one would inherit exactly the
    /// state this exists to prevent.</para>
    /// </remarks>
    public async Task ResetLibrariesAsync()
    {
        var libraries = Services.GetRequiredService<ILibraryDataService>();
        foreach (var library in libraries.Libraries.ToList())
            libraries.RemoveLibrary(library.Id);

        await WaitForIdleAsync();
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
