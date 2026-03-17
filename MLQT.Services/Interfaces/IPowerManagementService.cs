namespace MLQT.Services.Interfaces;

/// <summary>
/// Service to prevent the system from sleeping during long-running operations.
/// Platform-specific implementations use OS APIs (e.g., SetThreadExecutionState on Windows).
/// </summary>
public interface IPowerManagementService
{
    /// <summary>
    /// Prevents the system from entering sleep mode.
    /// Call <see cref="AllowSleep"/> when the long-running operation completes.
    /// </summary>
    void PreventSleep();

    /// <summary>
    /// Re-enables normal sleep behavior.
    /// </summary>
    void AllowSleep();
}
