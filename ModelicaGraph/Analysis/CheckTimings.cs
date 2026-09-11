using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Where the time in a check run went, accumulated as it goes.
/// </summary>
/// <remarks>
/// <para>Backlog B128. The style check on one library went from a ~105 s median in late August to
/// ~300 s, on the same repository, and the growth is explained — phase 5 added suppression reads,
/// phase 6 added the whole-graph analyses and coverage measurement, more rules are enabled, and the
/// project loads far more libraries. Explained is not the same as understood: **nothing reported
/// where the time actually goes**, so any attempt to reduce it would have started by guessing.</para>
///
/// <para>One instance per run, handed through <c>StyleCheckContext</c> and
/// <see cref="GraphAnalysisContext"/> beside the other once-per-run inputs, rather than a static
/// accumulator — the per-class checks run in parallel and the test suites run in parallel with each
/// other, and shared mutable statics are how two runs come to report each other's numbers.</para>
///
/// <para><b>Cheap enough to leave on.</b> A phase costs two <see cref="Stopwatch.GetTimestamp"/>
/// reads (about 20 ns each) and one interlocked add. Measured over a 39,860-class graph the whole
/// apparatus is under a tenth of a second against a run of several minutes, so there is no switch to
/// forget to turn on, and the numbers are there on the day somebody asks rather than after a rebuild.
/// Callers that do not want them pass null.</para>
/// </remarks>
public sealed class CheckTimings
{
    /// <summary>The phases worth separating. Names are stable: they are read in logs and reports.</summary>
    public static class Phase
    {
        /// <summary>Turning a class's source into a parse tree.</summary>
        public const string Parse = "parse";

        /// <summary>One per-class style rule. Suffixed with the visitor's name.</summary>
        /// <remarks>
        /// Per rule rather than one figure for all of them, because the first measurement said the
        /// visitors were 86% of a check — true, and naming no rule to look at.
        /// </remarks>
        public const string RulePrefix = "rule:";

        /// <summary>
        /// What the per-class check does after the visitors: stamping configured severities, applying
        /// <c>__MLQT</c> suppression and collapsing duplicate findings.
        /// </summary>
        /// <remarks>
        /// Separate from the rules because it scales with a different thing — how much a library has
        /// wrong, rather than how many rules are enabled.
        /// </remarks>
        public const string RulesTail = "rules:post";

        /// <summary>Measuring a class's coverage contribution while its tree is still in hand.</summary>
        public const string Coverage = "coverage";

        /// <summary>
        /// Resolving one class's inherited element names, which the spell rules need so that prose
        /// naming an inherited member is not reported as a misspelling.
        /// </summary>
        /// <remarks>
        /// Shared and lazy: computed once per class and cached for the rest of the run. Timed where
        /// it is computed rather than where it is asked for, because otherwise the whole cost lands
        /// on whichever rule reaches a class first and reads as that rule being slow.
        /// </remarks>
        public const string InheritedNames = "inherited-names";

        /// <summary>One whole-graph analysis. Suffixed with the analyzer's name.</summary>
        public const string AnalysisPrefix = "analysis:";
    }

    private sealed class Accumulator
    {
        public long Ticks;
        public long Count;
    }

    private readonly ConcurrentDictionary<string, Accumulator> _phases = new(StringComparer.Ordinal);

    /// <summary>
    /// Time this thread has spent in phases that are nested inside another one.
    /// </summary>
    /// <remarks>
    /// <para>Shared work is lazy: a class's inherited element names are resolved by whichever rule
    /// asks first and cached for the rest of the run, so that cost lands *inside* one rule's
    /// measurement. Reported raw it is counted twice - the first run of this said
    /// SpellCheckDescriptions 34% and inherited-names 33%, of which one entirely contains the
    /// other.</para>
    ///
    /// <para>Thread-static, because the nesting is within a call stack and the per-class checks run
    /// one class per thread. A phase that subtracts what its children recorded reports its own time,
    /// and the column adds up to the run.</para>
    /// </remarks>
    [ThreadStatic]
    private static long _nestedTicks;

    /// <summary>This thread's running total of nested time, for a caller measuring its own.</summary>
    public static long NestedTicks => _nestedTicks;

    /// <summary>Records that <paramref name="phase"/> took <paramref name="ticks"/> once more.</summary>
    /// <param name="ticks">A difference of <see cref="Stopwatch.GetTimestamp"/> values.</param>
    public void Add(string phase, long ticks)
    {
        var accumulator = _phases.GetOrAdd(phase, static _ => new Accumulator());
        Interlocked.Add(ref accumulator.Ticks, ticks);
        Interlocked.Increment(ref accumulator.Count);
    }

    /// <summary>
    /// Times shared work that runs inside another measured phase, so the enclosing one can discount it.
    /// </summary>
    /// <remarks>
    /// Use this for anything lazy and cached - work the first caller pays for and the rest inherit.
    /// <see cref="Measure{T}"/> is for a phase that stands on its own.
    /// </remarks>
    public static T MeasureNested<T>(CheckTimings? timings, string phase, Func<T> work)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return work();
        }
        finally
        {
            var elapsed = Stopwatch.GetTimestamp() - started;
            _nestedTicks += elapsed;
            timings?.Add(phase, elapsed);
        }
    }

    /// <summary>Times <paramref name="work"/> and records it, returning what it returned.</summary>
    /// <remarks>
    /// The form most call sites want. The timestamps are read whether or not anybody is collecting,
    /// because a null check around 40 ns is not worth two code paths through the checker.
    /// </remarks>
    public static T Measure<T>(CheckTimings? timings, string phase, Func<T> work)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return work();
        }
        finally
        {
            timings?.Add(phase, Stopwatch.GetTimestamp() - started);
        }
    }

    /// <inheritdoc cref="Measure{T}"/>
    public static void Measure(CheckTimings? timings, string phase, Action work)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            work();
        }
        finally
        {
            timings?.Add(phase, Stopwatch.GetTimestamp() - started);
        }
    }

    /// <summary>What one phase cost.</summary>
    /// <param name="Phase">The phase name.</param>
    /// <param name="Elapsed">Total wall time attributed to it, summed across threads.</param>
    /// <param name="Count">How many times it ran.</param>
    public readonly record struct PhaseTiming(string Phase, TimeSpan Elapsed, long Count);

    /// <summary>Every phase recorded, slowest first.</summary>
    /// <remarks>
    /// <b>These are thread-seconds, not wall-clock seconds.</b> The per-class checks run on every
    /// core, so the parse and rule totals will exceed the run's own duration - deliberately, because
    /// the question this answers is "what is the work", and dividing by a core count that varies
    /// between machines would make two runs incomparable. The ratios are the point.
    /// </remarks>
    public IReadOnlyList<PhaseTiming> Snapshot() =>
        [.. _phases
            .Select(kv => new PhaseTiming(
                kv.Key,
                TimeSpan.FromSeconds((double) Interlocked.Read(ref kv.Value.Ticks) / Stopwatch.Frequency),
                Interlocked.Read(ref kv.Value.Count)))
            .OrderByDescending(p => p.Elapsed)
            .ThenBy(p => p.Phase, StringComparer.Ordinal)];

    /// <summary>The breakdown as lines, for a log or a console report.</summary>
    /// <returns>An empty list when nothing was recorded, so a caller can say so rather than print a heading over nothing.</returns>
    public IReadOnlyList<string> Format()
    {
        var phases = Snapshot();
        if (phases.Count == 0)
            return [];

        var total = phases.Sum(p => p.Elapsed.TotalSeconds);
        var width = phases.Max(p => p.Phase.Length);

        return
        [
            $"Check timings (thread-seconds, {total.ToString("F1", CultureInfo.InvariantCulture)}s of measured work):",
            .. phases.Select(p => string.Format(
                CultureInfo.InvariantCulture,
                "  {0}  {1,8:F1}s  {2,5:P0}  {3,9:N0} call(s)",
                p.Phase.PadRight(width),
                p.Elapsed.TotalSeconds,
                total > 0 ? p.Elapsed.TotalSeconds / total : 0,
                p.Count)),
        ];
    }
}
