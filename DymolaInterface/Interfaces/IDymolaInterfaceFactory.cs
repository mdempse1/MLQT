namespace DymolaInterface.Interfaces;

/// <summary>
/// Factory interface for creating DymolaInterface instances with configuration.
/// </summary>
public interface IDymolaInterfaceFactory
{
    /// <summary>
    /// Gets or creates the singleton DymolaInterface instance.
    /// </summary>
    /// <param name="cancellationToken">Stops the wait - for the factory, for a busy Dymola's
    /// connection window, or for one being started. A Dymola already launched is left running.</param>
    /// <exception cref="OperationCanceledException">The token fired.</exception>
    Task<IDymolaInterface> GetOrCreateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an instance exists and is connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Forgets the current session; the Dymola behind it is left running.
    /// </summary>
    Task ResetAsync();

    /// <summary>
    /// MLQT is exiting: lets go of the session <b>without ending the Dymola behind it</b>, and starts
    /// no other - <see cref="GetOrCreateAsync"/> refuses afterwards. Dymola has a window and the user
    /// may carry on working in the one MLQT started, so it is left running, on Windows and Linux
    /// alike (B493) - the opposite of what OpenModelica's factory does at the same moment. Never
    /// waits and never throws. Safe to call more than once.
    /// </summary>
    void Shutdown();

    /// <summary>
    /// Update the settings used by the Dymola instances
    /// </summary>
    public void UpdateSettings(DymolaSettings settings);
}
