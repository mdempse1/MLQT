using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.Interfaces;
using ModelicaParser.DataTypes;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// A class longer than the viewer can lay out is shown cut short, with a notice saying so, rather than
/// freezing the app.
/// </summary>
/// <remarks>
/// <para>A Dymola FMU import model of 106,354 lines left the webview's renderer at a full core for more
/// than five minutes, with the app frozen behind it: MLQT's own part of the render took 1.7 s, and the
/// rest was the browser laying out one element per line. <c>CodeReview.MaxShownLines</c> caps what it
/// is given. The unit tests hold the cap; only a browser can say whether what is left is quick enough
/// to show, so this opens a class three times the cap and waits for it, with a limit that the stall it
/// replaces could never meet. The cap was chosen by running this at other sizes: 0.6 s for 2,000
/// lines, 2.1 s for 5,000, 6.9 s for 10,000, and a dropped connection at 20,000.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class LargeClassLineLimitJourney(TestHostFixture host) : IAsyncLifetime
{
    /// <summary><c>CodeReview.MaxShownLines</c>, which is internal to MLQT.Shared.</summary>
    private const int MaxShownLines = 5_000;

    /// <summary>Three times the cap, shaped like the generated models that meet it.</summary>
    private const int ClassLines = 15_000;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mlqt-journey-" + Guid.NewGuid().ToString("N"));

    private string LibraryPath => Path.Combine(_root, "HugeLib");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(LibraryPath);
        File.WriteAllText(Path.Combine(LibraryPath, "package.mo"), "within;\npackage HugeLib\nend HugeLib;\n");

        // Constants with descriptions, as Dymola writes an FMU's model description into its import.
        var model = new StringBuilder("within HugeLib;\nmodel HugeModel \"Generated\"\n");
        for (var i = 0; i < ClassLines - 3; i++)
            model.Append($"  constant Real c{i:D5} = {i} \"Constant {i}\";\n");
        model.Append("end HugeModel;\n");
        File.WriteAllText(Path.Combine(LibraryPath, "HugeModel.mo"), model.ToString());
        File.WriteAllText(Path.Combine(LibraryPath, "package.order"), "HugeModel\n");

        await host.ResetLibrariesAsync();
        await host.Services.GetRequiredService<ILibraryDataService>().AddLibraryFromDirectoryAsync(LibraryPath);

        var review = host.Services.GetRequiredService<ICodeReviewService>();
        review.ClearLogMessages();
        review.AddLogMessages([new LogMessage("HugeLib.HugeModel", "Style warning", 1, "Open the huge model")]);
        await host.WaitForIdleAsync();
    }

    public async ValueTask DisposeAsync()
    {
        host.Services.GetRequiredService<ICodeReviewService>().ClearLogMessages();
        await host.ResetLibrariesAsync();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* the temp directory will be swept */ }
    }

    [Fact]
    public async Task AClassPastTheLimit_IsShownCutShort_WithANotice_InReasonableTime()
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator(".mud-tab").Nth(4).WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
        await ShellReadiness.WaitUntilClickableAsync(page);
        await page.Locator(".mud-tab").Nth(0).ClickAsync();
        var row = page.Locator(".mlqt-findings-pane tbody tr").Filter(new LocatorFilterOptions { HasTextString = "Open the huge model" }).First;
        await row.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

        var timer = Stopwatch.StartNew();
        await row.ClickAsync();

        // The parse's paint - `Real` coloured as a type - is the last the class goes through.
        await page.WaitForFunctionAsync(
            "() => { const c = document.querySelector('.code-viewer-content'); return !!c && !!c.querySelector('span.code-type'); }",
            null, new PageWaitForFunctionOptions { Timeout = 60_000 });
        var elapsed = timer.Elapsed;

        var lines = await page.Locator(".code-viewer .code-line").CountAsync();
        var notice = await page.Locator(".mlqt-lines-omitted").InnerTextAsync();

        Assert.Equal(MaxShownLines, lines);
        Assert.Contains($"{ClassLines - 1:N0} lines", notice);   // the file's within clause is not the class's
        Assert.Contains("HugeModel.mo", notice);

        // The cap exists because the uncapped class never finished; say how long the capped one took.
        TestContext.Current.SendDiagnosticMessage($"{MaxShownLines:N0} of {ClassLines:N0} lines on screen in {elapsed.TotalSeconds:F1} s");
        Assert.True(elapsed < TimeSpan.FromSeconds(15), $"the capped class took {elapsed.TotalSeconds:F1} s to show");
    }
}
