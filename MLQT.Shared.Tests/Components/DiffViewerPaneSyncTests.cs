using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using MLQT.Shared.Models;
using Moq;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// That the diff viewer only wires its panes together when it has rendered any.
/// </summary>
/// <remarks>
/// <para><b>What broke.</b> Opening a large FMU model in side-by-side mode took the whole
/// application down, leaving the red "An unhandled error has occurred. Reload" banner:</para>
/// <code>
/// leftEl.addEventListener is not a function
///   at Object.initSyncScroll (diffViewer.js:64)
///   at MLQT.Shared.Components.DiffViewer.OnAfterRenderAsync
/// </code>
/// <para>A file too big for the O(m·n) diff renders a message instead of the panes, so
/// <c>@ref</c> never runs and both element references stay default. <c>OnAfterRenderAsync</c> asked
/// only what the <i>view mode</i> was, not what had been rendered, and called the interop anyway.
/// A default <c>ElementReference</c> still serialises to an object, which is truthy — so it went
/// straight past <c>if (!leftEl || !rightEl) return;</c> in the script as well.</para>
///
/// <para><b>Two things are asserted, and the second is the one that matters.</b> That the call is
/// not made when there are no panes, and that the viewer survives it being made and failing — an
/// exception out of <c>OnAfterRenderAsync</c> is what turns a missing convenience into a dead
/// window.</para>
/// </remarks>
public class DiffViewerPaneSyncTests : MlqtComponentTestBase
{
    /// <summary>Larger than <c>MaxLcsCells</c> allows, which is what puts the viewer on the message path.</summary>
    private static string HugeFile(string marker) =>
        string.Join('\n', Enumerable.Range(0, 8000).Select(i => $"  Real x{i} = {i}; // {marker}"));

    /// <summary>
    /// The viewer reads the theme on initialise, and a bare Moq double hands back a null Task for
    /// it — which is a NullReferenceException before any of this gets a chance to run.
    /// </summary>
    private void ArrangeSettings()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<UISettings>()))
                .ReturnsAsync((string _, UISettings fallback) => fallback);
        Services.AddSingleton(settings.Object);
    }

    private IRenderedComponent<DiffViewer> Render(string original, string modified, DiffViewMode mode)
    {
        ArrangeSettings();

        return Render<DiffViewer>(p => p
            .Add(c => c.OriginalContent, original)
            .Add(c => c.ModifiedContent, modified)
            .Add(c => c.FileName, "Big.mo")
            .Add(c => c.ViewMode, mode));
    }

    private static int SyncCalls(BunitJSInterop interop) =>
        interop.Invocations.Count(i => i.Identifier == "diffViewer.initSyncScroll");

    // ---------------------------------------------------------------- when the panes are not there

    [Fact]
    public void AFileTooLargeToDiffDoesNotAskToSyncPanesThatWereNotRendered()
    {
        var viewer = Render(HugeFile("a"), HugeFile("b"), DiffViewMode.SideBySide);

        Assert.Contains("too large", viewer.Markup);
        Assert.Empty(viewer.FindAll(".diff-pane-content"));
        Assert.Equal(0, SyncCalls(JSInterop));
    }

    [Fact]
    public void TwoEmptyVersionsDoNotAskEither()
    {
        var viewer = Render("", "", DiffViewMode.SideBySideFull);

        Assert.Empty(viewer.FindAll(".diff-pane-content"));
        Assert.Equal(0, SyncCalls(JSInterop));
    }

    [Fact]
    public void TheUnifiedViewHasNoPanesToSync()
    {
        var viewer = Render("Real x;\n", "Real y;\n", DiffViewMode.Unified);

        Assert.Empty(viewer.FindAll(".diff-pane-content"));
        Assert.Equal(0, SyncCalls(JSInterop));
    }

    // ---------------------------------------------------------------- when they are

    /// <summary>The control: with two panes on screen the viewer does still wire them together.</summary>
    [Theory]
    [InlineData(DiffViewMode.SideBySide)]
    [InlineData(DiffViewMode.SideBySideFull)]
    public void ARenderedPairIsSynchronised(DiffViewMode mode)
    {
        var viewer = Render("Real x;\n", "Real y;\n", mode);

        Assert.Equal(2, viewer.FindAll(".diff-pane-content").Count);
        Assert.True(SyncCalls(JSInterop) > 0);
    }

    // ---------------------------------------------------------------- when the interop fails anyway

    /// <summary>
    /// The guard that stops a missing convenience becoming a dead window. Scroll synchronisation
    /// failing is worth a line in the log; it is not worth the red banner whose only offer is
    /// Reload — which, in this application, returns the user to the project selector.
    /// </summary>
    [Fact]
    public void AFailureToSyncDoesNotTakeTheViewerDown()
    {
        ArrangeSettings();
        // SetupVoid, not Setup<T>: the component calls InvokeVoidAsync, and a Setup<T> never
        // matches it - so the arrangement would be ignored and the test would pass on a component
        // that had no guard at all.
        JSInterop.SetupVoid("diffViewer.initSyncScroll", _ => true)
                 .SetException(new JSException("leftEl.addEventListener is not a function"));

        var viewer = Render<DiffViewer>(p => p
            .Add(c => c.OriginalContent, "Real x;\n")
            .Add(c => c.ModifiedContent, "Real y;\n")
            .Add(c => c.FileName, "Small.mo")
            .Add(c => c.ViewMode, DiffViewMode.SideBySide));

        // Markup, not FindAll: this is where bUnit re-raises whatever the render threw, so it is
        // the assertion that can actually fail if the exception escaped.
        Assert.Contains("diff-pane-content", viewer.Markup);
        Assert.Equal(2, viewer.FindAll(".diff-pane-content").Count);
    }
}
