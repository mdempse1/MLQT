using MLQT.Services.DataTypes;

namespace MLQT.Services.Helpers;

/// <summary>
/// What an external tool running out of time looks like to the user — one wording for both tools
/// (B263).
/// </summary>
/// <remarks>
/// <para>A check that ran out of time is not a failed check. The model may be perfectly sound and
/// merely large, and reporting it as "Dymola Check Failed" with whatever the log said last points the
/// user at their model instead of at the setting that decides how long to wait. So the result says
/// what happened, names the setting, and says what state the tool has been left in, which is the part
/// that differs: Dymola cannot be interrupted and is still busy, while omc's session has been closed.</para>
///
/// <para>Shared because the two checking services are siblings, and the sibling shape is how a
/// question answered for one tool came to be left unanswered for the other more than once (B170).</para>
/// </remarks>
public static class ToolTimeLimit
{
    /// <summary>Where the limit is set, as the settings dialog labels it.</summary>
    public const string SettingName = "Check time limit";

    /// <summary>The limit as a person reads it: "5 min", "90 s", "no limit".</summary>
    public static string Describe(TimeSpan limit)
    {
        if (limit == Timeout.InfiniteTimeSpan)
            return "no limit";

        if (limit >= TimeSpan.FromMinutes(1) && limit.Seconds == 0 && limit.Milliseconds == 0)
            return $"{(int)limit.TotalMinutes} min";

        return limit.TotalSeconds >= 1
            ? $"{limit.TotalSeconds:0.#} s"
            : $"{limit.TotalMilliseconds:0} ms";
    }

    /// <summary>
    /// The advice both tools give: where to raise the limit.
    /// </summary>
    public static string Advice =>
        $"If the model is simply large, raise \"{SettingName}\" for this tool in Settings → External Tools " +
        "(0 means no limit).";

    /// <summary>The result for a class whose check ran out of time.</summary>
    /// <param name="consequence">What state the tool has been left in — the one part that differs
    /// between the two.</param>
    public static ModelCheckResult CheckTimedOut(
        string tool, string modelId, TimeSpan limit, string consequence) => new()
    {
        ModelId = modelId,
        Success = false,
        TimedOut = true,
        Summary = $"{tool} ran out of time",
        ErrorMessage =
            $"{tool} did not finish checking {modelId} within {Describe(limit)}. {consequence} {Advice}",
    };

    /// <summary>The message for a library that could not be opened in time.</summary>
    public static string LoadTimedOut(string tool, string file, TimeSpan limit, string consequence) =>
        $"{tool} did not finish opening {Path.GetFileName(file)} within {Describe(limit)}. {consequence} {Advice}";
}
