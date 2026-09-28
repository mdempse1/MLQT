using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.Interfaces;
using ModelicaParser.DataTypes;
using MudBlazor;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// B402 - a class over 64 KB with something to hide is not painted until it can be hidden, while one
/// with nothing to hide is still painted before its parse (B185).
/// </summary>
/// <remarks>
/// <para>The row's hand check, performed, with the browser watching every paint rather than a person
/// watching for a flash. The unit tests decide whether the first paint happens; what they could not
/// see is a first paint reaching the screen, since in bUnit the parse of a 64 KB class wins the race.
/// In a browser the lexer's paint arrives first, and a <c>MutationObserver</c> installed before the
/// class is opened records each state the viewer passes through.</para>
///
/// <para><b>How a paint is told apart:</b> the lexer cannot know that <c>Real</c> is a type, so its
/// paint colours it as an identifier (<c>code-ident</c>) and the parse's as a type (<c>code-type</c>)
/// - see <c>CodeReviewLargeClassTests.WhatOnlyTheTreeKnowsIsWhatArrivesLate</c>. And the thing that is
/// about to be hidden carries a marker no hidden view contains.</para>
///
/// <para>The row's three checks: a package's nested classes never appear in full; with annotations
/// hidden, no annotation appears in a large model; with them shown, that model is still painted before
/// its colouring arrives.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class LargeClassFirstPaintJourney(TestHostFixture host) : IAsyncLifetime
{
    /// <summary>In a nested class's body, and nowhere a hidden view shows.</summary>
    private const string NestedMarker = "nestedBodyMarker";

    /// <summary>In every annotation of the large model, and nowhere else.</summary>
    private const string AnnotationMarker = "annotationMarker";

    /// <summary><c>CodeReview.PaintBeforeParsingAbove</c>, which is internal to MLQT.Shared.</summary>
    private const int PaintBeforeParsingAbove = 64 * 1024;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mlqt-journey-" + Guid.NewGuid().ToString("N"));

    private string LibraryPath => Path.Combine(_root, "BigLib");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(LibraryPath);

        // A package whose own source holds its nested classes - hidden for every package - and a
        // constant ahead of them, so there is a `Real` to tell the paints apart by.
        var package = new StringBuilder("within;\npackage BigLib \"A package over 64 KB\"\n");
        package.Append("  constant Real packageConstant = 1 \"Something the package itself declares\";\n");
        for (var i = 0; i < 1500; i++)
            package.Append($"  model Nested{i:D4} \"Nested class {i}\"\n")
                   .Append($"    Real {NestedMarker}{i:D4} \"Only visible with the nested class shown\";\n")
                   .Append("  equation\n")
                   .Append($"    der({NestedMarker}{i:D4}) = -{NestedMarker}{i:D4};\n")
                   .Append($"  end Nested{i:D4};\n");
        package.Append("end BigLib;\n");
        File.WriteAllText(Path.Combine(LibraryPath, "package.mo"), package.ToString());

        // A model with an annotation on every declaration and no nested class.
        var model = new StringBuilder("within BigLib;\nmodel BigModel \"A model over 64 KB\"\n");
        for (var i = 0; i < 2500; i++)
            model.Append($"  parameter Real p{i:D4} = {i} \"Parameter {i}\" annotation(Dialog(group=\"{AnnotationMarker}\"));\n");
        model.Append("end BigModel;\n");
        File.WriteAllText(Path.Combine(LibraryPath, "BigModel.mo"), model.ToString());
        File.WriteAllText(Path.Combine(LibraryPath, "package.order"), "BigModel\n");

        Assert.True(package.Length > PaintBeforeParsingAbove && model.Length > PaintBeforeParsingAbove,
            "the fixture classes are below the size at which the lexer paints first, so this would test nothing");

        await host.ResetLibrariesAsync();
        await host.Services.GetRequiredService<ILibraryDataService>().AddLibraryFromDirectoryAsync(LibraryPath);

        // One finding on each class, whose rows are how they are opened.
        var review = host.Services.GetRequiredService<ICodeReviewService>();
        review.ClearLogMessages();
        review.AddLogMessages([
            new LogMessage("BigLib", "Style warning", 1, "Open the package"),
            new LogMessage("BigLib.BigModel", "Style warning", 1, "Open the model"),
        ]);
        await host.WaitForIdleAsync();
    }

    public async ValueTask DisposeAsync()
    {
        host.Services.GetRequiredService<ICodeReviewService>().ClearLogMessages();
        await host.ResetLibrariesAsync();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* the temp directory will be swept */ }
    }

    private async Task<IPage> OpenCodeReviewAsync()
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator(".mud-tab").Nth(4).WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
        await ShellReadiness.WaitUntilClickableAsync(page);
        await page.Locator(".mud-tab").Nth(0).ClickAsync();
        await page.Locator(".mlqt-findings-pane tbody tr").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        return page;
    }

    /// <summary>
    /// Starts recording each state the viewer shows: how its first <c>Real</c> is coloured, and
    /// whether <paramref name="marker"/> is on screen.
    /// </summary>
    /// <remarks>
    /// Only of a viewer that was not already there. Opening a class replaces the viewer with a
    /// spinner and then draws a new one, so the one on screen now is the class shown before - which
    /// in a shared host may be the previous journey's, since the selection lives on a singleton.
    /// </remarks>
    private static Task WatchThePaintsAsync(IPage page, string marker) => page.EvaluateAsync(@"marker => {
        window.__paints = [];
        if (window.__paintObserver) window.__paintObserver.disconnect();
        const stale = document.querySelector('.code-viewer-content');
        const record = () => {
            const content = document.querySelector('.code-viewer-content');
            if (!content || content === stale || !content.querySelector('.code-line')) return;
            const real = [...content.querySelectorAll('span.code-type, span.code-ident')].find(s => s.textContent === 'Real');
            const state = (real ? real.className : 'none') + '|' + content.textContent.includes(marker);
            if (window.__paints[window.__paints.length - 1] !== state) window.__paints.push(state);
        };
        window.__paintObserver = new MutationObserver(record);
        window.__paintObserver.observe(document.body, { childList: true, subtree: true, characterData: true });
    }", marker);

    /// <summary>Waits for the parse's paint - <c>Real</c> coloured as a type - and returns every state seen.</summary>
    private static async Task<string[]> PaintsUntilParsedAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => (window.__paints || []).some(p => p.startsWith('code-type'))",
            null, new PageWaitForFunctionOptions { Timeout = 60_000 });
        return await page.EvaluateAsync<string[]>("() => window.__paints");
    }

    private static Task OpenFindingAsync(IPage page, string message) =>
        page.Locator(".mlqt-findings-pane tbody tr").Filter(new LocatorFilterOptions { HasTextString = message }).First.ClickAsync();

    [Fact]
    public async Task APackageOver64KB_NeverShowsItsNestedClassesBeforeHidingThem()
    {
        var page = await OpenCodeReviewAsync();

        await WatchThePaintsAsync(page, NestedMarker);
        await OpenFindingAsync(page, "Open the package");
        var paints = await PaintsUntilParsedAsync(page);

        // One paint, the parse's, with the nested classes already hidden. The lexer's would have
        // been "code-ident|true": every nested class in full, collapsing a moment later.
        Assert.Equal(["code-type|false"], paints);
    }

    [Fact]
    public async Task AModelOver64KB_IsPaintedAtOnceWithAnnotationsShown_AndNotUntilParsedWithThemHidden()
    {
        var page = await OpenCodeReviewAsync();

        // Annotations shown, the default: nothing to hide, so the lexer paints it first and the
        // parse recolours it (B185) - the case B402 had to leave alone. This is also what shows the
        // watching can see a first paint at all.
        await WatchThePaintsAsync(page, AnnotationMarker);
        await OpenFindingAsync(page, "Open the model");
        var shown = await PaintsUntilParsedAsync(page);
        Assert.Equal(["code-ident|true", "code-type|true"], shown);

        // Annotations hidden: the lexer cannot hide them, so no paint until the parse can.
        await WatchThePaintsAsync(page, AnnotationMarker);
        await RepositoryShell.ButtonWithIcon(page, Icons.Material.Filled.Bookmark).First.ClickAsync();
        var hidden = await PaintsUntilParsedAsync(page);
        Assert.Equal(["code-type|false"], hidden);
    }
}
