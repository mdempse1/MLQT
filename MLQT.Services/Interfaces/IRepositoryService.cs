using MLQT.Services.DataTypes;
using RevisionControl;

namespace MLQT.Services.Interfaces;

/// <summary>
/// Service for managing version-controlled repositories containing Modelica libraries.
/// </summary>
public interface IRepositoryService
{
    /// <summary>
    /// Gets all currently configured repositories.
    /// </summary>
    IReadOnlyList<Repository> Repositories { get; }

    /// <summary>
    /// Adds a new repository from a local path or URL.
    /// Auto-detects VCS type and discovers Modelica libraries.
    /// </summary>
    /// <param name="pathOrUrl">Local path or remote URL.</param>
    /// <param name="checkoutPath">Local path for checkout (required for remote repos).</param>
    /// <param name="name">Optional display name (derived from path if not provided).</param>
    /// <param name="startMonitoring">Whether to start file monitoring immediately (false during initial load).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the repository and discovered libraries.</returns>
    Task<AddRepositoryResult> AddRepositoryAsync(
        string pathOrUrl,
        string? checkoutPath = null,
        string? name = null,
        bool startMonitoring = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects the VCS type for a given path or URL.
    /// </summary>
    /// <param name="pathOrUrl">Local path or remote URL to check.</param>
    /// <returns>Detected VCS type and whether path is local.</returns>
    (RepositoryVcsType vcsType, bool isLocal) DetectVcsType(string pathOrUrl);

    /// <summary>
    /// Discovers the VCS working copy root for a local path by walking up the directory tree.
    /// Returns the path itself when it is already the VCS root, or for non-VCS directories.
    /// </summary>
    /// <param name="localPath">Local path to inspect.</param>
    /// <returns>The absolute VCS working copy root path.</returns>
    string FindVcsRoot(string localPath);

    /// <summary>
    /// Discovers Modelica libraries within a repository.
    /// Only scans top-level and immediate subdirectories.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <returns>List of discovered library information.</returns>
    Task<List<DiscoveredLibraryInfo>> DiscoverLibrariesAsync(string repositoryId);

    /// <summary>
    /// Loads libraries from a repository into the LibraryDataService.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="libraryPaths">Specific library paths to load (null = all discovered).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LoadLibrariesAsync(
        string repositoryId,
        IEnumerable<string>? libraryPaths = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a repository and optionally its loaded libraries.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="unloadLibraries">Whether to unload associated libraries.</param>
    void RemoveRepository(string repositoryId, bool unloadLibraries = true);

    /// <summary>
    /// Refreshes a repository (re-discover libraries).
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a repository by its ID.
    /// </summary>
    Repository? GetRepository(string repositoryId);

    /// <summary>
    /// Gets the repository that contains a specific library.
    /// </summary>
    Repository? GetRepositoryForLibrary(string libraryId);

    /// <summary>
    /// Saves repository configurations to settings.
    /// </summary>
    Task SaveRepositorySettingsAsync();

    /// <summary>
    /// Loads repositories from saved settings and auto-loads if configured.
    /// </summary>
    /// <param name="projectId">Optional project ID to load. If null, uses the persisted active project.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LoadRepositorySettingsAsync(string? projectId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all repositories.
    /// </summary>
    void ClearAllRepositories();

    /// <summary>
    /// Starts file system monitoring for all loaded repositories.
    /// Should be called after startup is complete to avoid monitoring during initial file saves.
    /// </summary>
    void StartMonitoringAllRepositories();

    /// <summary>
    /// Event fired when repositories change (added, removed, updated).
    /// </summary>
    event Action? OnRepositoriesChanged;

    /// <summary>
    /// Event fired when a repository load operation starts/completes.
    /// Parameters: repositoryId, isLoading
    /// </summary>
    event Action<string, bool>? OnRepositoryLoadStateChanged;

    /// <summary>
    /// Gets log entries (commit history) for a repository.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="options">Options for filtering log entries (dates, max entries, etc.).</param>
    /// <returns>List of log entries, or empty list if repository not found or not a VCS repository.</returns>
    List<VcsLogEntry> GetLogEntries(string repositoryId, VcsLogOptions? options = null);

    /// <summary>
    /// Gets the list of files changed in a specific revision.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="revision">Revision identifier to get changed files for.</param>
    /// <returns>List of changed files, or empty list if repository not found or not a VCS repository.</returns>
    List<VcsChangedFile> GetChangedFiles(string repositoryId, string revision);

    /// <summary>
    /// Updates the repository to the latest version from the remote.
    /// For Git: Fetches from origin and pulls changes for the current branch.
    /// For SVN: Updates to HEAD.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing success status and any error message.</returns>
    Task<VcsUpdateResult> UpdateRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of files with uncommitted changes in a repository.
    /// Results are cached for a short period to avoid repeated expensive VCS status calls.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <returns>List of files with uncommitted changes.</returns>
    List<VcsWorkingCopyFile> GetWorkingCopyChanges(string repositoryId);

    /// <summary>
    /// Invalidates the cached working copy changes, forcing the next call to
    /// GetWorkingCopyChanges to query the VCS system directly.
    /// </summary>
    /// <param name="repositoryId">Specific repository to invalidate, or null to clear all.</param>
    void InvalidateWorkingCopyCache(string? repositoryId = null);

    /// <summary>
    /// Gets the list of available branches for a repository.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="includeRemote">Whether to include remote tracking branches.</param>
    /// <returns>List of branch information.</returns>
    List<VcsBranchInfo> GetBranches(string repositoryId, bool includeRemote = false);

    /// <summary>
    /// Commits changes to a repository.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="message">Commit message.</param>
    /// <param name="filesToCommit">Specific files to commit (null = all changes).</param>
    /// <param name="progress">Optional progress reporter for status updates during long operations.</param>
    /// <returns>Result of the commit operation.</returns>
    Task<VcsCommitResult> CommitAsync(string repositoryId, string message, IEnumerable<string>? filesToCommit = null, IProgress<string>? progress = null);

    /// <summary>
    /// Reverts changes to specific files in a repository.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="filesToRevert">Files to revert.</param>
    /// <returns>Result of the revert operation.</returns>
    Task<VcsOperationResult> RevertFilesAsync(string repositoryId, IEnumerable<string> filesToRevert);

    /// <summary>
    /// Switches a repository to a different branch.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="branchName">Name of the branch to switch to.</param>
    /// <returns>Result of the switch operation.</returns>
    Task<VcsOperationResult> SwitchBranchAsync(string repositoryId, string branchName);

    /// <summary>
    /// Creates a new branch in a repository.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="branchName">Name of the new branch.</param>
    /// <param name="switchToBranch">Whether to switch to the new branch after creation.</param>
    /// <returns>Result of the branch creation operation.</returns>
    Task<VcsOperationResult> CreateBranchAsync(string repositoryId, string branchName, bool switchToBranch = true);

    /// <summary>
    /// Gets the content of a file at a specific revision (or HEAD if not specified).
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="filePath">Relative path to the file within the repository.</param>
    /// <param name="revision">Revision identifier (null or "HEAD" for last committed version).</param>
    /// <returns>File content, or null if file not found at that revision.</returns>
    string? GetFileContentAtRevision(string repositoryId, string filePath, string? revision = null);

    /// <summary>
    /// Merges changes from a source branch into the current working copy.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="sourceBranch">Name of the branch to merge from.</param>
    /// <returns>Result of the merge operation.</returns>
    Task<VcsMergeResult> MergeBranchAsync(string repositoryId, string sourceBranch);

    /// <summary>
    /// Checks out a specific revision into the repository's working directory.
    /// Any uncommitted changes in the working copy will be discarded.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="revision">Revision identifier to checkout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the checkout operation.</returns>
    Task<VcsOperationResult> CheckoutRevisionAsync(string repositoryId, string revision, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverts all uncommitted changes and deletes all untracked files in the working copy.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <returns>Result of the clean operation.</returns>
    Task<VcsOperationResult> CleanWorkspaceAsync(string repositoryId);

    /// <summary>
    /// Resolves a merge conflict in a specific file.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="filePath">Absolute path to the conflicted file.</param>
    /// <param name="choice">How to resolve the conflict.</param>
    /// <returns>Result of the resolve operation.</returns>
    Task<VcsOperationResult> ResolveConflictAsync(string repositoryId, string filePath, ConflictResolutionChoice choice);

    /// <summary>
    /// Pushes the current branch to the remote repository.
    /// For Git: pushes to the tracked remote (typically 'origin').
    /// For SVN: no-op — SVN commits already write directly to the remote.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <returns>Result of the push operation.</returns>
    Task<VcsOperationResult> PushAsync(string repositoryId);

    /// <summary>
    /// Returns the "ours" and "theirs" content of a conflicted file for diff viewing.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="filePath">Absolute path to the conflicted file.</param>
    /// <returns>Tuple of (ours content, theirs content); either may be null if unavailable.</returns>
    Task<(string? ours, string? theirs)> GetConflictVersionsAsync(string repositoryId, string filePath);

    // ========== Project Profile Management ==========

    /// <summary>
    /// Gets all defined project profiles.
    /// </summary>
    IReadOnlyList<ProjectProfile> GetProjects();

    /// <summary>
    /// Gets the currently active project profile, or null if none.
    /// </summary>
    ProjectProfile? GetActiveProject();

    /// <summary>
    /// Creates a new empty project profile.
    /// </summary>
    /// <param name="name">Display name for the project.</param>
    /// <returns>The created project profile.</returns>
    ProjectProfile CreateProject(string name);

    /// <summary>
    /// Renames an existing project profile.
    /// </summary>
    /// <param name="projectId">ID of the project to rename.</param>
    /// <param name="newName">New display name.</param>
    void RenameProject(string projectId, string newName);

    /// <summary>
    /// Deletes a project profile. Cannot delete the last remaining project.
    /// </summary>
    /// <param name="projectId">ID of the project to delete.</param>
    /// <returns>True if deleted, false if it was the last project.</returns>
    bool DeleteProject(string projectId);

    /// <summary>
    /// Switches to a different project profile: unloads current repos, loads the target project's repos.
    /// </summary>
    /// <param name="projectId">ID of the project to switch to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SwitchProjectAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when the active project changes. Parameter: new project ID.
    /// </summary>
    event Action<string>? OnProjectChanged;

    /// <summary>
    /// Rebases the current branch onto the given target branch.
    /// Git only — replays local commits on top of the target branch tip.
    /// </summary>
    Task<VcsMergeResult> RebaseAsync(string repositoryId, string targetBranch);

    /// <summary>
    /// Continues a rebase that was suspended due to conflicts.
    /// Call after all conflicted files have been resolved and staged.
    /// </summary>
    Task<VcsMergeResult> ContinueRebaseAsync(string repositoryId);

    /// <summary>
    /// Aborts the current in-progress rebase and restores the pre-rebase state.
    /// </summary>
    Task<VcsOperationResult> AbortRebaseAsync(string repositoryId);

    /// <summary>
    /// Force-pushes the current branch using --force-with-lease.
    /// Required after a rebase because history is rewritten.
    /// </summary>
    Task<VcsOperationResult> ForcePushAsync(string repositoryId);

    /// <summary>
    /// Checks whether the current branch has been pushed to its tracked remote branch.
    /// For SVN: always returns true (commits go directly to remote).
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <returns>True if the branch is up to date on the remote.</returns>
    Task<bool> IsBranchPushedAsync(string repositoryId);

    /// <summary>
    /// Constructs the web URL for creating a pull request for the current branch.
    /// Supports GitHub, GitLab, Bitbucket, and Azure DevOps. For SVN: returns null.
    /// </summary>
    /// <param name="repositoryId">The repository ID.</param>
    /// <param name="baseBranch">Target branch for the PR (null = auto-detect remote default).</param>
    /// <returns>URL to open in a browser, or null if the service could not be determined.</returns>
    Task<string?> GetPullRequestUrlAsync(string repositoryId, string? baseBranch = null);
}
