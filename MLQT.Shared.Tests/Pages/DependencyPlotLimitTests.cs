using MLQT.Shared.Pages;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// B199 — above a size where the plot shows nothing, it is not drawn.
///
/// <para><b>The threshold is about legibility, not speed.</b> Measured in a real browser, generating
/// the plot costs 377ms at 250 nodes, 539ms at 1,000 and 2,149ms at 2,000 for a force-directed
/// layout — noticeable at the top end, and not what makes it useless. In a 400px panel five hundred
/// nodes are a couple of pixels each, so past that the cost buys a picture nobody can read. The list
/// beneath it answers the same question and stays legible at any size.</para>
///
/// <para><b>What these do not cover, stated plainly.</b> The markup that calls this is one line in
/// <c>Dependencies.razor</c>, and nothing here fails if it is deleted. A rendered test was written
/// and abandoned: the page runs its analysis twice during initialisation, asynchronously, so an
/// assertion against the rendered markup is a race rather than a test. That is a real gap, not a
/// decision that the wiring does not matter.</para>
/// </summary>
public class DependencyPlotLimitTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(499)]
    [InlineData(500)]
    public void UpToTheLimitThePlotIsDrawn(int nodes)
    {
        // Inclusive at the boundary: 500 is the largest graph still worth plotting, not the first
        // one refused.
        Assert.True(Dependencies.ShouldPlot(nodes, askedForItAnyway: false));
    }

    [Theory]
    [InlineData(501)]
    [InlineData(1000)]
    [InlineData(40000)]
    public void PastTheLimitItIsNot(int nodes)
    {
        Assert.False(Dependencies.ShouldPlot(nodes, askedForItAnyway: false));
    }

    [Theory]
    [InlineData(501)]
    [InlineData(40000)]
    public void AskingForItAnywayOverridesTheLimit(int nodes)
    {
        // Nothing is taken away — the plot stops being the default at a size where it shows nothing,
        // and a user who wants it anyway can still have it.
        Assert.True(Dependencies.ShouldPlot(nodes, askedForItAnyway: true));
    }

    [Fact]
    public void TheLimitIsTheMeasuredOne()
    {
        // Pinned so that changing it is a decision rather than a typo. 500 was chosen from a
        // measurement over a real browser and a judgement about a 400px panel; moving it should mean
        // re-taking one or the other.
        Assert.Equal(500, Dependencies.PlotNodeLimit);
    }
}
