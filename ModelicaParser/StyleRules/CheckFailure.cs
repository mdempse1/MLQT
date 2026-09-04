using ModelicaParser.DataTypes;

namespace ModelicaParser.StyleRules;

/// <summary>
/// The report MLQT makes about itself when a check does not finish: <see cref="RuleIds.CheckFailed"/>
/// with the class it happened to, what was being attempted, and why it stopped.
///
/// <para>There is one of these because there were four, written by hand in four places — the shared
/// check session, the desktop app's background worker, the layout's dependency-analysis pass, and the
/// graph-analysis runner — two of them with the sentence copied across verbatim. Nothing held them
/// together, so the same failure read differently depending on which pass hit it, and the next site
/// to need one would have been a fifth copy. The wording matters more than most: it is the sentence
/// that tells somebody the total they are looking at is short, and it is the string a support
/// conversation starts from.</para>
/// </summary>
public static class CheckFailure
{
    /// <summary>What was being attempted, when it was the ordinary per-class check.</summary>
    public const string Checking = "Checking this class";

    /// <summary>What was being attempted, when it was dependency analysis.</summary>
    public const string Analysing = "Analysing this class";

    /// <summary>
    /// A failure report as a structured finding.
    /// </summary>
    /// <param name="modelId">The class it happened to. For a whole-graph pass, the class the failure
    /// is attributed to — a stable choice, so the fingerprint does not move between runs.</param>
    /// <param name="ex">What stopped it. Its type and message go into the text; a support request is
    /// unanswerable without them.</param>
    /// <param name="what">What was being attempted — <see cref="Checking"/> by default.</param>
    /// <param name="alsoMissing">
    /// Anything lost besides this class's findings. Dependency analysis also loses the class's edges,
    /// which changes what every graph rule can see, so that pass says so.
    /// </param>
    /// <param name="discriminator">
    /// Distinguishes several failures on one class — the name of the analysis, say. Left null for the
    /// per-class check, which fails at most once per class.
    /// </param>
    public static Finding For(
        string modelId,
        Exception ex,
        string what = Checking,
        string? alsoMissing = null,
        string? discriminator = null) =>
        new()
        {
            RuleId = RuleIds.CheckFailed,
            ModelId = modelId,
            Discriminator = discriminator,
            Message = Describe(what, ex, alsoMissing),
            Severity = RuleSeverity.Error,
        };

    /// <summary>
    /// The same report in the flat shape the desktop app's issue list consumes. It goes through
    /// <see cref="Finding.ToLogMessage"/>, so the fingerprint and rule id ride along and a re-check
    /// clears it like any other style finding — a class that fails once may well succeed next time.
    /// </summary>
    public static LogMessage Message(
        string modelId,
        Exception ex,
        string what = Checking,
        string? alsoMissing = null,
        string? discriminator = null) =>
        For(modelId, ex, what, alsoMissing, discriminator).ToLogMessage();

    private static string Describe(string what, Exception ex, string? alsoMissing)
    {
        var lost = alsoMissing is null
            ? "Its findings are missing from these results."
            : $"Its findings and {alsoMissing} are missing from these results.";

        return $"{what} failed ({ex.GetType().Name}: {ex.Message}). {lost}";
    }
}
