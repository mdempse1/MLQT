namespace OpenModelicaInterface;

/// <summary>
/// omc exited while a command was waiting for its reply (B334).
/// </summary>
/// <remarks>
/// A ZeroMQ REQ socket gives no sign that its peer has gone: a request to an omc that has exited is
/// queued and never answered. So a command in flight when omc died waited out the whole time limit
/// and was then reported as a timeout - "raise Check time limit" - or, with no limit, waited until
/// somebody pressed Stop. The wait now watches the process as well, and says what happened.
/// </remarks>
public class OpenModelicaExitedException : InvalidOperationException
{
    public OpenModelicaExitedException(string message) : base(message)
    {
    }
}
