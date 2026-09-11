using System.Diagnostics;
using ModelicaGraph.Analysis;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// The breakdown that says where a check run's time went (backlog B128).
/// </summary>
/// <remarks>
/// The behaviour worth pinning is not "it can add up numbers" — it is that a phase running inside
/// another is not counted twice. The first version of this reported SpellCheckDescriptions at 34% and
/// inherited-names at 33%, of which one entirely contained the other, and the conclusion drawn from
/// it (that the spell rules were the problem) was wrong.
/// </remarks>
public class CheckTimingsTests
{
    [Fact]
    public void NothingRecordedFormatsToNothing()
    {
        // A caller can then say "no timings" rather than print a heading over an empty table.
        Assert.Empty(new CheckTimings().Format());
    }

    [Fact]
    public void PhasesAreSummedAndCounted()
    {
        var timings = new CheckTimings();
        timings.Add("parse", Stopwatch.Frequency);          // one second
        timings.Add("parse", Stopwatch.Frequency / 2);      // half a second
        timings.Add("rules", Stopwatch.Frequency * 2);

        var byPhase = timings.Snapshot().ToDictionary(p => p.Phase);

        Assert.Equal(1.5, byPhase["parse"].Elapsed.TotalSeconds, precision: 2);
        Assert.Equal(2, byPhase["parse"].Count);
        Assert.Equal(2.0, byPhase["rules"].Elapsed.TotalSeconds, precision: 2);
        Assert.Equal(1, byPhase["rules"].Count);
    }

    [Fact]
    public void TheSlowestPhaseComesFirst()
    {
        // The report is read top-down by someone looking for what to fix.
        var timings = new CheckTimings();
        timings.Add("quick", Stopwatch.Frequency / 10);
        timings.Add("slow", Stopwatch.Frequency * 5);
        timings.Add("middling", Stopwatch.Frequency);

        Assert.Equal(["slow", "middling", "quick"], timings.Snapshot().Select(p => p.Phase));
    }

    [Fact]
    public void NestedWorkIsRecordedAndAlsoOfferedBackForDiscounting()
    {
        // MeasureNested is for shared lazy work: the first caller pays for it, inside its own phase.
        var timings = new CheckTimings();

        var before = CheckTimings.NestedTicks;
        var outerStarted = Stopwatch.GetTimestamp();

        var answer = CheckTimings.MeasureNested(timings, "shared", () =>
        {
            Spin(TimeSpan.FromMilliseconds(30));
            return 42;
        });

        var outerElapsed = Stopwatch.GetTimestamp() - outerStarted;
        var nested = CheckTimings.NestedTicks - before;

        Assert.Equal(42, answer);

        // It is recorded as its own phase...
        var shared = Assert.Single(timings.Snapshot());
        Assert.Equal("shared", shared.Phase);
        Assert.True(shared.Elapsed.TotalMilliseconds >= 20, $"recorded only {shared.Elapsed.TotalMilliseconds}ms");

        // ...and the enclosing phase can take it back off its own measurement, which is what stops
        // the two being added together.
        Assert.True(nested > 0, "nothing was offered back for discounting");
        Assert.True(outerElapsed - nested < outerElapsed / 2,
            "discounting the nested phase should leave the enclosing one with almost nothing");
    }

    [Fact]
    public void PlainMeasureDoesNotOfferItselfForDiscounting()
    {
        // The difference between the two methods, stated: Measure is for a phase that stands on its
        // own, and a caller that discounted it would lose the time entirely.
        var before = CheckTimings.NestedTicks;

        CheckTimings.Measure(new CheckTimings(), "standalone", () => Spin(TimeSpan.FromMilliseconds(20)));

        Assert.Equal(before, CheckTimings.NestedTicks);
    }

    [Fact]
    public void MeasuringWithNoCollectorStillRunsTheWork()
    {
        // Every call site passes null when nobody is measuring, which is most of the test suite.
        var ran = false;

        CheckTimings.Measure(null, "ignored", () => ran = true);
        var answer = CheckTimings.MeasureNested(null, "ignored", () => 7);

        Assert.True(ran);
        Assert.Equal(7, answer);
    }

    [Fact]
    public void WorkThatThrowsIsStillTimed()
    {
        // Otherwise a phase that fails disappears from the breakdown, and the run that went wrong is
        // the one whose timings matter most.
        var timings = new CheckTimings();

        Assert.Throws<InvalidOperationException>(() =>
            CheckTimings.Measure(timings, "boom", () => throw new InvalidOperationException()));

        Assert.Equal("boom", Assert.Single(timings.Snapshot()).Phase);
    }

    [Fact]
    public void TheFormattedReportNamesEachPhaseItsShareAndHowOftenItRan()
    {
        var timings = new CheckTimings();
        timings.Add("parse", Stopwatch.Frequency);
        timings.Add("rule:Something", Stopwatch.Frequency * 3);

        var lines = timings.Format();

        Assert.Contains("thread-seconds", lines[0]);
        Assert.Contains("rule:Something", lines[1]);
        Assert.Contains("75", lines[1]);        // three of the four seconds
        Assert.Contains("parse", lines[2]);
    }

    [Fact]
    public void EveryThreadsWorkIsCounted()
    {
        // The per-class checks run on every core, so the collector is written to from all of them at
        // once. Interlocked rather than a lock, for the same reason the spell checker stopped taking
        // one: this is on the hot path of every class.
        var timings = new CheckTimings();

        Parallel.For(0, 64, _ => timings.Add("concurrent", Stopwatch.Frequency));

        var phase = Assert.Single(timings.Snapshot());
        Assert.Equal(64, phase.Count);
        Assert.Equal(64, phase.Elapsed.TotalSeconds, precision: 1);
    }

    private static void Spin(TimeSpan duration)
    {
        // Not Thread.Sleep: the point is elapsed time under the timestamp source being measured, and
        // a sleep can return early or late by more than the interval being asserted on.
        var until = Stopwatch.GetTimestamp() + (long) (duration.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < until)
        {
        }
    }
}
