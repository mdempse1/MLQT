using MLQT.Shared.Layout;
using Xunit;

namespace MLQT.Shared.Tests.Layout;

/// <summary>
/// The combined deferred pass says what it is doing (B257).
/// </summary>
public class DeferredDependencyStepTests
{
    [Fact]
    public void TheCombinedPass_NamesStyleCheckingAtBothEnds()
    {
        // The report was a faster path read as a slower one: the log's end said "dependency
        // analysis" for a pass that was also checking style, so its duration was charged to the
        // wrong half. Whatever the pass is called, it is called it at start and at finish.
        var step = DeferredDependencyStep.For(combinedWithStyleChecking: true);

        Assert.Contains("style checking", step.ProcessName);
        Assert.Contains("checking style", step.Starting);
        Assert.Contains("style checking complete", step.Finished);
    }

    [Fact]
    public void DependencyAnalysisAlone_DoesNotClaimStyleChecking()
    {
        var step = DeferredDependencyStep.For(combinedWithStyleChecking: false);

        Assert.DoesNotContain("style", step.ProcessName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style", step.Starting, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style", step.Finished, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheTwoPasses_AreNamedDifferentlyInTheLog()
    {
        // The log is the evidence for every performance question in this repository, and a series
        // built from it has to be able to tell the two passes apart.
        Assert.NotEqual(
            DeferredDependencyStep.For(true).ProcessName,
            DeferredDependencyStep.For(false).ProcessName);
    }
}
