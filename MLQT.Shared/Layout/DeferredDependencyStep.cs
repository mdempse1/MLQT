namespace MLQT.Shared.Layout;

/// <summary>
/// What the deferred dependency step calls itself — in the log and to the user — for the pass that is
/// actually running.
/// </summary>
/// <remarks>
/// <para><b>Why this exists (B257).</b> The deferred steps can run combined: style checking rides on
/// dependency analysis, checking each class while its parse tree is still in hand. That path is the
/// faster one — 321s against 353s end to end on the same library — and it reported itself as the
/// slower. It logged its start as the combined pass, its end as "dependency analysis", and then a
/// "style checking" completion with no start; its closing message said only that dependency analysis
/// was complete. Read the way anyone reads a log, dependencies took four minutes and style checking
/// took none, and a faster path was reported as a regression.</para>
///
/// <para>One value for both ends of the log, so the name a pass starts under is the name it finishes
/// under, and the messages cannot say less than the pass did.</para>
/// </remarks>
internal sealed record DeferredDependencyStep(string ProcessName, string Starting, string Finished)
{
    public static DeferredDependencyStep For(bool combinedWithStyleChecking) => combinedWithStyleChecking
        ? new("Running deferred dependency analysis + style checking (combined)",
              "Analysing dependencies and checking style...",
              "Dependency analysis and style checking complete.")
        : new("Running deferred dependency analysis",
              "Analysing dependencies...",
              "Dependency analysis complete.");
}
