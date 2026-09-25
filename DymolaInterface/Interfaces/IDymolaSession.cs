namespace DymolaInterface.Interfaces;

/// <summary>
/// A Dymola session as <see cref="DymolaInterfaceFactory"/> manages it: what it needs to decide
/// whether to reuse, wait for, start or drop one.
/// </summary>
/// <remarks>
/// <para><b>This exists so the factory can be tested without Dymola</b> (B331). It built the concrete
/// <see cref="DymolaInterface"/> itself, so the one decision that could kill the user's Dymola -
/// reuse, or dispose and start again - was reachable only with a live tool, which no automated run
/// has, and it was wrong. Kept apart from <see cref="IDymolaInterface"/>, which carries only what the
/// checking service calls.</para>
/// </remarks>
public interface IDymolaSession : IDymolaInterface, IDisposable
{
    /// <summary>The per-command time limit; see <see cref="DymolaInterface.CommandTimeout"/>.</summary>
    TimeSpan CommandTimeout { get; set; }

    /// <summary>Whether commands are being held back because Dymola did not answer, or was told to.</summary>
    bool IsOfflineMode();

    /// <summary>Holds commands back (<c>true</c>), or sends them and lets them wait (<c>false</c>).</summary>
    void SetOfflineMode(bool enable);

    /// <summary>
    /// Starts Dymola with its server on this session's port, or - when the process this session
    /// started is still coming up - waits for that one to answer instead of starting another.
    /// </summary>
    Task StartDymolaProcessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives up ownership of the Dymola process without ending it, so disposing the session leaves
    /// the user's Dymola window alone.
    /// </summary>
    void Detach();
}
