namespace DymolaInterface.Interfaces;

/// <summary>
/// Factory interface for creating DymolaInterface instances with configuration.
/// </summary>
public interface IDymolaInterfaceFactory
{
    /// <summary>
    /// Gets or creates the singleton DymolaInterface instance.
    /// </summary>
    Task<IDymolaInterface> GetOrCreateAsync();

    /// <summary>
    /// Checks if an instance exists and is connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Forgets the current session; the Dymola behind it is left running.
    /// </summary>
    Task ResetAsync();

    /// <summary>
    /// Update the settings used by the Dymola instances
    /// </summary>
    public void UpdateSettings(DymolaSettings settings);
}
