namespace DymolaInterface.Interfaces;

/// <summary>
/// Factory interface for creating DymolaInterface instances with configuration.
/// </summary>
public interface IDymolaInterfaceFactory
{
    /// <summary>
    /// Gets or creates the singleton DymolaInterface instance.
    /// </summary>
    Task<DymolaInterface> GetOrCreateAsync();

    /// <summary>
    /// Checks if an instance exists and is connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Disposes the current instance if it exists.
    /// </summary>
    Task ResetAsync();

    /// <summary>
    /// Update the settings used by the Dymola instances
    /// </summary>
    public void UpdateSettings(DymolaSettings settings);
}
