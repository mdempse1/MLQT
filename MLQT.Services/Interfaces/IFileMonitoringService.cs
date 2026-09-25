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
    /// Whether one particular repository is being watched.
    ///
    /// <para>Separate from <see cref="IsMonitoring"/>, which answers for the whole project. Some
    /// repositories are deliberately not watched — a reference-only one, because a change in a
    /// vendor's checkout must not start a pipeline that ends in MLQT writing to it — and "is this one
    /// watched" is the only way to ask whether that held.</para>
    /// </summary>
    bool IsMonitoringRepository(string repositoryId);

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
    /// Says which part of the watched directory is this repository's own - its <c>LocalPath</c> -
    /// so a change is recorded only for the repository it belongs to (B325). Null forgets it.
    /// </summary>
    /// <remarks>
    /// <para>A repository is watched at its VCS root, so two libraries checked out in one tree share
    /// one watcher, and every edit under it used to be offered to both. Recorded against whichever
    /// asked last, a change to library B's file could be formatted with library A's settings, and
    /// A's own formatter writes became B's pending changes.</para>
    ///
    /// <para>With a scope, a change is recorded for the repositories whose scope holds it - the
    /// deepest, where one repository's folder is inside another's - and for none when it is outside
    /// all of them. Kept across <see cref="StopMonitoring"/>, which pauses a repository rather than
    /// forgetting it; a repository with no scope is offered everything under its watched path.</para>
    /// </remarks>
    void SetRepositoryScope(string repositoryId, string? scopePath);

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
