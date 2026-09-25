using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using MLQT.Shared.Models;
using Moq;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// B341: the diff viewer re-ran an O(m·n) LCS and a parse of each side on every parameter set, on
/// the dispatcher — and the Code Review page re-renders for every findings batch and progress
/// update, so during a background check each render cost two parses of an unchanged class.
/// </summary>
public class DiffViewerMemoTests : MlqtComponentTestBase
{
    private const string Original = "model M\n  Real x;\nend M;\n";
    private const string Modified = "model M\n  Real y;\nend M;\n";

    private IRenderedComponent<DiffViewer> Render(string original, string modified, DiffViewMode mode)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<UISettings>()))
                .ReturnsAsync((string _, UISettings fallback) => fallback);
        Services.AddSingleton(settings.Object);

        var viewer = Render<DiffViewer>(p => p
            .Add(c => c.OriginalContent, original)
            .Add(c => c.ModifiedContent, modified)
            .Add(c => c.FileName, "M.mo")
            .Add(c => c.ViewMode, mode));
        viewer.WaitForState(() => !viewer.Instance.IsPreparing);
        return viewer;
    }

    private static void Settle(IRenderedComponent<DiffViewer> viewer) =>
        viewer.WaitForState(() => !viewer.Instance.IsPreparing);

    [Fact]
    public void ReRenderingWithTheSameContentDoesNotDiffItAgain()
    {
        var viewer = Render(Original, Modified, DiffViewMode.Unified);
        Assert.Equal(1, viewer.Instance.Preparations);

        // What the page does on every findings batch: render again with nothing changed. New string
        // instances with the same text, because that is what the page hands over after a reload.
        for (var i = 0; i < 5; i++)
        {
            viewer.Render(p => p
                .Add(c => c.OriginalContent, new string(Original.AsSpan()))
                .Add(c => c.ModifiedContent, new string(Modified.AsSpan())));
            Settle(viewer);
        }

        Assert.Equal(1, viewer.Instance.Preparations);
        Assert.Contains("Real", viewer.Markup);
    }

    [Fact]
    public void ChangingTheViewModeLaysOutAgainWithoutDiffingAgain()
    {
        var viewer = Render(Original, Modified, DiffViewMode.Unified);

        viewer.Render(p => p.Add(c => c.ViewMode, DiffViewMode.SideBySideFull));
        Settle(viewer);

        Assert.Equal(1, viewer.Instance.Preparations);
        Assert.Equal(2, viewer.FindAll(".diff-pane-content").Count);
    }

    [Fact]
    public void ChangedContentIsDiffedAgain()
    {
        // The control: the memo is keyed on the text, so a different text is a different diff.
        var viewer = Render(Original, Modified, DiffViewMode.Unified);

        viewer.Render(p => p.Add(c => c.ModifiedContent, "model M\n  Real z;\nend M;\n"));
        Settle(viewer);

        Assert.Equal(2, viewer.Instance.Preparations);
        Assert.Contains("z", viewer.Markup);
        Assert.DoesNotContain(">y<", viewer.Markup);
    }

    [Fact]
    public void PreparingIsStaticAndCarriesTheInputsItWasMadeFrom()
    {
        // It runs on the pool, so everything it needs comes in and everything it makes goes out.
        var diff = DiffViewer.Prepare(Original, Modified, isModelica: true);

        Assert.Same(Original, diff.Original);
        Assert.Same(Modified, diff.Modified);
        Assert.Null(diff.Error);
        Assert.Equal(4, diff.OriginalHtml.Length);
        Assert.Contains(diff.Ops, op => op.Type == DiffViewer.DiffOpType.Insert);
    }
}
