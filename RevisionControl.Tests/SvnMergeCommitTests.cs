using SharpSvn;

namespace RevisionControl.Tests;

/// <summary>
/// Integration tests for the SVN merge-then-commit scenario.
///
/// These tests reproduce the bug where committing a locally-created settings file
/// (e.g. .mlqt/settings.json) fails after a two-phase merge commit:
///
/// Phase 1 commit: Commits the directory Added via merge (.mlqt/) and svn:mergeinfo,
///                 but must SKIP any locally-created files inside it.
/// Phase 2 commit: Commits those skipped files now that their parent is versioned.
///
/// IMPORTANT: These tests NEVER commit to trunk.
/// Instead, they create two temporary branches from tags/v2.0:
///   - Source branch: receives the new directory (simulates trunk having .mlqt/)
///   - Target branch: the branch being worked on (simulates the user's working copy)
/// Both branches are disposable test artefacts that do not affect trunk.
///
/// Test repository: file:///C:/Projects/SVN/ModelicaEditorTest
/// Branch source:   tags/v2.0  (predates the test directories)
/// </summary>
public class SvnMergeCommitTests : IDisposable
{
    private const string TestRepoUrl = "file:///C:/Projects/SVN/ModelicaEditorTest";
    private const string TrunkUrl = TestRepoUrl + "/trunk";
    private const string TagV2Url = TestRepoUrl + "/tags/v2.0";

    private readonly SvnRevisionControlSystem _svn;
    private readonly List<string> _checkoutPaths = new();
    private readonly bool _repositoryAvailable;

    public SvnMergeCommitTests()
    {
        _svn = new SvnRevisionControlSystem();

        try
        {
            using var client = new SvnClient();
            client.GetInfo(new Uri(TrunkUrl), out _);
            client.GetInfo(new Uri(TagV2Url), out _);
            _repositoryAvailable = true;
        }
        catch
        {
            _repositoryAvailable = false;
        }
    }

    public void Dispose()
    {
        foreach (var path in _checkoutPaths)
            ForceDeleteDirectory(path);
    }

    private string CreateCheckoutPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "SvnMergeTest_" + Guid.NewGuid());
        _checkoutPaths.Add(path);
        return path;
    }

    private static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    /// <summary>
    /// Creates two fresh branches from tags/v2.0:
    ///   source — receives the test directory (simulates trunk having .mlqt/)
    ///   target — the branch being merged into (simulates the user's working copy)
    /// Returns relative paths like "branches/test-src-abc123" and "branches/test-tgt-abc123".
    /// Both branches are isolated from trunk.
    /// </summary>
    private (string Source, string Target) CreateTestBranches(string testId)
    {
        using var client = new SvnClient();
        var repoRoot = new Uri(TestRepoUrl + "/");
        var tagUri = new Uri(repoRoot, "tags/v2.0");

        var source = $"branches/test-src-{testId}";
        var target = $"branches/test-tgt-{testId}";

        var ok1 = client.RemoteCopy(tagUri, new Uri(repoRoot, source),
            new SvnCopyArgs { LogMessage = $"Create source branch for merge test {testId}" });
        Assert.True(ok1, $"Failed to create source branch '{source}'");

        var ok2 = client.RemoteCopy(tagUri, new Uri(repoRoot, target),
            new SvnCopyArgs { LogMessage = $"Create target branch for merge test {testId}" });
        Assert.True(ok2, $"Failed to create target branch '{target}'");

        return (source, target);
    }

    // ============================================================================
    // Main regression test: two-phase commit after merge
    // ============================================================================

    /// <summary>
    /// Reproduces the full two-phase commit scenario after a merge.
    ///
    /// Scenario (settings.json created AFTER merge — simplest case):
    ///   1. Create two branches from tags/v2.0 (source + target)
    ///   2. Add a new directory to the source branch (simulates .mlqt/ on trunk)
    ///   3. Checkout the target branch
    ///   4. Merge source into the target — directory shows as Added via merge
    ///   5. Create settings.json locally inside the Added directory
    ///   6. First commit: include directory + mergeinfo, skip settings.json
    ///   7. Second commit: successfully commit settings.json
    /// </summary>
    [Fact]
    public void Commit_MergeScenario_SkippedFileCommittedSuccessfullyInSecondCommit()
    {
        if (!_repositoryAvailable) return;

        var testId = Guid.NewGuid().ToString("N")[..8];
        var (sourceBranch, targetBranch) = CreateTestBranches(testId);
        var mlqtDir = $".mlqt-test-{testId}";

        // === Step 1: Add a new directory to the source branch ===
        // (simulates trunk having the .mlqt/ directory that the target branch doesn't yet have)
        var sourceCheckoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(TestRepoUrl + "/" + sourceBranch, "HEAD", sourceCheckoutPath);

        Directory.CreateDirectory(Path.Combine(sourceCheckoutPath, mlqtDir));
        File.WriteAllText(
            Path.Combine(sourceCheckoutPath, mlqtDir, "config.json"),
            $"{{\"testId\":\"{testId}\"}}");

        var addResult = _svn.Commit(
            sourceCheckoutPath,
            $"Add {mlqtDir} to source branch (test {testId})",
            new[] { Path.Combine(mlqtDir, "config.json") });
        Assert.True(addResult.Success,
            $"Step 1: failed to add {mlqtDir} to source branch — {addResult.ErrorMessage}");

        // === Step 2: Checkout the target branch ===
        var targetCheckoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(TestRepoUrl + "/" + targetBranch, "HEAD", targetCheckoutPath);

        Assert.False(Directory.Exists(Path.Combine(targetCheckoutPath, mlqtDir)),
            $"Step 2: {mlqtDir} should not exist in the target branch");

        // === Step 3: Merge source into target ===
        var mergeResult = _svn.MergeBranch(targetCheckoutPath, sourceBranch);
        Assert.True(mergeResult.Success,
            $"Step 3: merge failed — {mergeResult.ErrorMessage}");

        Assert.True(Directory.Exists(Path.Combine(targetCheckoutPath, mlqtDir)),
            $"Step 3: {mlqtDir} should exist in target after merge");

        // === Step 4: Simulate MLQT creating settings.json locally ===
        var settingsRelPath = Path.Combine(mlqtDir, "settings.json");
        File.WriteAllText(
            Path.Combine(targetCheckoutPath, settingsRelPath),
            $"{{\"repo\":\"{targetCheckoutPath.Replace("\\", "\\\\")}\"}}");

        // === Step 5: Inspect working copy changes ===
        // Collect raw SVN status for diagnostics
        var rawSvnStatus = new List<(string Path, SvnStatus Content, SvnStatus Prop)>();
        using (var diagClient = new SvnClient())
        {
            diagClient.Status(targetCheckoutPath,
                new SvnStatusArgs { Depth = SvnDepth.Infinity },
                (_, e) => rawSvnStatus.Add((
                    Path.GetRelativePath(targetCheckoutPath, e.FullPath),
                    e.LocalContentStatus,
                    e.LocalPropertyStatus)));
        }
        var rawStatusSummary = string.Join(", ",
            rawSvnStatus.Select(s => $"{s.Path}(c={s.Content},p={s.Prop})"));

        var changesAfterMerge = _svn.GetWorkingCopyChanges(targetCheckoutPath);
        var changesSummary = string.Join(", ",
            changesAfterMerge.Select(c => $"{c.Path}({c.Status})"));

        Assert.NotEmpty(changesAfterMerge);

        // The test directory must show as Added (from merge, not Untracked)
        var mlqtEntry = changesAfterMerge.FirstOrDefault(c =>
            c.Path.TrimEnd(Path.DirectorySeparatorChar)
             .Equals(mlqtDir, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(mlqtEntry);
        Assert.Equal(VcsFileStatus.Added, mlqtEntry!.Status);

        // settings.json should be Untracked (locally created, not in any branch)
        var settingsEntry = changesAfterMerge.FirstOrDefault(c =>
            c.Path.Contains("settings.json", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(settingsEntry);
        Assert.Equal(VcsFileStatus.Untracked, settingsEntry!.Status);

        // Root should show a property change (svn:mergeinfo)
        var rootEntry = changesAfterMerge.FirstOrDefault(c =>
            c.Path is "." or "" ||
            c.Path.Equals(targetCheckoutPath, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(rootEntry);
        Assert.Equal(VcsFileStatus.Modified, rootEntry!.Status);

        // === Step 6: First commit — all visible changed files ===
        var firstCommitFiles = changesAfterMerge.Select(c => c.Path).ToList();
        var firstCommitResult = _svn.Commit(
            targetCheckoutPath,
            $"Merge source into target branch (test {testId})",
            firstCommitFiles);

        Assert.True(firstCommitResult.Success,
            $"Step 6: first commit failed — {firstCommitResult.ErrorMessage}\n" +
            $"  Files submitted  : [{string.Join(", ", firstCommitFiles)}]\n" +
            $"  GetWCChanges     : [{changesSummary}]\n" +
            $"  Raw SVN status   : [{rawStatusSummary}]");

        // settings.json must have been skipped (parent Added via merge)
        Assert.True(
            firstCommitResult.SkippedFiles.Any(f =>
                f.Contains("settings.json", StringComparison.OrdinalIgnoreCase)),
            $"Step 6: settings.json should be skipped. " +
            $"SkippedFiles=[{string.Join(", ", firstCommitResult.SkippedFiles)}]\n" +
            $"GetWCChanges=[{changesSummary}]\nRaw SVN=[{rawStatusSummary}]");

        // === Step 7: Inspect working copy after first commit ===
        var changesAfterFirst = _svn.GetWorkingCopyChanges(targetCheckoutPath);
        var changesAfterFirstSummary = string.Join(", ",
            changesAfterFirst.Select(c => $"{c.Path}({c.Status})"));

        // The directory should be committed — no longer in pending changes
        var mlqtAfterFirst = changesAfterFirst.FirstOrDefault(c =>
            c.Path.TrimEnd(Path.DirectorySeparatorChar)
             .Equals(mlqtDir, StringComparison.OrdinalIgnoreCase));
        Assert.Null(mlqtAfterFirst);

        // settings.json should still be Untracked (it was skipped)
        var settingsAfterFirst = changesAfterFirst.FirstOrDefault(c =>
            c.Path.Contains("settings.json", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(settingsAfterFirst);
        Assert.Equal(VcsFileStatus.Untracked, settingsAfterFirst!.Status);

        // Verify the directory is now versioned (Normal) using GetInfo
        string mlqtDirStatusMsg;
        using (var diagClient = new SvnClient())
        {
            var isVersioned = diagClient.GetInfo(
                Path.Combine(targetCheckoutPath, mlqtDir), out var mlqtInfo);
            mlqtDirStatusMsg = isVersioned
                ? $"versioned (rev {mlqtInfo?.Revision})"
                : "NOT versioned";
            Assert.True(isVersioned,
                $"Step 7: {mlqtDir} should be versioned after first commit");
        }

        // Verify settings.json has NotVersioned status (using RetrieveAllEntries so the
        // callback fires even though its parent is now Normal/clean)
        SvnStatusEventArgs? settingsFileStatus = null;
        using (var diagClient = new SvnClient())
        {
            diagClient.Status(
                Path.Combine(targetCheckoutPath, settingsRelPath),
                new SvnStatusArgs { Depth = SvnDepth.Empty, RetrieveAllEntries = true },
                (_, e) => { settingsFileStatus = e; });
        }
        Assert.NotNull(settingsFileStatus);
        Assert.Equal(SvnStatus.NotVersioned, settingsFileStatus!.LocalContentStatus);

        // === Step 8: Second commit — the skipped files ===
        var secondCommitFiles = firstCommitResult.SkippedFiles;
        var secondCommitResult = _svn.Commit(
            targetCheckoutPath,
            $"Add local settings file (test {testId})",
            secondCommitFiles);

        Assert.True(secondCommitResult.Success,
            $"Step 8: second commit failed — {secondCommitResult.ErrorMessage}\n" +
            $"  Files submitted       : [{string.Join(", ", secondCommitFiles)}]\n" +
            $"  Changes before        : [{changesAfterFirstSummary}]\n" +
            $"  {mlqtDir} after commit : {mlqtDirStatusMsg}\n" +
            $"  settings.json status  : {settingsFileStatus?.LocalContentStatus}");

        Assert.Empty(secondCommitResult.SkippedFiles);

        // === Step 9: Final state — working copy should be clean ===
        var changesAfterSecond = _svn.GetWorkingCopyChanges(targetCheckoutPath);
        Assert.Empty(changesAfterSecond);
    }

    /// <summary>
    /// Variant where settings.json is created BEFORE the merge
    /// (the real-world MLQT case: MLQT creates .mlqt/settings.json when opening a branch,
    /// then the user triggers a merge from the source branch).
    /// </summary>
    [Fact]
    public void Commit_MergeScenario_SettingsCreatedBeforeMerge_BothCommitsSucceed()
    {
        if (!_repositoryAvailable) return;

        var testId = Guid.NewGuid().ToString("N")[..8];
        var (sourceBranch, targetBranch) = CreateTestBranches(testId);
        var mlqtDir = $".mlqt-pre-{testId}";

        // === Step 1: Add directory to source branch ===
        var sourceCheckoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(TestRepoUrl + "/" + sourceBranch, "HEAD", sourceCheckoutPath);

        Directory.CreateDirectory(Path.Combine(sourceCheckoutPath, mlqtDir));
        File.WriteAllText(
            Path.Combine(sourceCheckoutPath, mlqtDir, "config.json"),
            $"{{\"testId\":\"{testId}\"}}");

        var addResult = _svn.Commit(
            sourceCheckoutPath,
            $"Add {mlqtDir} to source branch (pre-merge test {testId})",
            new[] { Path.Combine(mlqtDir, "config.json") });
        Assert.True(addResult.Success,
            $"Step 1 failed: {addResult.ErrorMessage}");

        // === Step 2: Checkout target branch ===
        var targetCheckoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(TestRepoUrl + "/" + targetBranch, "HEAD", targetCheckoutPath);

        // === Step 3: MLQT creates settings.json BEFORE the merge ===
        var settingsRelPath = Path.Combine(mlqtDir, "settings.json");
        Directory.CreateDirectory(Path.Combine(targetCheckoutPath, mlqtDir));
        File.WriteAllText(
            Path.Combine(targetCheckoutPath, settingsRelPath),
            $"{{\"repo\":\"{targetCheckoutPath.Replace("\\", "\\\\")}\"}}");

        // === Step 4: Merge source into target ===
        var mergeResult = _svn.MergeBranch(targetCheckoutPath, sourceBranch);

        var conflictSummary = mergeResult.HasConflicts
            ? $"Conflicts: [{string.Join(", ", mergeResult.ConflictedFiles)}]"
            : "No conflicts";

        // === Step 5: Inspect changes and run first commit ===
        var changesAfterMerge = _svn.GetWorkingCopyChanges(targetCheckoutPath);
        var changesSummary = string.Join(", ",
            changesAfterMerge.Select(c => $"{c.Path}({c.Status})"));

        Assert.NotEmpty(changesAfterMerge);

        // settings.json should exist and NOT be Added (we did not run svn add on it)
        var settingsEntry = changesAfterMerge.FirstOrDefault(c =>
            c.Path.Contains("settings.json", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(settingsEntry);
        Assert.NotEqual(VcsFileStatus.Added, settingsEntry!.Status);

        var firstCommitFiles = changesAfterMerge.Select(c => c.Path).ToList();
        var firstCommitResult = _svn.Commit(
            targetCheckoutPath,
            $"Merge source into target (pre-merge test {testId})",
            firstCommitFiles);

        Assert.True(firstCommitResult.Success,
            $"Step 5: first commit failed — {firstCommitResult.ErrorMessage}\n" +
            $"  Changes: [{changesSummary}]\n" +
            $"  Merge: {conflictSummary}");

        // === Step 6: Second commit for any skipped files ===
        if (firstCommitResult.SkippedFiles.Count > 0)
        {
            var secondCommitResult = _svn.Commit(
                targetCheckoutPath,
                $"Add local settings file (pre-merge test {testId})",
                firstCommitResult.SkippedFiles);

            Assert.True(secondCommitResult.Success,
                $"Step 6: second commit failed — {secondCommitResult.ErrorMessage}\n" +
                $"  Skipped files: [{string.Join(", ", firstCommitResult.SkippedFiles)}]");
        }

        // Working copy should be clean
        var finalChanges = _svn.GetWorkingCopyChanges(targetCheckoutPath);
        Assert.Empty(finalChanges);
    }

    // ============================================================================
    // Diagnostic: verify root property change is visible
    // ============================================================================

    [Fact]
    public void GetWorkingCopyChanges_AfterMerge_IncludesRootSvnMergeInfoPropertyChange()
    {
        if (!_repositoryAvailable) return;

        var testId = Guid.NewGuid().ToString("N")[..8];
        var (sourceBranch, targetBranch) = CreateTestBranches(testId);

        // Put something on the source branch so the merge has something to do
        var sourceCheckoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(TestRepoUrl + "/" + sourceBranch, "HEAD", sourceCheckoutPath);
        File.WriteAllText(
            Path.Combine(sourceCheckoutPath, "merge-marker.txt"),
            $"Marker for merge test {testId}");
        var addResult = _svn.Commit(sourceCheckoutPath,
            $"Add merge marker (test {testId})",
            new[] { "merge-marker.txt" });
        Assert.True(addResult.Success, $"Marker commit failed: {addResult.ErrorMessage}");

        var targetCheckoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(TestRepoUrl + "/" + targetBranch, "HEAD", targetCheckoutPath);

        var mergeResult = _svn.MergeBranch(targetCheckoutPath, sourceBranch);
        Assert.True(mergeResult.Success, $"Merge failed: {mergeResult.ErrorMessage}");

        var changes = _svn.GetWorkingCopyChanges(targetCheckoutPath);
        Assert.NotEmpty(changes);

        // Root directory should appear with Modified status due to svn:mergeinfo
        var rootEntry = changes.FirstOrDefault(c =>
            c.Path is "." or "" ||
            c.Path.Equals(targetCheckoutPath, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(rootEntry);
        Assert.Equal(VcsFileStatus.Modified, rootEntry!.Status);
    }

    // ============================================================================
    // Diagnostic: directory status transitions across merge + commit
    // ============================================================================

    [Fact]
    public void Commit_AfterFirstMergeCommit_mlqtDirIsVersionedNotAdded()
    {
        if (!_repositoryAvailable) return;

        var testId = Guid.NewGuid().ToString("N")[..8];
        var (sourceBranch, targetBranch) = CreateTestBranches(testId);
        var mlqtDir = $".mlqt-diag-{testId}";

        // Add directory to source branch
        var sourceCheckoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(TestRepoUrl + "/" + sourceBranch, "HEAD", sourceCheckoutPath);

        Directory.CreateDirectory(Path.Combine(sourceCheckoutPath, mlqtDir));
        File.WriteAllText(Path.Combine(sourceCheckoutPath, mlqtDir, "config.json"), "{}");
        var addResult = _svn.Commit(sourceCheckoutPath, $"Add {mlqtDir} (diag {testId})",
            new[] { Path.Combine(mlqtDir, "config.json") });
        Assert.True(addResult.Success, $"Add to source failed: {addResult.ErrorMessage}");

        // Checkout target and merge
        var targetCheckoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(TestRepoUrl + "/" + targetBranch, "HEAD", targetCheckoutPath);

        var mergeResult = _svn.MergeBranch(targetCheckoutPath, sourceBranch);
        Assert.True(mergeResult.Success, $"Merge failed: {mergeResult.ErrorMessage}");

        // Verify mlqtDir is Added before first commit
        SvnStatusEventArgs? beforeStatus = null;
        using (var client = new SvnClient())
        {
            client.Status(
                Path.Combine(targetCheckoutPath, mlqtDir),
                new SvnStatusArgs { Depth = SvnDepth.Empty },
                (_, e) => { beforeStatus = e; });
        }
        Assert.NotNull(beforeStatus);
        Assert.Equal(SvnStatus.Added, beforeStatus!.LocalContentStatus);

        // First commit
        var changes = _svn.GetWorkingCopyChanges(targetCheckoutPath);
        var commitFiles = changes.Select(c => c.Path).ToList();
        var commitResult = _svn.Commit(targetCheckoutPath,
            $"Merge commit (diag {testId})", commitFiles);
        Assert.True(commitResult.Success, $"First commit failed: {commitResult.ErrorMessage}");

        // After commit, mlqtDir should be versioned (Normal) — GetInfo succeeds
        using (var client = new SvnClient())
        {
            var isVersioned = client.GetInfo(
                Path.Combine(targetCheckoutPath, mlqtDir), out _);
            Assert.True(isVersioned,
                $"{mlqtDir} should be versioned (committed) after first commit");
        }

        // SVN status should NOT report it (it's Normal/clean — no change to report)
        SvnStatusEventArgs? afterStatus = null;
        using (var client = new SvnClient())
        {
            client.Status(
                Path.Combine(targetCheckoutPath, mlqtDir),
                new SvnStatusArgs { Depth = SvnDepth.Empty },
                (_, e) => { afterStatus = e; });
        }
        // afterStatus is null for a clean Normal dir (nothing to report) — that's correct
        if (afterStatus != null)
        {
            Assert.NotEqual(SvnStatus.Added, afterStatus.LocalContentStatus);
        }
    }

    // ============================================================================
    // Regression: merged .mo file (no within-clause) must survive as VCS-Added
    // ============================================================================

    /// <summary>
    /// Regression test for the bug where MLQT's formatter deleted Modelica files
    /// that were added to the working copy by an SVN merge.
    ///
    /// Root cause: a file with no within-clause is treated as a top-level model
    /// and written to a different path by the formatter.  The original file becomes
    /// an "orphan" (not in allWrittenFiles) and is deleted.  SVN still shows the
    /// file as "scheduled for addition", causing a commit error.
    ///
    /// Fix: before deleting orphans, check GetWorkingCopyChanges() and skip any
    /// file with VcsFileStatus.Added.
    ///
    /// This test verifies:
    ///   1. SVN merge writes the file to disk.
    ///   2. GetWorkingCopyChanges() correctly reports the file as VcsFileStatus.Added.
    ///   3. (The application-level formatter fix is in MergeBranchDialog and MainLayout.)
    /// </summary>
    [Fact]
    public void Merge_NewModelicaFile_ExistsOnDiskAndReportedAsAdded()
    {
        if (!_repositoryAvailable) return;

        var testId = Guid.NewGuid().ToString("N")[..8];
        var (sourceBranch, targetBranch) = CreateTestBranches(testId);

        // === Step 1: Add a .mo file (no within-clause, name mismatches filename) to source ===
        // This mimics the real-world scenario where a poorly-formed Modelica file in trunk
        // would be merged into a branch and then deleted by the MLQT formatter.
        var sourceCheckoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(TestRepoUrl + "/" + sourceBranch, "HEAD", sourceCheckoutPath);

        var newFileName = $"Diag_{testId}.mo";
        var newFilePath = Path.Combine(sourceCheckoutPath, "Models", newFileName);

        // Intentionally malformed: no within-clause, model name differs from filename.
        // This is the content that triggered the original bug.
        File.WriteAllText(newFilePath, "model DiagModel end DiagModel;");

        var addResult = _svn.Commit(
            sourceCheckoutPath,
            $"Add {newFileName} to source branch (merge regression test {testId})",
            new[] { Path.Combine("Models", newFileName) });
        Assert.True(addResult.Success, $"Step 1 (add .mo to source): {addResult.ErrorMessage}");

        // === Step 2: Checkout target branch and merge ===
        var targetCheckoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(TestRepoUrl + "/" + targetBranch, "HEAD", targetCheckoutPath);

        var mergeResult = _svn.MergeBranch(targetCheckoutPath, sourceBranch);
        Assert.True(mergeResult.Success, $"Step 2 (merge): {mergeResult.ErrorMessage}");

        // === Step 3: Verify the file is on disk after the merge ===
        var mergedFilePath = Path.Combine(targetCheckoutPath, "Models", newFileName);
        Assert.True(File.Exists(mergedFilePath),
            $"Step 3: {newFileName} must exist on disk after merge — " +
            "if missing the SVN merge itself failed to write the file");

        // === Step 4: Verify GetWorkingCopyChanges reports it as Added ===
        // This is what the formatter fix queries before deciding to delete orphans.
        var changes = _svn.GetWorkingCopyChanges(targetCheckoutPath);
        var moFileEntry = changes.FirstOrDefault(c =>
            c.Path.EndsWith(newFileName, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(moFileEntry);
        Assert.Equal(VcsFileStatus.Added, moFileEntry!.Status);
        // Confirm the VCS path resolves to the on-disk file
        Assert.True(
            File.Exists(Path.Combine(targetCheckoutPath, moFileEntry.Path)),
            "Path.Combine(targetCheckoutPath, moFileEntry.Path) must point to the real file");
    }

    // ============================================================================
    // Diagnostic: svn add behaviour on an untracked file inside a Normal directory
    // ============================================================================

    [Fact]
    public void SvnAdd_UntrackedFileInVersionedDirectory_StatusBecomesAdded()
    {
        if (!_repositoryAvailable) return;

        var testId = Guid.NewGuid().ToString("N")[..8];

        // Use a branch (not trunk) so we never commit to trunk
        var branchName = $"branches/test-add-{testId}";
        using (var client = new SvnClient())
        {
            var ok = client.RemoteCopy(
                new Uri(TestRepoUrl + "/tags/v2.0"),
                new Uri(TestRepoUrl + "/" + branchName),
                new SvnCopyArgs { LogMessage = $"Create branch for add test {testId}" });
            Assert.True(ok, "Failed to create test branch");
        }

        var checkoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(TestRepoUrl + "/" + branchName, "HEAD", checkoutPath);

        // Models directory should exist (it's in tags/v2.0)
        var modelsDir = Path.Combine(checkoutPath, "Models");
        Assert.True(Directory.Exists(modelsDir), "Models directory should exist");

        // Create a new untracked file inside the versioned Models directory
        var newFileName = $"Diag_{testId}.mo";
        var newFilePath = Path.Combine(modelsDir, newFileName);
        File.WriteAllText(newFilePath, "model DiagModel end DiagModel;");

        // Verify status is NotVersioned before adding
        SvnStatusEventArgs? beforeAddStatus = null;
        using (var client = new SvnClient())
        {
            client.Status(newFilePath, new SvnStatusArgs { Depth = SvnDepth.Empty },
                (_, e) => { beforeAddStatus = e; });
        }
        Assert.NotNull(beforeAddStatus);
        Assert.Equal(SvnStatus.NotVersioned, beforeAddStatus!.LocalContentStatus);

        // Run svn add
        using (var client = new SvnClient())
        {
            client.Add(newFilePath, new SvnAddArgs { AddParents = false });
        }

        // Verify status is now Added
        SvnStatusEventArgs? afterAddStatus = null;
        using (var client = new SvnClient())
        {
            client.Status(newFilePath, new SvnStatusArgs { Depth = SvnDepth.Empty },
                (_, e) => { afterAddStatus = e; });
        }
        Assert.NotNull(afterAddStatus);
        Assert.Equal(SvnStatus.Added, afterAddStatus!.LocalContentStatus);

        // Revert (do NOT commit — we do not want to leave test artefacts in the branch)
        using (var client = new SvnClient())
        {
            client.Revert(newFilePath);
        }
    }
}
