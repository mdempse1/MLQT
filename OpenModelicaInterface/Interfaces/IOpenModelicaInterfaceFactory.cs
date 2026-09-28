namespace OpenModelicaInterface.Interfaces;

/// <summary>
/// Factory interface for creating and managing OpenModelica interface instances.
/// </summary>
public interface IOpenModelicaInterfaceFactory
{
    /// <summary>
    /// Gets or creates a singleton OpenModelica interface instance.
    /// </summary>
    /// <param name="cancellationToken">Stops the wait for the factory or for omc to start; an omc
    /// cancelled while starting is stopped.</param>
    /// <exception cref="OperationCanceledException">The token fired.</exception>
    Task<IOpenModelicaInterface> GetOrCreateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether the OpenModelica interface is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Resets the OpenModelica interface by disposing the current instance.
    /// The next call to GetOrCreateAsync will create a new instance.
    /// </summary>
    Task ResetAsync();

    /// <summary>
    /// MLQT is exiting: ends the session and the omc process behind it, with everything omc started,
    /// and starts no other - <see cref="GetOrCreateAsync"/> refuses afterwards. Synchronous and
    /// bounded, because it runs on the way out. omc is headless, so one left running when MLQT
    /// closes is found later in a task manager, if at all (B260, B493). Safe to call more than once.
    /// </summary>
    void Shutdown();

    /// <summary>
    /// Update the settings used by the OpeNModelicaInstances instances
    /// </summary>
    public void UpdateSettings(OpenModelicaSettings settings);
}
