namespace MLQT.Services.Helpers;

/// <summary>
/// How far apart the coverage-trend chart's gridlines go.
///
/// <para>The chart itself is MudBlazor's, which fits the axis to the data and asks only for a tick
/// interval. Left at its default of 20, a percentage trend from 78.4 to 79.1 is drawn on an axis
/// labelled 60 and 80 — every point in the gap between two lines, which is the same as showing
/// nothing. The interval therefore comes from the data, rounded to 1, 2 or 5 × 10ⁿ so that the labels
/// are round numbers and stay round when MudBlazor doubles the interval to fit its tick limit.</para>
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
    /// The gridline interval to give a chart plotting <paramref name="values"/>.
    ///
    /// <para>MudBlazor takes a tick <i>interval</i> and scales it up — doubling — when the data would
    /// need more lines than it allows. Doubling from a round number stays round, so the interval handed
    /// over is chosen for the data rather than left at the default 20, which is what turned a 78.4–79.1
    /// percentage trend into an axis labelled 60 and 80 with the whole series between them.</para>
    ///
    /// <para>Whole numbers only, because that is what the chart accepts: a range narrower than a couple
    /// of units gets an interval of 1 and no finer.</para>
    /// </summary>
    /// <param name="values">Every value that will be plotted, across all series.</param>
    public static int TickInterval(IEnumerable<double> values)
    {
        var list = values as IReadOnlyCollection<double> ?? values.ToList();
        if (list.Count == 0)
            return 1;

        var range = list.Max() - list.Min();
        if (range <= 0)
            return 1;

        return Math.Max(1, (int)Math.Round(NiceStep(range / TargetIntervals)));
    }
}
