using MLQT.Shared.Pages;
using ModelicaParser.Helpers;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// How a background render of the class lands on the page: which one wins when several are in
/// flight (B345), and what may happen over the lexer's first paint of a large class (B342).
///
/// <para>No renderer is needed: the page is constructed and the landing methods are called as the
/// render continuations call them, on what would be the dispatcher.</para>
/// </summary>
public class CodeReviewRenderLandingTests
{
    private static CodeReview.ShownClass Shown(params string[] lines) => new([.. lines], SourceElision.None);

    private static CodeReview.RenderCacheKey Key(bool showAnnotations) =>
        new("M", showAnnotations, ShowHighlighted: true, ExcludeClassDefs: false);

    // ── B345: a stale render must not overwrite the current one ──────────────────

    [Fact]
    public void ARenderAskedForEarlierDoesNotLandOverOneAskedForSince()
    {
        var page = new CodeReview();

        // Hide annotations: a slow render, because it parses twice.
        var hide = page.BeginRender();
        page.BeginLoading();

        // Show annotations, clicked while the Hide render is still running; this one is quick.
        var show = page.BeginRender();
        page.BeginLoading();
        var shownWithAnnotations = Shown("model M", "  Real x annotation(Evaluate=true);", "end M;");
        Assert.True(page.TryApplyRender(show, Key(showAnnotations: true), shownWithAnnotations));

        // Now the Hide render lands, last.
        var shownWithout = Shown("model M", "  Real x;", "end M;");
        Assert.False(page.TryApplyRender(hide, Key(showAnnotations: false), shownWithout));

        Assert.Same(shownWithAnnotations.Lines, page.DisplayedLines);
    }

    [Fact]
    public void TheRenderAskedForLastLands()
    {
        // The control: with nothing asked for since, the render is shown.
        var page = new CodeReview();
        var generation = page.BeginRender();
        page.BeginLoading();
        var shown = Shown("model M", "end M;");

        Assert.True(page.TryApplyRender(generation, Key(true), shown));
        Assert.Same(shown.Lines, page.DisplayedLines);
        Assert.True(page.ReadyForPendingScrolls);
    }

    // ── B342: the lexer's first paint ────────────────────────────────────────────

    [Fact]
    public void PendingScrollsWaitForTheRenderThatFollowsTheFirstPaint()
    {
        var page = new CodeReview();
        var generation = page.BeginRender();
        page.BeginLoading();

        Assert.True(page.TryApplyQuickPaint(generation, Shown("model Big", "  Real x;", "end Big;")));

        // The class is on screen - the spinner has gone - but not as it will stay, so a finding's
        // line cannot be aimed at it yet.
        Assert.NotNull(page.DisplayedLines);
        Assert.False(page.ReadyForPendingScrolls);

        Assert.True(page.TryApplyRender(generation, Key(true), Shown("model Big", "  Real x;", "end Big;")));
        Assert.True(page.ReadyForPendingScrolls);
    }

    [Fact]
    public void TheSearchIsCountedAgainstTheFirstPaintNotThePreviousClass()
    {
        var page = new CodeReview();
        page.CodeSearch = "gain";

        var previous = page.BeginRender();
        page.BeginLoading();
        page.TryApplyRender(previous, Key(true), Shown("model Small", "end Small;"));
        Assert.Equal(0, page.CodeMatchCount);

        var generation = page.BeginRender();
        page.BeginLoading();
        page.TryApplyQuickPaint(generation, Shown("model Big", "  <IDENT>gain</IDENT>;", "  <COMMENT>// gain</COMMENT>", "end Big;"));

        Assert.Equal(2, page.CodeMatchCount);
    }

    [Fact]
    public void AFirstPaintThatArrivesAfterTheParseIsDropped()
    {
        var page = new CodeReview();
        var generation = page.BeginRender();
        page.BeginLoading();
        var final = Shown("model Big", "end Big;");
        page.TryApplyRender(generation, Key(true), final);

        Assert.False(page.TryApplyQuickPaint(generation, Shown("model Big", "end Big;")));
        Assert.Same(final.Lines, page.DisplayedLines);
        Assert.True(page.ReadyForPendingScrolls);
    }

    [Fact]
    public void AFirstPaintForAnEarlierRenderIsDropped()
    {
        var page = new CodeReview();
        var earlier = page.BeginRender();
        page.BeginLoading();
        page.BeginRender();
        page.BeginLoading();

        Assert.False(page.TryApplyQuickPaint(earlier, Shown("model Big", "end Big;")));
        Assert.Null(page.DisplayedLines);
    }
}
