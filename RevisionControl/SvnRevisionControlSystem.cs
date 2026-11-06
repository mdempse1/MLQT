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
                $"SVN checkout failed for '{repositoryPath}' at revision '{revision}' to '{outputPath}': {RevisionControlLogger.Describe(ex)}", ex);
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
            // A label, asked of the server after every update: a short limit, so a server that
            // stalled the update does not hold the reload after it for a second ten minutes (B330).
            var doc = SvnCli.RunXml(SvnCli.QueryIdleTimeout, "log", info.RepositoryRoot, "-r", rev, "-l", "1");
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
                var doc = SvnCli.RunXml(SvnCli.QueryIdleTimeout, "log", url, "-l", "2");
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
                if (!SvnCli.RunOnWorkingCopy(checkoutPath, "switch", targetUrl, checkoutPath).Success)
                {
                    RevisionControlLogger.Error("UpdateExistingCheckout",
                        new InvalidOperationException($"SVN switch from {wcInfo.Url} to {targetUrl} failed"));
                    return false;
                }
            }

            var rev = SvnCli.NormalizeRevision(revision);
            return SvnCli.RunOnWorkingCopy(checkoutPath, "update", "-r", rev, checkoutPath).Success;
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
                if (!SvnCli.RunOnWorkingCopy(checkoutPath, "switch", targetUrl, checkoutPath).Success)
                {
                    RevisionControlLogger.Error("UpdateRevisionInPlace",
                        new InvalidOperationException($"SVN switch from {wcInfo.Url} to {targetUrl} failed"));
                    return false;
                }
            }
            switchSw.Stop();

            // 3. Run the update. Not quiet: the output is how SvnCli tells an update that is
            //    still working from one that has stalled (B297), so an update of a large working
            //    copy that printed nothing for the idle limit was stopped as a stall (B383). One
            //    line per file is drained as it arrives, so it cannot fill a pipe.
            updateSw.Start();
            var rev = SvnCli.NormalizeRevision(revision);
            var update = SvnCli.RunOnWorkingCopy(checkoutPath, "update", "-r", rev, checkoutPath);
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
            SvnCli.RunOnWorkingCopy(checkoutPath, "revert", "-R", checkoutPath);

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

            var update = SvnCli.RunOnWorkingCopy(repositoryPath, "update", "-r", "HEAD", repositoryPath);
            if (!update.Success)
            {
                result.ErrorMessage = update.FailureMessage("SVN update failed.");
                return result;
            }

            var newInfo = GetInfo(repositoryPath);
            result.NewRevision = newInfo?.Revision.ToString() ?? result.OldRevision;
            result.HasChanges = result.OldRevision != result.NewRevision;
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = RevisionControlLogger.Failed("UpdateToLatest", ex);
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

            // **A handful of svn processes, not two per file.** This asked `svn status` of each file
            // and then ran `svn revert` on it alone: every one a process that opens and locks the
            // working copy's database, so reverting 17,000 files was 34,000 of them where TortoiseSVN
            // runs one revert. Now one status per chunk of paths says which are unversioned, and one
            // revert takes every other path from a --targets file.
            var fullPaths = filesToRevert
                .Select(file => Path.GetFullPath(Path.Combine(repositoryPath, file)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var unversioned = UnversionedAmong(fullPaths);
            var toRevert = fullPaths.Where(p => !unversioned.Contains(p)).ToList();

            if (toRevert.Count > 0)
            {
                var revert = RunWithTargets(repositoryPath, ["revert"], toRevert);

                // Checked, which the per-file revert never was: a locked working copy reverted
                // nothing and the operation still reported success.
                if (!revert.Success)
                {
                    result.ErrorMessage = $"Could not revert {toRevert.Count} file(s): {revert.FailureMessage("svn revert failed.")}";
                    return result;
                }
            }

            // Unversioned files are deleted, which is what reverting a new file means. Files first,
            // so a folder's own entry does not take a file listed after it from under the loop.
            foreach (var path in unversioned.Where(File.Exists))
                File.Delete(path);
            foreach (var path in unversioned.Where(Directory.Exists))
                Directory.Delete(path, recursive: true);

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = RevisionControlLogger.Failed("RevertFiles", ex);
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
            var workingCopy = Directory.Exists(repositoryPath) ? GetInfo(repositoryPath) : null;
            var url = workingCopy?.Url ?? ResolveUrl(repositoryPath);
            var currentBranch = ExtractBranchFromSvnUrl(url, branchDirectories);

            // -v (verbose) retrieves changed paths, needed to determine the actual branch for
            // each revision since SVN log follows copy history across branches.
            XDocument? doc;
            if (!string.IsNullOrEmpty(options.Revision))
            {
                // One revision: looked up exactly as GetChangedFiles looks it up (B386, B419).
                var revision = SvnCli.NormalizeRevision(options.Revision);
                var target = workingCopy is null ? url : PegForChangedFiles(url, revision, workingCopy.Revision);
                doc = SvnCli.RunXml(SvnCli.HistoryIdleTimeout, "log", target, "-v", "-r", revision, "-l", "1");
            }
            else
            {
                var limit = options.MaxEntries.ToString();
                doc = SvnCli.RunXml(SvnCli.HistoryIdleTimeout, "log", url, "-v", "-l", limit);

                // Unpegged, the URL is pegged at HEAD, which is what shows commits newer than the
                // working copy - but a branch deleted or renamed since has nothing there, and the
                // History dialog came back empty (B419). Then the history is read back from the
                // working copy's own revision, where its URL certainly exists.
                if (doc?.Root == null && HistoryPeg(url, workingCopy?.Revision ?? 0) is { } pegged)
                    doc = SvnCli.RunXml(SvnCli.HistoryIdleTimeout, "log", pegged, "-v", "-l", limit);
            }

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

            var lookup = SvnCli.RunXml(SvnCli.QueryIdleTimeout, "log", info.RepositoryRoot, "-r", copyFromRevision.Value.ToString(), "-l", "1");
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

        var doc = SvnCli.RunXml(SvnCli.HistoryIdleTimeout, "log", target, "--stop-on-copy", "-v");
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
            var rev = SvnCli.NormalizeRevision(revision);
            var target = ChangedFilesTarget(repositoryPath, rev);

            var doc = SvnCli.RunXml(SvnCli.QueryIdleTimeout, "log", target, "-r", rev, "-v", "-l", "1");
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

    /// <summary>
    /// What <see cref="GetChangedFiles"/> asks <c>svn log</c> about: a working copy's URL pegged
    /// where it is known to exist (B386).
    /// </summary>
    /// <remarks>
    /// Unpegged, the URL is pegged at HEAD, and a working copy whose branch has since been deleted or
    /// renamed has nothing at its URL there - so every revision's changed files came back empty.
    /// Pegging at the revision asked for (B328's fix for content) would be wrong here: svn follows
    /// copy history backwards from the peg, which is how a revision from before the branch was
    /// created is found on trunk, and the branch's URL did not exist at such a revision. So the peg
    /// is the later of the two - the working copy's own revision, where its URL certainly exists,
    /// or the revision asked for when that is newer.
    /// </remarks>
    private string ChangedFilesTarget(string repositoryPath, string normalizedRevision)
    {
        if (Directory.Exists(repositoryPath) && GetInfo(repositoryPath) is { } info)
            return PegForChangedFiles(info.Url, normalizedRevision, info.Revision);
        return ResolveUrl(repositoryPath);
    }

    /// <summary>
    /// A working copy's URL pegged at the later of its own revision and the one asked for; left
    /// unpegged for a keyword, which svn resolves itself.
    /// </summary>
    internal static string PegForChangedFiles(string workingCopyUrl, string normalizedRevision, long workingCopyRevision)
    {
        if (!long.TryParse(normalizedRevision, out var asked) || workingCopyRevision <= 0)
            return workingCopyUrl;
        return Pegged(workingCopyUrl, Math.Max(asked, workingCopyRevision).ToString());
    }

    /// <summary>
    /// Where <see cref="GetLogEntries(string, VcsLogOptions?, IReadOnlyList{string}?)"/> reads a
    /// working copy's history from when its URL has nothing at HEAD (B419): the working copy's own
    /// revision. None when that revision is not known - there is no working copy to ask.
    /// </summary>
    internal static string? HistoryPeg(string workingCopyUrl, long workingCopyRevision)
        => workingCopyRevision > 0 ? Pegged(workingCopyUrl, workingCopyRevision.ToString()) : null;

    /// <inheritdoc/>
    public IReadOnlyList<string>? GetChangedFilePathsSince(string repositoryPath, string sinceRevision)
    {
        var result = new List<string>();

        try
        {
            // `svn diff --summarize -r <rev> <wc>` compares the revision to the working copy
            // (including local modifications). --xml gives absolute working-copy paths.
            var res = SvnCli.Run(["diff", "--summarize", "--xml", "-r", sinceRevision, repositoryPath], stdinText: null, SvnCli.HistoryIdleTimeout);
            if (!res.Success)
            {
                RevisionControlLogger.Error("GetChangedFilePathsSince",
                    new SvnCliException("diff", res.ExitCode, res.StdErr));
                return null;   // svn said no; an empty list would read as "nothing changed"
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
            return null;
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
                    var doc = SvnCli.RunXml(SvnCli.QueryIdleTimeout, "list", branchUrl, "--depth", "immediates");
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

            var switched = SvnCli.RunOnWorkingCopy(repositoryPath, "switch", branchUrl, repositoryPath);
            // Asked of a failed switch too: one that stopped part way has still rewritten files.
            result.WorkingCopyChanged = SwitchChangedFiles(switched.StdOut);
            if (!switched.Success)
            {
                result.ErrorMessage = switched.FailureMessage("SVN switch failed.");
                return result;
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = RevisionControlLogger.Failed("SwitchBranch", ex);
        }

        return result;
    }

    /// <summary>
    /// Creates a new branch as a server-side copy of the working copy's URL at the working copy's
    /// revision, and optionally switches the working copy to it.
    /// </summary>
    public VcsOperationResult CreateBranch(string repositoryPath, string branchName, bool switchToBranch = true)
        => CreateBranch(repositoryPath, branchName, switchToBranch, branchDirectories: null);

    /// <summary>
    /// Creates a new branch as a server-side copy of the working copy's URL at HEAD, and optionally
    /// switches the working copy to it.
    /// </summary>
    /// <remarks>
    /// <para><b>Where the branch goes</b> is <see cref="BranchPathFor"/>'s decision: a name whose
    /// folder already exists in the repository is used as typed, any other is placed in the first
    /// branch container of <paramref name="branchDirectories"/>.</para>
    ///
    /// <para><b>Local modifications are kept, not committed.</b> The copy is taken from the
    /// repository, never from the working copy itself (<c>svn copy WC URL</c> would commit every
    /// local change to the new branch in the same commit). <c>svn switch</c> then carries the
    /// uncommitted changes across. That is the case this exists for: changes made on trunk that
    /// belong on a branch.</para>
    ///
    /// <para><b>From HEAD, not from the working copy's revision.</b> A commit leaves a working copy
    /// mixed-revision: the files committed are at the new revision, its root still at the old one.
    /// A branch copied at the root's revision lacked those commits, and switching to it rolled the
    /// committed files back to their older text. HEAD holds everything this working copy committed;
    /// what others committed since its last update arrives with the switch, as an update brings it,
    /// local changes merged. TortoiseSVN's branch dialog defaults to HEAD for the same reason.</para>
    /// </remarks>
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

            var relative = BranchPathFor(branchName, branchDirectories,
                folder => GetInfo($"{repoRoot}/{folder}") != null);
            if (relative == null)
            {
                result.ErrorMessage = $"'{branchName}' is not a branch name.";
                return result;
            }

            var branchUrl = $"{repoRoot}/{relative}";

            if (GetInfo(branchUrl) != null)
            {
                result.ErrorMessage = $"Branch '{branchName}' already exists at ^/{relative}.";
                return result;
            }

            // svn copy's own message for a missing parent is E160013 "File not found: transaction
            // ..., path ...", which names neither the folder that is missing nor why the branch was
            // going there.
            var parent = relative[..relative.LastIndexOf('/')];
            if (GetInfo($"{repoRoot}/{parent}") == null)
            {
                result.ErrorMessage = MissingBranchFolderMessage(branchName, relative, parent, repoRoot, branchDirectories);
                return result;
            }

            var source = Pegged(currentUrl, "HEAD");
            var copy = SvnCli.Run("copy", source, branchUrl, "-m", $"Create branch: {branchName}");
            if (!copy.Success)
            {
                result.ErrorMessage = $"Could not copy {source} to {branchUrl}: {copy.FailureMessage("svn copy failed.")}";
                return result;
            }

            if (switchToBranch)
            {
                var switchResult = SwitchBranch(repositoryPath, relative);
                result.WorkingCopyChanged = switchResult.WorkingCopyChanged;
                if (!switchResult.Success)
                {
                    result.ErrorMessage = $"Branch created at ^/{relative}, but switching the working copy to it failed, " +
                                          $"so the working copy is still on {currentUrl}: {switchResult.ErrorMessage}";
                    return result;
                }
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = RevisionControlLogger.Failed("CreateBranch", ex);
        }

        return result;
    }

    /// <summary>
    /// The repository-relative path a new branch called <paramref name="branchName"/> is created
    /// at, or null when the name is empty.
    /// </summary>
    /// <remarks>
    /// <para>A name that already says where it goes is used as typed: one that starts with a
    /// configured branch container (<c>branches/x</c>), or one whose folder exists in the
    /// repository (<paramref name="folderExists"/>). Every other name is placed in the first
    /// container, <paramref name="branchDirectories"/>[1].</para>
    ///
    /// <para>The second test is the one that matters when the setting and the repository disagree.
    /// A repository whose branch directories were set to <c>trunk, tickets, releases</c> but whose
    /// branches are under <c>branches</c> turned <c>branches/test-7</c> into
    /// <c>tickets/branches/test-7</c>, and svn refused it with E160013 because <c>tickets</c> does
    /// not exist. A name under the trunk folder is never taken as typed: a branch is not created
    /// inside trunk.</para>
    /// </remarks>
    internal static string? BranchPathFor(string branchName, IReadOnlyList<string>? branchDirectories, Func<string, bool> folderExists)
    {
        var name = branchName.Trim().Trim('/');
        if (name.Length == 0)
            return null;

        branchDirectories ??= DefaultBranchDirectories;
        var trunk = branchDirectories.Count > 0 ? branchDirectories[0] : "trunk";
        var defaultContainer = branchDirectories.Count > 1 ? branchDirectories[1] : "branches";

        var slash = name.LastIndexOf('/');
        if (slash > 0)
        {
            var first = name[..name.IndexOf('/')];
            if (!first.Equals(trunk, OIC))
            {
                if (branchDirectories.Skip(1).Any(d => d.Trim('/').Equals(first, OIC)))
                    return name;

                if (folderExists(name[..slash]))
                    return name;
            }
        }

        return $"{defaultContainer.Trim('/')}/{name}";
    }

    /// <summary>
    /// Whether <c>svn switch</c> (or <c>update</c>) says it changed anything in the working copy.
    /// </summary>
    /// <remarks>
    /// Each item it touched is one line: up to four status columns - the item (A, D, U, C, G, E, R),
    /// its properties (U, C, G), a lock (B) and a tree conflict (C) - then the path. Every other line
    /// is a summary ("Updated to revision 19.", "At revision 19.", "Summary of conflicts:" and the
    /// indented counts under it), and a switch with no item lines rewrote nothing. A property change
    /// counts: svn:eol-style or svn:keywords rewrite the file they are set on.
    /// </remarks>
    internal static bool SwitchChangedFiles(string? output)
    {
        if (string.IsNullOrEmpty(output))
            return false;

        foreach (var line in output.Split('\n'))
        {
            var text = line.TrimEnd('\r');
            if (text.Length < 6)
                continue;

            var item = text[0];
            var props = text[1];
            var locked = text[2];
            var tree = text[3];
            // A versioned file missing from disk is put back by the switch, and said so on a line of
            // its own: "Restored 'path'". It is a file the libraries do not hold.
            if (text.StartsWith("Restored '", StringComparison.Ordinal))
                return true;

            var columnsValid = "ADUCGER ".Contains(item) && "UCG ".Contains(props)
                && "B ".Contains(locked) && "C ".Contains(tree);
            var anyFlag = item != ' ' || props != ' ' || locked != ' ' || tree != ' ';
            if (columnsValid && anyFlag && text[4] == ' ' && !string.IsNullOrWhiteSpace(text[5..]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// What to say when the folder a new branch would go in does not exist: the URL that was
    /// going to be created, why it was that one, and the folders the repository does have.
    /// </summary>
    private static string MissingBranchFolderMessage(string branchName, string relative, string parent, string repoRoot,
        IReadOnlyList<string> branchDirectories)
    {
        var containers = string.Join(", ", branchDirectories.Skip(1));
        var message = $"Cannot create branch '{branchName}' at ^/{relative}: the folder ^/{parent} does not exist in {repoRoot}. " +
                      $"A name without an existing folder goes under the first branch directory in this repository's settings ({containers}).";

        var list = SvnCli.Run(["list", repoRoot], stdinText: null, SvnCli.QueryIdleTimeout);
        if (list.Success)
        {
            var folders = list.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(e => e.EndsWith('/'))
                .Select(e => e.TrimEnd('/'))
                .ToList();
            if (folders.Count > 0)
                message += $" The repository's top-level folders are: {string.Join(", ", folders)}.";
        }

        return message;
    }

    /// <summary>
    /// Gets the content of a file at a specific revision via <c>svn cat</c>. When no revision
    /// (or HEAD) is requested, returns the BASE (last committed) version of the working file.
    /// For a numeric revision, the file's HEAD peg is used so SVN can follow copy history back
    /// to revisions that predate the current branch.
    /// </summary>
    /// <summary>
    /// The revision before this one, which in SVN is arithmetic.
    /// </summary>
    /// <remarks>
    /// Revision numbers are global and sequential, so N-1 is the state of the whole repository
    /// immediately before N - whether or not N-1 touched the file being looked at, which is exactly
    /// what "before this commit" means. Revision 1 has nothing before it. A non-numeric revision is
    /// not something SVN can count back from, and null says so rather than guessing.
    /// </remarks>
    public string? GetPreviousRevision(string repositoryPath, string revision)
    {
        if (!long.TryParse(revision, out var number) || number <= 1)
            return null;

        return (number - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public byte[]? GetFileBytesAtRevision(string repositoryPath, string filePath, string? revision = null)
    {
        try
        {
            if (!Directory.Exists(repositoryPath))
                return null;

            var fullPath = Path.Combine(repositoryPath, filePath);

            var useBase = string.IsNullOrEmpty(revision)
                || revision.Equals("HEAD", OIC)
                || !long.TryParse(revision, out _);

            // RunForBytes, not Run: `svn cat` writes a file, and a file's encoding is its own
            // business - decoding it as UTF-8 loses a Windows-1252 library's accented characters
            // before any caller can say otherwise (B264).
            // Pegged even here, with nothing to follow: a path containing '@' is otherwise read as
            // one carrying a peg revision, and a file called "a@b.mo" is not found.
            //
            // **No `svn info` first.** `cat` at BASE fails by itself outside a working copy, so
            // asking first only doubled the processes: classifying the changes of a large working
            // copy reads every changed file's committed version this way, and 17,372 changed files
            // were 34,744 svn processes.
            if (useBase)
                return SvnCli.RunForBytes(SvnCli.QueryIdleTimeout, "cat", Pegged(fullPath, "BASE")) is { Success: true } b ? b.StdOut : null;

            if (GetInfo(repositoryPath) == null)
                return null;

            if (File.Exists(fullPath))
            {
                // Peg at HEAD to identify the file, operate at the requested revision so SVN
                // follows copy history (e.g. a branch created from trunk).
                var local = SvnCli.RunForBytes(SvnCli.QueryIdleTimeout, "cat", "-r", revision!, Pegged(fullPath, "HEAD"));
                if (local.Success)
                    return local.StdOut;
            }

            // The working copy could not answer, and that is ordinary rather than exceptional: the
            // path may be **repository-root-relative** ("trunk/Modelica/Foo.mo"), which is what
            // `svn log` reports and what the history view passes back; the working copy may be older
            // than the revision being looked at, so the file is not there yet; or the file may have
            // been deleted since. Asking the server by URL answers all three, and the revision the
            // caller named is a server revision in any case (B265).
            //
            // Pegged at that revision, not left to default: an unpegged URL is pegged at HEAD, which
            // asks for "the file now at this path, as it was then" - and a file deleted or moved
            // since has nothing at this path now, so the one case the fallback exists for failed
            // (B328). The path came from `svn log` for that revision, so that revision is where it
            // is to be found.
            foreach (var url in ContentUrlCandidates(repositoryPath, filePath))
            {
                var remote = SvnCli.RunForBytes(SvnCli.QueryIdleTimeout, "cat", Pegged(url, revision!));
                if (remote.Success)
                    return remote.StdOut;
            }

            return null;
        }
        catch (Exception ex)
        {
            RevisionControlLogger.Error("GetFileBytesAtRevision", ex);
            return null;
        }
    }

    /// <summary>
    /// A path or URL with an explicit peg revision: <c>target@revision</c>.
    /// </summary>
    /// <remarks>
    /// svn takes the <b>last</b> '@' of a target as the start of its peg revision, so always adding
    /// one is also what makes a target that contains an '@' of its own safe - its '@' is no longer
    /// the last. A URL's own '@' is escaped as <c>%40</c> by <see cref="ContentUrls"/> in any case.
    /// </remarks>
    internal static string Pegged(string target, string revision) => $"{target}@{revision}";

    /// <summary>
    /// The URLs a file path might mean, most likely first.
    /// </summary>
    /// <remarks>
    /// <para>Two path spaces reach this class and they are not distinguishable by looking at them.
    /// <c>svn log</c> reports <b>repository-root-relative</b> paths - <c>trunk/Modelica/Foo.mo</c> -
    /// while everything working from the checkout on disk uses <b>working-copy-relative</b> ones -
    /// <c>Modelica/Foo.mo</c>. A caller cannot reliably convert between them either: doing it by
    /// stripping a <c>trunk/</c> prefix and testing whether the result exists on disk fails whenever
    /// the working copy is older than the revision, and fails silently.</para>
    ///
    /// <para>So both are tried, against the repository root and against the working copy's own URL.
    /// One <c>svn cat</c> each, and only when the local route has already failed.</para>
    /// </remarks>
    internal static IReadOnlyList<string> ContentUrlCandidates(string repositoryPath, string filePath)
    {
        var info = GetInfo(repositoryPath);
        return info == null ? [] : ContentUrls(info.RepositoryRoot, info.Url, filePath);
    }

    /// <summary>
    /// The same two URLs without asking svn anything, so the shape of them can be tested without a
    /// server — which is most of what there is to get wrong here.
    /// </summary>
    internal static IReadOnlyList<string> ContentUrls(
        string repositoryRoot, string workingCopyUrl, string filePath)
    {
        // Forward slashes and escaped spaces: these are URLs, and library directories here are
        // called things like "VehicleLib - SuspensionLib".
        var relative = string.Join('/',
            filePath.Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(segment => segment.Length > 0)
                .Select(Uri.EscapeDataString));

        if (relative.Length == 0)
            return [];

        var fromRoot = $"{repositoryRoot.TrimEnd('/')}/{relative}";

        // The working copy's own URL is the branch it is checked out from, so this is the same file
        // asked for the other way - and it differs from the first only for a working-copy-relative
        // path, which is exactly the case the first one gets wrong.
        var fromWorkingCopy = $"{workingCopyUrl.TrimEnd('/')}/{relative}";

        return string.Equals(fromRoot, fromWorkingCopy, StringComparison.Ordinal)
            ? [fromRoot]
            : [fromRoot, fromWorkingCopy];
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
                    // Forward-slashed, as Git reports it on both platforms. GetRelativePath gives
                    // the OS's separator, so on Windows every consumer was handed Lib\Thing.mo by
                    // SVN and Lib/Thing.mo by Git for the same file (B472). See VcsRelativePath.
                    Path = VcsRelativePath.Canonical(Path.GetRelativePath(repositoryPath, e.Path)),
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
                            Path = VcsRelativePath.Canonical(Path.GetRelativePath(repositoryPath, newFile)),
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

            // Full paths throughout, so a path compares equal however the caller spelt it - the
            // skipped files below are taken out of this list by comparison.
            var workingCopyRoot = Path.GetFullPath(repositoryPath);
            var pathsToCommit = new List<string>();
            if (filesToCommit != null)
            {
                foreach (var file in filesToCommit)
                {
                    var fullPath = Path.GetFullPath(Path.Combine(repositoryPath, file));
                    if (!pathsToCommit.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                        pathsToCommit.Add(fullPath);
                }
            }
            else
            {
                pathsToCommit.Add(workingCopyRoot);
            }

            // If the repository root has uncommitted property changes (e.g. svn:mergeinfo updated
            // during a merge), include it in this commit regardless of selection. Leaving merge
            // metadata uncommitted puts the working copy in an inconsistent state that blocks
            // subsequent commits.
            var rootStatus = GetSingleStatus(repositoryPath);
            var rootPropClean = rootStatus != null && rootStatus.Props is "none" or "normal";
            if (!rootPropClean &&
                !pathsToCommit.Contains(workingCopyRoot, StringComparer.OrdinalIgnoreCase))
            {
                pathsToCommit.Add(workingCopyRoot);
            }

            // SVN requires files to be explicitly added before they can be committed.
            var addedParentDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Files that cannot be in this commit because their parent directory was Added via merge.
            // SVN forbids adding new files to a merge-committed directory in the same transaction.
            var filesToSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // **A handful of svn processes, not several per file.** Each path was asked about on its
            // own - a status, often an info, a status of its folder, an add - and every one of those
            // is a process that opens and locks the working copy's database. With 17,000 files the
            // dialog sat on "Preparing file N of M" for most of an hour. Now the paths and the
            // folders above the new ones are classified a chunk at a time, before anything is
            // changed, and the deletes and adds go to svn as one --targets list each.
            progress?.Report($"Reading the status of {pathsToCommit.Count} selected file(s)...");

            var root = Path.GetFullPath(repositoryPath);
            var selected = pathsToCommit.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var kinds = KindsOf(selected);

            // Missing from disk: scheduled for deletion, so the commit removes it from the
            // repository. Without this, SVN treats it as "missing" but not "deleted" and the commit
            // either ignores it or fails.
            var missing = selected
                .Where(p => !File.Exists(p) && !Directory.Exists(p) && kinds.GetValueOrDefault(p) == PathKind.Missing)
                .ToList();

            var newPaths = selected
                .Where(p => (File.Exists(p) || Directory.Exists(p)) && kinds.GetValueOrDefault(p) == PathKind.Unversioned)
                .ToList();

            // Every folder above a new path, up to the working copy root, asked about before anything
            // is added: which are new as well (and so added with the path), and which were added by a
            // merge.
            var ancestors = newPaths
                .SelectMany(p => FoldersBetween(root, p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var ancestorKinds = KindsOf(ancestors);

            var toAdd = new List<string>();
            foreach (var path in newPaths)
            {
                // If the parent directory was itself added via SVN merge, adding a new file to it in
                // the same commit causes an "out of date" server error. Mark the file for a separate
                // commit instead. Asked of the folder as it was before this commit added anything, so
                // a folder this commit adds is never mistaken for one a merge added.
                var parentDir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parentDir)
                    && !parentDir.Equals(root, OIC)
                    && ancestorKinds.GetValueOrDefault(parentDir) == PathKind.Added)
                {
                    filesToSkip.Add(path);
                    continue;
                }

                // The path is genuinely new. Its new folders are added with it, and listed in the
                // commit so they are in it.
                foreach (var folder in FoldersBetween(root, path))
                {
                    if (ancestorKinds.GetValueOrDefault(folder) == PathKind.Unversioned)
                        addedParentDirs.Add(folder);
                }

                toAdd.Add(path);
            }

            if (missing.Count > 0)
            {
                progress?.Report($"Scheduling {missing.Count} missing file(s) for deletion...");
                var deleted = RunWithTargets(repositoryPath, ["delete", "--force"], missing);
                if (!deleted.Success)
                {
                    result.ErrorMessage = $"Could not schedule {missing.Count} missing file(s) for deletion: {deleted.FailureMessage("svn delete failed.")}";
                    return result;
                }
            }

            if (toAdd.Count > 0)
            {
                progress?.Report($"Adding {toAdd.Count} new file(s)...");

                // A new file, and every new folder above one, on its own (--depth empty), so a folder
                // brings in nothing that was not selected. A new folder that was itself selected is
                // added with everything in it, as `svn add --parents folder` always added it.
                // A selected folder is added whole even when it is also above another selected file;
                // that file, and anything else inside such a folder, is left to the whole add, which
                // runs second, after its parents exist.
                var addWhole = toAdd.Where(Directory.Exists).ToList();
                bool InsideAWholeFolder(string p) => addWhole.Any(folder =>
                    p.Equals(folder, OIC) || p.StartsWith(folder + Path.DirectorySeparatorChar, OIC));
                var addEmpty = addedParentDirs
                    .Concat(toAdd.Where(p => !Directory.Exists(p)))
                    .Where(p => !InsideAWholeFolder(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p.Length)
                    .ToList();

                foreach (var (args, paths) in new (string[] Args, List<string> Paths)[]
                         {
                             (["add", "--parents", "--depth", "empty"], addEmpty),
                             (["add", "--parents"], addWhole),
                         })
                {
                    if (paths.Count == 0)
                        continue;

                    var add = RunWithTargets(repositoryPath, args, paths);
                    if (!add.Success)
                    {
                        result.ErrorMessage = add.FailureMessage("svn add failed.");
                        return result;
                    }
                }
            }

            // Remove files that must be committed separately (new files in merge-added directories).
            // Record them as skipped so the UI can offer a follow-up commit. Forward-slashed, the
            // shape GetWorkingCopyChanges reports them in, so the list matches the one the dialog
            // reloads for that commit (B478).
            foreach (var skipPath in filesToSkip)
            {
                pathsToCommit.RemoveAll(p => p.Equals(skipPath, OIC));
                result.SkippedFiles.Add(VcsRelativePath.Canonical(Path.GetRelativePath(repositoryPath, skipPath)));
            }

            // Add any parent directories that were scheduled for add to the commit paths.
            foreach (var dir in addedParentDirs)
            {
                if (!pathsToCommit.Contains(dir, StringComparer.OrdinalIgnoreCase))
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
            var commit = RunWithTargets(repositoryPath, ["commit", "-m", message], pathsToCommit);

            if (!commit.Success)
            {
                result.ErrorMessage = commit.FailureMessage("SVN commit failed.");
                result.IsOutOfDate = IsOutOfDateError(commit.StdErr);
                return result;
            }

            var newRevision = ParseCommittedRevision(commit.StdOut);
            if (newRevision == null)
            {
                // svn ran and committed nothing: every selected path already matches what the server
                // has. That is a different thing from a commit that failed, and saying "commit
                // failed" for it sent a diagnosis a long way down the wrong road (B266) - the svn
                // command had succeeded, so there was no error anywhere to find.
                result.ErrorMessage =
                    "Nothing was committed: the selected files match the versions already in the repository.";
                return result;
            }

            // Commit succeeded so run update.
            UpdateToLatest(repositoryPath);

            result.Success = true;
            result.NewRevision = newRevision;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = RevisionControlLogger.Failed("Commit", ex);
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
            var eligible = SvnCli.Run(["mergeinfo", "--show-revs", "eligible", sourceUrl, repositoryPath], stdinText: null, SvnCli.HistoryIdleTimeout);
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
            var merge = SvnCli.RunOnWorkingCopy(repositoryPath, "merge", sourceUrl, repositoryPath, "--accept", "postpone");

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
                result.ErrorMessage = merge.FailureMessage("SVN merge failed.");
                return result;
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = RevisionControlLogger.Failed("MergeBranch", ex);
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

            var resolve = SvnCli.RunOnWorkingCopy(repositoryPath, "resolve", "--accept", accept, filePath);
            result.Success = resolve.Success;
            if (!result.Success)
                result.ErrorMessage = resolve.FailureMessage("SVN resolve returned false.");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = RevisionControlLogger.Failed("ResolveConflict", ex);
        }
        return result;
    }

    /// <summary>
    /// Returns the "ours" and "theirs" versions of a conflicted SVN file.
    /// SVN writes sidecar files: filename.ext.mine (ours) and filename.ext.r{n} (theirs = highest revision).
    /// </summary>
    public (byte[]? ours, byte[]? theirs) GetConflictVersions(string repositoryPath, string filePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath) ?? "";
            var fileName = Path.GetFileName(filePath);

            // "Ours" = the working copy version before the merge conflict markers were applied.
            var mineFile = Path.Combine(dir, fileName + ".mine");
            // Bytes, not text: File.ReadAllText decodes as UTF-8 and a Windows-1252 library's
            // accented characters come back as replacement characters (B240).
            var ours = File.Exists(mineFile) ? File.ReadAllBytes(mineFile) : null;

            // "Theirs" = the incoming branch revision — highest-numbered .r{n} sidecar file.
            var rFiles = Directory.GetFiles(dir, fileName + ".r*")
                .Select(f =>
                {
                    var ext = Path.GetExtension(f).TrimStart('.');  // e.g. "r357" → "357" after [1..]
                    return (path: f, rev: long.TryParse(ext.Length > 1 ? ext[1..] : "", out var r) ? r : 0L);
                })
                .OrderByDescending(x => x.rev)
                .ToList();

            var theirs = rFiles.Count > 0 ? File.ReadAllBytes(rFiles[0].path) : null;
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

    /// <summary>
    /// Always null: SVN has no detached state. A working copy switched to a tag is switched to a
    /// directory like any other, and <c>GetCurrentBranch</c> names it (B193).
    /// </summary>
    public string? GetDetachedHeadLabel(string repositoryPath) => null;

    /// <inheritdoc/>
    public int CountCommitsOnNoBranch(string repositoryPath) => 0;

    /// <inheritdoc/>
    public VcsRebaseInProgress? GetRebaseInProgress(string repositoryPath) => null;

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

        var entry = SvnCli.RunXml(SvnCli.QueryIdleTimeout, args.ToArray())?.Root?.Element("entry");
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
    /// Which of these paths are unversioned: reported so by <c>svn status</c>, or not found by it
    /// and on disk, which is a path inside a folder that is itself unversioned.
    /// </summary>
    /// <remarks>
    /// <c>svn status</c> takes no <c>--targets</c> file, so the paths go on the command line in
    /// chunks kept well inside Windows' 32,767-character limit: a process per chunk of a hundred or
    /// so files rather than one per file. A path svn cannot find comes back as an empty
    /// <c>&lt;target&gt;</c> with a W155010 warning, and the command still succeeds.
    /// </remarks>
    private static HashSet<string> UnversionedAmong(IReadOnlyList<string> fullPaths) =>
        KindsOf(fullPaths)
            .Where(kind => kind.Value == PathKind.Unversioned)
            .Select(kind => kind.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>What a path is to the working copy, as far as a revert or a commit needs to know.</summary>
    internal enum PathKind
    {
        /// <summary>Under version control, modified or not.</summary>
        Versioned,

        /// <summary>Scheduled for addition - by the user, or by a merge.</summary>
        Added,

        /// <summary>Versioned, and gone from disk without being deleted through svn.</summary>
        Missing,

        /// <summary>On disk and not under version control, a path inside a new folder included.</summary>
        Unversioned,

        /// <summary>Neither on disk nor known to svn.</summary>
        NotFound,
    }

    /// <summary>
    /// What each path is to the working copy, asked of all of them in a few svn processes: one
    /// <c>svn status</c> per chunk of paths, then one <c>svn info</c> for the paths status could not
    /// place.
    /// </summary>
    /// <remarks>
    /// <para><c>svn status</c> takes no <c>--targets</c> file, so the paths go on the command line in
    /// chunks kept well inside Windows' 32,767-character limit (<see cref="CommandLineChunks"/>).</para>
    ///
    /// <para><b>An empty <c>&lt;target&gt;</c> is two things</b>: a versioned path with nothing to
    /// report, and a path svn cannot find - a file inside a folder that is itself new, which status
    /// warns about (W155010) and leaves empty. Those are told apart by one <c>svn info --targets</c>,
    /// which lists the versioned ones and ends with E200009 for the rest.</para>
    /// </remarks>
    private static Dictionary<string, PathKind> KindsOf(IReadOnlyList<string> fullPaths)
    {
        var kinds = new Dictionary<string, PathKind>(StringComparer.OrdinalIgnoreCase);
        var unplaced = new List<string>();

        foreach (var chunk in CommandLineChunks(fullPaths))
        {
            var args = new List<string> { "status", "--depth", "empty" };
            args.AddRange(chunk);
            var targets = SvnCli.RunXml(args.ToArray())?.Root?.Elements("target");
            if (targets is null)
            {
                // Status itself failed for this chunk: nothing in it is known, so it is asked again
                // below rather than taken for versioned.
                unplaced.AddRange(chunk);
                continue;
            }

            foreach (var target in targets)
            {
                var path = target.Attribute("path")?.Value;
                if (string.IsNullOrEmpty(path))
                    continue;

                path = Path.GetFullPath(path);
                var item = target.Element("entry")?.Element("wc-status")?.Attribute("item")?.Value;
                switch (item)
                {
                    case null:
                        unplaced.Add(path);
                        break;
                    case "unversioned":
                        kinds[path] = PathKind.Unversioned;
                        break;
                    case "added":
                        kinds[path] = PathKind.Added;
                        break;
                    case "missing":
                        kinds[path] = PathKind.Missing;
                        break;
                    default:
                        kinds[path] = PathKind.Versioned;
                        break;
                }
            }
        }

        if (unplaced.Count == 0)
            return kinds;

        var info = WithTargetsFile(unplaced, targets => SvnCli.RunXmlAllowingMissingTargets("info", "--targets", targets));
        foreach (var (path, kind) in ClassifyUnplaced(unplaced, info, p => File.Exists(p) || Directory.Exists(p)))
            kinds[path] = kind;

        return kinds;
    }

    /// <summary>
    /// What the paths status could not place are, from <c>svn info</c>'s answer about them: the ones
    /// it lists are versioned, and of the rest those on disk are unversioned.
    /// </summary>
    /// <remarks>
    /// <b>No answer is not "unversioned".</b> When <c>svn info</c> itself failed - stopped for going
    /// silent, a working copy that needs a cleanup - <paramref name="info"/> is null and nothing is
    /// known, so every path is taken for versioned. Taken for unversioned, a revert deleted them: the
    /// user's modified files, with no <c>svn revert</c> left to run, and fail, first. Versioned is the
    /// safe mistake: a revert hands the path to svn, which reverts it or says why not, and a commit
    /// does not add it.
    /// </remarks>
    internal static IEnumerable<(string Path, PathKind Kind)> ClassifyUnplaced(
        IReadOnlyList<string> unplaced, System.Xml.Linq.XDocument? info, Func<string, bool> existsOnDisk)
    {
        if (info is null)
            return unplaced.Select(path => (path, PathKind.Versioned));

        var versioned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in info.Root?.Elements("entry") ?? [])
        {
            if (entry.Attribute("path")?.Value is { Length: > 0 } path)
                versioned.Add(Path.GetFullPath(path));
        }

        return unplaced.Select(path => (path,
            versioned.Contains(path) ? PathKind.Versioned
            : existsOnDisk(path) ? PathKind.Unversioned
            : PathKind.NotFound));
    }

    /// <summary>
    /// Runs <paramref name="run"/> with the paths written to a temporary <c>--targets</c> file, which
    /// keeps any number of paths, and paths with spaces, out of the command line, and deletes the file
    /// afterwards.
    /// </summary>
    private static T WithTargetsFile<T>(IEnumerable<string> paths, Func<string, T> run)
    {
        var targetsFile = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(targetsFile, paths);
            return run(targetsFile);
        }
        finally
        {
            try { File.Delete(targetsFile); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// The folders strictly between a working copy root and a path inside it, nearest the root first.
    /// </summary>
    internal static IEnumerable<string> FoldersBetween(string root, string path)
    {
        var folders = new List<string>();
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        while (!string.IsNullOrEmpty(folder)
               && folder.Length > normalizedRoot.Length
               && folder.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, OIC))
        {
            folders.Add(folder);
            folder = Path.GetDirectoryName(folder);
        }

        folders.Reverse();
        return folders;
    }

    /// <summary>
    /// Runs an svn command on a working copy over a list of paths given as a <c>--targets</c> file,
    /// which keeps any number of paths, and paths with spaces, out of the command line.
    /// </summary>
    private static SvnCli.Result RunWithTargets(string workingCopy, string[] args, IReadOnlyCollection<string> paths) =>
        WithTargetsFile(paths, targets => SvnCli.RunOnWorkingCopy(workingCopy, [.. args, "--targets", targets]));

    /// <summary>The paths in groups whose command line stays inside Windows' limit.</summary>
    internal static IEnumerable<List<string>> CommandLineChunks(IReadOnlyList<string> paths, int maxChars = 24_000)
    {
        var chunk = new List<string>();
        var length = 0;
        foreach (var path in paths)
        {
            // Quotes and a space each, as the runtime writes them.
            var cost = path.Length + 3;
            if (chunk.Count > 0 && length + cost > maxChars)
            {
                yield return chunk;
                chunk = new List<string>();
                length = 0;
            }

            chunk.Add(path);
            length += cost;
        }

        if (chunk.Count > 0)
            yield return chunk;
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
