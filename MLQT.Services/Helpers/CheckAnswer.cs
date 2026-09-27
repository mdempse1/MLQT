using MLQT.Services.DataTypes;

namespace MLQT.Services.Helpers;

/// <summary>
/// What a tool said when asked to check one class: its verdict, or why there is none.
/// </summary>
/// <remarks>
/// The two tools say "no verdict" differently - Dymola answers <c>false</c> and leaves the reason in
/// <c>LastOutcome</c>, omc throws and closes its session - so each service turns its own way of saying
/// it into one of these, and <see cref="ModelCheckingServiceBase{TSession}"/> does everything else the
/// same way for both (B398). Only a <see cref="Verdict"/> is the model's answer; the others are not
/// worth a log read, because the session that would answer it is busy or gone (B263).
/// </remarks>
internal readonly record struct CheckAnswer(bool Verdict, ModelCheckResult? Instead, bool Cancelled)
{
    /// <summary>The tool checked the class: true when it passed.</summary>
    public static CheckAnswer Checked(bool passed) => new(passed, null, false);

    /// <summary>The caller cancelled the check - not a result.</summary>
    public static CheckAnswer WasCancelled => new(false, null, true);

    /// <summary>
    /// The tool gave no verdict - it ran out of time, or went away - and this result says so.
    /// </summary>
    public static CheckAnswer NoVerdict(ModelCheckResult result) => new(false, result, false);
}
