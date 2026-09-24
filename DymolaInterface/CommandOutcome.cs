namespace DymolaInterface;

/// <summary>
/// What became of the last command sent through a <see cref="DymolaInterface"/> (B262).
/// </summary>
/// <remarks>
/// <para>Every command answers <c>false</c>, <c>null</c> or an empty string when it does not get a
/// result, whatever the reason — which is the right shape for a scripting wrapper and the wrong one
/// for a caller deciding what to do next. A check that <see cref="TimedOut"/> has left Dymola still
/// working on it, deaf to everything else until it finishes, so asking for its log waits out a
/// second time limit; one that was <see cref="Cancelled"/> is the user's decision, not a failure.
/// Neither is the model's fault, and a caller that cannot tell them from <see cref="Answered"/> with
/// <c>false</c> reports a model as broken for having been slow.</para>
/// </remarks>
public enum CommandOutcome
{
    /// <summary>Dymola replied. Whether the reply was <c>true</c> is the command's own answer.</summary>
    Answered,

    /// <summary>Not sent: the interface is offline and Dymola did not answer the probe, or the caller
    /// set offline mode.</summary>
    Offline,

    /// <summary>Sent, and not answered within the command's time limit. Dymola carries on with it.</summary>
    TimedOut,

    /// <summary>Given up on because the caller's cancellation token fired, before or after sending.</summary>
    Cancelled,

    /// <summary>Anything else: a transport error, an unreadable reply, or Dymola reporting an error.</summary>
    Failed,
}
