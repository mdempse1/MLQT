namespace RevisionControl.Interfaces;

/// <summary>
/// Interface for revision control system integration.
/// Allows comparing different revisions of a Modelica library from version control.
/// </summary>
public interface IRevisionControlSystem
{
    /// <summary>
    /// Checks out a specific revision to a temporary directory.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository</param>
    /// <param name="revision">Revision identifier (commit hash, branch name, tag, etc.)</param>
    /// <param name="outputPath">Path where the revision should be checked out</param>
    /// <returns>True if checkout was successful, false otherwise</returns>
    bool CheckoutRevision(string repositoryPath, string revision, string outputPath);

    /// <summary>
    /// Gets the current revision identifier (e.g., current commit hash).
    /// </summary>
    /// <param name="repositoryPath">Path to the repository</param>
    /// <returns>Current revision identifier, or null if not in a repository</returns>
    string? GetCurrentRevision(string repositoryPath);

    /// <summary>
    /// Validates that the given path is a valid repository for this VCS.
    /// </summary>
    /// <param name="repositoryPath">Path to check</param>
    /// <returns>True if the path is a valid repository, false otherwise</returns>
    bool IsValidRepository(string repositoryPath);

    /// <summary>
    /// Discovers the root of the VCS working copy that contains the given path.
    /// Walks up the directory tree to find the actual repository root.
    /// </summary>
    /// <param name="path">Any path within the working copy</param>
    /// <returns>The absolute path to the VCS working copy root, or null if path is not within a working copy</returns>
    string? FindRepositoryRoot(string path);

    /// <summary>
    /// Gets a human-readable description of a revision (e.g., commit message, tag name).
    /// </summary>
    /// <param name="repositoryPath">Path to the repository</param>
    /// <param name="revision">Revision identifier</param>
    /// <returns>Description of the revision, or null if not found</returns>
    string? GetRevisionDescription(string repositoryPath, string revision);

    /// <summary>
    /// Resolves a revision identifier to its canonical form (e.g., branch name to commit hash).
    /// </summary>
    /// <param name="repositoryPath">Path to the repository</param>
    /// <param name="revision">Revision identifier (branch, tag, hash, etc.)</param>
    /// <returns>Canonical revision identifier (e.g., full commit hash), or null if not found</returns>
    string? ResolveRevision(string repositoryPath, string revision);

    /// <summary>
    /// Updates an existing checkout to a different revision, discarding any local changes.
    /// This is more efficient than deleting and re-checking out for large repositories.
    /// </summary>
    /// <param name="checkoutPath">Path to the existing checkout</param>
    /// <param name="repositoryPath">Path to the source repository</param>
    /// <param name="revision">Revision identifier to update to</param>
    /// <returns>True if update was successful, false otherwise</returns>
    bool UpdateExistingCheckout(string checkoutPath, string repositoryPath, string revision);

    /// <summary>
    /// Cleans a workspace by reverting all changes and removing untracked files.
    /// </summary>
    /// <param name="checkoutPath">Path to the workspace to clean</param>
    /// <returns>True if cleaning was successful, false otherwise</returns>
    bool CleanWorkspace(string checkoutPath);

    /// <summary>
    /// Gets the current branch name for a repository or working copy.
    /// For Git: Returns the current branch name (e.g., "main", "feature/xyz").
    /// For SVN: Returns the branch derived from the URL path (e.g., "trunk", "branches/release-1.0").
    /// </summary>
    /// <param name="repositoryPath">Path to the repository or working copy</param>
    /// <returns>Branch name, or null if not available or not on a branch (e.g., detached HEAD in Git)</returns>
    string? GetCurrentBranch(string repositoryPath);

    /// <summary>
    /// Gets log entries (commit history) from the repository.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository or working copy</param>
    /// <param name="options">Options for filtering and limiting the log entries</param>
    /// <returns>List of log entries, or empty list if retrieval failed</returns>
    List<VcsLogEntry> GetLogEntries(string repositoryPath, VcsLogOptions? options = null);

    /// <summary>
    /// Gets the list of files changed in a specific revision.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository or working copy</param>
    /// <param name="revision">Revision identifier to get changed files for</param>
    /// <returns>List of changed files, or empty list if retrieval failed</returns>
    List<VcsChangedFile> GetChangedFiles(string repositoryPath, string revision);

    /// <summary>
    /// Updates the working copy to the latest version from the remote.
    /// For Git: Fetches from origin and pulls changes for the current branch.
    /// For SVN: Updates to HEAD.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository or working copy</param>
    /// <returns>Result containing success status and any error message</returns>
    VcsUpdateResult UpdateToLatest(string repositoryPath);

    /// <summary>
    /// Gets the list of files with uncommitted changes in the working copy.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository or working copy</param>
    /// <returns>List of files with uncommitted changes</returns>
    List<VcsWorkingCopyFile> GetWorkingCopyChanges(string repositoryPath);

    /// <summary>
    /// Gets the list of available branches.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository or working copy</param>
    /// <param name="includeRemote">Whether to include remote tracking branches</param>
    /// <returns>List of branch information</returns>
    List<VcsBranchInfo> GetBranches(string repositoryPath, bool includeRemote = false);

    /// <summary>
    /// Commits changes to the repository.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository or working copy</param>
    /// <param name="message">Commit message</param>
    /// <param name="filesToCommit">Specific files to commit (null = all staged/modified files)</param>
    /// <param name="progress">Optional progress reporter for status updates during long operations</param>
    /// <returns>Result of the commit operation</returns>
    VcsCommitResult Commit(string repositoryPath, string message, IEnumerable<string>? filesToCommit = null, IProgress<string>? progress = null);

    /// <summary>
    /// Reverts changes to specific files in the working copy.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository or working copy</param>
    /// <param name="filesToRevert">Files to revert (relative paths from repository root)</param>
    /// <returns>Result of the revert operation</returns>
    VcsOperationResult RevertFiles(string repositoryPath, IEnumerable<string> filesToRevert);

    /// <summary>
    /// Switches to a different branch.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository or working copy</param>
    /// <param name="branchName">Name of the branch to switch to</param>
    /// <returns>Result of the switch operation</returns>
    VcsOperationResult SwitchBranch(string repositoryPath, string branchName);

    /// <summary>
    /// Creates a new branch and optionally switches to it.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository or working copy</param>
    /// <param name="branchName">Name of the new branch</param>
    /// <param name="switchToBranch">Whether to switch to the new branch after creation</param>
    /// <returns>Result of the branch creation operation</returns>
    VcsOperationResult CreateBranch(string repositoryPath, string branchName, bool switchToBranch = true);

    /// <summary>
    /// Gets the content of a file at a specific revision.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository or working copy</param>
    /// <param name="filePath">Relative path to the file within the repository</param>
    /// <param name="revision">Revision identifier (null or "HEAD" for the last committed version)</param>
    /// <returns>The file content, or null if the file doesn't exist at that revision</returns>
    string? GetFileContentAtRevision(string repositoryPath, string filePath, string? revision = null);

    /// <summary>
    /// Merges changes from a source branch into the current working copy.
    /// For SVN: Performs "svn merge" to merge all revisions from the source branch.
    /// For Git: Performs "git merge" (to be implemented later).
    /// </summary>
    /// <param name="repositoryPath">Path to the repository or working copy</param>
    /// <param name="sourceBranch">The branch to merge from</param>
    /// <returns>Result of the merge operation</returns>
    VcsMergeResult MergeBranch(string repositoryPath, string sourceBranch);

    /// <summary>
    /// Resolves a conflict in a specific file using the given resolution strategy.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository or working copy</param>
    /// <param name="filePath">Absolute path to the conflicted file</param>
    /// <param name="choice">How to resolve the conflict</param>
    /// <returns>Result of the resolve operation</returns>
    VcsOperationResult ResolveConflict(string repositoryPath, string filePath, ConflictResolutionChoice choice);

    /// <summary>
    /// Pushes the current branch to the remote repository.
    /// For Git: Pushes the specified (or current) branch to its tracked remote.
    /// For SVN: No-op — SVN commits go directly to the remote server.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository or working copy</param>
    /// <param name="branchName">Branch to push (null = current branch)</param>
    /// <returns>Result of the push operation</returns>
    VcsOperationResult Push(string repositoryPath, string? branchName = null);

    /// <summary>
    /// Returns the "ours" (current branch) and "theirs" (incoming branch) content of a
    /// conflicted file so the user can compare the two versions before resolving.
    /// For Git: reads blobs from the index conflict entry.
    /// For SVN: reads the .mine (ours) and highest-revision .r{n} (theirs) sidecar files.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository or working copy</param>
    /// <param name="filePath">Absolute path to the conflicted file</param>
    /// <returns>Tuple of (ours content, theirs content); either may be null if unavailable</returns>
    (string? ours, string? theirs) GetConflictVersions(string repositoryPath, string filePath);

    /// <summary>
    /// Rebases the current branch onto a target branch, replaying local commits on top of it.
    /// For Git: runs "git rebase &lt;targetBranch&gt;". Not supported for SVN.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository</param>
    /// <param name="targetBranch">The branch to rebase onto</param>
    /// <returns>Result with conflict information if the rebase stops mid-way</returns>
    VcsMergeResult Rebase(string repositoryPath, string targetBranch);

    /// <summary>
    /// Continues a rebase that was stopped due to conflicts, after conflicts have been resolved.
    /// For Git: runs "git rebase --continue". Not supported for SVN.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository</param>
    /// <returns>Result with conflict information if further conflicts remain</returns>
    VcsMergeResult ContinueRebase(string repositoryPath);

    /// <summary>
    /// Aborts the current in-progress rebase and restores the pre-rebase state.
    /// For Git: runs "git rebase --abort". Not supported for SVN.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository</param>
    /// <returns>Result of the abort operation</returns>
    VcsOperationResult AbortRebase(string repositoryPath);

    /// <summary>
    /// Force-pushes the current branch to its tracked remote using --force-with-lease.
    /// Required after a rebase since history is rewritten. Not supported for SVN.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository</param>
    /// <param name="branchName">Branch to push (null = current branch)</param>
    /// <returns>Result of the push operation</returns>
    VcsOperationResult ForcePush(string repositoryPath, string? branchName = null);

    /// <summary>
    /// Checks whether the current branch's local tip has been pushed to its tracked remote branch
    /// (i.e., the local branch has a tracking remote and is not ahead of it).
    /// For SVN: always returns true since commits go directly to the remote.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository</param>
    /// <returns>True if the branch is up to date on the remote</returns>
    bool IsBranchPushed(string repositoryPath);

    /// <summary>
    /// Constructs the web URL for creating a pull request (or merge request) for the current
    /// branch on the detected hosting service (GitHub, GitLab, Bitbucket, Azure DevOps).
    /// For SVN: always returns null.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository</param>
    /// <param name="baseBranch">Target branch to merge into (null = auto-detect remote default)</param>
    /// <returns>URL to open in a browser, or null if the service could not be determined</returns>
    string? GetPullRequestUrl(string repositoryPath, string? baseBranch = null);
}

