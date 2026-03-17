namespace MLQT.Services.Interfaces;

/// <summary>
/// Service for managing application settings with platform-specific persistence
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Get a setting value by key, returning defaultValue if not found
    /// </summary>
    Task<T> GetAsync<T>(string key, T defaultValue);

    /// <summary>
    /// Set a setting value by key
    /// </summary>
    Task SetAsync<T>(string key, T value);

    /// <summary>
    /// Remove a setting by key
    /// </summary>
    Task RemoveAsync(string key);

    /// <summary>
    /// Clear all settings
    /// </summary>
    Task ClearAsync();
}
