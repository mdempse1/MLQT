namespace OpenModelicaInterface.Interfaces;

/// <summary>
/// Factory interface for creating and managing OpenModelica interface instances.
/// </summary>
public interface IOpenModelicaInterfaceFactory
{
    /// <summary>
    /// Gets or creates a singleton OpenModelica interface instance.
    /// </summary>
    Task<OpenModelicaInterface> GetOrCreateAsync();

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
    /// Update the settings used by the OpeNModelicaInstances instances
    /// </summary>
    public void UpdateSettings(OpenModelicaSettings settings);
}
