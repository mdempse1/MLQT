using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.Interfaces;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// Generates the screenshots in <c>Documentation/Images</c>, from the real UI.
/// </summary>
/// <remarks>
/// <para>Off unless <c>MLQT_DOC_SCREENSHOTS</c> names a directory, so an ordinary run does not write
/// to the repository. Run it with:</para>
/// <code>
/// MLQT_DOC_SCREENSHOTS=Documentation/Images dotnet test MLQT.Journeys --filter DocumentationScreenshots
/// </code>
///
/// <para><b>Why this is possible at all:</b> the journeys already drive MLQT's real components in a
/// real browser against a real library, through <c>MLQT.TestHost</c>. A screenshot is the same
/// navigation with one more call at the end, so the pictures are produced by the same mechanism that
/// proves the pages work — and they are reproducible, contain no personal data and no local paths,
/// and can be regenerated when the UI changes rather than being re-taken by hand.</para>
///
/// <para><b>What it cannot do.</b> The test host renders <c>MLQT.Shared</c> under Blazor Server in
/// Chromium, so these show the application's content exactly as it ships and the *window* not at all:
/// no native title bar, no taskbar, no menu. Anything about the window itself — the icon, the window
/// size, a native file dialog — still needs a photograph of the real Photino host, because nothing
/// can drive that automatically (7a says so, and says why: a CDP-driven WebView2 is work that cannot
/// be carried to WebKitGTK).</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class DocumentationScreenshots(TestHostFixture host) : IDisposable
{
    private readonly LibraryFixture _library = new();

    public void Dispose() => _library.Dispose();

    /// <summary>Where to write them, or null when nobody asked.</summary>
    private static string? OutputDirectory =>
        Environment.GetEnvironmentVariable("MLQT_DOC_SCREENSHOTS") is { Length: > 0 } dir ? dir : null;

    /// <summary>
    /// The window the documentation is shot at.
    /// </summary>
    /// <remarks>
    /// The size MLQT opens at (<c>WindowGeometry.PreferredWidth/Height</c>) less the window chrome,
    /// so a reader sees the layout the application actually gives them rather than one stretched to
    /// whatever the runner's display is. Fixed, so two regenerations produce comparable images.
    /// </remarks>
    private const int Width = 1200;
    private const int Height = 860;

    [Fact]
    public async Task EveryTab()
    {
        if (OutputDirectory is null)
            return;   // an ordinary run: the journeys themselves are the test, not these

        Directory.CreateDirectory(OutputDirectory);

        var page = await host.NewPageAsync();
        await page.SetViewportSizeAsync(Width, Height);
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        // The library is opened *after* the page is showing, which is the order a user does it in -
        // and the order the tree is built for. Loading first and rendering afterwards left the
        // browser empty: the tree is filled by the OnTreeDataChanged event, not read on first render.
        var libraries = host.Services.GetRequiredService<ILibraryDataService>();
        await libraries.AddLibraryFromDirectoryAsync(_library.LibraryPath);
        libraries.NotifyTreeDataChanged();

        await host.WaitForIdleAsync();
        await page.WaitForTimeoutAsync(1000);

        var tabs = new (int Index, string Name)[]
        {
            (0, "code-review"),
            (1, "dependencies"),
            (2, "external-resources"),
            (3, "metrics"),
            (4, "settings"),
        };

        foreach (var (index, name) in tabs)
        {
            await page.Locator(".mud-tab").Nth(index).ClickAsync();

            // Off the toolbar afterwards, or the tooltip the click raised sits over the buttons in
            // the picture - which reads as part of the interface rather than as a hover.
            await page.Mouse.MoveAsync(Width / 2, Height - 10);

            // The pages do interop on first render, and a screenshot taken mid-render shows a
            // half-drawn panel - which is worse than no screenshot, because it looks like a defect.
            await page.WaitForTimeoutAsync(1500);

            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(OutputDirectory, $"tab-{name}.png"),
            });
        }
    }
}
