using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RevisionControl.Interfaces;

namespace RevisionControl;

/// <summary>
/// Subversion (SVN) implementation of the revision control system interface.
/// Talks to SVN exclusively through the <c>svn</c> command-line client via
/// <see cref="SvnCli"/> (the bundled SlikSVN client, then MLQT_SVN_PATH, then svn on
/// PATH). The managed SharpSvn library has been removed from the shipped product because
/// the CLI is roughly an order of magnitude faster on large working copies.
/// </summary>
public class SvnRevisionControlSystem : IRevisionControlSystem
{
    // ===================================================================================
    // Small value types used to carry parsed svn --xml output around internally.
    // ===================================================================================

    /// <summary>Parsed subset of <c>svn info --xml</c> for a single entry.</summary>
    private sealed record SvnInfo(string Url, string RepositoryRoot, long Revision, long LastChangedRevision);

    /// <summary>Parsed subset of one <c>svn status --xml</c> entry.</summary>
    private sealed record SvnStatusEntry(string Path, string Item, string Props, bool TreeConflicted);

    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;
    private static readonly string[] DefaultBranchDirectories = ["trunk", "branches", "tags"];

    // ===================================================================================
    // Read-only operations.
    // ===================================================================================

    /// <summary>
    /// Checks out a specific revision to a directory.
    /// </summary>
    public bool CheckoutRevision(string repositoryPath, string revision, string outputPath)
    {
        try
        {
            Directory.CreateDirectory(outputPath);
            var url = ResolveUrl(repositoryPath);
            var rev = SvnCli.NormalizeRevision(revision);
            SvnCli.Run("checkout", "-r", rev, url, outputPath).EnsureSuccess("checkout");
            return true;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("CheckoutRevision", ex);
            throw new InvalidOperationException(
                $"SVN checkout failed for '{repositoryPath}' at revision '{revision}' to '{outputPath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets the current revision identifier. Uses the last-changed revision of the path
    /// (the last commit that affected THIS path) rather than the global repository HEAD,
    /// so commits on other branches in the same repository don't register as changes.
    /// </summary>
    public string? GetCurrentRevision(string repositoryPath)
    {
        try
        {
            return GetInfo(repositoryPath)?.LastChangedRevision.ToString();
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
            return GetInfo(repositoryPath) != null;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("IsValidRepository", ex);
            return false;
        }
    }

    /// <summary>
    /// Discovers the root of the SVN working copy containing the given path. SVN 1.7+
    /// stores .svn metadata only at the working copy root, so we walk up the directory
    /// tree and return the highest ancestor that still contains a .svn directory.
    /// </summary>
    public string? FindRepositoryRoot(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return null;
            var current = new DirectoryInfo(Path.GetFullPath(path));
            string? root = null;
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, ".svn")))
                    root = current.FullName;
                current = current.Parent;
            }
            return root;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("FindRepositoryRoot", ex);
            return null;
        }
    }

    /// <summary>
    /// Gets a human-readable description of a revision (commit message and author).
    /// Queries the log at the repository root so the revision can be found even if it
    /// touched a different branch/path than the one passed in.
    /// </summary>
    public string? GetRevisionDescription(string repositoryPath, string revision)
    {
        try
        {
            var info = GetInfo(repositoryPath);
            if (info == null) return null;

            var rev = SvnCli.NormalizeRevision(revision);
            var doc = SvnCli.RunXml("log", info.RepositoryRoot, "-r", rev, "-l", "1");
            var logEntry = doc?.Root?.Element("logentry");
            if (logEntry == null) return null;

            var message = logEntry.Element("msg")?.Value ?? "";
            var author = logEntry.Element("author")?.Value ?? "";
            var date = ParseSvnDateUtc(logEntry.Element("date")?.Value);
            return $"{message} (by {author} on {date:yyyy-MM-dd})";
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetRevisionDescription", ex);
            return null;
        }
    }

    /// <summary>
    /// Resolves a revision identifier to its canonical revision number. PREV is handled
    /// specially (the second-most-recent revision from the log), since the PREV keyword is
    /// only meaningful in a working-copy context and not against a URL.
    /// </summary>
    public string? ResolveRevision(string repositoryPath, string revision)
    {
        try
        {
            var url = ResolveUrl(repositoryPath);

            if (revision.Equals("PREV", OIC))
            {
                var doc = SvnCli.RunXml("log", url, "-l", "2");
                var revisions = doc?.Root?.Elements("logentry")
                    .Select(e => e.Attribute("revision")?.Value)
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (revisions == null || revisions.Count < 2)
                    return null;
                return revisions[1];
            }

            return GetInfo(url, revision)?.Revision.ToString();
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("ResolveRevision", ex);
            return null;
        }
    }

    // ===================================================================================
    // Workspace operations.
    // ===================================================================================

    /// <summary>
    /// Updates an existing checkout to a different revision, discarding any local changes.
    /// Much more efficient than deleting and re-checking out for large repositories.
    /// </summary>
    public bool UpdateExistingCheckout(string checkoutPath, string repositoryPath, string revision)
    {
        try
        {
            bool isWorkingCopy = false;
            if (Directory.Exists(checkoutPath))
            {
                isWorkingCopy = GetInfo(checkoutPath) != null;
                if (!isWorkingCopy)
                    // Directory exists but is not a valid working copy — remove it so
                    // CheckoutRevision can start fresh (it calls Directory.CreateDirectory).
                    Directory.Delete(checkoutPath, recursive: true);
            }

            if (!isWorkingCopy)
                return CheckoutRevision(repositoryPath, revision, checkoutPath);

            // Clean the workspace first (revert changes, remove untracked files).
            if (!CleanWorkspace(checkoutPath))
                return false;

            // Switch the working copy if its URL no longer matches the target (e.g. it was
            // trunk and is now tickets/ML-123) before updating to the target revision.
            var targetUrl = ResolveUrl(repositoryPath);
            var wcInfo = GetInfo(checkoutPath);
            if (wcInfo != null && !UrlEquals(wcInfo.Url, targetUrl))
            {
                RevisionControlLogger.Debug($"Switching workspace from {wcInfo.Url} to {targetUrl}");
                if (!SvnCli.Run("switch", targetUrl, checkoutPath).Success)
                {
                    RevisionControlLogger.Error("UpdateExistingCheckout",
                        new InvalidOperationException($"SVN switch from {wcInfo.Url} to {targetUrl} failed"));
                    return false;
                }
            }

            var rev = SvnCli.NormalizeRevision(revision);
            return SvnCli.Run("update", "-r", rev, checkoutPath).Success;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("UpdateExistingCheckout", ex);
            return false;
        }
    }

    /// <summary>
    /// Updates a working copy in place to the requested revision via <c>svn update</c>.
    /// On a multi-thousand-file working copy this is an order of magnitude faster than the
    /// old SharpSvn path (measured ~2.7s CLI vs ~38s SharpSvn on a 30000-file Modelica
    /// library). Timing of each phase is logged for diagnosis.
    /// </summary>
    public bool UpdateRevisionInPlace(string checkoutPath, string repositoryPath, string revision)
    {
        var totalSw = Stopwatch.StartNew();
        var infoSw = new Stopwatch();
        var switchSw = new Stopwatch();
        var updateSw = new Stopwatch();

        try
        {
            // 1. Validate the working copy.
            infoSw.Start();
            bool isWorkingCopy = false;
            SvnInfo? wcInfo = null;
            if (Directory.Exists(checkoutPath))
            {
                wcInfo = GetInfo(checkoutPath);
                isWorkingCopy = wcInfo != null;
                if (!isWorkingCopy)
                    // Stale / corrupt wc state — wipe so CheckoutRevision starts fresh.
                    Directory.Delete(checkoutPath, recursive: true);
            }
            infoSw.Stop();

            if (!isWorkingCopy)
                return CheckoutRevision(repositoryPath, revision, checkoutPath);

            // 2. Switch URL only if it differs (cheap to check, no-op when they match).
            switchSw.Start();
            var targetUrl = ResolveUrl(repositoryPath);
            if (wcInfo != null && !UrlEquals(wcInfo.Url, targetUrl))
            {
                RevisionControlLogger.Debug($"Switching workspace from {wcInfo.Url} to {targetUrl}");
                if (!SvnCli.Run("switch", targetUrl, checkoutPath).Success)
                {
                    RevisionControlLogger.Error("UpdateRevisionInPlace",
                        new InvalidOperationException($"SVN switch from {wcInfo.Url} to {targetUrl} failed"));
                    return false;
                }
            }
            switchSw.Stop();

            // 3. Run the update. --quiet suppresses per-file output so stdout doesn't fill
            //    with one line per file on a 30000-file working copy.
            updateSw.Start();
            var rev = SvnCli.NormalizeRevision(revision);
            var update = SvnCli.Run("update", "-r", rev, "--quiet", checkoutPath);
            updateSw.Stop();

            if (!update.Success)
            {
                RevisionControlLogger.Error("UpdateRevisionInPlace",
                    new InvalidOperationException($"`svn update` failed: {update.StdErr}"));
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("UpdateRevisionInPlace", ex);
            return false;
        }
        finally
        {
            totalSw.Stop();
            RevisionControlLogger.Info(
                $"UpdateRevisionInPlace({checkoutPath} -> r{revision}): " +
                $"total {totalSw.Elapsed.TotalSeconds:F2}s " +
                $"[getInfo {infoSw.Elapsed.TotalSeconds:F2}s, " +
                $"switch {switchSw.Elapsed.TotalSeconds:F2}s, " +
                $"update {updateSw.Elapsed.TotalSeconds:F2}s]");
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
                return false;

            if (GetInfo(checkoutPath) == null)
                return false;

            // Revert all changes recursively.
            SvnCli.Run("revert", "-R", checkoutPath);

            // Remove all unversioned files and directories (deeper paths first).
            var unversioned = GetStatusEntries(checkoutPath)
                .Where(e => e.Item == "unversioned")
                .OrderByDescending(e => e.Path.Length)
                .ToList();

            foreach (var item in unversioned)
            {
                try
                {
                    if (File.Exists(item.Path))
                        File.Delete(item.Path);
                    else if (Directory.Exists(item.Path))
                        Directory.Delete(item.Path, recursive: true);
                }
                catch
                {
                    // Continue with other items even if one fails.
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

            var info = GetInfo(repositoryPath);
            if (info == null)
            {
                result.ErrorMessage = "Not a valid SVN working copy.";
                return result;
            }

            result.OldRevision = info.Revision.ToString();

            var update = SvnCli.Run("update", "-r", "HEAD", repositoryPath);
            if (!update.Success)
            {
                result.ErrorMessage = "SVN update failed.";
                return result;
            }

            var newInfo = GetInfo(repositoryPath);
            result.NewRevision = newInfo?.Revision.ToString() ?? result.OldRevision;
            result.HasChanges = result.OldRevision != result.NewRevision;
            result.Success = true;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("UpdateToLatest", ex);
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Reverts changes to specific files in the working copy. Unversioned files are simply
    /// deleted; versioned files are reverted.
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

            foreach (var file in filesToRevert)
            {
                var fullPath = Path.Combine(repositoryPath, file);
                var status = GetSingleStatus(fullPath);

                if (status?.Item == "unversioned")
                {
                    if (File.Exists(fullPath))
                        File.Delete(fullPath);
                    else if (Directory.Exists(fullPath))
                        Directory.Delete(fullPath, recursive: true);
                }
                else
                {
                    SvnCli.Run("revert", fullPath);
                }
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("RevertFiles", ex);
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    // ===================================================================================
    // Branch / history operations.
    // ===================================================================================

    /// <summary>
    /// Gets the current branch name from the SVN URL (trunk, branches/*, tags/*).
    /// </summary>
    public string? GetCurrentBranch(string repositoryPath)
        => GetCurrentBranch(repositoryPath, branchDirectories: null);

    public string? GetCurrentBranch(string repositoryPath, IReadOnlyList<string>? branchDirectories)
    {
        try
        {
            string? url = null;

            if (Directory.Exists(repositoryPath))
            {
                var info = GetInfo(repositoryPath);
                if (info == null)
                    return null;
                url = info.Url;
            }
            else if (Uri.TryCreate(repositoryPath, UriKind.Absolute, out var confirmedUri))
            {
                url = confirmedUri.ToString();
            }

            if (url == null)
                return null;

            return ExtractBranchFromSvnUrl(url, branchDirectories);
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetCurrentBranch", ex);
            return null;
        }
    }

    /// <summary>
    /// Extracts the branch name from an SVN URL. The first directory in the list is treated
    /// as the trunk equivalent (matched as a leaf segment); the rest are branch containers.
    /// </summary>
    internal string? ExtractBranchFromSvnUrl(string url, IReadOnlyList<string>? branchDirectories = null)
    {
        branchDirectories ??= DefaultBranchDirectories;

        var urlLower = url.ToLowerInvariant();

        foreach (var dir in branchDirectories)
        {
            var dirLower = dir.ToLowerInvariant();

            if (dir == branchDirectories[0] || dirLower == "trunk")
            {
                var trunkIndex = urlLower.IndexOf($"/{dirLower}", StringComparison.Ordinal);
                if (trunkIndex >= 0)
                {
                    var afterTrunk = trunkIndex + dirLower.Length + 1;
                    if (afterTrunk >= url.Length || url[afterTrunk] == '/')
                        return dir;
                }
            }
            else
            {
                var result = CheckForBranchName(url, dir);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

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
            var url = ResolveUrl(repositoryPath);
            var currentBranch = ExtractBranchFromSvnUrl(url, branchDirectories);

            // -v (verbose) retrieves changed paths, needed to determine the actual branch for
            // each revision since SVN log follows copy history across branches.
            var args = new List<string> { "log", url, "-v" };
            if (!string.IsNullOrEmpty(options.Revision))
            {
                args.Add("-r");
                args.Add(SvnCli.NormalizeRevision(options.Revision));
                args.Add("-l");
                args.Add("1");
            }
            else
            {
                args.Add("-l");
                args.Add(options.MaxEntries.ToString());
            }

            var doc = SvnCli.RunXml(args.ToArray());
            if (doc?.Root == null)
                return entries;

            const int minEntriesFromSinceFilter = 10;
            var sinceDate = options.Since;
            var untilDate = options.Until;

            foreach (var le in doc.Root.Elements("logentry"))
            {
                var time = ParseSvnDateUtc(le.Element("date")?.Value);

                if (untilDate.HasValue && time > untilDate.Value.UtcDateTime)
                    continue;

                if (sinceDate.HasValue && time < sinceDate.Value.UtcDateTime &&
                    entries.Count >= minEntriesFromSinceFilter)
                    break;

                var message = le.Element("msg")?.Value?.Trim() ?? "";
                var messageShort = message;
                var newlineIndex = message.IndexOf('\n');
                if (newlineIndex > 0)
                    messageShort = message.Substring(0, newlineIndex).Trim();

                // Determine the actual branch from the changed paths rather than assuming all
                // entries are on the current branch. SVN log follows copy history, so older
                // entries may be from trunk or another branch.
                var branch = currentBranch;
                var paths = le.Element("paths");
                if (paths != null)
                {
                    foreach (var p in paths.Elements("path"))
                    {
                        var pathStr = p.Value;
                        if (!string.IsNullOrEmpty(pathStr))
                        {
                            branch = ExtractBranchFromSvnUrl(pathStr, branchDirectories);
                            if (branch != null)
                                break;
                        }
                    }
                    branch ??= currentBranch;
                }

                var revision = le.Attribute("revision")?.Value ?? "";
                entries.Add(new VcsLogEntry
                {
                    Revision = revision,
                    ShortRevision = revision,
                    Author = le.Element("author")?.Value ?? "",
                    AuthorEmail = "",
                    Date = new DateTimeOffset(time, TimeSpan.Zero),
                    Message = message,
                    MessageShort = messageShort,
                    Branch = branch
                });
            }
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetLogEntries", ex);
        }

        return entries;
    }

    /// <summary>
    /// Gets the commit date of the source revision this branch was created from. See
    /// <see cref="IRevisionControlSystem.GetBranchPointDate"/> for semantics.
    ///
    /// Implementation: walks the branch with <c>svn log --stop-on-copy -v</c>. The oldest
    /// entry is the copy that established the branch; the highest <c>copyfrom-rev</c> among
    /// its changed paths identifies the source revision. We then read that revision's date
    /// via a single-revision log query at the repository root (the repo root exists at every
    /// revision, whereas the branch URL does not exist at the source revision). Returns null
    /// if the path has no copy origin (trunk itself, or a non-branch path), or on any failure.
    /// </summary>
    public DateTimeOffset? GetBranchPointDate(string repositoryPath)
    {
        try
        {
            var url = ResolveUrl(repositoryPath);

            var copyFromRevision = FindBranchPointRevision(url);
            if (copyFromRevision == null)
                return null;

            // Resolve the date of the copy-from revision. Query at the repository root so the
            // lookup is independent of which paths existed at that revision.
            var info = GetInfo(url);
            if (info == null)
                return null;

            var lookup = SvnCli.RunXml("log", info.RepositoryRoot, "-r", copyFromRevision.Value.ToString(), "-l", "1");
            var sourceEntry = lookup?.Root?.Element("logentry");
            if (sourceEntry == null)
                return null;

            var sourceDate = ParseSvnDateUtc(sourceEntry.Element("date")?.Value);
            return new DateTimeOffset(sourceDate, TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetBranchPointDate", ex);
            return null;
        }
    }

    /// <summary>
    /// Gets the copy-from revision the specified branch was created from. See
    /// <see cref="IRevisionControlSystem.GetBranchPointRevision"/> for semantics.
    /// </summary>
    public string? GetBranchPointRevision(string repositoryPath, string? startRevision = null)
    {
        try
        {
            var url = ResolveUrl(repositoryPath);
            var copyFromRevision = FindBranchPointRevision(url, startRevision);
            return copyFromRevision?.ToString();
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetBranchPointRevision", ex);
            return null;
        }
    }

    /// <summary>
    /// Finds the copy that established this branch and returns its copy-from revision, by
    /// walking the branch history with <c>svn log --stop-on-copy -v</c>. Entries are
    /// newest-first; we keep overwriting, so the final capture corresponds to the oldest
    /// entry — the branch-establishing copy. Returns null if the path has no copy origin
    /// (trunk itself, or a non-branch path).
    ///
    /// When <paramref name="startRevision"/> is supplied, the URL is pegged at that revision
    /// (<c>URL@REV</c>) so the branch is resolved as it existed at that revision — important
    /// for branches that were later rebased (renamed + recreated from a newer trunk revision),
    /// where HEAD would otherwise report a copy-from revision newer than the revision under test.
    /// </summary>
    private static long? FindBranchPointRevision(string url, string? startRevision = null)
    {
        var target = url;
        if (!string.IsNullOrWhiteSpace(startRevision))
            target = $"{url}@{SvnCli.NormalizeRevision(startRevision)}";

        var doc = SvnCli.RunXml("log", target, "--stop-on-copy", "-v");
        if (doc?.Root == null)
            return null;

        long? copyFromRevision = null;
        foreach (var le in doc.Root.Elements("logentry"))
        {
            long? candidate = null;
            var paths = le.Element("paths");
            if (paths != null)
            {
                foreach (var p in paths.Elements("path"))
                {
                    if (long.TryParse(p.Attribute("copyfrom-rev")?.Value, out var r) && r > 0)
                        candidate = r;
                }
            }
            if (candidate.HasValue)
                copyFromRevision = candidate;
        }

        return copyFromRevision;
    }

    /// <summary>
    /// Gets the list of files changed in a specific revision.
    /// </summary>
    public List<VcsChangedFile> GetChangedFiles(string repositoryPath, string revision)
    {
        var changedFiles = new List<VcsChangedFile>();

        try
        {
            var url = ResolveUrl(repositoryPath);
            var rev = SvnCli.NormalizeRevision(revision);

            var doc = SvnCli.RunXml("log", url, "-r", rev, "-v", "-l", "1");
            var paths = doc?.Root?.Element("logentry")?.Element("paths");
            if (paths == null)
                return changedFiles;

            foreach (var p in paths.Elements("path"))
            {
                var path = p.Value ?? "";
                var action = p.Attribute("action")?.Value ?? "";
                var copyFromPath = p.Attribute("copyfrom-path")?.Value;

                var changedFile = new VcsChangedFile
                {
                    Path = path.TrimStart('/'),
                    ChangeType = action switch
                    {
                        "A" => VcsChangeType.Added,
                        "D" => VcsChangeType.Deleted,
                        "M" => VcsChangeType.Modified,
                        "R" => VcsChangeType.Modified,
                        _ => VcsChangeType.Modified
                    }
                };

                if (!string.IsNullOrEmpty(copyFromPath))
                {
                    changedFile.OldPath = copyFromPath.TrimStart('/');
                    changedFile.ChangeType = VcsChangeType.Copied;
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

    /// <inheritdoc/>
    public IReadOnlyList<string> GetChangedFilePathsSince(string repositoryPath, string sinceRevision)
    {
        var result = new List<string>();

        try
        {
            // `svn diff --summarize -r <rev> <wc>` compares the revision to the working copy
            // (including local modifications). --xml gives absolute working-copy paths.
            var res = SvnCli.Run("diff", "--summarize", "--xml", "-r", sinceRevision, repositoryPath);
            if (!res.Success)
            {
                RevisionControlLogger.Error("GetChangedFilePathsSince",
                    new SvnCliException("diff", res.ExitCode, res.StdErr));
                return result;
            }

            var doc = XDocument.Parse(res.StdOut);
            foreach (var path in doc.Descendants("path"))
            {
                if ((path.Attribute("item")?.Value ?? "") == "deleted")
                    continue; // a deleted file can't be checked

                var value = path.Value?.Trim();
                if (!string.IsNullOrEmpty(value))
                    result.Add(Path.GetFullPath(value));
            }
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetChangedFilePathsSince", ex);
        }

        return result;
    }

    /// <summary>
    /// Gets the list of available branches by inspecting the standard branch paths.
    /// </summary>
    public List<VcsBranchInfo> GetBranches(string repositoryPath, bool includeRemote = false)
        => GetBranches(repositoryPath, includeRemote, branchDirectories: null);

    public List<VcsBranchInfo> GetBranches(string repositoryPath, bool includeRemote, IReadOnlyList<string>? branchDirectories)
    {
        branchDirectories ??= DefaultBranchDirectories;
        var branches = new List<VcsBranchInfo>();

        try
        {
            var url = ResolveUrl(repositoryPath);
            var currentBranch = GetCurrentBranch(repositoryPath, branchDirectories);

            var info = GetInfo(url);
            if (info == null)
                return branches;

            var repoRoot = info.RepositoryRoot.TrimEnd('/');

            foreach (var branchPath in branchDirectories)
            {
                // Build the URL by string concatenation rather than new Uri(base, rel): the
                // repository root from svn has no trailing slash, and Uri's relative-resolution
                // would otherwise replace the last path segment.
                var branchUrl = $"{repoRoot}/{branchPath}";

                // The first entry is the trunk equivalent — a single branch, not a container.
                if (branchPath == branchDirectories[0])
                {
                    if (GetInfo(branchUrl) != null)
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
                    var doc = SvnCli.RunXml("list", branchUrl, "--depth", "immediates");
                    var list = doc?.Root?.Element("list");
                    if (list == null)
                        continue;

                    foreach (var entry in list.Elements("entry"))
                    {
                        if (entry.Attribute("kind")?.Value != "dir")
                            continue;
                        var entryName = entry.Element("name")?.Value;
                        if (string.IsNullOrEmpty(entryName))
                            continue;

                        var name = $"{branchPath}/{entryName}";
                        branches.Add(new VcsBranchInfo
                        {
                            Name = name,
                            IsCurrent = name == currentBranch,
                            IsRemote = true,
                            LastCommit = entry.Element("commit")?.Attribute("revision")?.Value
                        });
                    }
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
    /// Switches the working copy to a different branch via <c>svn switch</c>.
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

            var info = GetInfo(repositoryPath);
            if (info == null)
            {
                result.ErrorMessage = "Not a valid SVN working copy.";
                return result;
            }

            var branchUrl = $"{info.RepositoryRoot.TrimEnd('/')}/{branchName.TrimStart('/')}";

            if (GetInfo(branchUrl) == null)
            {
                result.ErrorMessage = $"Branch '{branchName}' not found.";
                return result;
            }

            if (!SvnCli.Run("switch", branchUrl, repositoryPath).Success)
            {
                result.ErrorMessage = "SVN switch failed.";
                return result;
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("SwitchBranch", ex);
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Creates a new branch as a server-side copy of the current working copy URL.
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

            var info = GetInfo(repositoryPath);
            if (info == null)
            {
                result.ErrorMessage = "Not a valid SVN working copy.";
                return result;
            }

            var repoRoot = info.RepositoryRoot.TrimEnd('/');
            var currentUrl = info.Url;

            // If the name already carries a known branch-directory prefix, use it as-is;
            // otherwise place it under the first branch-like directory (default "branches").
            var hasPrefix = branchDirectories.Any(d => branchName.StartsWith($"{d}/", OIC));
            var defaultBranchDir = branchDirectories.Count > 1 ? branchDirectories[1] : "branches";
            var branchUrl = hasPrefix
                ? $"{repoRoot}/{branchName}"
                : $"{repoRoot}/{defaultBranchDir}/{branchName}";

            if (GetInfo(branchUrl) != null)
            {
                result.ErrorMessage = $"Branch '{branchName}' already exists.";
                return result;
            }

            var copy = SvnCli.Run("copy", currentUrl, branchUrl, "-m", $"Create branch: {branchName}");
            if (!copy.Success)
            {
                result.ErrorMessage = "Failed to create branch.";
                return result;
            }

            if (switchToBranch)
            {
                var rel = branchUrl.Substring(repoRoot.Length).TrimStart('/');
                var switchResult = SwitchBranch(repositoryPath, rel);
                if (!switchResult.Success)
                {
                    result.ErrorMessage = $"Branch created but failed to switch: {switchResult.ErrorMessage}";
                    return result;
                }
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("CreateBranch", ex);
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Gets the content of a file at a specific revision via <c>svn cat</c>. When no revision
    /// (or HEAD) is requested, returns the BASE (last committed) version of the working file.
    /// For a numeric revision, the file's HEAD peg is used so SVN can follow copy history back
    /// to revisions that predate the current branch.
    /// </summary>
    public string? GetFileContentAtRevision(string repositoryPath, string filePath, string? revision = null)
    {
        try
        {
            if (!Directory.Exists(repositoryPath))
                return null;

            if (GetInfo(repositoryPath) == null)
                return null;

            var fullPath = Path.Combine(repositoryPath, filePath);

            var useBase = string.IsNullOrEmpty(revision)
                || revision.Equals("HEAD", OIC)
                || !long.TryParse(revision, out _);

            SvnCli.Result result;
            if (useBase)
            {
                result = SvnCli.Run("cat", "-r", "BASE", fullPath);
            }
            else if (File.Exists(fullPath))
            {
                // Peg at HEAD to identify the file, operate at the requested revision so SVN
                // follows copy history (e.g. a branch created from trunk).
                result = SvnCli.Run("cat", "-r", revision!, $"{fullPath}@HEAD");
            }
            else
            {
                result = SvnCli.Run("cat", "-r", revision!, fullPath);
            }

            return result.Success ? result.StdOut : null;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetFileContentAtRevision", ex);
            return null;
        }
    }

    // ===================================================================================
    // Commit / merge operations.
    // ===================================================================================

    /// <summary>
    /// Gets the list of files with uncommitted changes in the working copy.
    /// </summary>
    public List<VcsWorkingCopyFile> GetWorkingCopyChanges(string repositoryPath)
    {
        var files = new List<VcsWorkingCopyFile>();

        try
        {
            if (!Directory.Exists(repositoryPath))
                return files;

            if (GetInfo(repositoryPath) == null)
                return files;

            foreach (var e in GetStatusEntries(repositoryPath))
            {
                var propClean = e.Props is "none" or "normal";

                // Skip unchanged files.
                if (e.Item == "normal" && propClean && !e.TreeConflicted)
                    continue;

                // Only skip items SVN has marked as explicitly ignored. Items with content
                // changes AND property-only changes (e.g. svn:mergeinfo on the root, which
                // must be committed to finalise a merge) are included.
                if (e.Item == "ignored")
                    continue;

                var file = new VcsWorkingCopyFile
                {
                    Path = Path.GetRelativePath(repositoryPath, e.Path),
                    IsStaged = false, // SVN doesn't have staging.
                    Status = e.TreeConflicted
                        ? VcsFileStatus.Conflicted
                        : e.Item switch
                        {
                            "modified" => VcsFileStatus.Modified,
                            "added" => VcsFileStatus.Added,
                            "deleted" => VcsFileStatus.Deleted,
                            "replaced" => VcsFileStatus.Modified,
                            "unversioned" => VcsFileStatus.Untracked,
                            "conflicted" => VcsFileStatus.Conflicted,
                            "missing" => VcsFileStatus.Deleted,
                            // Property-only change (e.g. svn:mergeinfo updated during a merge).
                            "normal" when !propClean => VcsFileStatus.Modified,
                            _ => VcsFileStatus.Modified
                        }
                };

                files.Add(file);
            }

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
                        filesToAdd.Add(new VcsWorkingCopyFile
                        {
                            Path = Path.GetRelativePath(repositoryPath, newFile),
                            Status = VcsFileStatus.Untracked,
                            IsStaged = false
                        });
                    }
                    filesToRemove.Add(f);
                }
            }
            foreach (var f in filesToRemove)
                files.Remove(f);

            // Deduplicate: SVN may have already returned some of these files individually.
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

            if (GetInfo(repositoryPath) == null)
            {
                result.ErrorMessage = "Not a valid SVN working copy.";
                return result;
            }

            var pathsToCommit = new List<string>();
            if (filesToCommit != null)
            {
                foreach (var file in filesToCommit)
                    pathsToCommit.Add(Path.Combine(repositoryPath, file));
            }
            else
            {
                pathsToCommit.Add(repositoryPath);
            }

            // If the repository root has uncommitted property changes (e.g. svn:mergeinfo updated
            // during a merge), include it in this commit regardless of selection. Leaving merge
            // metadata uncommitted puts the working copy in an inconsistent state that blocks
            // subsequent commits.
            var rootStatus = GetSingleStatus(repositoryPath);
            var rootPropClean = rootStatus != null && rootStatus.Props is "none" or "normal";
            if (!rootPropClean &&
                !pathsToCommit.Contains(repositoryPath, StringComparer.OrdinalIgnoreCase))
            {
                pathsToCommit.Add(repositoryPath);
            }

            // SVN requires files to be explicitly added before they can be committed.
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
                    // File/directory is missing from disk. Schedule it for deletion so the commit
                    // removes it from the repository. Without this, SVN treats it as "missing" but
                    // not "deleted" and the commit either ignores it or fails.
                    var missingStatus = GetSingleStatus(path);
                    if (missingStatus?.Item == "missing")
                        SvnCli.Run("delete", "--force", path);
                    continue;
                }

                var status = GetSingleStatus(path);
                // A brand-new file inside a brand-new (still unversioned) directory makes svn
                // report its node as "not found" rather than "unversioned", so GetSingleStatus
                // returns null. Fall back to an explicit version-control check to tell a genuinely
                // new path apart from a clean, already-versioned one (both yield a null status).
                var isUnversioned = status?.Item == "unversioned"
                    || (status == null && !IsVersioned(path));
                if (isUnversioned)
                {
                    var parentDir = Path.GetDirectoryName(path);

                    // If the parent directory was itself added via SVN merge, adding a new file to
                    // it in the same commit causes an "out of date" server error. Mark the file for
                    // a separate commit instead. Skip this check for directories we ourselves added
                    // during this commit preparation (tracked in addedParentDirs).
                    if (!string.IsNullOrEmpty(parentDir) &&
                        !parentDir.Equals(repositoryPath, OIC) &&
                        !addedParentDirs.Contains(parentDir) &&
                        IsDirectoryAddedViaMerge(parentDir))
                    {
                        filesToSkip.Add(path);
                        continue;
                    }

                    // File is genuinely new. Stage any unversioned parent directories first so they
                    // appear in pathsToCommit (recorded in addedParentDirs), then add the file.
                    if (!string.IsNullOrEmpty(parentDir) &&
                        !parentDir.Equals(repositoryPath, OIC))
                    {
                        AddParentDirectories(parentDir, repositoryPath, addedParentDirs);
                    }

                    var add = SvnCli.Run("add", "--parents", path);
                    if (!add.Success)
                    {
                        result.ErrorMessage = add.StdErr.Trim();
                        return result;
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

            // Add any parent directories that were scheduled for add to the commit paths.
            foreach (var dir in addedParentDirs)
            {
                if (!pathsToCommit.Contains(dir))
                    pathsToCommit.Add(dir);
            }

            if (pathsToCommit.Count == 0)
            {
                result.ErrorMessage = result.SkippedFiles.Count > 0
                    ? "All selected files are in directories that were added via merge and cannot be " +
                      "added in the same transaction. Please also select the parent directory so it is " +
                      "committed first, then commit these files separately."
                    : "No files to commit.";
                return result;
            }

            // Commit via a --targets file so the (potentially large) path list and any paths
            // containing spaces are passed safely.
            progress?.Report($"Committing {pathsToCommit.Count} files to server...");
            var targetsFile = Path.GetTempFileName();
            SvnCli.Result commit;
            try
            {
                File.WriteAllLines(targetsFile, pathsToCommit);
                commit = SvnCli.Run("commit", "--targets", targetsFile, "-m", message);
            }
            finally
            {
                try { File.Delete(targetsFile); } catch { /* best effort */ }
            }

            if (!commit.Success)
            {
                result.ErrorMessage = string.IsNullOrWhiteSpace(commit.StdErr) ? "SVN commit failed." : commit.StdErr.Trim();
                result.IsOutOfDate = IsOutOfDateError(commit.StdErr);
                return result;
            }

            var newRevision = ParseCommittedRevision(commit.StdOut);
            if (newRevision == null)
            {
                // svn committed nothing (no actual changes were staged).
                result.ErrorMessage = "SVN commit failed.";
                return result;
            }

            // Commit succeeded so run update.
            UpdateToLatest(repositoryPath);

            result.Success = true;
            result.NewRevision = newRevision;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("Commit", ex);
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Merges changes from a source branch into the current working copy. Uses SVN merge
    /// tracking (<c>svn mergeinfo --show-revs eligible</c>) to determine which revisions are
    /// not yet merged, then runs an automatic <c>svn merge</c> with <c>--accept postpone</c>
    /// so the user resolves any conflicts via the dialog.
    /// </summary>
    public VcsMergeResult MergeBranch(string repositoryPath, string sourceBranch)
    {
        var result = new VcsMergeResult();

        try
        {
            if (!Directory.Exists(repositoryPath))
            {
                result.ErrorMessage = "Repository path does not exist.";
                return result;
            }

            var info = GetInfo(repositoryPath);
            if (info == null)
            {
                result.ErrorMessage = "Not a valid SVN working copy.";
                return result;
            }

            var repoRoot = info.RepositoryRoot.TrimEnd('/');
            var sourceUrl = $"{repoRoot}/{sourceBranch.TrimStart('/')}";

            var sourceInfo = GetInfo(sourceUrl);
            if (sourceInfo == null)
            {
                result.ErrorMessage = $"Source branch '{sourceBranch}' not found.";
                return result;
            }

            // Determine eligible (not-yet-merged) revisions. This prevents "File already exists"
            // errors that an r0:HEAD range merge would cause when a file independently exists in
            // both source and target (e.g. .mlqt/settings.json committed to both branches).
            var eligible = SvnCli.Run("mergeinfo", "--show-revs", "eligible", sourceUrl, repositoryPath);
            var eligibleRevs = ParseMergeinfoRevs(eligible.StdOut);
            if (!eligible.Success || eligibleRevs.Count == 0)
            {
                // Nothing eligible to merge — already up to date per SVN merge tracking.
                result.Success = true;
                return result;
            }

            // Postpone all conflicts; the user resolves them via the dialog. With --accept
            // postpone the CLI exits 0 even when conflicts occur, so we classify the outcome
            // from the working-copy status afterwards rather than from an exception.
            var merge = SvnCli.Run("merge", sourceUrl, repositoryPath, "--accept", "postpone");

            var textConflictedFiles = new List<string>();
            var treeConflictedFiles = new List<string>();
            var modifiedFiles = new List<string>();
            foreach (var e in GetStatusEntries(repositoryPath))
            {
                if (e.TreeConflicted)
                    treeConflictedFiles.Add(e.Path);
                else if (e.Item == "conflicted")
                    textConflictedFiles.Add(e.Path);
                else if (e.Item is "modified" or "added" or "deleted" or "replaced")
                    modifiedFiles.Add(e.Path);
            }

            var anyConflicts = textConflictedFiles.Count > 0 || treeConflictedFiles.Count > 0;
            result.HasChanges = modifiedFiles.Count > 0 || anyConflicts;
            result.ModifiedFiles = modifiedFiles;
            result.ConflictedFiles = textConflictedFiles;
            result.TreeConflictedFiles = treeConflictedFiles;
            result.HasConflicts = anyConflicts;
            result.SourceBranch = sourceBranch;

            // Record the revision range from the eligible set, falling back to source HEAD.
            if (eligibleRevs.Count > 0)
            {
                result.StartRevision = eligibleRevs.Min();
                result.EndRevision = eligibleRevs.Max();
            }
            else if (result.HasChanges && sourceInfo.Revision > 0)
            {
                result.EndRevision = sourceInfo.Revision;
            }

            if (!merge.Success && !anyConflicts)
            {
                result.ErrorMessage = string.IsNullOrWhiteSpace(merge.StdErr) ? "SVN merge failed." : merge.StdErr.Trim();
                return result;
            }

            result.Success = true;
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
            var accept = choice switch
            {
                ConflictResolutionChoice.AcceptIncoming => "theirs-full",
                ConflictResolutionChoice.KeepMine => "mine-full",
                ConflictResolutionChoice.MarkResolved => "working",
                _ => "postpone"
            };

            var resolve = SvnCli.Run("resolve", "--accept", accept, filePath);
            result.Success = resolve.Success;
            if (!result.Success)
                result.ErrorMessage = string.IsNullOrWhiteSpace(resolve.StdErr) ? "SVN resolve returned false." : resolve.StdErr.Trim();
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

            // "Ours" = the working copy version before the merge conflict markers were applied.
            var mineFile = Path.Combine(dir, fileName + ".mine");
            var ours = File.Exists(mineFile) ? File.ReadAllText(mineFile) : null;

            // "Theirs" = the incoming branch revision — highest-numbered .r{n} sidecar file.
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

    // ===================================================================================
    // Git-only operations: no-ops / unsupported for SVN.
    // ===================================================================================

    /// <summary>SVN commits go directly to the remote server, so push is a no-op.</summary>
    public VcsOperationResult Push(string repositoryPath, string? branchName = null)
        => new() { Success = true };

    public VcsMergeResult Rebase(string repositoryPath, string targetBranch)
        => new() { ErrorMessage = "Rebase is not supported for SVN repositories." };

    public VcsMergeResult ContinueRebase(string repositoryPath)
        => new() { ErrorMessage = "Rebase is not supported for SVN repositories." };

    public VcsOperationResult AbortRebase(string repositoryPath)
        => new() { ErrorMessage = "Rebase is not supported for SVN repositories." };

    public VcsOperationResult ForcePush(string repositoryPath, string? branchName = null)
        => new() { Success = true };

    public bool IsBranchPushed(string repositoryPath)
        => true;

    public string? GetPullRequestUrl(string repositoryPath, string? baseBranch = null)
        => null;

    // ===================================================================================
    // Private helpers.
    // ===================================================================================

    /// <summary>
    /// Runs <c>svn info --xml</c> on a working copy or URL and parses the single entry.
    /// Returns null when the target is not a valid working copy or repository object.
    /// </summary>
    private static SvnInfo? GetInfo(string target, string? revision = null)
    {
        var args = new List<string> { "info", target };
        if (!string.IsNullOrEmpty(revision))
        {
            args.Add("-r");
            args.Add(SvnCli.NormalizeRevision(revision));
        }

        var entry = SvnCli.RunXml(args.ToArray())?.Root?.Element("entry");
        if (entry == null)
            return null;

        var url = entry.Element("url")?.Value ?? "";
        var root = entry.Element("repository")?.Element("root")?.Value ?? "";
        long.TryParse(entry.Attribute("revision")?.Value, out var revisionNumber);

        var lastChanged = revisionNumber;
        var commit = entry.Element("commit");
        if (commit != null)
            long.TryParse(commit.Attribute("revision")?.Value, out lastChanged);

        return new SvnInfo(url, root, revisionNumber, lastChanged);
    }

    /// <summary>
    /// Resolves a repository path (working copy or URL) to a repository URL string suitable
    /// for passing to the svn CLI.
    /// </summary>
    private string ResolveUrl(string repositoryPath)
    {
        if (Directory.Exists(repositoryPath))
        {
            var info = GetInfo(repositoryPath);
            if (info != null)
                return info.Url;
        }

        if (Uri.TryCreate(repositoryPath, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https" || uri.Scheme == "svn" || uri.Scheme == "file"))
        {
            return repositoryPath;
        }

        // Fallback: a file:// URL built from the full filesystem path.
        return new Uri(Path.GetFullPath(repositoryPath)).ToString();
    }

    /// <summary>
    /// Runs <c>svn status --xml</c> recursively and returns one entry per reported path.
    /// </summary>
    private static List<SvnStatusEntry> GetStatusEntries(string path)
    {
        var list = new List<SvnStatusEntry>();
        var target = SvnCli.RunXml("status", path)?.Root?.Element("target");
        if (target == null)
            return list;

        foreach (var entry in target.Elements("entry"))
        {
            var wc = entry.Element("wc-status");
            if (wc == null)
                continue;
            list.Add(new SvnStatusEntry(
                Path.GetFullPath(entry.Attribute("path")?.Value ?? ""),
                wc.Attribute("item")?.Value ?? "",
                wc.Attribute("props")?.Value ?? "",
                wc.Attribute("tree-conflicted")?.Value == "true"));
        }

        return list;
    }

    /// <summary>
    /// Runs <c>svn status --depth empty --xml</c> for a single path. Returns null when SVN
    /// reports nothing for it (a versioned, unmodified item).
    /// </summary>
    private static SvnStatusEntry? GetSingleStatus(string path)
    {
        var entry = SvnCli.RunXml("status", path, "--depth", "empty")?.Root?.Element("target")?.Element("entry");
        var wc = entry?.Element("wc-status");
        if (wc == null)
            return null;
        return new SvnStatusEntry(
            Path.GetFullPath(path),
            wc.Attribute("item")?.Value ?? "",
            wc.Attribute("props")?.Value ?? "",
            wc.Attribute("tree-conflicted")?.Value == "true");
    }

    /// <summary>
    /// Returns true if the directory was added to the working copy via an SVN merge (or copy).
    /// New files cannot be added to such a directory in the same commit transaction. The caller
    /// already excludes directories it created itself, so any remaining Added directory came
    /// from a merge/copy and must be committed first.
    /// </summary>
    private static bool IsDirectoryAddedViaMerge(string directoryPath)
        => GetSingleStatus(directoryPath)?.Item == "added";

    /// <summary>
    /// Returns true if the path is under version control. Uses <c>svn info</c> (exit 0) rather
    /// than <c>svn status</c> because status cannot locate a node whose parent directory is
    /// itself still unversioned — it reports "node not found" instead of "unversioned", which
    /// would otherwise be mistaken for a clean, versioned path. The caller guarantees the path
    /// exists on disk.
    /// </summary>
    private static bool IsVersioned(string path)
        => SvnCli.Run("info", path).Success;

    /// <summary>
    /// Stages unversioned ancestor directories (deepest-first via recursion) with
    /// <c>svn add --depth empty --parents</c>, recording each in <paramref name="addedDirs"/>
    /// so they can be included in the commit.
    /// </summary>
    private void AddParentDirectories(string path, string repositoryRoot, HashSet<string> addedDirs)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;

        // Don't go above the repository root.
        if (path.Equals(repositoryRoot, OIC) || !path.StartsWith(repositoryRoot, OIC))
            return;

        // Already under version control (normal, added, or merge-added) — nothing to stage.
        // GetSingleStatus returns null both for a clean path and for one whose parent is itself
        // unversioned, so check version-control membership directly rather than via status.
        if (IsVersioned(path))
            return;

        // Ensure ancestors are versioned first.
        var parentDir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parentDir))
            AddParentDirectories(parentDir, repositoryRoot, addedDirs);

        if (SvnCli.Run("add", "--depth", "empty", "--parents", path).Success)
            addedDirs.Add(path);
    }

    /// <summary>Compares two SVN URLs ignoring a trailing slash.</summary>
    private static bool UrlEquals(string a, string b)
        => string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.Ordinal);

    /// <summary>Parses an ISO-8601 svn:date string to a UTC <see cref="DateTime"/>.</summary>
    private static DateTime ParseSvnDateUtc(string? value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
            return dto.UtcDateTime;
        return default;
    }

    private static readonly Regex CommittedRevisionRegex =
        new(@"Committed revision (\d+)\.", RegexOptions.Compiled);

    /// <summary>Extracts the revision number from <c>svn commit</c>'s "Committed revision N." line.</summary>
    private static string? ParseCommittedRevision(string stdout)
    {
        var match = CommittedRevisionRegex.Match(stdout);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Returns true when commit stderr indicates the working copy is out of date.</summary>
    private static bool IsOutOfDateError(string stderr)
        => stderr.Contains("E160028", StringComparison.Ordinal) ||
           stderr.Contains("out of date", OIC);

    private static readonly Regex MergeinfoRevisionRegex =
        new(@"^\s*r(\d+)", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Parses the "rN" lines from <c>svn mergeinfo --show-revs eligible</c> output.</summary>
    private static List<long> ParseMergeinfoRevs(string stdout)
    {
        var revs = new List<long>();
        foreach (Match m in MergeinfoRevisionRegex.Matches(stdout))
        {
            if (long.TryParse(m.Groups[1].Value, out var r))
                revs.Add(r);
        }
        return revs;
    }
}
