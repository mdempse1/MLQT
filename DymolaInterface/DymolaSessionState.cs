namespace DymolaInterface;

/// <summary>
/// What a Dymola session is doing, as far as one probe can tell (B331).
/// </summary>
/// <remarks>
/// <para><b>Busy and gone look alike to a ping and must never be treated alike.</b> Dymola's JSON-RPC
/// server is single-threaded and answers nothing while it runs a command, so a Dymola still working
/// on a check that timed out or was stopped fails a two-second ping exactly as a closed one does. The
/// factory used to take either for dead and dispose the session, which killed the Dymola window MLQT
/// had started - with whatever the user had open in it - or, against a Dymola MLQT had only attached
/// to, started a second one on a port that was taken. What separates them is the TCP connect: a busy
/// Dymola's listening socket still accepts, a closed one refuses.</para>
/// </remarks>
public enum DymolaSessionState
{
    /// <summary>Dymola answered the probe.</summary>
    Answering,

    /// <summary>Dymola accepted the connection and did not answer: it is working on a command, ours
    /// or somebody else's, and will answer when it has finished. Wait for it; never replace it.</summary>
    Busy,

    /// <summary>The Dymola this session started is still running but not yet listening.</summary>
    Starting,

    /// <summary>Nothing is there: the process this session started has exited, nothing is listening
    /// on the port, the session was stopped or taken offline, or it has been disposed.</summary>
    Gone,
}
