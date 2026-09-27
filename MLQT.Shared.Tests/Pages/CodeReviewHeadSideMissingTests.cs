using Bunit;
using MLQT.Shared.Components;
using MLQT.Shared.Pages;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// B410, on the page: a class the HEAD side cannot find is said to be missing, not diffed against
/// the whole file at HEAD.
/// </summary>
/// <remarks>
/// Found checking B344 by hand against a 7.5 MB single-file library: the class's id had detached
/// (B409), so neither its full name nor its short name was found at HEAD, and the whole 157,852-line
/// file became the HEAD side of a ten-line class. The page said "Comparing…" and then showed
/// Blazor's unhandled-error banner.
/// </remarks>
public class CodeReviewHeadSideMissingTests : CodeReviewTestBase
{
    private const string File = "Lib/package.mo";

    private const string WorkingCopy = """
        package Lib "lib"
          model Renamed "was Original"
            Real x = 2;
          end Renamed;
        end Lib;
        """;

    // A HEAD file that is long, so that substituting it for the class would be conspicuous - every
    // one of these lines would be a removed row.
    private static string HeadWithoutTheClass() =>
        "package Lib \"lib\"\n  model Original \"o\"\n"
        + string.Join('\n', Enumerable.Range(0, 400).Select(i => $"    Real x{i} = {i};"))
        + "\n  end Original;\nend Lib;\n";

    [Theory]
    [InlineData(DiffViewMode.Unified)]
    [InlineData(DiffViewMode.SideBySide)]
    public void AClassHeadDoesNotHaveIsSaidToBeMissing(DiffViewMode mode)
    {
        LoadFile(File, WorkingCopy);
        SetHead(File, HeadWithoutTheClass());
        var page = RenderPage();

        Select(page, "Lib.Renamed");
        WaitForDiffAvailable(page);
        (mode == DiffViewMode.Unified ? UnifiedDiffButton(page) : SideBySideDiffButton(page)).Click();

        Eventually(page, () =>
        {
            var alert = page.Find(".mlqt-diff-unavailable");
            Assert.Contains("Lib.Renamed could not be found in Lib/package.mo at HEAD", alert.TextContent);
        });
        Assert.Empty(page.FindComponents<DiffViewer>());
        Assert.Empty(page.FindAll(".diff-line-removed"));
    }

    [Fact]
    public void AClassHeadDoesHaveIsStillDiffed()
    {
        // The control: the same page, the same file, and the class at HEAD under its own name.
        LoadFile(File, WorkingCopy);
        SetHead(File, WorkingCopy.Replace("Real x = 2;", "Real x = 1;"));
        var page = RenderPage();

        Select(page, "Lib.Renamed");
        WaitForDiffAvailable(page);
        UnifiedDiffButton(page).Click();

        var diff = WaitForDiff(page);
        Assert.Empty(page.FindAll(".mlqt-diff-unavailable"));
        Assert.Single(diff.FindAll(".diff-line-removed"));
    }

    [Fact]
    public void SelectingAClassHeadDoesHaveAfterOneItDoesNotShowsItsDiff()
    {
        // The message belongs to the class it was about, and must not follow the user to the next.
        LoadFile(File, """
            package Lib "lib"
              model Renamed "was Original"
                Real x = 2;
              end Renamed;
              model Kept "k"
                Real k = 2;
              end Kept;
            end Lib;
            """);
        SetHead(File, """
            package Lib "lib"
              model Original "o"
                Real x = 1;
              end Original;
              model Kept "k"
                Real k = 1;
              end Kept;
            end Lib;
            """);
        var page = RenderPage();

        Select(page, "Lib.Renamed");
        WaitForDiffAvailable(page);
        UnifiedDiffButton(page).Click();
        Eventually(page, () => page.Find(".mlqt-diff-unavailable"));

        Select(page, "Lib.Kept");

        Eventually(page, () =>
        {
            Assert.Empty(page.FindAll(".mlqt-diff-unavailable"));
            Assert.Contains("Real k = 1;", ShownDiff(page).Instance.OriginalContent);
        });
    }
}
