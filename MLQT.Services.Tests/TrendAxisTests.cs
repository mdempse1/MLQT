using MLQT.Services.Helpers;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// The gridline interval handed to MudBlazor's chart. The behaviour worth pinning is that it comes
/// from the data: a hundred findings coming off a library of twenty-five thousand is the movement the
/// chart exists to show, and the chart's default interval of 20 renders it as a flat line between two
/// labels.
/// </summary>
public class TrendAxisTests
{
    [Fact]
    public void ATightRangeHighAboveZero_GetsAnIntervalThatFitsIt()
    {
        // 25,304–25,406 renders as 25,300 / 25,350 / 25,400 / 25,450 — the movement is visible.
        Assert.Equal(50, TrendAxis.TickInterval(new double[] { 25406, 25380, 25304 }));
    }

    [Fact]
    public void PercentagesThatBarelyMove_GetTheFinestIntervalTheChartAccepts()
    {
        // 78.4 → 79.1 is real progress on a documentation push. MudBlazor's default interval of 20
        // draws it on a 60/80 axis with every point in between; 1 gives 78 / 79 / 80.
        Assert.Equal(1, TrendAxis.TickInterval(new double[] { 78.4, 78.9, 79.1 }));
    }

    [Fact]
    public void AFullRangeOfPercentages_GetsACoarserInterval()
    {
        // 4–99 spans the axis, so half-unit gridlines would be noise: 0 / 50 / 100 is the reading.
        Assert.Equal(50, TrendAxis.TickInterval(new double[] { 4, 52, 99 }));
    }

    [Fact]
    public void IdenticalValues_GetAUsableInterval()
    {
        // A metric that has not moved still has to be drawn against something.
        Assert.Equal(1, TrendAxis.TickInterval(new double[] { 42, 42, 42 }));
    }

    [Fact]
    public void NoValues_GetAUsableInterval()
    {
        // Every series switched off in the legend.
        Assert.Equal(1, TrendAxis.TickInterval(Array.Empty<double>()));
    }

    [Fact]
    public void TheIntervalIsAlwaysARoundNumber_SoDoublingItStaysRound()
    {
        // MudBlazor doubles the interval when the data needs more lines than it allows. Starting from
        // a round number keeps the labels round; starting from 1 gives axes labelled in 16s.
        foreach (var interval in new[]
                 {
                     TrendAxis.TickInterval(new double[] { 0, 970 }),
                     TrendAxis.TickInterval(new double[] { 25304, 25406 }),
                     TrendAxis.TickInterval(new double[] { 1200, 34329 }),
                 })
        {
            var mantissa = interval / Math.Pow(10, Math.Floor(Math.Log10(interval)));
            Assert.Contains(Math.Round(mantissa), new double[] { 1, 2, 5 });
        }
    }

    [Theory]
    [InlineData(0.07, 0.1)]
    [InlineData(1, 1)]
    [InlineData(1.5, 2)]
    [InlineData(3, 5)]
    [InlineData(6, 10)]
    [InlineData(25.5, 50)]
    public void NiceStep_RoundsUpToOneTwoOrFive(double raw, double expected)
    {
        Assert.Equal(expected, TrendAxis.NiceStep(raw), 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public void NiceStep_HasAFallback_ForAStepThatCannotBeComputed(double raw)
    {
        Assert.Equal(1, TrendAxis.NiceStep(raw));
    }
}
