using SharpSvn;
using RevisionControl.Interfaces;

namespace RevisionControl;

/// <summary>
/// Subversion (SVN) implementation of the revision control system interface.
/// Uses SharpSvn to interact with SVN repositories.
/// </summary>
public class SvnRevisionControlSystem : IRevisionControlSystem
{
    /// <summary>
    /// Checks out a specific revision to a temporary directory.
    /// </summary>
    public bool CheckoutRevision(string repositoryPath, string revision, string outputPath)
    {
        try
        {
            using var client = new SvnClient();

            // Resolve the revision
            var svnRevision = ParseRevision(revision);

            // Create the output directory if it doesn't exist
            Directory.CreateDirectory(outputPath);

            // Perform the checkout
            var uri = GetRepositoryUri(repositoryPath);
            return client.CheckOut(uri, outputPath, new SvnCheckOutArgs { Revision = svnRevision });
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("CheckoutRevision", ex);
            throw new InvalidOperationException(
                $"SVN checkout failed for '{repositoryPath}' at revision '{revision}' to '{outputPath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets the current revision identifier (current revision number).
    /// </summary>
    public string? GetCurrentRevision(string repositoryPath)
    {
        try
        {
            using var client = new SvnClient();

            // Get the working copy info
            if (!client.GetInfo(repositoryPath, out var info))
            {
                return null;
            }

            return info.Revision.ToString();
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetCurrentRevision", ex);
            return null;
        }
    }

    /// <summary>
    /// Validates that the given path is a valid SVN working copy or repository URL.
    /// </summary>
    public bool IsValidRepository(string repositoryPath)
    {
        try
        {
            using var client = new SvnClient();

            // Check if it's a working copy
            if (Directory.Exists(repositoryPath))
            {
                return client.GetInfo(repositoryPath, out _);
            }

            // Check if it's a valid repository URL
            if (Uri.TryCreate(repositoryPath, UriKind.Absolute, out var uri))
            {
                return client.GetInfo(uri, out _);
            }

            return false;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("IsValidRepository", ex);
            return false;
        }
    }

    /// <summary>
    /// Discovers the root of the SVN working copy containing the given path.
    /// SVN 1.7+ stores .svn metadata only at the working copy root, so we walk
    /// up the directory tree and return the highest ancestor that is still a
    /// valid working copy.
    /// </summary>
    public string? FindRepositoryRoot(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return null;
            // SVN 1.7+ stores .svn metadata only at the working copy root.
            var current = new DirectoryInfo(Path.GetFullPath(path));
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, ".svn")))
                    return current.FullName;
                current = current.Parent;
            }
            return null;
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
            using var client = new SvnClient();

            var svnRevision = ParseRevision(revision);

            // Get the repository root to query logs at repo level (not path level)
            // This ensures we can get log info even if the revision was on a different branch/path
            Uri repoRoot;
            if (Directory.Exists(repositoryPath))
            {
                if (!client.GetInfo(repositoryPath, out var info))
                {
                    return null;
                }
                repoRoot = info.RepositoryRoot;
            }
            else
            {
                var uri = GetRepositoryUri(repositoryPath);
                if (!client.GetInfo(uri, out var info))
                {
                    return null;
                }
                repoRoot = info.RepositoryRoot;
            }

            // Get log information for the revision at repo root level
            var logArgs = new SvnLogArgs
            {
                Start = svnRevision,
                End = svnRevision
            };

            SvnLogEventArgs? logEntry = null;
            client.Log(repoRoot, logArgs, (sender, e) => { logEntry = e; });

            if (logEntry == null)
            {
                return null;
            }

            return $"{logEntry.LogMessage} (by {logEntry.Author} on {logEntry.Time:yyyy-MM-dd})";
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetRevisionDescription", ex);
            return null;
        }
    }

    /// <summary>
    /// Resolves a revision identifier to its canonical form (revision number).
    /// PREV is handled specially by querying the log for the second-most-recent revision,
    /// since SvnRevision.Previous only works in working copy context and fails against URIs.
    /// </summary>
    public string? ResolveRevision(string repositoryPath, string revision)
    {
        try
        {
            using var client = new SvnClient();
            var uri = GetRepositoryUri(repositoryPath);

            // PREV requires special handling — query the log for the 2nd most recent revision
            if (revision.Equals("PREV", StringComparison.OrdinalIgnoreCase))
            {
                var logArgs = new SvnLogArgs { Limit = 2 };
                var revisions = new List<long>();
                client.Log(uri.Uri, logArgs, (sender, e) => { revisions.Add(e.Revision); });

                if (revisions.Count < 2)
                {
                    return null;
                }

                return revisions[1].ToString();
            }

            var svnRevision = ParseRevision(revision);

            // Get info at the specified revision to resolve it
            SvnInfoEventArgs? info = null;
            client.Info(uri, new SvnInfoArgs { Revision = svnRevision }, (sender, e) => { info = e; });

            if (info == null)
            {
                return null;
            }

            return info.Revision.ToString();
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("ResolveRevision", ex);
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
            using var client = new SvnClient();

            // If checkout doesn't exist or isn't a valid working copy, do initial checkout
            bool isWorkingCopy = false;
            if (Directory.Exists(checkoutPath))
            {
                try
                {
                    isWorkingCopy = client.GetInfo(checkoutPath, out _);
                }
                catch (SvnException)
                {
                    // Directory exists but is not a valid working copy — remove it so
                    // CheckoutRevision can start fresh (it calls Directory.CreateDirectory).
                    Directory.Delete(checkoutPath, recursive: true);
                }
            }

            if (!isWorkingCopy)
            {
                return CheckoutRevision(repositoryPath, revision, checkoutPath);
            }

            // Clean the workspace first (revert changes, remove untracked files)
            if (!CleanWorkspace(checkoutPath))
            {
                return false;
            }

            // Resolve the revision
            var svnRevision = ParseRevision(revision);

            // Update to the specified revision
            return client.Update(checkoutPath, new SvnUpdateArgs { Revision = svnRevision });
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("UpdateExistingCheckout", ex);
            return false;
        }
    }

    /// <summary>
    /// Cleans a workspace by reverting all changes and removing untracked files.
    /// </summary>
    public bool CleanWorkspace(string checkoutPath)
    {
        try
        {
            if (!Directory.Exists(checkoutPath))
            {
                return false;
            }

            using var client = new SvnClient();

            // Verify it's a valid working copy
            if (!client.GetInfo(checkoutPath, out _))
            {
                return false;
            }

            // Revert all changes recursively
            client.Revert(checkoutPath, new SvnRevertArgs { Depth = SvnDepth.Infinity });

            // Get status to find unversioned items
            var unversionedItems = new List<SvnStatusEventArgs>();
            client.Status(checkoutPath, new SvnStatusArgs { Depth = SvnDepth.Infinity },
                (sender, e) =>
                {
                    if (e.LocalContentStatus == SvnStatus.NotVersioned)
                    {
                        unversionedItems.Add(e);
                    }
                });

            // Remove all unversioned files and directories (deeper paths first)
            var itemsToDelete = unversionedItems
                .OrderByDescending(s => s.FullPath.Length)
                .ToList();

            foreach (var item in itemsToDelete)
            {
                try
                {
                    if (File.Exists(item.FullPath))
                    {
                        File.Delete(item.FullPath);
                    }
                    else if (Directory.Exists(item.FullPath))
                    {
                        Directory.Delete(item.FullPath, recursive: true);
                    }
                }
                catch
                {
                    // Continue with other items even if one fails
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
    /// Gets the current branch name from the SVN URL.
    /// Extracts branch from standard SVN layouts: trunk, branches/*, tags/*
    /// </summary>
    public string? GetCurrentBranch(string repositoryPath)
        => GetCurrentBranch(repositoryPath, branchDirectories: null);

    public string? GetCurrentBranch(string repositoryPath, IReadOnlyList<string>? branchDirectories)
    {
        try
        {
            using var client = new SvnClient();

            // Get info for the working copy or URL
            string? uri = null;

            if (Directory.Exists(repositoryPath))
            {
                // It's a working copy
                if (!client.GetInfo(repositoryPath, out var wcInfo))
                {
                    return null;
                }
                // Get the URL from working copy info
                uri = wcInfo.Uri.ToString();
            }
            else if (Uri.TryCreate(repositoryPath, UriKind.Absolute, out var confirmedUri))
            {
                // It's a URL
                uri = confirmedUri.ToString();
            }

            if (uri == null)
            {
                return null;
            }

            // Extract branch from URL path
            return ExtractBranchFromSvnUrl(uri, branchDirectories);
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetCurrentBranch", ex);
            return null;
        }
    }

    /// <summary>
    /// Extracts the branch name from an SVN URL.
    /// Uses the provided branch directories, or falls back to defaults.
    /// The first directory in the list is treated as the trunk equivalent (no subdirectories).
    /// </summary>
    internal string? ExtractBranchFromSvnUrl(string url, IReadOnlyList<string>? branchDirectories = null)
    {
        branchDirectories ??= DefaultBranchDirectories;

        var urlLower = url.ToLowerInvariant();

        foreach (var dir in branchDirectories)
        {
            var dirLower = dir.ToLowerInvariant();

            // Check if this is a trunk-like directory (first entry, or "trunk" by convention)
            // Trunk directories match as a leaf: /trunk or /trunk/ but have no subdirectories
            if (dir == branchDirectories[0] || dirLower == "trunk")
            {
                var trunkIndex = urlLower.IndexOf($"/{dirLower}", StringComparison.Ordinal);
                if (trunkIndex >= 0)
                {
                    // Verify it's a full segment (followed by / or end of string)
                    var afterTrunk = trunkIndex + dirLower.Length + 1;
                    if (afterTrunk >= url.Length || url[afterTrunk] == '/')
                        return dir;
                }
            }
            else
            {
                // Branch-like directory: has subdirectories (e.g., branches/feature-x)
                var result = CheckForBranchName(url, dir);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    private static readonly string[] DefaultBranchDirectories = ["trunk", "branches", "tags"];

    private string? CheckForBranchName(string url, string branchGroupName)
    {
        var urlLower = url.ToLowerInvariant();
        var branchesIndex = urlLower.IndexOf($"/{branchGroupName}/", StringComparison.Ordinal);
        if (branchesIndex >= 0)
        {
            var afterBranches = url.Substring(branchesIndex + $"/{branchGroupName}/".Length);
            var nextSlash = afterBranches.IndexOf('/');
            var branchName = nextSlash >= 0 ? afterBranches.Substring(0, nextSlash) : afterBranches;
            return $"{branchGroupName}/{branchName}";
        }
        return null;
    }

    /// <summary>
    /// Gets log entries (commit history) from the repository.
    /// </summary>
    public List<VcsLogEntry> GetLogEntries(string repositoryPath, VcsLogOptions? options = null)
        => GetLogEntries(repositoryPath, options, branchDirectories: null);

    public List<VcsLogEntry> GetLogEntries(string repositoryPath, VcsLogOptions? options, IReadOnlyList<string>? branchDirectories)
    {
        var entries = new List<VcsLogEntry>();
        options ??= new VcsLogOptions();

        try
        {
            using var client = new SvnClient();
            var uri = GetRepositoryUri(repositoryPath);

            // Get the current branch as a fallback for entries without changed paths
            var currentBranch = ExtractBranchFromSvnUrl(uri.Uri.ToString(), branchDirectories);

            // Configure log args - use revision-based range instead of date-based
            // to ensure full revision properties (author, message) are retrieved.
            // RetrieveChangedPaths is needed to determine the actual branch for each
            // revision, since SVN log follows copy history across branches.
            var logArgs = new SvnLogArgs
            {
                Limit = options.MaxEntries,
                RetrieveChangedPaths = true
            };

            var minEntriesFromSinceFilter = 10;
            var sinceDate = options.Since;
            var untilDate = options.Until;

            // Fetch logs and filter by date in the callback
            // IMPORTANT: We must extract values immediately in the callback because
            // SharpSvn reuses/clears the SvnLogEventArgs object after the callback returns
            client.Log(uri.Uri, logArgs, (sender, e) =>
            {
                // Apply date filters
                if (untilDate.HasValue && e.Time > untilDate.Value.UtcDateTime)
                {
                    return; // Skip entries after the until date
                }

                if (sinceDate.HasValue && e.Time < sinceDate.Value.UtcDateTime)
                {
                    // If we have enough entries, we can stop
                    if (entries.Count >= minEntriesFromSinceFilter)
                    {
                        e.Cancel = true;
                        return;
                    }
                }

                // Extract all values immediately - do not store the event args object
                var message = e.LogMessage?.Trim() ?? "";
                var messageShort = message;
                var newlineIndex = message.IndexOf('\n');
                if (newlineIndex > 0)
                {
                    messageShort = message.Substring(0, newlineIndex).Trim();
                }

                // Determine the actual branch from the changed paths rather than
                // assuming all entries are on the current branch. SVN log follows
                // copy history, so older entries may be from trunk or another branch.
                var branch = currentBranch;
                if (e.ChangedPaths != null && e.ChangedPaths.Count > 0)
                {
                    foreach (var changedPath in e.ChangedPaths)
                    {
                        var pathStr = changedPath.Path;
                        if (!string.IsNullOrEmpty(pathStr))
                        {
                            branch = ExtractBranchFromSvnUrl(pathStr, branchDirectories);
                            if (branch != null)
                                break;
                        }
                    }
                    branch ??= currentBranch;
                }

                var entry = new VcsLogEntry
                {
                    Revision = e.Revision.ToString(),
                    ShortRevision = $"{e.Revision}",
                    Author = e.Author ?? "",
                    AuthorEmail = "", // SVN doesn't typically have email in author field
                    Date = new DateTimeOffset(e.Time),
                    Message = message,
                    MessageShort = messageShort,
                    Branch = branch
                };

                entries.Add(entry);
            });
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
            using var client = new SvnClient();
            var uri = GetRepositoryUri(repositoryPath);
            var svnRevision = ParseRevision(revision);

            // Get the log entry with changed paths
            var logArgs = new SvnLogArgs
            {
                Start = svnRevision,
                End = svnRevision,
                RetrieveChangedPaths = true
            };

            client.Log(uri.Uri, logArgs, (sender, e) =>
            {
                if (e.ChangedPaths != null)
                {
                    foreach (var changedPath in e.ChangedPaths)
                    {
                        // Extract all values immediately - SharpSvn reuses event args
                        var path = changedPath.Path ?? "";
                        var action = changedPath.Action;
                        var copyFromPath = changedPath.CopyFromPath;

                        var changedFile = new VcsChangedFile
                        {
                            Path = path.TrimStart('/'),
                            ChangeType = action switch
                            {
                                SvnChangeAction.Add => VcsChangeType.Added,
                                SvnChangeAction.Delete => VcsChangeType.Deleted,
                                SvnChangeAction.Modify => VcsChangeType.Modified,
                                SvnChangeAction.Replace => VcsChangeType.Modified,
                                _ => VcsChangeType.Modified
                            }
                        };

                        // Check if this is a copy/rename (has CopyFromPath)
                        if (!string.IsNullOrEmpty(copyFromPath))
                        {
                            changedFile.OldPath = copyFromPath.TrimStart('/');
                            changedFile.ChangeType = VcsChangeType.Copied;
                        }

                        changedFiles.Add(changedFile);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetChangedFiles", ex);
        }

        return changedFiles;
    }

    /// <summary>
    /// Parses a revision string to an SvnRevision object.
    /// Supports revision numbers (e.g., "123") and special keywords (e.g., "HEAD").
    /// </summary>
    private SvnRevision ParseRevision(string revision)
    {
        if (string.IsNullOrWhiteSpace(revision))
        {
            return SvnRevision.Head;
        }

        // Try to parse as revision number
        if (long.TryParse(revision, out var revisionNumber))
        {
            return new SvnRevision(revisionNumber);
        }

        // Handle special keywords
        return revision.ToUpperInvariant() switch
        {
            "HEAD" => SvnRevision.Head,
            "BASE" => SvnRevision.Base,
            "COMMITTED" => SvnRevision.Committed,
            "PREV" => SvnRevision.Previous,
            _ => SvnRevision.Head
        };
    }

    /// <summary>
    /// Gets a repository URI from a path (working copy or URL).
    /// </summary>
    private SvnUriTarget GetRepositoryUri(string repositoryPath)
    {
        // Check if it's a local directory first (working copy)
        if (Directory.Exists(repositoryPath))
        {
            using var client = new SvnClient();
            if (client.GetInfo(repositoryPath, out var info))
            {
                return new SvnUriTarget(info.Uri);
            }
        }

        // If it's a URL, use it directly
        if (Uri.TryCreate(repositoryPath, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https" || uri.Scheme == "svn" || uri.Scheme == "file"))
        {
            return new SvnUriTarget(uri);
        }

        // Fallback: try to create a file:// URL from the path
        return new SvnUriTarget(new Uri(Path.GetFullPath(repositoryPath)));
    }

    /// <summary>
    /// Updates the working copy to the latest version from the remote (HEAD).
    /// </summary>
    public VcsUpdateResult UpdateToLatest(string repositoryPath)
    {
        var result = new VcsUpdateResult();

        try
        {
            if (!Directory.Exists(repositoryPath))
            {
                result.ErrorMessage = "Repository path does not exist.";
                return result;
            }

            using var client = new SvnClient();

            // Verify it's a valid working copy
            if (!client.GetInfo(repositoryPath, out var info))
            {
                result.ErrorMessage = "Not a valid SVN working copy.";
                return result;
            }

            // Get the current revision before update
            result.OldRevision = info.Revision.ToString();

            // Perform the update to HEAD
            SvnUpdateResult? updateResult = null;
            var success = client.Update(repositoryPath, new SvnUpdateArgs
            {
                Revision = SvnRevision.Head
            }, out updateResult);

            if (!success)
            {
                result.ErrorMessage = "SVN update failed.";
                return result;
            }

            // Get the new revision
            result.NewRevision = updateResult?.Revision.ToString() ?? result.OldRevision;
            result.HasChanges = result.OldRevision != result.NewRevision;
            result.Success = true;
        }
        catch (SvnException ex)
        {
            RevisionControlLogger.Error("UpdateToLatest", ex);
            result.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("UpdateToLatest", ex);
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
            if (!Directory.Exists(repositoryPath))
            {
                return files;
            }

            using var client = new SvnClient();

            // Verify it's a valid working copy
            if (!client.GetInfo(repositoryPath, out _))
            {
                return files;
            }

            // Get status of all files
            client.Status(repositoryPath, new SvnStatusArgs { Depth = SvnDepth.Infinity },
                (sender, e) =>
                {
                    // Skip unchanged files
                    if (e.LocalContentStatus == SvnStatus.Normal &&
                        e.LocalPropertyStatus == SvnStatus.Normal)
                    {
                        return;
                    }

                    // Extract values immediately - SharpSvn reuses event args
                    var path = Path.GetRelativePath(repositoryPath, e.FullPath);
                    var contentStatus = e.LocalContentStatus;
                    var propStatus = e.LocalPropertyStatus;

                    var file = new VcsWorkingCopyFile
                    {
                        Path = path,
                        IsStaged = false // SVN doesn't have staging
                    };

                    file.Status = contentStatus switch
                    {
                        SvnStatus.Modified => VcsFileStatus.Modified,
                        SvnStatus.Added => VcsFileStatus.Added,
                        SvnStatus.Deleted => VcsFileStatus.Deleted,
                        SvnStatus.Replaced => VcsFileStatus.Modified,
                        SvnStatus.NotVersioned => VcsFileStatus.Untracked,
                        SvnStatus.Conflicted => VcsFileStatus.Conflicted,
                        SvnStatus.Missing => VcsFileStatus.Deleted,
                        // Property-only change (e.g. svn:mergeinfo updated on the repo root during a merge)
                        SvnStatus.Normal when propStatus != SvnStatus.Normal => VcsFileStatus.Modified,
                        _ => VcsFileStatus.Modified
                    };

                    // Include items with content changes AND items with property-only changes
                    // (e.g. svn:mergeinfo on the root must be committed to finalise a merge).
                    // Only skip items that SVN has marked as explicitly ignored.
                    if (contentStatus != SvnStatus.Ignored)
                    {
                        files.Add(file);
                    }
                });

            // SVN's recursive status does not enumerate files inside unversioned directories,
            // so any directory entry with Untracked status needs to be expanded manually into
            // its individual files. Directories with a versioned status (Added, Modified, etc.)
            // are kept as-is: their file children are already returned by SVN's recursive status,
            // and the directory entry itself must appear in the commit list so it can be committed
            // (e.g. a directory Added via merge must be committed before new files can be added to it).
            var filesToRemove = new List<VcsWorkingCopyFile>();
            var filesToAdd = new List<VcsWorkingCopyFile>();
            foreach (var f in files)
            {
                if (Directory.Exists(Path.Combine(repositoryPath, f.Path)) &&
                    f.Status == VcsFileStatus.Untracked)
                {
                    var newFiles = Directory.GetFiles(Path.Combine(repositoryPath, f.Path), "*", SearchOption.AllDirectories);
                    foreach (var newFile in newFiles)
                    {
                        var relativePath = Path.GetRelativePath(repositoryPath, newFile);
                        filesToAdd.Add(new VcsWorkingCopyFile
                        {
                            Path = relativePath,
                            Status = VcsFileStatus.Untracked,
                            IsStaged = false
                        });
                    }
                    filesToRemove.Add(f);
                }
            }
            foreach (var f in filesToRemove)
            {
                files.Remove(f);
            }
            // Deduplicate: SVN may have already returned some of these files individually
            var existingPaths = new HashSet<string>(files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            foreach (var f in filesToAdd)
            {
                if (existingPaths.Add(f.Path))
                    files.Add(f);
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
    /// For SVN, this looks for standard branch paths in the repository.
    /// </summary>
    public List<VcsBranchInfo> GetBranches(string repositoryPath, bool includeRemote = false)
        => GetBranches(repositoryPath, includeRemote, branchDirectories: null);

    public List<VcsBranchInfo> GetBranches(string repositoryPath, bool includeRemote, IReadOnlyList<string>? branchDirectories)
    {
        branchDirectories ??= DefaultBranchDirectories;
        var branches = new List<VcsBranchInfo>();

        try
        {
            using var client = new SvnClient();
            var uri = GetRepositoryUri(repositoryPath);
            var currentBranch = GetCurrentBranch(repositoryPath, branchDirectories);

            // Get the repository root
            if (!client.GetInfo(uri, out var info))
            {
                return branches;
            }

            var repoRoot = info.RepositoryRoot;

            foreach (var branchPath in branchDirectories)
            {
                try
                {
                    var branchUri = new Uri(repoRoot, branchPath);

                    // The first entry is the trunk equivalent — it's a single branch, not a container
                    if (branchPath == branchDirectories[0])
                    {
                        if (client.GetInfo(branchUri, out _))
                        {
                            branches.Add(new VcsBranchInfo
                            {
                                Name = branchPath,
                                IsCurrent = currentBranch == branchPath,
                                IsRemote = true
                            });
                        }
                    }
                    else
                    {
                        // List subdirectories for branches/tags/etc.
                        var listArgs = new SvnListArgs { Depth = SvnDepth.Children };
                        client.List(branchUri, listArgs, (sender, e) =>
                        {
                            if (e.Entry.NodeKind == SvnNodeKind.Directory && !string.IsNullOrEmpty(e.Name))
                            {
                                var name = $"{branchPath}/{e.Name}";
                                branches.Add(new VcsBranchInfo
                                {
                                    Name = name,
                                    IsCurrent = name == currentBranch,
                                    IsRemote = true,
                                    LastCommit = e.Entry.Revision.ToString()
                                });
                            }
                        });
                    }
                }
                catch
                {
                    // Branch path doesn't exist, skip
                }
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
            if (!Directory.Exists(repositoryPath))
            {
                result.ErrorMessage = "Repository path does not exist.";
                return result;
            }

            using var client = new SvnClient();

            // Verify it's a valid working copy
            if (!client.GetInfo(repositoryPath, out _))
            {
                result.ErrorMessage = "Not a valid SVN working copy.";
                return result;
            }

            // Prepare files to commit
            var pathsToCommit = new List<string>();
            if (filesToCommit != null)
            {
                foreach (var file in filesToCommit)
                {
                    pathsToCommit.Add(Path.Combine(repositoryPath, file));
                }
            }
            else
            {
                pathsToCommit.Add(repositoryPath);
            }

            // If the repository root has uncommitted property changes (e.g. svn:mergeinfo updated
            // during a merge), always include it in this commit regardless of what the caller
            // selected. Leaving merge metadata uncommitted puts the working copy in an inconsistent
            // state that blocks subsequent commits.
            SvnStatusEventArgs? rootStatus = null;
            client.Status(repositoryPath, new SvnStatusArgs { Depth = SvnDepth.Empty },
                (_, e) => { rootStatus = e; });
            if (rootStatus?.LocalPropertyStatus != SvnStatus.Normal &&
                !pathsToCommit.Contains(repositoryPath, StringComparer.OrdinalIgnoreCase))
            {
                pathsToCommit.Add(repositoryPath);
            }

            // Check for unversioned files and add them first.
            // SVN requires files to be explicitly added before they can be committed.
            // Also track any parent directories that were added so they can be included in the commit.
            var addedParentDirs = new HashSet<string>();
            // Files that cannot be in this commit because their parent directory was Added via merge.
            // SVN forbids adding new files to a merge-committed directory in the same transaction.
            var filesToSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var pathsList = pathsToCommit.ToList();
            var totalPaths = pathsList.Count;
            for (int i = 0; i < pathsList.Count; i++)
            {
                var path = pathsList[i];
                if (i % 100 == 0 || i == totalPaths - 1)
                    progress?.Report($"Preparing file {i + 1} of {totalPaths}...");

                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    // File/directory is missing from disk. Schedule it for deletion in SVN
                    // so the commit removes it from the repository. Without this, SVN treats
                    // the file as "missing" but not "deleted" and the commit either ignores
                    // it or fails.
                    try
                    {
                        SvnStatusEventArgs? missingStatus = null;
                        client.Status(path, new SvnStatusArgs { Depth = SvnDepth.Empty },
                            (_, e) => { missingStatus = e; });

                        if (missingStatus?.LocalContentStatus == SvnStatus.Missing)
                        {
                            client.Delete(path, new SvnDeleteArgs { Force = true });
                        }
                    }
                    catch (SvnException)
                    {
                        // If delete fails (e.g., already scheduled), continue — the commit
                        // will handle it or report the error.
                    }
                    continue;
                }

                {
                    try
                    {
                        // Check if the file is already versioned
                        SvnStatusEventArgs? status = null;
                        client.Status(path, new SvnStatusArgs { Depth = SvnDepth.Empty },
                            (sender, e) => { status = e; });

                        if (status?.LocalContentStatus == SvnStatus.NotVersioned)
                        {
                            var parentDir = Path.GetDirectoryName(path);

                            // If the parent directory was itself added via SVN merge (has svn:mergeinfo),
                            // adding a new file to it in the same commit causes an "out of date" server
                            // error. Mark the file for a separate commit instead.
                            // Skip this check for directories we ourselves added during this commit
                            // preparation (tracked in addedParentDirs) — those are new directories
                            // created by the formatter, not by a merge operation. Without this
                            // exclusion, GetProperty can return inherited svn:mergeinfo from an
                            // ancestor directory, causing false positives (e.g., after branch creation).
                            if (!string.IsNullOrEmpty(parentDir) &&
                                !parentDir.Equals(repositoryPath, StringComparison.OrdinalIgnoreCase) &&
                                !addedParentDirs.Contains(parentDir) &&
                                IsDirectoryAddedViaMerge(client, parentDir))
                            {
                                filesToSkip.Add(path);
                                continue;
                            }

                            // File is genuinely new. Before adding it, explicitly check and stage any
                            // unversioned parent directories. We do this BEFORE the svn add so that we
                            // can detect them while they are still NotVersioned — querying status after
                            // the add is unreliable in SharpSvn. AddParentDirectories also records them
                            // in addedParentDirs so they appear in pathsToCommit.
                            if (!string.IsNullOrEmpty(parentDir) &&
                                !parentDir.Equals(repositoryPath, StringComparison.OrdinalIgnoreCase))
                            {
                                AddParentDirectories(client, parentDir, repositoryPath, addedParentDirs);
                            }

                            client.Add(path, new SvnAddArgs { AddParents = true });
                        }
                    }
                    catch (SvnException svnError)
                    {
                        // SVN_ERR_WC_PATH_NOT_FOUND means the file's parent directory is not part of the
                        // working copy at all — add parent directories explicitly first.
                        if (svnError.SvnErrorCode == SvnErrorCode.SVN_ERR_WC_PATH_NOT_FOUND)
                        {
                            var parentDir = Path.GetDirectoryName(path);
                            if (!string.IsNullOrEmpty(parentDir))
                            {
                                AddParentDirectories(client, parentDir, repositoryPath, addedParentDirs);
                            }
                            try
                            {
                                client.Add(path, new SvnAddArgs { AddParents = true });
                            }
                            catch (SvnException addError)
                            {
                                // Ignore "already under version control" errors
                                if (addError.SvnErrorCode != SvnErrorCode.SVN_ERR_ENTRY_EXISTS)
                                {
                                    result.ErrorMessage = BuildFullSvnErrorMessage(addError);
                                    return result;
                                }
                            }
                        }
                        else
                        {
                            result.ErrorMessage = BuildFullSvnErrorMessage(svnError);
                            return result;
                        }
                    }
                }
            }

            // Remove files that must be committed separately (new files in merge-added directories).
            // Record them as skipped so the UI can offer a follow-up commit.
            foreach (var skipPath in filesToSkip)
            {
                pathsToCommit.Remove(skipPath);
                result.SkippedFiles.Add(Path.GetRelativePath(repositoryPath, skipPath));
            }

            // Add any parent directories that were scheduled for add to the commit paths
            foreach (var dir in addedParentDirs)
            {
                if (!pathsToCommit.Contains(dir))
                {
                    pathsToCommit.Add(dir);
                }
            }

            // If everything was skipped there is nothing left to commit. Tell the user they
            // must include the parent directory in the commit first.
            if (pathsToCommit.Count == 0)
            {
                if (result.SkippedFiles.Count > 0)
                    result.ErrorMessage =
                        "All selected files are in directories that were added via merge and cannot be " +
                        "added in the same transaction. Please also select the parent directory so it is " +
                        "committed first, then commit these files separately.";
                else
                    result.ErrorMessage = "No files to commit.";
                return result;
            }

            // Perform the commit with progress reporting via SVN notifications.
            // SVN commit has two phases: processing files (CommitModified/Added/Deleted)
            // and transmitting data (CommitSendData). Track both phases.
            SvnCommitResult? commitResult = null;
            var commitArgs = new SvnCommitArgs { LogMessage = message };
            var processedCount = 0;
            var sentCount = 0;
            var totalToCommit = pathsToCommit.Count;
            progress?.Report($"Committing {totalToCommit} files to server...");
            commitArgs.Notify += (_, e) =>
            {
                switch (e.Action)
                {
                    case SvnNotifyAction.CommitModified:
                    case SvnNotifyAction.CommitAdded:
                    case SvnNotifyAction.CommitDeleted:
                    case SvnNotifyAction.CommitReplaced:
                    {
                        var count = Interlocked.Increment(ref processedCount);
                        if (count % 100 == 0 || count == totalToCommit)
                            progress?.Report($"Processing file {count} of {totalToCommit}...");
                        break;
                    }
                    case SvnNotifyAction.CommitSendData:
                    {
                        var count = Interlocked.Increment(ref sentCount);
                        if (count % 100 == 0)
                            progress?.Report($"Sending data ({count} files sent)...");
                        break;
                    }
                }
            };
            var success = client.Commit(pathsToCommit, commitArgs, out commitResult);

            if (!success || commitResult == null)
            {
                result.ErrorMessage = "SVN commit failed.";
                return result;
            }

            //Commit succeeded so run update
            UpdateToLatest(repositoryPath);
            
            result.Success = true;
            result.NewRevision = commitResult.Revision.ToString();
        }
        catch (SvnException ex)
        {
            RevisionControlLogger.Error("Commit", ex);
            result.ErrorMessage = BuildFullSvnErrorMessage(ex);
            result.IsOutOfDate = IsOutOfDateError(ex);
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("Commit", ex);
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Builds a full error message by walking the SVN exception inner chain.
    /// SvnException.Message only returns the top-level summary line; the details live in inner exceptions.
    /// </summary>
    private static string BuildFullSvnErrorMessage(Exception ex)
    {
        var messages = new List<string>();
        var current = ex;
        while (current != null)
        {
            if (!string.IsNullOrWhiteSpace(current.Message) && !messages.Contains(current.Message))
                messages.Add(current.Message);
            current = current.InnerException;
        }
        return string.Join("\n", messages);
    }

    /// <summary>
    /// Returns true if the exception (or any inner exception) indicates the working copy is out of date.
    /// </summary>
    private static bool IsOutOfDateError(SvnException ex)
    {
        Exception? current = ex;
        while (current != null)
        {
            if (current is SvnException svnEx &&
                svnEx.SvnErrorCode == SvnErrorCode.SVN_ERR_FS_TXN_OUT_OF_DATE)
                return true;
            current = current.InnerException;
        }
        // Fallback: check message text in case error code is wrapped differently
        return BuildFullSvnErrorMessage(ex).Contains("out of date", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if the directory at <paramref name="directoryPath"/> was added to the working
    /// copy via an SVN merge operation. Such directories carry <c>svn:mergeinfo</c> in the WC.
    /// New files cannot be added to a merge-committed directory in the same commit transaction.
    /// </summary>
    private static bool IsDirectoryAddedViaMerge(SvnClient client, string directoryPath)
    {
        try
        {
            SvnStatusEventArgs? status = null;
            client.Status(directoryPath, new SvnStatusArgs { Depth = SvnDepth.Empty },
                (_, e) => { status = e; });

            if (status?.LocalContentStatus != SvnStatus.Added)
                return false;

            // The directory is scheduled for addition. When SVN merges a branch that introduces
            // a new directory, the child directory is Added but svn:mergeinfo is only set on the
            // merge root — not on the child itself. So we cannot rely on property checks here.
            // The caller already excludes directories we created ourselves (tracked in
            // addedParentDirs), so any remaining Added directory came from a merge or manual
            // svn copy. In both cases, committing a new unversioned file inside it in the same
            // transaction would fail with an "out of date" error, so we must skip.
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void AddParentDirectories(SvnClient client, string path, string repositoryRoot, HashSet<string> addedDirs)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return;
        }

        // Don't go above the repository root
        if (path.Equals(repositoryRoot, StringComparison.OrdinalIgnoreCase) ||
            !path.StartsWith(repositoryRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            // Check if the directory is already versioned
            SvnStatusEventArgs? status = null;
            client.Status(path, new SvnStatusArgs { Depth = SvnDepth.Empty },
                (sender, e) => { status = e; });

            if (status?.LocalContentStatus == SvnStatus.NotVersioned)
            {
                // Add unversioned directory to SVN - use Depth.Empty to only add the directory,
                // not its contents (files will be added separately)
                client.Add(path, new SvnAddArgs { AddParents = true, Depth = SvnDepth.Empty });
                addedDirs.Add(path);
            }
        }
        catch (SvnException)
        {
            // Parent directory may not be versioned, recursively add it first
            var parentDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parentDir))
            {
                AddParentDirectories(client, parentDir, repositoryRoot, addedDirs);
                // Now try to add this directory again
                try
                {
                    client.Add(path, new SvnAddArgs { AddParents = true, Depth = SvnDepth.Empty });
                    addedDirs.Add(path);
                }
                catch (SvnException)
                {
                    // Ignore if already added
                }
            }
        }
    }

    /// <summary>
    /// Reverts changes to specific files in the working copy.
    /// </summary>
    public VcsOperationResult RevertFiles(string repositoryPath, IEnumerable<string> filesToRevert)
    {
        var result = new VcsOperationResult();

        try
        {
            if (!Directory.Exists(repositoryPath))
            {
                result.ErrorMessage = "Repository path does not exist.";
                return result;
            }

            using var client = new SvnClient();

            foreach (var file in filesToRevert)
            {
                var fullPath = Path.Combine(repositoryPath, file);

                // Check if file is unversioned - just delete it
                SvnStatusEventArgs? status = null;
                client.Status(fullPath, new SvnStatusArgs { Depth = SvnDepth.Empty },
                    (sender, e) => { status = e; });

                if (status?.LocalContentStatus == SvnStatus.NotVersioned)
                {
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                    }
                    else if (Directory.Exists(fullPath))
                    {
                        Directory.Delete(fullPath, recursive: true);
                    }
                }
                else
                {
                    // Revert versioned file
                    client.Revert(fullPath, new SvnRevertArgs { Depth = SvnDepth.Empty });
                }
            }

            result.Success = true;
        }
        catch (SvnException ex)
        {
            RevisionControlLogger.Error("RevertFiles", ex);
            result.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("RevertFiles", ex);
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Switches to a different branch.
    /// In SVN, this performs an svn switch to the branch URL.
    /// </summary>
    public VcsOperationResult SwitchBranch(string repositoryPath, string branchName)
    {
        var result = new VcsOperationResult();

        try
        {
            if (!Directory.Exists(repositoryPath))
            {
                result.ErrorMessage = "Repository path does not exist.";
                return result;
            }

            using var client = new SvnClient();

            // Get repository root
            if (!client.GetInfo(repositoryPath, out var info))
            {
                result.ErrorMessage = "Not a valid SVN working copy.";
                return result;
            }

            var repoRoot = info.RepositoryRoot;

            // Construct the branch URL
            var branchUri = new Uri(repoRoot, branchName);

            // Verify the branch exists
            if (!client.GetInfo(branchUri, out _))
            {
                result.ErrorMessage = $"Branch '{branchName}' not found.";
                return result;
            }

            // Perform the switch
            var success = client.Switch(repositoryPath, branchUri);
            if (!success)
            {
                result.ErrorMessage = "SVN switch failed.";
                return result;
            }

            result.Success = true;
        }
        catch (SvnException ex)
        {
            RevisionControlLogger.Error("SwitchBranch", ex);
            result.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("SwitchBranch", ex);
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Creates a new branch by copying the current working copy URL.
    /// In SVN, this creates a server-side copy.
    /// </summary>
    public VcsOperationResult CreateBranch(string repositoryPath, string branchName, bool switchToBranch = true)
        => CreateBranch(repositoryPath, branchName, switchToBranch, branchDirectories: null);

    public VcsOperationResult CreateBranch(string repositoryPath, string branchName, bool switchToBranch, IReadOnlyList<string>? branchDirectories)
    {
        branchDirectories ??= DefaultBranchDirectories;
        var result = new VcsOperationResult();

        try
        {
            if (!Directory.Exists(repositoryPath))
            {
                result.ErrorMessage = "Repository path does not exist.";
                return result;
            }

            using var client = new SvnClient();

            // Get current URL
            if (!client.GetInfo(repositoryPath, out var info))
            {
                result.ErrorMessage = "Not a valid SVN working copy.";
                return result;
            }

            var repoRoot = info.RepositoryRoot;
            var currentUri = info.Uri;

            // Construct the new branch URL
            // Check if the name already starts with a known branch directory prefix
            var hasPrefix = branchDirectories.Any(d => branchName.StartsWith($"{d}/", StringComparison.OrdinalIgnoreCase));
            // Default to the second directory (first branch-like directory) for new branches
            var defaultBranchDir = branchDirectories.Count > 1 ? branchDirectories[1] : "branches";
            var branchUri = hasPrefix
                ? new Uri(repoRoot, branchName)
                : new Uri(repoRoot, $"{defaultBranchDir}/{branchName}");

            // Check if branch already exists
            try
            {
                if (client.GetInfo(branchUri, out _))
                {
                    result.ErrorMessage = $"Branch '{branchName}' already exists.";
                    return result;
                }                
            }
            catch (Exception ex)
            {
                //SVN throws an exception if the path doesn't exist, so we can ignore it here
                RevisionControlLogger.Debug($"CreateBranch: Branch existence check threw (expected for new branches): {ex.Message}");
            }

            // Create the branch (server-side copy)
            var copyArgs = new SvnCopyArgs
            {
                LogMessage = $"Create branch: {branchName}"
            };
            var success = client.RemoteCopy(currentUri, branchUri, copyArgs);

            if (!success)
            {
                result.ErrorMessage = "Failed to create branch.";
                return result;
            }

            // Switch to the new branch if requested
            if (switchToBranch)
            {
                var switchResult = SwitchBranch(repositoryPath, branchUri.ToString().Substring(repoRoot.ToString().Length));
                if (!switchResult.Success)
                {
                    result.ErrorMessage = $"Branch created but failed to switch: {switchResult.ErrorMessage}";
                    return result;
                }
            }

            result.Success = true;
        }
        catch (SvnException ex)
        {
            RevisionControlLogger.Error("CreateBranch", ex);
            result.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("CreateBranch", ex);
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
            if (!Directory.Exists(repositoryPath))
            {
                return null;
            }

            using var client = new SvnClient();

            // Verify it's a valid working copy
            if (!client.GetInfo(repositoryPath, out _))
            {
                return null;
            }

            var fullPath = Path.Combine(repositoryPath, filePath);

            // Determine the revision
            var svnRevision = string.IsNullOrEmpty(revision) || revision.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
                ? SvnRevision.Base  // BASE is the last committed version in SVN
                : long.TryParse(revision, out var revNum)
                    ? new SvnRevision(revNum)
                    : SvnRevision.Base;

            using var memoryStream = new MemoryStream();

            if (svnRevision != SvnRevision.Base && File.Exists(fullPath))
            {
                // For specific revisions, use the local path with HEAD as the peg revision
                // (to identify the file) and the requested revision as the operational revision.
                // This allows SVN to follow copy history (e.g., branch created from trunk)
                // to find the file content at revisions that predate the current branch.
                var target = new SvnPathTarget(fullPath);
                var writeArgs = new SvnWriteArgs { Revision = svnRevision };
                if (!client.Write(target, memoryStream, writeArgs))
                    return null;
            }
            else
            {
                // For BASE revision or when file doesn't exist locally, use local path target
                var target = new SvnPathTarget(fullPath, svnRevision);
                if (!client.Write(target, memoryStream))
                    return null;
            }

            memoryStream.Position = 0;
            using var reader = new StreamReader(memoryStream);
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

        // Declare tracking variables outside the try block so they're visible in the catch.
        // SharpSvn throws SvnException when conflicts occur with SvnAccept.Postpone, so the
        // catch block needs to inspect what was collected before the exception was raised.
        var textConflictedFiles = new List<string>();   // content conflicts in files
        var treeConflictedFiles = new List<string>();   // structural conflicts (add/add, del/mod, etc.)
        var modifiedFiles = new List<string>();
        long minRevision = long.MaxValue;
        long maxRevision = long.MinValue;
        SvnInfoEventArgs? sourceInfo = null;

        try
        {
            if (!Directory.Exists(repositoryPath))
            {
                result.ErrorMessage = "Repository path does not exist.";
                return result;
            }

            using var client = new SvnClient();

            // Get repository root
            if (!client.GetInfo(repositoryPath, out var info))
            {
                result.ErrorMessage = "Not a valid SVN working copy.";
                return result;
            }

            var repoRoot = info.RepositoryRoot;

            // Construct the source branch URL
            var sourceUri = new Uri(repoRoot, sourceBranch);

            // Verify the source branch exists and capture its HEAD revision
            if (!client.GetInfo(sourceUri, out sourceInfo))
            {
                result.ErrorMessage = $"Source branch '{sourceBranch}' not found.";
                return result;
            }

            // Set up merge arguments to merge all eligible revisions
            var mergeArgs = new SvnMergeArgs
            {
                // Don't record mergeinfo for now (simpler)
                RecordOnly = false
            };

            client.Notify += (sender, e) =>
            {
                if (e.FullPath == null) return;

                if (e.Action == SvnNotifyAction.TreeConflict)
                {
                    treeConflictedFiles.Add(e.FullPath);
                }
                else if (e.ContentState == SvnNotifyState.Conflicted)
                {
                    // Text conflict — file content could not be auto-merged
                    textConflictedFiles.Add(e.FullPath);
                }
                else if (e.Action == SvnNotifyAction.UpdateUpdate ||
                         e.Action == SvnNotifyAction.UpdateAdd ||
                         e.Action == SvnNotifyAction.UpdateDelete ||
                         e.Action == SvnNotifyAction.UpdateReplace)
                {
                    modifiedFiles.Add(e.FullPath);
                    if (e.Revision > 0)
                    {
                        if (e.Revision < minRevision) minRevision = e.Revision;
                        if (e.Revision > maxRevision) maxRevision = e.Revision;
                    }
                }
            };

            // Postpone all conflicts — the user resolves them via the dialog.
            // Do NOT track e.Path here: it can be a repository-relative or working-copy-relative
            // path that differs from the absolute local path we need. The Notify handler above
            // captures absolute paths via e.FullPath instead.
            client.Conflict += (sender, e) =>
            {
                e.Choice = SvnAccept.Postpone;
            };

            // Use SVN merge tracking via GetMergesEligible to determine which revisions from the
            // source have not yet been merged into the target, based on svn:mergeinfo properties.
            // This prevents "File already exists" commit errors that occur with r0:HEAD range merges
            // when a file independently exists in both the source branch and the target branch
            // (e.g. .mlqt/settings.json committed to trunk AND to the feature branch independently).
            bool gotEligible = client.GetMergesEligible(
                new SvnPathTarget(repositoryPath),
                new SvnUriTarget(sourceUri),
                out var eligibleRevisions);

            if (!gotEligible || eligibleRevisions == null || eligibleRevisions.Count == 0)
            {
                // Nothing eligible to merge - already up to date per SVN merge tracking
                result.Success = true;
                return result;
            }

            var eligibleRanges = eligibleRevisions.Select(e => e.AsRange()).ToList();
            var success = client.Merge(repositoryPath, new SvnUriTarget(sourceUri), eligibleRanges, mergeArgs);

            var anyConflicts = textConflictedFiles.Count > 0 || treeConflictedFiles.Count > 0;
            result.HasChanges = modifiedFiles.Count > 0 || anyConflicts;
            result.ModifiedFiles = modifiedFiles;
            result.ConflictedFiles = textConflictedFiles;
            result.TreeConflictedFiles = treeConflictedFiles;
            result.HasConflicts = anyConflicts;
            result.SourceBranch = sourceBranch;

            // Record the revision range actually merged, falling back to source HEAD if no revisions tracked
            if (minRevision <= maxRevision && minRevision != long.MaxValue)
            {
                result.StartRevision = minRevision;
                result.EndRevision = maxRevision;
            }
            else if (result.HasChanges && sourceInfo?.Revision > 0)
            {
                result.EndRevision = sourceInfo.Revision;
            }

            if (!success)
            {
                result.ErrorMessage = "SVN merge failed.";
                return result;
            }

            result.Success = true;
        }
        catch (SvnException ex)
        {
            var anyConflicts = textConflictedFiles.Count > 0 || treeConflictedFiles.Count > 0;
            if (anyConflicts)
            {
                // SharpSvn throws SvnException when conflicts occur with SvnAccept.Postpone.
                // The notify events already fired and populated the conflict lists before the
                // exception was raised — treat this as a completed merge-with-conflicts.
                result.Success = true;
                result.HasConflicts = true;
                result.HasChanges = modifiedFiles.Count > 0 || anyConflicts;
                result.ModifiedFiles = modifiedFiles;
                result.ConflictedFiles = textConflictedFiles.Distinct().ToList();
                result.TreeConflictedFiles = treeConflictedFiles.Distinct().ToList();
                result.SourceBranch = sourceBranch;
                if (minRevision <= maxRevision && minRevision != long.MaxValue)
                {
                    result.StartRevision = minRevision;
                    result.EndRevision = maxRevision;
                }
                else if (sourceInfo?.Revision > 0)
                {
                    result.EndRevision = sourceInfo.Revision;
                }
            }
            else
            {
                RevisionControlLogger.Error("MergeBranch", ex);
                result.ErrorMessage = ex.Message;
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
    /// Resolves a merge conflict in a specific file.
    /// </summary>
    public VcsOperationResult ResolveConflict(string repositoryPath, string filePath, ConflictResolutionChoice choice)
    {
        var result = new VcsOperationResult();
        try
        {
            using var client = new SvnClient();

            var accept = choice switch
            {
                ConflictResolutionChoice.AcceptIncoming => SvnAccept.TheirsFull,
                ConflictResolutionChoice.KeepMine       => SvnAccept.MineFull,
                ConflictResolutionChoice.MarkResolved   => SvnAccept.Working,
                _                                       => SvnAccept.Postpone
            };

            result.Success = client.Resolve(filePath, accept, new SvnResolveArgs { Depth = SvnDepth.Empty });
            if (!result.Success)
                result.ErrorMessage = "SVN resolve returned false.";
        }
        catch (SvnException ex)
        {
            RevisionControlLogger.Error("ResolveConflict", ex);
            result.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("ResolveConflict", ex);
            result.ErrorMessage = ex.Message;
        }
        return result;
    }

    /// <summary>
    /// Returns the "ours" and "theirs" versions of a conflicted SVN file.
    /// SVN writes sidecar files: filename.ext.mine (ours) and filename.ext.r{n} (theirs = highest revision).
    /// </summary>
    public (string? ours, string? theirs) GetConflictVersions(string repositoryPath, string filePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath) ?? "";
            var fileName = Path.GetFileName(filePath);

            // "Ours" = the working copy version before the merge conflict markers were applied
            var mineFile = Path.Combine(dir, fileName + ".mine");
            var ours = File.Exists(mineFile) ? File.ReadAllText(mineFile) : null;

            // "Theirs" = the incoming branch revision — highest-numbered .r{n} sidecar file
            var rFiles = Directory.GetFiles(dir, fileName + ".r*")
                .Select(f =>
                {
                    var ext = Path.GetExtension(f).TrimStart('.');  // e.g. "r357" → "357" after [1..]
                    return (path: f, rev: long.TryParse(ext.Length > 1 ? ext[1..] : "", out var r) ? r : 0L);
                })
                .OrderByDescending(x => x.rev)
                .ToList();

            var theirs = rFiles.Count > 0 ? File.ReadAllText(rFiles[0].path) : null;
            return (ours, theirs);
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetConflictVersions", ex);
            return (null, null);
        }
    }

    /// <summary>
    /// SVN commits go directly to the remote server, so push is a no-op.
    /// </summary>
    public VcsOperationResult Push(string repositoryPath, string? branchName = null)
        => new() { Success = true }; // SVN commits go directly to remote

    // Rebase is a Git-only concept; SVN has no equivalent operation.
    public VcsMergeResult Rebase(string repositoryPath, string targetBranch)
        => new() { ErrorMessage = "Rebase is not supported for SVN repositories." };

    public VcsMergeResult ContinueRebase(string repositoryPath)
        => new() { ErrorMessage = "Rebase is not supported for SVN repositories." };

    public VcsOperationResult AbortRebase(string repositoryPath)
        => new() { ErrorMessage = "Rebase is not supported for SVN repositories." };

    public VcsOperationResult ForcePush(string repositoryPath, string? branchName = null)
        => new() { Success = true }; // SVN commits go directly to remote

    public bool IsBranchPushed(string repositoryPath)
        => true; // SVN commits go directly to the remote server

    public string? GetPullRequestUrl(string repositoryPath, string? baseBranch = null)
        => null; // Pull requests are not a native SVN concept
}
