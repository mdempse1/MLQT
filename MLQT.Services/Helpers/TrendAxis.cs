namespace MLQT.Services.Helpers;

/// <summary>
/// The y-axis window for the coverage-trend chart: where the axis starts, where it ends, and the
/// gridline step between.
///
/// <para><b>The window fits the data and is not anchored at zero.</b> An outstanding-finding count
/// that moves from 25,406 to 25,304 is a flat line on a 0–30,000 axis — the movement, which is the
/// only thing a trend exists to show, rounds away to nothing. Fitting gives 25,300–25,450 and the
/// same data becomes a visible slope. Zero-based is still one keystroke away: the chart's bounds are
/// editable, and typing 0 pins the bottom.</para>
///
/// <para>Bounds are rounded outwards to whole steps of 1, 2 or 5 × 10ⁿ so the labels read as round
/// numbers rather than as the data's own extremes.</para>
/// </summary>
public static class TrendAxis
{
    /// <summary>How many gridline intervals to aim for. The step is chosen for roughly this many;
    /// rounding the bounds outwards can yield a few more or fewer.</summary>
    private const int TargetIntervals = 4;

    /// <summary>A "nice" step ≥ <paramref name="raw"/>, rounded up to 1, 2 or 5 × 10ⁿ.</summary>
    public static double NiceStep(double raw)
    {
        if (raw <= 0 || double.IsNaN(raw) || double.IsInfinity(raw))
            return 1;

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normalized = raw / magnitude;
        var nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return nice * magnitude;
    }

    /// <summary>
    /// The window enclosing <paramref name="values"/>.
    /// </summary>
    /// <param name="countMode">True when plotting outstanding counts, false for percentages — which
    /// only changes the clamps: neither goes below zero, and a percentage stops at 100.</param>
    public static (double Min, double Max, double Step) Window(IEnumerable<double> values, bool countMode)
    {
        var list = values as IReadOnlyCollection<double> ?? values.ToList();
        if (list.Count == 0)
            return countMode ? (0, 10, 2.5) : (0, 100, 25);

        double lo = list.Min(), hi = list.Max();
        if (hi - lo < 1e-9)
        {
            // One value, or a run of identical ones: give it room rather than a window with no height.
            var pad = Math.Max(Math.Abs(hi) * 0.01, countMode ? 1 : 0.5);
            lo -= pad;
            hi += pad;
        }

        var step = NiceStep((hi - lo) / TargetIntervals);
        var min = Math.Floor(lo / step) * step;
        var max = Math.Ceiling(hi / step) * step;
        if (max - min < step)
            max = min + step;

        min = Math.Max(0, min);              // neither a percentage nor a count goes below zero
        if (!countMode)
            max = Math.Min(100, max);
        if (max <= min)
            max = min + step;                // never a zero-height window: the caller divides by it

        return (min, max, step);
    }
}
