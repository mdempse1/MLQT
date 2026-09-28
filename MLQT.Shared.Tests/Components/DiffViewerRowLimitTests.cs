using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using MLQT.Shared.Models;
using MLQT.TestSupport;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// B410: a diff small enough in cells to compute can still be far too many rows to render. A
/// ten-line class against a 157,852-line file was 1.9M cells of the 50M allowed, and every line of
/// the file became a removed row - rendered side by side, it took the page down.
/// </summary>
public class DiffViewerRowLimitTests : MlqtComponentTestBase
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private static string Lines(int count, string marker) =>
        string.Join('\n', Enumerable.Range(0, count).Select(i => $"  Real {marker}{i} = {i};"));

    private IRenderedComponent<DiffViewer> Render(string original, string modified, DiffViewMode mode)
    {
        Services.AddSingleton<ISettingsService>(new InMemorySettingsService());

        var viewer = Render<DiffViewer>(p => p
            .Add(c => c.OriginalContent, original)
            .Add(c => c.ModifiedContent, modified)
            .Add(c => c.FileName, "model.mo")
            .Add(c => c.ViewMode, mode));
        viewer.WaitForState(() => !viewer.Instance.IsPreparing, Patience);
        return viewer;
    }

    [Theory]
    [InlineData(DiffViewMode.SideBySideFull)]
    [InlineData(DiffViewMode.SideBySide)]
    [InlineData(DiffViewMode.Unified)]
    public void AWholeFileAgainstAFewLinesSaysSoInsteadOfRenderingEveryRow(DiffViewMode mode)
    {
        // The B410 shape: well inside the cell limit, far outside any sensible number of rows.
        var viewer = Render(Lines(DiffViewer.MaxRows + 5_000, "x"), Lines(10, "y"), mode);

        Assert.Contains("too many to show", viewer.Markup);
        Assert.Empty(viewer.FindAll(".diff-line"));
        Assert.Empty(viewer.FindAll(".diff-pane-content"));
        // The summary still says how big the change is.
        Assert.Contains($"-{DiffViewer.MaxRows + 5_000}", viewer.Markup.Replace(",", ""));
    }

    [Fact]
    public void ALongFileWithAShortDiffIsStillShownInContext()
    {
        // The control, and why rows are counted after laying out rather than from the edit script:
        // 5,000 lines with one changed is 5,000 operations and a handful of rows in context.
        var original = Lines(5_000, "x");
        var modified = original.Replace("Real x2500 = 2500;", "Real x2500 = 0;");

        var viewer = Render(original, modified, DiffViewMode.Unified);

        Assert.DoesNotContain("too many to show", viewer.Markup);
        Assert.Single(viewer.FindAll(".diff-line-added"));
        Assert.True(viewer.FindAll(".diff-line").Count < 20);
    }

    [Fact]
    public void JustUnderTheLimitIsRendered()
    {
        // The other control: the limit is on rows, and a full view that fits is drawn in full.
        var viewer = Render(Lines(DiffViewer.MaxRows - 20, "x"), Lines(10, "y"), DiffViewMode.SideBySideFull);

        Assert.DoesNotContain("too many to show", viewer.Markup);
        Assert.Equal(DiffViewer.MaxRows - 10, viewer.FindAll(".diff-pane-left .diff-line").Count);
    }
}
