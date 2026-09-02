using MLQT.Services.Helpers;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// The trend chart's y-axis window. The behaviour worth pinning is that it fits the data rather than
/// starting at zero — a hundred findings coming off a library of twenty-five thousand is the movement
/// the chart exists to show, and a zero-based axis renders it as a flat line.
/// </summary>
public class TrendAxisTests
{
    [Fact]
    public void ATightRangeHighAboveZero_GetsATightWindow()
    {
        var (min, max, step) = TrendAxis.Window(new double[] { 25406, 25380, 25304 }, countMode: true);

        Assert.Equal(25300, min);
        Assert.Equal(25450, max);
        Assert.Equal(50, step);
    }

    [Fact]
    public void TheWindowEnclosesEveryValue()
    {
        var values = new double[] { 25406, 25380, 25304 };

        var (min, max, _) = TrendAxis.Window(values, countMode: true);

        Assert.True(min <= values.Min());
        Assert.True(max >= values.Max());
    }

    [Fact]
    public void BoundsAreWholeStepsApart_SoGridlinesLandOnRoundNumbers()
    {
        var (min, max, step) = TrendAxis.Window(new double[] { 25406, 25304 }, countMode: true);

        var intervals = (max - min) / step;
        Assert.Equal(Math.Round(intervals), intervals, 6);
        Assert.InRange(intervals, 1, 8);
        Assert.Equal(0, min % step, 6);
    }

    [Fact]
    public void Percentages_GetASubUnitStep_WhenTheyBarelyMove()
    {
        // 78.4 → 79.1 is real progress on a documentation push; on a 0–100 axis it is nothing.
        var (min, max, step) = TrendAxis.Window(new double[] { 78.4, 78.9, 79.1 }, countMode: false);

        Assert.True(min >= 78);
        Assert.True(max <= 80);
        Assert.True(step <= 0.5);
    }

    [Fact]
    public void Percentages_NeverExceedAHundred()
    {
        var (min, max, _) = TrendAxis.Window(new double[] { 99.4, 99.8, 100 }, countMode: false);

        Assert.Equal(100, max);
        Assert.True(min < max);
    }

    [Fact]
    public void NothingGoesBelowZero()
    {
        var (min, _, _) = TrendAxis.Window(new double[] { 0, 1, 2 }, countMode: true);

        Assert.Equal(0, min);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IdenticalValues_StillGetAWindowWithHeight(bool countMode)
    {
        // A metric that has not moved must not collapse the axis — the caller divides by its height.
        var (min, max, step) = TrendAxis.Window(new double[] { 42, 42, 42 }, countMode);

        Assert.True(max > min);
        Assert.True(step > 0);
        Assert.True(min <= 42 && max >= 42);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoValues_GetADefaultWindow(bool countMode)
    {
        // Every series switched off in the legend. The gridlines still have to be labelled something.
        var (min, max, step) = TrendAxis.Window(Array.Empty<double>(), countMode);

        Assert.True(max > min);
        Assert.True(step > 0);
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
