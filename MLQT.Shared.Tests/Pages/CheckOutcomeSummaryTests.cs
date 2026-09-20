using MLQT.Shared.Pages;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// B170 — what an external tool check says when it finishes.
///
/// <para><b>It used to say nothing.</b> A check that passed produced no window, no dialog and no
/// finding, so the only evidence an OpenModelica check had run was that the button had been pressed
/// — and the only evidence for Dymola was that Dymola's own window appeared, which made the answer
/// depend on a vendor window MLQT does not control.</para>
///
/// <para>The sentence is what the user reads first, so it is tested on its own. The failures
/// underneath it are the tool's own words, quoted rather than paraphrased: a dialog that says "check
/// failed" sends the user to the tool to find out why, which is the thing running it was meant to
/// save.</para>
/// </summary>
public class CheckOutcomeSummaryTests
{
    [Fact]
    public void ACleanCheckSaysSo()
    {
        // The case that produced nothing at all before, and the most common one.
        Assert.Equal("Dymola checked 1 class with no problems reported.",
            CodeReview.CheckOutcomeSummary("Dymola", passed: 1, failed: 0, cancelled: false));
    }

    [Fact]
    public void ACleanPackageCheckCountsThem()
    {
        Assert.Equal("OpenModelica checked 27 classes with no problems reported.",
            CodeReview.CheckOutcomeSummary("OpenModelica", passed: 27, failed: 0, cancelled: false));
    }

    [Fact]
    public void OneFailureOutOfOneDoesNotSayOneOfOne()
    {
        // "reported problems with 1 of 1 class" is technically true and reads like a bug.
        Assert.Equal("Dymola reported a problem with it.",
            CodeReview.CheckOutcomeSummary("Dymola", passed: 0, failed: 1, cancelled: false));
    }

    [Fact]
    public void EverythingFailingSaysSoPlainly()
    {
        Assert.Equal("Dymola reported a problem with all 12.",
            CodeReview.CheckOutcomeSummary("Dymola", passed: 0, failed: 12, cancelled: false));
    }

    [Fact]
    public void AMixtureNamesBothNumbers()
    {
        // The number that matters is how many failed; the total is what makes it proportionate.
        Assert.Equal("OpenModelica reported problems with 3 of 27 classes.",
            CodeReview.CheckOutcomeSummary("OpenModelica", passed: 24, failed: 3, cancelled: false));
    }

    [Fact]
    public void StoppingEarlySaysHowFarItGot()
    {
        // Cancelling is not a clean result, and must not read as one — the classes after the stop
        // were never looked at.
        Assert.Equal("Dymola check stopped after 5 classes.",
            CodeReview.CheckOutcomeSummary("Dymola", passed: 4, failed: 1, cancelled: true));
    }

    [Fact]
    public void CheckingNothingIsItsOwnAnswer()
    {
        // Reachable when a package holds no checkable classes. "checked 0 classes with no problems
        // reported" would be a pass the tool never gave.
        Assert.Equal("Dymola checked nothing.",
            CodeReview.CheckOutcomeSummary("Dymola", passed: 0, failed: 0, cancelled: false));
    }

    [Fact]
    public void TheToolIsAlwaysNamed()
    {
        // Two tools can be configured at once and the buttons sit next to each other, so the dialog
        // has to say which one answered.
        foreach (var tool in new[] { "Dymola", "OpenModelica" })
            foreach (var (passed, failed, cancelled) in new[]
                     { (1, 0, false), (0, 1, false), (2, 2, false), (1, 0, true), (0, 0, false) })
            {
                Assert.StartsWith(tool, CodeReview.CheckOutcomeSummary(tool, passed, failed, cancelled));
            }
    }
}
