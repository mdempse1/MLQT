using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.Interfaces;
using MLQT.TestHost;
using MLQT.TestHost.Services;

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
        _app = TestHostFactory.Build(ConnectionLogging);
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

        Browser = await type.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = BrowserName == "chromium" && TraceDirectory is not null ? [.. NetLogArgs(TraceDirectory)] : [],
        });
    }

    /// <summary>
    /// Kestrel's connection-level events, which the request log does not show: a connection
    /// accepted, reset or ended without a request.
    /// </summary>
    /// <remarks>
    /// <para>Run 36398522354 lost a navigation to <c>net::ERR_CONNECTION_FAILED</c> ten
    /// milliseconds after it began, between two successful requests to the same host on the same
    /// port - and the request log could not say whether the browser's connection ever arrived.
    /// Chromium reports that error for an operating-system connect failure it has no name for,
    /// so the two halves of the question are here and in <see cref="NetLogArgs"/>.</para>
    /// </remarks>
    private static readonly string[] ConnectionLogging =
    [
        "--Logging:LogLevel:Microsoft.AspNetCore.Server.Kestrel.Connections=Debug",
        "--Logging:LogLevel:Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets=Debug",
    ];

    /// <summary>
    /// Chromium's own network log, written beside the traces - the one record of which
    /// operating-system error a failed connect met (see <see cref="ConnectionLogging"/>).
    /// </summary>
    /// <remarks>
    /// Only with a trace directory, and so only in CI, where the directory is uploaded when the job
    /// failed and discarded when it did not. A Playwright trace has the failed request but not
    /// the socket error beneath it.
    /// </remarks>
    private static IEnumerable<string> NetLogArgs(string directory)
    {
        Directory.CreateDirectory(directory);
        yield return $"--log-net-log={Path.Combine(directory, "chromium-netlog.json")}";
        yield return "--net-log-capture-mode=Default";
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
        await CloseLastPageAsync();

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

    /// <summary>Closes the page handed out last, if it is still open. See <see cref="NewPageAsync"/>.</summary>
    private async Task CloseLastPageAsync()
    {
        if (_lastPage is { IsClosed: false } previous)
        {
            try
            {
                // Navigated away first, so the page unloads and Blazor tells the server the circuit
                // is finished. Closed outright, it may not: the circuit is then kept for the
                // disconnected-circuit retention period, and its MainLayout goes on answering the
                // singletons' events - seen as a Format All run by the closed page's layout while
                // the open page's was refused as overlapping it, its warning drawn nowhere.
                await previous.GotoAsync("about:blank");
                await previous.CloseAsync();
            }
            catch (PlaywrightException)
            {
                // Already gone with its context, or the browser is shutting down. Either way there
                // is no circuit left to leak.
            }
        }
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

    /// <summary>Longer than startup's pause before it reads the saved projects. See ResetRepositoriesAsync.</summary>
    private static readonly TimeSpan StartupPauseAllowance = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Unloads every repository and forgets every saved project, so the next page opened starts as
    /// the application does on a first run.
    /// </summary>
    /// <remarks>
    /// <para>For the journeys that add a repository, or save projects for startup to load. Both
    /// outlive the journey in a shared host, and either is enough to change what every later page
    /// does when it opens: a page whose startup finds saved repositories loads them, and one that
    /// finds more than one project <b>asks the user to choose</b> - a modal nothing in another
    /// journey will ever answer.</para>
    ///
    /// <para>Called at the start of a journey and again at its end, for the reason
    /// <see cref="ResetLibrariesAsync"/> gives for the first and so that a journey that runs
    /// before one of these, and does not know to call this, is not handed what it left.</para>
    /// </remarks>
    public async Task ResetRepositoriesAsync()
    {
        // The last journey's page may still be starting up, and closing it does not stop that: the
        // run is on the singletons. Startup pauses 200 ms before it reads the saved projects, so a
        // repository this journey saves inside that pause is loaded by that run too - a second copy
        // beside ours, which the Library Browser refuses to render (two siblings with one key). Seen
        // once in a full run. There is no signal for "no startup is pending" that a page with nothing
        // to load raises, so this outlasts the pause instead.
        await CloseLastPageAsync();
        await Task.Delay(StartupPauseAllowance);

        var repositories = Services.GetRequiredService<IRepositoryService>();
        repositories.ClearAllRepositories();

        // Clearing saves in the background; this save queues behind it on the same gate, so the
        // settings are removed after both and not raced by either.
        await repositories.SaveRepositorySettingsAsync();
        await Services.GetRequiredService<ISettingsService>().RemoveAsync("Repositories");

        // And memory back to one empty project, as a first run leaves it.
        await repositories.LoadRepositorySettingsAsync();
        await ResetLibrariesAsync();

        // And no VCS work still counted from the last journey - an analysis pipeline a VCS dialog
        // started carries on after its page is closed. Format All is refused while one runs (B385),
        // which is right for a user, who sees the buttons disabled and waits; this waits too.
        var state = Services.GetRequiredService<MLQT.Shared.Models.AppState>();
        var deadline = DateTime.UtcNow.AddMinutes(1);
        while (state.IsVcsWorkInProgress)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("VCS work from an earlier journey is still counted after a minute");
            await Task.Delay(100);
        }
    }

    /// <summary>The formatting pipeline's door, for a journey that needs a pass held part-way.</summary>
    public GatedFormattingPipeline Formatting => Services.GetRequiredService<GatedFormattingPipeline>();

    /// <summary>The settings service's door, for a journey that needs a save held part-way.</summary>
    public GatedSettingsService Settings => Services.GetRequiredService<GatedSettingsService>();

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
