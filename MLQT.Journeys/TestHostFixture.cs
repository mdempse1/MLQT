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
/// </remarks>
public sealed class TestHostFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private IPlaywright? _playwright;

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
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
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
