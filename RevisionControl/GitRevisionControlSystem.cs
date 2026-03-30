using LibGit2Sharp;
using RevisionControl.Interfaces;

namespace RevisionControl;

/// <summary>
/// Git implementation of the revision control system interface.
/// Uses LibGit2Sharp to interact with Git repositories.
/// </summary>
public class GitRevisionControlSystem : IRevisionControlSystem
{
    /// <summary>
    /// Checks out a specific revision to a temporary directory.
    /// When outputPath equals repositoryPath, performs an in-place checkout via Commands.Checkout.
    /// When outputPath differs, extracts the commit tree directly to outputPath without modifying the source repo.
    /// When repositoryPath is an HTTP URL, clones (or updates an existing clone at outputPath).
    /// </summary>
    public bool CheckoutRevision(string repositoryPath, string revision, string outputPath)
    {
        try
        {
            if (repositoryPath.StartsWith("http"))
            {
                // Clone from remote URL or update an existing clone
                if (!Directory.Exists(Path.Combine(outputPath, ".git")))
                {
                    Repository.Clone(repositoryPath, outputPath);
                }
                else
                {
                    using var repo = new Repository(outputPath);
                    var commit = ResolveToCommit(repo, revision);
                    if (commit == null)
                        return false;

                    Commands.Checkout(repo, commit, new CheckoutOptions
                    {
                        CheckoutModifiers = CheckoutModifiers.Force
                    });
                }
            }
            else if (string.Equals(Path.GetFullPath(repositoryPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            {
                // In-place checkout: update the working directory to a specific revision
                using var repo = new Repository(repositoryPath);
                var commit = ResolveToCommit(repo, revision);
                if (commit == null)
                    return false;

                Commands.Checkout(repo, commit, new CheckoutOptions
                {
                    CheckoutModifiers = CheckoutModifiers.Force
                });
            }
            else
            {
                // Extract commit tree to a separate output directory (non-destructive)
                using var repo = new Repository(repositoryPath);
                var commit = ResolveToCommit(repo, revision);
                if (commit == null)
                    return false;

                Directory.CreateDirectory(outputPath);
                CheckoutTree(repo, commit, outputPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("CheckoutRevision", ex);
            return false;
        }
    }

    /// <summary>
    /// Checks out a commit's tree to a specified directory without modifying the source repository.
    /// </summary>
    private static void CheckoutTree(Repository repo, Commit commit, string outputPath)
    {
        foreach (var entry in commit.Tree)
        {
            WriteTreeEntry(entry, outputPath);
        }
    }

    /// <summary>
    /// Recursively writes tree entries to disk.
    /// </summary>
    private static void WriteTreeEntry(TreeEntry entry, string basePath)
    {
        var fullPath = Path.Combine(basePath, entry.Name);

        if (entry.TargetType == TreeEntryTargetType.Tree)
        {
            Directory.CreateDirectory(fullPath);
            if (entry.Target is Tree tree)
            {
                foreach (var subEntry in tree)
                {
                    WriteTreeEntry(subEntry, fullPath);
                }
            }
        }
        else if (entry.TargetType == TreeEntryTargetType.Blob)
        {
            if (entry.Target is Blob blob)
            {
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                using var contentStream = blob.GetContentStream();
                using var fileStream = File.OpenWrite(fullPath);
                contentStream.CopyTo(fileStream);
            }
        }
    }

    /// <summary>
    /// Gets the current revision identifier (current commit hash).
    /// </summary>
    public string? GetCurrentRevision(string repositoryPath)
    {
        try
        {
            using var repo = new Repository(repositoryPath);
            return repo.Head?.Tip?.Sha;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetCurrentRevision", ex);
            return null;
        }
    }

    /// <summary>
    /// Validates that the given path is a valid Git repository.
    /// Uses Repository.Discover which walks up the directory tree.
    /// </summary>
    public bool IsValidRepository(string repositoryPath)
    {
        try
        {
            return Repository.Discover(repositoryPath) != null;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("IsValidRepository", ex);
            return false;
        }
    }

    /// <summary>
    /// Discovers the root of the Git working tree containing the given path.
    /// </summary>
    public string? FindRepositoryRoot(string path)
    {
        try
        {
            var discovered = Repository.Discover(path);
            if (discovered == null)
                return null;

            // Discover returns the .git directory path (with trailing separator).
            // For a standard repo the working tree root is its parent.
            // For a bare repo there is no working tree root.
            var gitDir = discovered.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            using var repo = new Repository(discovered);
            if (repo.Info.IsBare)
                return null;

            return repo.Info.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("FindRepositoryRoot", ex);
            return null;
        }
    }

    /// <summary>
    /// Gets a human-readable description of a revision (commit message and author).
    /// </summary>
    public string? GetRevisionDescription(string repositoryPath, string revision)
    {
        try
        {
            using var repo = new Repository(repositoryPath);
            var commit = ResolveToCommit(repo, revision);
            if (commit == null)
            {
                return null;
            }

            return $"{commit.MessageShort} (by {commit.Author.Name} on {commit.Author.When:yyyy-MM-dd})";
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetRevisionDescription", ex);
            return null;
        }
    }

    /// <summary>
    /// Resolves a revision identifier to its canonical form (full commit hash).
    /// </summary>
    public string? ResolveRevision(string repositoryPath, string revision)
    {
        try
        {
            using var repo = new Repository(repositoryPath);
            var commit = ResolveToCommit(repo, revision);
            return commit?.Sha;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("ResolveRevision", ex);
            return null;
        }
    }

    /// <summary>
    /// Resolves a revision string (branch, tag, hash, HEAD~1, etc.) to a Commit object.
    /// </summary>
    private Commit? ResolveToCommit(Repository repo, string revision)
    {
        try
        {
            // Try to resolve as a branch, tag, or commit reference
            var gitObject = repo.Lookup(revision);

            if (gitObject is Commit commit)
            {
                return commit;
            }

            if (gitObject is TagAnnotation tag)
            {
                return tag.Target as Commit;
            }

            // Try to peel to a commit (handles references)
            if (gitObject != null)
            {
                var peeled = gitObject.Peel<Commit>();
                if (peeled != null)
                {
                    return peeled;
                }
            }

            // Try to resolve as a branch name
            var branch = repo.Branches[revision];
            if (branch != null)
            {
                return branch.Tip;
            }

            return null;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("ResolveToCommit", ex);
            return null;
        }
    }

    /// <summary>
    /// Updates an existing checkout to a different revision, discarding any local changes.
    /// Much more efficient than deleting and re-checking out for large repositories.
    /// </summary>
    public bool UpdateExistingCheckout(string checkoutPath, string repositoryPath, string revision)
    {
        try
        {
            if (!Directory.Exists(checkoutPath) || !Repository.IsValid(checkoutPath))
            {
                Directory.CreateDirectory(checkoutPath);
                Repository.Init(checkoutPath);
            }

            // Resolve the revision using the source repository
            string? resolvedRevision;
            using (var sourceRepo = new Repository(repositoryPath))
            {
                var commit = ResolveToCommit(sourceRepo, revision);
                if (commit == null)
                    return false;
                resolvedRevision = commit.Sha;
            }

            // Clean workspace and set up remote, then close the repo before fetching
            using (var repo = new Repository(checkoutPath))
            {
                if (repo.Head?.Tip != null && !CleanWorkspace(checkoutPath))
                    return false;

                var remote = repo.Network.Remotes["origin"];
                if (remote == null)
                    repo.Network.Remotes.Add("origin", repositoryPath);
                else if (remote.Url != repositoryPath)
                    repo.Network.Remotes.Update("origin", r => r.Url = repositoryPath);
            }

            // Fetch via git.exe — uses full credential stack; also works for local paths
            var (fetchExit, _, _) = RunGitCommand(checkoutPath, "fetch origin");
            if (fetchExit != 0)
                return false;

            // Checkout the specific commit (re-open repo so fetched objects are visible)
            using (var repo = new Repository(checkoutPath))
            {
                var commitToCheckout = repo.Lookup<Commit>(resolvedRevision);
                if (commitToCheckout == null)
                    return false;

                Commands.Checkout(repo, commitToCheckout, new CheckoutOptions
                {
                    CheckoutModifiers = CheckoutModifiers.Force
                });
            }

            return true;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("UpdateExistingCheckout", ex);
            return false;
        }
    }

    /// <summary>
    /// Gets the current branch name for the repository.
    /// Returns the branch name (e.g., "main", "feature/xyz"), or null if in detached HEAD state.
    /// </summary>
    public string? GetCurrentBranch(string repositoryPath)
    {
        try
        {
            using var repo = new Repository(repositoryPath);

            // Check if HEAD is pointing to a branch (not detached)
            if (!repo.Info.IsHeadDetached)
            {
                // Return the friendly branch name
                return repo.Head.FriendlyName;
            }

            // In detached HEAD state - no branch name
            return null;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetCurrentBranch", ex);
            return null;
        }
    }

    /// <summary>
    /// Gets log entries (commit history) from the repository.
    /// </summary>
    public List<VcsLogEntry> GetLogEntries(string repositoryPath, VcsLogOptions? options = null)
    {
        var entries = new List<VcsLogEntry>();
        options ??= new VcsLogOptions();

        try
        {
            using var repo = new Repository(repositoryPath);

            // Get the current branch for reference
            var currentBranch = !repo.Info.IsHeadDetached ? repo.Head.FriendlyName : null;

            // If a specific revision is requested, look it up directly
            if (!string.IsNullOrEmpty(options.Revision))
            {
                var commit = ResolveToCommit(repo, options.Revision);
                if (commit != null)
                {
                    entries.Add(new VcsLogEntry
                    {
                        Revision = commit.Sha,
                        ShortRevision = commit.Sha[..Math.Min(7, commit.Sha.Length)],
                        Author = commit.Author.Name,
                        AuthorEmail = commit.Author.Email,
                        Date = commit.Author.When,
                        Message = commit.Message.Trim(),
                        MessageShort = commit.MessageShort.Trim(),
                        Branch = currentBranch
                    });
                }
                return entries;
            }

            // Create commit filter
            var filter = new CommitFilter
            {
                SortBy = CommitSortStrategies.Time | CommitSortStrategies.Topological
            };

            // If branch is specified, use it as the starting point
            if (!string.IsNullOrEmpty(options.Branch))
            {
                var branch = repo.Branches[options.Branch];
                if (branch != null)
                {
                    filter.IncludeReachableFrom = branch;
                }
            }

            var commits = repo.Commits.QueryBy(filter);
            var count = 0;
            var minEntriesFromSinceFilter = 10; // Ensure at least 10 entries even with date filter

            foreach (var commit in commits)
            {
                // Apply date filters
                if (options.Since.HasValue && commit.Author.When < options.Since.Value)
                {
                    // If we have a Since filter but haven't hit minimum entries, keep going
                    if (count >= minEntriesFromSinceFilter)
                    {
                        break;
                    }
                }

                if (options.Until.HasValue && commit.Author.When > options.Until.Value)
                {
                    continue;
                }

                // Check max entries (but ensure minimum for date filter)
                if (count >= options.MaxEntries)
                {
                    break;
                }

                var entry = new VcsLogEntry
                {
                    Revision = commit.Sha,
                    ShortRevision = commit.Sha.Length >= 7 ? commit.Sha.Substring(0, 7) : commit.Sha,
                    Author = commit.Author.Name,
                    AuthorEmail = commit.Author.Email,
                    Date = commit.Author.When,
                    Message = commit.Message?.Trim() ?? "",
                    MessageShort = commit.MessageShort?.Trim() ?? "",
                    Branch = currentBranch,
                    ParentRevisions = commit.Parents.Select(p => p.Sha).ToList()
                };

                entries.Add(entry);
                count++;
            }
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetLogEntries", ex);
        }

        return entries;
    }

    /// <summary>
    /// Gets the list of files changed in a specific revision.
    /// </summary>
    public List<VcsChangedFile> GetChangedFiles(string repositoryPath, string revision)
    {
        var changedFiles = new List<VcsChangedFile>();

        try
        {
            using var repo = new Repository(repositoryPath);
            var commit = ResolveToCommit(repo, revision);
            if (commit == null)
            {
                return changedFiles;
            }

            // Compare with parent commit (or empty tree for initial commit)
            var parentCommit = commit.Parents.FirstOrDefault();

            Tree? oldTree = parentCommit?.Tree;
            Tree newTree = commit.Tree;

            var changes = repo.Diff.Compare<TreeChanges>(oldTree, newTree);

            foreach (var change in changes)
            {
                var changedFile = new VcsChangedFile
                {
                    Path = change.Path,
                    ChangeType = change.Status switch
                    {
                        ChangeKind.Added => VcsChangeType.Added,
                        ChangeKind.Deleted => VcsChangeType.Deleted,
                        ChangeKind.Modified => VcsChangeType.Modified,
                        ChangeKind.Renamed => VcsChangeType.Renamed,
                        ChangeKind.Copied => VcsChangeType.Copied,
                        _ => VcsChangeType.Modified
                    }
                };

                if (change.Status == ChangeKind.Renamed || change.Status == ChangeKind.Copied)
                {
                    changedFile.OldPath = change.OldPath;
                }

                changedFiles.Add(changedFile);
            }
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetChangedFiles", ex);
        }

        return changedFiles;
    }

    /// <summary>
    /// Cleans a workspace by reverting all changes and removing untracked files.
    /// </summary>
    public bool CleanWorkspace(string checkoutPath)
    {
        try
        {
            if (!Directory.Exists(checkoutPath) || !Repository.IsValid(checkoutPath))
            {
                return false;
            }

            using var repo = new Repository(checkoutPath);

            // Reset all tracked files to HEAD
            repo.Reset(ResetMode.Hard);

            // Remove all untracked files and directories
            var status = repo.RetrieveStatus(new StatusOptions
            {
                IncludeUntracked = true,
                RecurseUntrackedDirs = true
            });

            // Build a set of all tracked file paths (from the index/HEAD tree)
            var trackedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in repo.Index)
            {
                trackedPaths.Add(entry.Path.Replace('/', Path.DirectorySeparatorChar));
            }

            // Track top-level untracked directories to avoid deleting same directory multiple times
            var deletedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in status.Untracked)
            {
                var fullPath = Path.Combine(checkoutPath, item.FilePath);

                // Find the top-level untracked directory or file
                var pathToDelete = fullPath;
                var parentDir = Path.GetDirectoryName(fullPath);

                while (parentDir != null && !string.Equals(parentDir, checkoutPath, StringComparison.OrdinalIgnoreCase))
                {
                    // Check if parent directory contains any tracked files - if so, we can't delete it
                    var parentRelative = Path.GetRelativePath(checkoutPath, parentDir).Replace('/', Path.DirectorySeparatorChar);
                    var hasTrackedFiles = trackedPaths.Any(p =>
                        p.StartsWith(parentRelative + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

                    if (hasTrackedFiles)
                    {
                        break;
                    }
                    pathToDelete = parentDir;
                    parentDir = Path.GetDirectoryName(parentDir);
                }

                // Only delete if we haven't already deleted this path or a parent
                if (!deletedPaths.Contains(pathToDelete) && !deletedPaths.Any(deleted => pathToDelete.StartsWith(deleted + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        if (File.Exists(pathToDelete))
                        {
                            File.Delete(pathToDelete);
                            deletedPaths.Add(pathToDelete);
                        }
                        else if (Directory.Exists(pathToDelete))
                        {
                            Directory.Delete(pathToDelete, recursive: true);
                            deletedPaths.Add(pathToDelete);
                        }
                    }
                    catch
                    {
                        // Continue with other files even if one fails
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("CleanWorkspace", ex);
            return false;
        }
    }

    /// <summary>
    /// Updates the working copy to the latest version from the remote.
    /// Fetches from origin (via git.exe for credential support) then merges.
    /// </summary>
    public VcsUpdateResult UpdateToLatest(string repositoryPath)
    {
        var result = new VcsUpdateResult();

        try
        {
            if (!Directory.Exists(repositoryPath) || !Repository.IsValid(repositoryPath))
            {
                result.ErrorMessage = "Invalid repository path.";
                return result;
            }

            // Phase 1: Validate state and capture pre-fetch info
            string remoteName;
            using (var repo = new Repository(repositoryPath))
            {
                result.OldRevision = repo.Head?.Tip?.Sha;

                if (repo.Info.IsHeadDetached)
                {
                    result.ErrorMessage = "Cannot update: HEAD is detached. Please checkout a branch first.";
                    return result;
                }

                var remote = repo.Network.Remotes["origin"];
                if (remote == null)
                {
                    result.ErrorMessage = "No 'origin' remote configured.";
                    return result;
                }
                remoteName = remote.Name;
            }

            // Phase 2: Fetch via git.exe — uses GCM / SSH agent / all credential helpers
            var (fetchExit, fetchOut, fetchErr) = RunGitCommand(repositoryPath, $"fetch {remoteName}");
            if (fetchExit != 0)
            {
                var msg = fetchErr.Trim();
                if (string.IsNullOrEmpty(msg)) msg = fetchOut.Trim();
                result.ErrorMessage = string.IsNullOrEmpty(msg) ? "git fetch failed." : msg;
                return result;
            }

            // Phase 3: Merge (re-open repo so fetched refs are visible to LibGit2Sharp)
            using (var repo = new Repository(repositoryPath))
            {
                var currentBranch = repo.Head;
                if (currentBranch == null)
                {
                    result.ErrorMessage = "No current branch.";
                    return result;
                }

                var trackedBranch = currentBranch.TrackedBranch
                    ?? repo.Branches[$"origin/{currentBranch.FriendlyName}"];
                if (trackedBranch == null)
                {
                    result.ErrorMessage = $"No tracking branch found for '{currentBranch.FriendlyName}'.";
                    return result;
                }

                if (currentBranch.Tip?.Sha == trackedBranch.Tip?.Sha)
                {
                    result.Success = true;
                    result.HasChanges = false;
                    result.NewRevision = currentBranch.Tip?.Sha;
                    return result;
                }

                var signature = new Signature("MLQT", "mlqt@localhost", DateTimeOffset.Now);
                var mergeResult = repo.Merge(trackedBranch, signature, new MergeOptions
                {
                    FastForwardStrategy = FastForwardStrategy.Default
                });

                if (mergeResult.Status == MergeStatus.Conflicts)
                {
                    result.ErrorMessage = "Merge conflicts detected. Please resolve manually.";
                    return result;
                }

                result.Success = true;
                result.HasChanges = true;
                result.NewRevision = repo.Head?.Tip?.Sha;
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Gets the list of files with uncommitted changes in the working copy.
    /// </summary>
    public List<VcsWorkingCopyFile> GetWorkingCopyChanges(string repositoryPath)
    {
        var files = new List<VcsWorkingCopyFile>();

        try
        {
            if (!Directory.Exists(repositoryPath) || !Repository.IsValid(repositoryPath))
            {
                return files;
            }

            using var repo = new Repository(repositoryPath);
            var status = repo.RetrieveStatus(new StatusOptions
            {
                IncludeUntracked = true,
                RecurseUntrackedDirs = true
            });

            foreach (var item in status)
            {
                var file = new VcsWorkingCopyFile
                {
                    Path = item.FilePath
                };

                // Determine status and staged state
                switch (item.State)
                {
                    case FileStatus.NewInIndex:
                        file.Status = VcsFileStatus.Added;
                        file.IsStaged = true;
                        break;
                    case FileStatus.ModifiedInIndex:
                        file.Status = VcsFileStatus.Modified;
                        file.IsStaged = true;
                        break;
                    case FileStatus.DeletedFromIndex:
                        file.Status = VcsFileStatus.Deleted;
                        file.IsStaged = true;
                        break;
                    case FileStatus.RenamedInIndex:
                        file.Status = VcsFileStatus.Renamed;
                        file.IsStaged = true;
                        break;
                    case FileStatus.NewInWorkdir:
                        file.Status = VcsFileStatus.Untracked;
                        file.IsStaged = false;
                        break;
                    case FileStatus.ModifiedInWorkdir:
                        file.Status = VcsFileStatus.Modified;
                        file.IsStaged = false;
                        break;
                    case FileStatus.DeletedFromWorkdir:
                        file.Status = VcsFileStatus.Deleted;
                        file.IsStaged = false;
                        break;
                    case FileStatus.Conflicted:
                        file.Status = VcsFileStatus.Conflicted;
                        file.IsStaged = false;
                        break;
                    default:
                        // Handle combined states (staged + modified in workdir)
                        if ((item.State & FileStatus.ModifiedInIndex) != 0 ||
                            (item.State & FileStatus.NewInIndex) != 0 ||
                            (item.State & FileStatus.DeletedFromIndex) != 0)
                        {
                            file.IsStaged = true;
                        }
                        if ((item.State & FileStatus.ModifiedInWorkdir) != 0)
                        {
                            file.Status = VcsFileStatus.Modified;
                        }
                        else if ((item.State & FileStatus.DeletedFromWorkdir) != 0)
                        {
                            file.Status = VcsFileStatus.Deleted;
                        }
                        else if ((item.State & FileStatus.NewInWorkdir) != 0)
                        {
                            file.Status = VcsFileStatus.Untracked;
                        }
                        break;
                }

                // Only include files that have actual changes
                if (item.State != FileStatus.Unaltered && item.State != FileStatus.Ignored)
                {
                    files.Add(file);
                }
            }
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetWorkingCopyChanges", ex);
        }

        return files;
    }

    /// <summary>
    /// Gets the list of available branches.
    /// </summary>
    public List<VcsBranchInfo> GetBranches(string repositoryPath, bool includeRemote = false)
    {
        var branches = new List<VcsBranchInfo>();

        try
        {
            if (!Directory.Exists(repositoryPath) || !Repository.IsValid(repositoryPath))
            {
                return branches;
            }

            using var repo = new Repository(repositoryPath);
            var currentBranch = !repo.Info.IsHeadDetached ? repo.Head.FriendlyName : null;

            foreach (var branch in repo.Branches)
            {
                // Skip remote branches if not requested
                if (branch.IsRemote && !includeRemote)
                {
                    continue;
                }

                branches.Add(new VcsBranchInfo
                {
                    Name = branch.FriendlyName,
                    IsCurrent = branch.FriendlyName == currentBranch,
                    IsRemote = branch.IsRemote,
                    LastCommit = branch.Tip?.Sha
                });
            }
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetBranches", ex);
        }

        return branches;
    }

    /// <summary>
    /// Commits changes to the repository.
    /// </summary>
    public VcsCommitResult Commit(string repositoryPath, string message, IEnumerable<string>? filesToCommit = null, IProgress<string>? progress = null)
    {
        var result = new VcsCommitResult();

        try
        {
            if (!Directory.Exists(repositoryPath) || !Repository.IsValid(repositoryPath))
            {
                result.ErrorMessage = "Invalid repository path.";
                return result;
            }

            using var repo = new Repository(repositoryPath);

            // Stage files if specific files are provided
            if (filesToCommit != null)
            {
                var files = filesToCommit.ToList();
                var totalFiles = files.Count;
                for (int i = 0; i < totalFiles; i++)
                {
                    // Normalize path separators for cross-platform compatibility
                    var normalizedPath = files[i].Replace('\\', '/');
                    Commands.Stage(repo, normalizedPath);

                    if (i % 100 == 0 || i == totalFiles - 1)
                        progress?.Report($"Staging file {i + 1} of {totalFiles}...");
                }
            }
            else
            {
                // Stage all changes - get status first and stage each file explicitly
                // This is more reliable than "*" pattern for untracked files
                var preStatus = repo.RetrieveStatus(new StatusOptions
                {
                    IncludeUntracked = true,
                    RecurseUntrackedDirs = true
                });

                var items = preStatus.Where(item => item.State != FileStatus.Ignored && item.State != FileStatus.Unaltered).ToList();
                for (int i = 0; i < items.Count; i++)
                {
                    Commands.Stage(repo, items[i].FilePath);

                    if (i % 100 == 0 || i == items.Count - 1)
                        progress?.Report($"Staging file {i + 1} of {items.Count}...");
                }
            }

            // Check if there are staged changes
            var status = repo.RetrieveStatus(new StatusOptions
            {
                IncludeUntracked = true,
                RecurseUntrackedDirs = true
            });

            // Check for any staged changes
            // - Added: new files staged for addition
            // - Staged: tracked files with staged modifications
            // - Removed: files staged for deletion
            // - RenamedInIndex: files staged for rename
            var hasStaged = status.Staged.Any() || status.Added.Any() || status.Removed.Any() || status.RenamedInIndex.Any();
            if (!hasStaged)
            {
                // Build diagnostic info
                var untrackedCount = status.Untracked.Count();
                var modifiedCount = status.Modified.Count();
                var addedCount = status.Added.Count();
                var stagedCount = status.Staged.Count();
                result.ErrorMessage = $"No changes to commit. Status: Untracked={untrackedCount}, Modified={modifiedCount}, Added={addedCount}, Staged={stagedCount}";
                return result;
            }

            // Create the commit
            progress?.Report("Creating commit...");
            var signature = new Signature("MLQT User", "user@mlqt.local", DateTimeOffset.Now);
            var commit = repo.Commit(message, signature, signature);

            result.Success = true;
            result.NewRevision = commit.Sha;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Reverts changes to specific files in the working copy.
    /// </summary>
    public VcsOperationResult RevertFiles(string repositoryPath, IEnumerable<string> filesToRevert)
    {
        var result = new VcsOperationResult();

        try
        {
            if (!Directory.Exists(repositoryPath) || !Repository.IsValid(repositoryPath))
            {
                result.ErrorMessage = "Invalid repository path.";
                return result;
            }

            using var repo = new Repository(repositoryPath);

            foreach (var filePath in filesToRevert)
            {
                var fullPath = Path.Combine(repositoryPath, filePath);
                var status = repo.RetrieveStatus(filePath);

                if (status == FileStatus.NewInWorkdir || status == FileStatus.NewInIndex)
                {
                    // Untracked or newly added - just delete the file
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                    }
                    // Unstage if staged
                    Commands.Unstage(repo, filePath);
                }
                else
                {
                    // Checkout the file from HEAD to revert changes
                    repo.CheckoutPaths("HEAD", new[] { filePath }, new CheckoutOptions
                    {
                        CheckoutModifiers = CheckoutModifiers.Force
                    });
                }
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Switches to a different branch.
    /// </summary>
    public VcsOperationResult SwitchBranch(string repositoryPath, string branchName)
    {
        var result = new VcsOperationResult();

        try
        {
            if (!Directory.Exists(repositoryPath) || !Repository.IsValid(repositoryPath))
            {
                result.ErrorMessage = "Invalid repository path.";
                return result;
            }

            using var repo = new Repository(repositoryPath);

            // Find the branch
            var branch = repo.Branches[branchName];
            if (branch == null)
            {
                // Try to find it as a remote branch and create a local tracking branch
                var remoteBranch = repo.Branches[$"origin/{branchName}"];
                if (remoteBranch != null)
                {
                    branch = repo.CreateBranch(branchName, remoteBranch.Tip);
                    repo.Branches.Update(branch, b => b.TrackedBranch = remoteBranch.CanonicalName);
                }
                else
                {
                    result.ErrorMessage = $"Branch '{branchName}' not found.";
                    return result;
                }
            }

            // Checkout the branch
            Commands.Checkout(repo, branch);

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Creates a new branch and optionally switches to it.
    /// </summary>
    public VcsOperationResult CreateBranch(string repositoryPath, string branchName, bool switchToBranch = true)
    {
        var result = new VcsOperationResult();

        try
        {
            if (!Directory.Exists(repositoryPath) || !Repository.IsValid(repositoryPath))
            {
                result.ErrorMessage = "Invalid repository path.";
                return result;
            }

            using var repo = new Repository(repositoryPath);

            // Check if branch already exists
            if (repo.Branches[branchName] != null)
            {
                result.ErrorMessage = $"Branch '{branchName}' already exists.";
                return result;
            }

            // Create the branch from HEAD
            var branch = repo.CreateBranch(branchName);

            if (switchToBranch)
            {
                Commands.Checkout(repo, branch);
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Gets the content of a file at a specific revision.
    /// </summary>
    public string? GetFileContentAtRevision(string repositoryPath, string filePath, string? revision = null)
    {
        try
        {
            if (!Directory.Exists(repositoryPath) || !Repository.IsValid(repositoryPath))
            {
                return null;
            }

            using var repo = new Repository(repositoryPath);

            // Default to HEAD if no revision specified
            var targetRevision = string.IsNullOrEmpty(revision) || revision.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
                ? "HEAD"
                : revision;

            // Get the commit using the same resolution logic as other methods
            var commit = ResolveToCommit(repo, targetRevision);
            if (commit == null)
            {
                return null;
            }

            // Normalize the file path (use forward slashes for git)
            var normalizedPath = filePath.Replace('\\', '/');

            // Find the file in the commit tree
            var treeEntry = commit[normalizedPath];
            if (treeEntry == null || treeEntry.TargetType != TreeEntryTargetType.Blob)
            {
                return null;
            }

            var blob = (Blob)treeEntry.Target;
            using var contentStream = blob.GetContentStream();
            using var reader = new StreamReader(contentStream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetFileContentAtRevision", ex);
            return null;
        }
    }

    /// <summary>
    /// Merges changes from a source branch into the current working copy.
    /// </summary>
    public VcsMergeResult MergeBranch(string repositoryPath, string sourceBranch)
    {
        var result = new VcsMergeResult();
        try
        {
            if (!Directory.Exists(repositoryPath) || !Repository.IsValid(repositoryPath))
            {
                result.ErrorMessage = "Invalid repository path.";
                return result;
            }

            using var repo = new Repository(repositoryPath);

            var branch = repo.Branches[sourceBranch];
            if (branch == null)
            {
                result.ErrorMessage = $"Branch '{sourceBranch}' not found.";
                return result;
            }

            var sig = new Signature("MLQT User", "user@mlqt.local", DateTimeOffset.Now);
            var mergeResult = repo.Merge(branch, sig, new MergeOptions
            {
                FastForwardStrategy = FastForwardStrategy.Default
            });

            result.SourceBranch = sourceBranch;

            switch (mergeResult.Status)
            {
                case MergeStatus.UpToDate:
                    result.Success = true;
                    result.HasChanges = false;
                    break;

                case MergeStatus.FastForward:
                case MergeStatus.NonFastForward:
                    result.Success = true;
                    result.HasChanges = true;
                    result.ModifiedFiles = repo.RetrieveStatus(new StatusOptions())
                        .Staged
                        .Select(s => Path.Combine(repositoryPath, s.FilePath))
                        .ToList();
                    break;

                case MergeStatus.Conflicts:
                    result.Success = true;
                    result.HasConflicts = true;
                    result.HasChanges = true;
                    result.ConflictedFiles = repo.RetrieveStatus(new StatusOptions())
                        .Where(s => s.State == FileStatus.Conflicted)
                        .Select(s => Path.Combine(repositoryPath, s.FilePath.Replace('/', Path.DirectorySeparatorChar)))
                        .ToList();
                    break;
            }
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("MergeBranch", ex);
            result.ErrorMessage = ex.Message;
        }
        return result;
    }

    /// <summary>
    /// Resolves a conflict in a specific file.
    /// AcceptIncoming / KeepMine check out the appropriate version and stage it.
    /// MarkResolved stages the manually-edited file as-is.
    /// </summary>
    public VcsOperationResult ResolveConflict(string repositoryPath, string filePath, ConflictResolutionChoice choice)
    {
        var result = new VcsOperationResult();
        try
        {
            if (!Directory.Exists(repositoryPath) || !Repository.IsValid(repositoryPath))
            {
                result.ErrorMessage = "Invalid repository path.";
                return result;
            }

            using var repo = new Repository(repositoryPath);
            var relPath = Path.GetRelativePath(repositoryPath, filePath).Replace('\\', '/');

            switch (choice)
            {
                case ConflictResolutionChoice.AcceptIncoming:
                    repo.CheckoutPaths("MERGE_HEAD", new[] { relPath },
                        new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });
                    Commands.Stage(repo, relPath);
                    break;

                case ConflictResolutionChoice.KeepMine:
                    repo.CheckoutPaths("HEAD", new[] { relPath },
                        new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });
                    Commands.Stage(repo, relPath);
                    break;

                case ConflictResolutionChoice.MarkResolved:
                    Commands.Stage(repo, relPath);
                    break;
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("ResolveConflict", ex);
            result.ErrorMessage = ex.Message;
        }
        return result;
    }

    /// <summary>
    /// Returns the "ours" and "theirs" versions of a conflicted file from the Git index.
    /// </summary>
    public (string? ours, string? theirs) GetConflictVersions(string repositoryPath, string filePath)
    {
        try
        {
            if (!Directory.Exists(repositoryPath) || !Repository.IsValid(repositoryPath))
                return (null, null);

            using var repo = new Repository(repositoryPath);
            var relPath = Path.GetRelativePath(repositoryPath, filePath).Replace('\\', '/');
            var conflict = repo.Index.Conflicts[relPath];
            if (conflict == null)
                return (null, null);

            var ours = conflict.Ours != null
                ? repo.Lookup<Blob>(conflict.Ours.Id)?.GetContentText()
                : null;
            var theirs = conflict.Theirs != null
                ? repo.Lookup<Blob>(conflict.Theirs.Id)?.GetContentText()
                : null;

            return (ours, theirs);
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetConflictVersions", ex);
            return (null, null);
        }
    }

    /// <summary>
    /// Pushes the current (or named) branch to its tracked remote.
    /// Shells out to git.exe so that all configured credential helpers
    /// (Git Credential Manager, GitHub Desktop, SSH keys, etc.) are used.
    /// </summary>
    public VcsOperationResult Push(string repositoryPath, string? branchName = null)
    {
        var result = new VcsOperationResult();
        try
        {
            if (!Directory.Exists(repositoryPath) || !Repository.IsValid(repositoryPath))
            {
                result.ErrorMessage = "Invalid repository path.";
                return result;
            }

            // Determine remote name from LibGit2Sharp so we can pass it explicitly.
            // Falls back to "origin" if no tracking branch is configured.
            string remoteName = "origin";
            string? localBranchName = branchName;
            using (var repo = new Repository(repositoryPath))
            {
                var branch = branchName != null ? repo.Branches[branchName] : repo.Head;
                if (branch != null)
                {
                    remoteName = branch.TrackedBranch?.RemoteName ?? "origin";
                    localBranchName ??= branch.FriendlyName;
                }
            }

            // Shell out to git push — uses the full Git credential stack
            // (GCM, GitHub Desktop, SSH agent, etc.) which LibGit2Sharp bypasses.
            var args = localBranchName != null
                ? $"push {remoteName} {localBranchName}"
                : "push";

            var (exitCode, stdout, stderr) = RunGitCommand(repositoryPath, args);

            if (exitCode == 0)
            {
                result.Success = true;
            }
            else
            {
                // git writes progress/errors to stderr; stdout is rarely useful on failure
                var message = stderr.Trim();
                if (string.IsNullOrEmpty(message))
                    message = stdout.Trim();
                result.ErrorMessage = string.IsNullOrEmpty(message) ? $"git push exited with code {exitCode}." : message;
            }
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("Push", ex);
            result.ErrorMessage = ex.Message;
        }
        return result;
    }

    /// <summary>
    /// Rebases the current branch onto the given target branch by replaying local commits on top.
    /// Shells out to git.exe. If conflicts occur the rebase is suspended and the conflicted
    /// files are returned; call ContinueRebase or AbortRebase to proceed.
    /// </summary>
    public VcsMergeResult Rebase(string repositoryPath, string targetBranch)
    {
        var result = new VcsMergeResult { SourceBranch = targetBranch };
        try
        {
            if (!Directory.Exists(repositoryPath) || !Repository.IsValid(repositoryPath))
            {
                result.ErrorMessage = "Invalid repository path.";
                return result;
            }

            var (exitCode, stdout, stderr) = RunGitCommand(repositoryPath, $"rebase {targetBranch}");

            if (exitCode == 0)
            {
                result.Success = true;
                result.HasChanges = true;
            }
            else
            {
                result = ParseRebaseConflictResult(repositoryPath, stdout, stderr, targetBranch);
            }
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("Rebase", ex);
            result.ErrorMessage = ex.Message;
        }
        return result;
    }

    /// <summary>
    /// Continues a suspended rebase after the user has resolved conflicts.
    /// May return further conflicts if the next replayed commit also conflicts.
    /// </summary>
    public VcsMergeResult ContinueRebase(string repositoryPath)
    {
        var result = new VcsMergeResult();
        try
        {
            if (!Directory.Exists(repositoryPath) || !Repository.IsValid(repositoryPath))
            {
                result.ErrorMessage = "Invalid repository path.";
                return result;
            }

            var (exitCode, stdout, stderr) = RunGitCommand(repositoryPath, "rebase --continue");

            if (exitCode == 0)
            {
                result.Success = true;
                result.HasChanges = true;
            }
            else
            {
                result = ParseRebaseConflictResult(repositoryPath, stdout, stderr, null);
            }
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("ContinueRebase", ex);
            result.ErrorMessage = ex.Message;
        }
        return result;
    }

    /// <summary>
    /// Aborts the in-progress rebase and restores the working copy to its pre-rebase state.
    /// </summary>
    public VcsOperationResult AbortRebase(string repositoryPath)
    {
        var result = new VcsOperationResult();
        try
        {
            if (!Directory.Exists(repositoryPath) || !Repository.IsValid(repositoryPath))
            {
                result.ErrorMessage = "Invalid repository path.";
                return result;
            }

            var (exitCode, stdout, stderr) = RunGitCommand(repositoryPath, "rebase --abort");
            if (exitCode == 0)
            {
                result.Success = true;
            }
            else
            {
                var msg = stderr.Trim();
                if (string.IsNullOrEmpty(msg)) msg = stdout.Trim();
                result.ErrorMessage = string.IsNullOrEmpty(msg) ? "git rebase --abort failed." : msg;
            }
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("AbortRebase", ex);
            result.ErrorMessage = ex.Message;
        }
        return result;
    }

    /// <summary>
    /// Force-pushes the current (or named) branch using --force-with-lease.
    /// Required after a rebase because history is rewritten.
    /// </summary>
    public VcsOperationResult ForcePush(string repositoryPath, string? branchName = null)
    {
        var result = new VcsOperationResult();
        try
        {
            if (!Directory.Exists(repositoryPath) || !Repository.IsValid(repositoryPath))
            {
                result.ErrorMessage = "Invalid repository path.";
                return result;
            }

            string remoteName = "origin";
            string? localBranchName = branchName;
            using (var repo = new Repository(repositoryPath))
            {
                var branch = branchName != null ? repo.Branches[branchName] : repo.Head;
                if (branch != null)
                {
                    remoteName = branch.TrackedBranch?.RemoteName ?? "origin";
                    localBranchName ??= branch.FriendlyName;
                }
            }

            var args = localBranchName != null
                ? $"push --force-with-lease {remoteName} {localBranchName}"
                : "push --force-with-lease";

            var (exitCode, stdout, stderr) = RunGitCommand(repositoryPath, args);
            if (exitCode == 0)
            {
                result.Success = true;
            }
            else
            {
                var message = stderr.Trim();
                if (string.IsNullOrEmpty(message)) message = stdout.Trim();
                result.ErrorMessage = string.IsNullOrEmpty(message)
                    ? $"git push --force-with-lease exited with code {exitCode}."
                    : message;
            }
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("ForcePush", ex);
            result.ErrorMessage = ex.Message;
        }
        return result;
    }

    public bool IsBranchPushed(string repositoryPath)
    {
        try
        {
            if (!Repository.IsValid(repositoryPath)) return false;
            using var repo = new Repository(repositoryPath);
            if (repo.Info.IsHeadDetached) return false;
            var head = repo.Head;
            if (head.TrackedBranch == null || head.Tip == null) return false;
            return (head.TrackingDetails.AheadBy ?? 0) == 0;
        }
        catch
        {
            return false;
        }
    }

    public string? GetPullRequestUrl(string repositoryPath, string? baseBranch = null)
    {
        try
        {
            if (!Repository.IsValid(repositoryPath)) return null;
            using var repo = new Repository(repositoryPath);
            if (repo.Info.IsHeadDetached) return null;

            var currentBranch = repo.Head.FriendlyName;
            if (string.IsNullOrEmpty(currentBranch)) return null;

            // Get the remote URL
            var (remoteExit, remoteStdout, _) = RunGitCommand(repositoryPath, "remote get-url origin");
            if (remoteExit != 0) return null;
            var remoteUrl = remoteStdout.Trim();

            if (!TryParseRemoteUrl(remoteUrl, out var host, out var repoPath)) return null;
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(repoPath)) return null;

            // Auto-detect base branch from the remote's symbolic HEAD (no network call needed)
            if (string.IsNullOrEmpty(baseBranch))
            {
                var (symExit, symOut, _) = RunGitCommand(repositoryPath, "symbolic-ref refs/remotes/origin/HEAD");
                if (symExit == 0)
                {
                    // Output is e.g. "refs/remotes/origin/main"
                    var refParts = symOut.Trim().Split('/');
                    baseBranch = refParts.LastOrDefault();
                }
                // Fallback: check for common default branch names
                if (string.IsNullOrEmpty(baseBranch))
                {
                    foreach (var candidate in new[] { "main", "master", "develop" })
                    {
                        if (repo.Branches[candidate] != null)
                        {
                            baseBranch = candidate;
                            break;
                        }
                    }
                }
                baseBranch ??= "main";
            }

            var branch = Uri.EscapeDataString(currentBranch);
            var @base = Uri.EscapeDataString(baseBranch);

            // GitHub (including GitHub Enterprise with any hostname)
            if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase))
            {
                return $"https://{host}/{repoPath}/compare/{@base}...{branch}?expand=1";
            }

            // GitLab (cloud and self-hosted — hostname often contains "gitlab")
            if (host.Equals("gitlab.com", StringComparison.OrdinalIgnoreCase) ||
                host.Contains("gitlab", StringComparison.OrdinalIgnoreCase))
            {
                return $"https://{host}/{repoPath}/-/merge_requests/new" +
                       $"?merge_request[source_branch]={branch}&merge_request[target_branch]={@base}";
            }

            // Bitbucket Cloud
            if (host.Equals("bitbucket.org", StringComparison.OrdinalIgnoreCase))
            {
                return $"https://bitbucket.org/{repoPath}/pull-requests/new?source={branch}&dest={@base}";
            }

            // Azure DevOps (dev.azure.com/{org}/{project}/_git/{repo})
            if (host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
            {
                var parts = repoPath.TrimStart('/').Split('/');
                if (parts.Length >= 4 && parts[2] == "_git")
                {
                    return $"https://dev.azure.com/{parts[0]}/{parts[1]}/_git/{parts[3]}" +
                           $"/pullrequestcreate?sourceRef={branch}&targetRef={@base}";
                }
            }

            // Fallback: GitHub-style compare URL (works for Gitea, Forgejo, and many others)
            return $"https://{host}/{repoPath}/compare/{@base}...{branch}?expand=1";
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseRemoteUrl(string remoteUrl, out string host, out string repoPath)
    {
        host = string.Empty;
        repoPath = string.Empty;

        // SSH format: git@github.com:owner/repo.git
        var sshMatch = System.Text.RegularExpressions.Regex.Match(
            remoteUrl, @"^git@([^:]+):(.+?)(?:\.git)?$");
        if (sshMatch.Success)
        {
            host = sshMatch.Groups[1].Value;
            repoPath = sshMatch.Groups[2].Value;
            return true;
        }

        // HTTPS format: https://github.com/owner/repo.git
        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "https" || uri.Scheme == "http"))
        {
            host = uri.Host;
            repoPath = uri.AbsolutePath.Trim('/');
            if (repoPath.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                repoPath = repoPath[..^4];
            return !string.IsNullOrEmpty(host);
        }

        return false;
    }

    /// <summary>
    /// Shared logic: detects whether a failed git rebase/continue left us in a conflict
    /// state and collects the conflicted file list, or reports a plain error.
    /// </summary>
    private VcsMergeResult ParseRebaseConflictResult(string repositoryPath, string stdout, string stderr, string? targetBranch)
    {
        var result = new VcsMergeResult { SourceBranch = targetBranch };

        var rebaseMergeDir = Path.Combine(repositoryPath, ".git", "rebase-merge");
        var rebaseApplyDir = Path.Combine(repositoryPath, ".git", "rebase-apply");
        var inRebase = Directory.Exists(rebaseMergeDir) || Directory.Exists(rebaseApplyDir);

        if (inRebase)
        {
            result.Success = true; // rebase is in progress, just stopped for conflicts
            result.HasConflicts = true;
            result.HasChanges = true;
            using var repo = new Repository(repositoryPath);
            result.ConflictedFiles = repo.RetrieveStatus(new StatusOptions())
                .Where(s => s.State == FileStatus.Conflicted)
                .Select(s => Path.Combine(repositoryPath, s.FilePath.Replace('/', Path.DirectorySeparatorChar)))
                .ToList();
        }
        else
        {
            var msg = stderr.Trim();
            if (string.IsNullOrEmpty(msg)) msg = stdout.Trim();
            result.ErrorMessage = string.IsNullOrEmpty(msg) ? "git rebase failed." : msg;
        }

        return result;
    }

    /// <summary>
    /// Runs a git command in the specified working directory and returns the exit code,
    /// stdout, and stderr. Shells out to git.exe so that all configured credential helpers
    /// (Git Credential Manager, GitHub Desktop, SSH agent, etc.) are used automatically.
    /// </summary>
    private static (int ExitCode, string Stdout, string Stderr) RunGitCommand(string workingDirectory, string arguments)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }
}
