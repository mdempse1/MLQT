using MLQT.Services.DataTypes;
namespace MLQT.Services.Interfaces;

/// <summary>
/// Service for monitoring file system changes in repository directories.
/// Accumulates changes for batch processing when user requests refresh.
/// </summary>
public interface IFileMonitoringService
{
    /// <summary>
    /// Gets whether file monitoring is currently active.
    /// </summary>
    bool IsMonitoring { get; }

    /// <summary>
    /// Gets the current list of pending changes.
    /// </summary>
    IReadOnlyList<FileChangeInfo> PendingChanges { get; }

    /// <summary>
    /// Gets a summary of pending changes.
    /// </summary>
    PendingChangesSummary GetPendingChangesSummary();

    /// <summary>
    /// Starts monitoring a repository directory.
    /// </summary>
    /// <param name="repositoryId">ID of the repository to monitor.</param>
    /// <param name="localPath">Local path to the repository directory.</param>
    void StartMonitoring(string repositoryId, string localPath);

    /// <summary>
    /// Stops monitoring a specific repository.
    /// </summary>
    /// <param name="repositoryId">ID of the repository to stop monitoring.</param>
    void StopMonitoring(string repositoryId);

    /// <summary>
    /// Stops monitoring all repositories.
    /// </summary>
    void StopAllMonitoring();

    /// <summary>
    /// Gets the pending changes for a specific repository.
    /// </summary>
    IReadOnlyList<FileChangeInfo> GetPendingChangesForRepository(string repositoryId);

    /// <summary>
    /// Clears all pending changes (called after processing).
    /// </summary>
    void ClearPendingChanges();

    /// <summary>
    /// Clears pending changes for a specific repository.
    /// </summary>
    void ClearPendingChanges(string repositoryId);

    /// <summary>
    /// Event fired when a new file change is detected.
    /// </summary>
    event Action<FileChangeInfo>? OnFileChanged;

    /// <summary>
    /// Event fired when the pending changes collection is updated.
    /// </summary>
    event Action? OnPendingChangesUpdated;

    /// <summary>
    /// Event fired when any file activity is detected in a monitored repository directory,
    /// including files that are not tracked as Modelica pending changes (e.g., .c, .h files).
    /// The string parameter is the repository ID. Used to refresh VCS status indicators
    /// (commit/revert button state) when non-Modelica files change.
    /// </summary>
    event Action<string>? OnRepositoryFileActivity;

    /// <summary>
    /// Manually fires the OnRepositoryFileActivity event for a repository.
    /// Used after bulk operations (e.g., formatting) where the file monitor was paused
    /// and needs to signal that files have changed.
    /// </summary>
    void NotifyFileActivity(string repositoryId);
}
