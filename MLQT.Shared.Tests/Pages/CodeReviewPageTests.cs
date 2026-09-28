using Bunit;
using MLQT.Shared.Components;
using MLQT.Shared.Pages;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// The Code Review page rendered, driven the way the user drives it (B377): select a class, switch
/// to the diff, move on while it loads.
/// </summary>
public class CodeReviewPageTests : CodeReviewTestBase
{
    private const string File = "Lib/package.mo";

    private const string Head = """
        package Lib "lib"
          model A "a"
            Real a = 1;
          end A;
          model B "b"
            Real b = 1;
          end B;
        end Lib;
        """;

    private const string WorkingCopy = """
        package Lib "lib"
          model A "a"
            Real a = 2;
          end A;
          model B "b"
            Real b = 2;
          end B;
        end Lib;
        """;

    private IRenderedComponent<CodeReview> PageOverTwoModifiedClasses()
    {
        LoadFile(File, WorkingCopy);
        SetHead(File, Head);
        return RenderPage();
    }

    // ---------------------------------------------------------------- the harness itself

    [Fact]
    public void SelectingAClassShowsItsCode()
    {
        var page = PageOverTwoModifiedClasses();

        Select(page, "Lib.B");

        Eventually(page, () =>
        {
            // The lines carry the classifier's tags; without them they are the class's own text.
            var text = System.Text.RegularExpressions.Regex.Replace(
                string.Join("\n", page.FindComponent<CodeViewer>().Instance.Lines), "<[^>]+>", "");
            Assert.StartsWith("model B", text);
            Assert.Contains("Real b = 2;", text);
            Assert.DoesNotContain("Real a", text);
        });
    }

    [Fact]
    public void AClassInAnUnmodifiedFileOffersNoDiff()
    {
        LoadFile(File, WorkingCopy);
        var page = RenderPage();

        Select(page, "Lib.A");

        // The positive control is every other test in this class: the buttons do come on once the
        // repository says the file is modified. Here it never says so.
        Eventually(page, () => Assert.Equal(1, Repositories.Invocations.Count(i => i.Method.Name == "GetWorkingCopyChanges")));
        Eventually(page, () => Assert.True(UnifiedDiffButton(page).HasAttribute("disabled")));
    }

    [Fact]
    public void TheDiffOfAModifiedClassIsItsOwn()
    {
        var page = PageOverTwoModifiedClasses();
        Select(page, "Lib.A");
        WaitForDiffAvailable(page);

        UnifiedDiffButton(page).Click();

        var diff = WaitForDiff(page);
        Assert.StartsWith("model A", diff.Instance.OriginalContent);
        Assert.Contains("Real a = 1;", diff.Instance.OriginalContent);
        Assert.Contains("Real a = 2;", diff.Instance.ModifiedContent);
        Assert.Single(diff.FindAll(".diff-line-added"));
        Assert.Single(diff.FindAll(".diff-line-removed"));
    }

    // ---------------------------------------------------------------- B344, the click path

    /// <summary>
    /// B344's mid-load half, which was left for a check by hand. A's diff is still loading when the
    /// user selects B; B's load finishes first, and A's lands after it. The page used to write A's
    /// HEAD over B's - and, having found the diff already set, never load B's again.
    /// </summary>
    [Fact]
    public void ALoadOvertakenBySelectingAnotherClassDoesNotLandOnIt()
    {
        var page = PageOverTwoModifiedClasses();
        Select(page, "Lib.A");
        WaitForDiffAvailable(page);

        HoldHeadReads();
        UnifiedDiffButton(page).Click();
        WaitForHeldReads(1);   // A's load is at the gate

        Select(page, "Lib.B");
        WaitForHeldReads(2);   // ...and so is B's

        ReleaseHeadRead(1);                                         // B's lands first
        Eventually(page, () =>
            Assert.Contains("Real b = 2;", ShownDiff(page).Instance.ModifiedContent));

        ReleaseHeadRead(0);                                         // then A's, too late
        StopHoldingHeadReads();

        // Both loads have come back to the dispatcher, A's last - whatever it then did.
        Eventually(page, () => Assert.Equal(2, page.Instance.DiffLoadsFinished));

        var diff = WaitForDiff(page);
        Assert.StartsWith("model B", diff.Instance.OriginalContent);
        Assert.Contains("Real b = 1;", diff.Instance.OriginalContent);
        Assert.Contains("Real b = 2;", diff.Instance.ModifiedContent);
        Assert.DoesNotContain("Real a", diff.Instance.OriginalContent + diff.Instance.ModifiedContent);
        Assert.Empty(page.FindAll(".mud-progress-circular"));
    }

    [Fact]
    public void AfterALoadWasOvertaken_SingleThenDiffStillShowsTheSelectedClass()
    {
        var page = PageOverTwoModifiedClasses();
        Select(page, "Lib.A");
        WaitForDiffAvailable(page);

        HoldHeadReads();
        UnifiedDiffButton(page).Click();
        WaitForHeldReads(1);
        Select(page, "Lib.B");
        WaitForHeldReads(2);
        ReleaseHeadRead(1);                                         // B's lands first...
        Eventually(page, () => Assert.Equal(1, page.Instance.DiffLoadsFinished));
        ReleaseHeadRead(0);                                         // ...and A's after it
        StopHoldingHeadReads();
        Eventually(page, () => Assert.Equal(2, page.Instance.DiffLoadsFinished));

        SingleViewButton(page).Click();
        Eventually(page, () => Assert.Empty(page.FindComponents<DiffViewer>()));
        UnifiedDiffButton(page).Click();

        var diff = WaitForDiff(page);
        Assert.Contains("Real b = 1;", diff.Instance.OriginalContent);
        Assert.Contains("Real b = 2;", diff.Instance.ModifiedContent);
        Assert.Single(diff.FindAll(".diff-line-added"));
    }
}
