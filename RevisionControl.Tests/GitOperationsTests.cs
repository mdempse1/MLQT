using LibGit2Sharp;

namespace RevisionControl.Tests;

/// <summary>
/// Tests for GitRevisionControlSystem operations:
/// GetLogEntries, GetChangedFiles, GetWorkingCopyChanges, GetBranches,
/// Commit, RevertFiles, SwitchBranch, CreateBranch, GetFileContentAtRevision,
/// MergeBranch, UpdateToLatest, Push, ForcePush, IsBranchPushed,
/// GetPullRequestUrl, Rebase, ContinueRebase, AbortRebase, CheckoutRevision (in-place).
/// All tests use temporary local repositories and clean up after themselves.
/// </summary>
public class GitOperationsTests : IDisposable
{
    private readonly GitRevisionControlSystem _git;
    private readonly string _tempBase;
    private readonly List<string> _tempPaths = new();

    public GitOperationsTests()
    {
        _git = new GitRevisionControlSystem();
        _tempBase = Path.Combine(Path.GetTempPath(), "GitOpsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempBase);
    }

    public void Dispose()
    {
        ForceDeleteDirectory(_tempBase);
        foreach (var path in _tempPaths)
            ForceDeleteDirectory(path);
    }

    private string NewTempPath(string prefix = "GitOps")
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        _tempPaths.Add(path);
        return path;
    }

    private static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    /// <summary>Creates a temp repo with one commit containing the given files.</summary>
    private (Repository repo, string repoPath) CreateRepoWithFiles(
        Dictionary<string, string> files,
        string commitMessage = "Initial commit")
    {
        var repoPath = NewTempPath();
        Repository.Init(repoPath);
        var repo = new Repository(repoPath);

        foreach (var (relativePath, content) in files)
        {
            var fullPath = Path.Combine(repoPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
            Commands.Stage(repo, relativePath);
        }

        var sig = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
        repo.Commit(commitMessage, sig, sig);
        return (repo, repoPath);
    }

    /// <summary>Adds a second commit to an existing repo.</summary>
    private static void AddCommit(Repository repo, string repoPath, Dictionary<string, string> files, string message)
    {
        foreach (var (relativePath, content) in files)
        {
            var fullPath = Path.Combine(repoPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
            Commands.Stage(repo, relativePath);
        }
        var sig = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
        repo.Commit(message, sig, sig);
    }

    #region FindRepositoryRoot Tests

    [Fact]
    public void FindRepositoryRoot_WithGitRepoRoot_ReturnsRoot()
    {
        // Arrange
        var (_, repoPath) = CreateRepoWithFiles(new() { ["README.md"] = "hello" });
        var expected = repoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Act
        var result = _git.FindRepositoryRoot(repoPath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expected, result.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindRepositoryRoot_WithSubdirectory_ReturnsRepoRoot()
    {
        // Arrange
        var (_, repoPath) = CreateRepoWithFiles(new() { ["Modelica/package.mo"] = "package P end P;" });
        var subDir = Path.Combine(repoPath, "Modelica");
        var expected = repoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Act
        var result = _git.FindRepositoryRoot(subDir);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expected, result.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindRepositoryRoot_WithDeeplyNestedSubdirectory_ReturnsRepoRoot()
    {
        // Arrange
        var (_, repoPath) = CreateRepoWithFiles(new() { ["Modelica/MyLib/package.mo"] = "package P end P;" });
        var deepDir = Path.Combine(repoPath, "Modelica", "MyLib");
        var expected = repoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Act
        var result = _git.FindRepositoryRoot(deepDir);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expected, result.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparer.OrdinalIgnoreCase);
    }

    #endregion

    #region VcsLogOptions Tests

    [Fact]
    public void VcsLogOptions_DefaultValues_AreCorrect()
    {
        var opts = new VcsLogOptions();

        Assert.Equal(50, opts.MaxEntries);
        Assert.Null(opts.Since);
        Assert.Null(opts.Until);
        Assert.Null(opts.Branch);
    }

    [Fact]
    public void VcsLogOptions_DefaultPastWeek_SetsSince()
    {
        var before = DateTimeOffset.Now.AddDays(-7).AddSeconds(-1);
        var opts = VcsLogOptions.DefaultPastWeek();
        var after = DateTimeOffset.Now.AddDays(-7).AddSeconds(1);

        Assert.True(opts.Since >= before && opts.Since <= after,
            $"Since should be approximately 7 days ago but was {opts.Since}");
        Assert.Equal(50, opts.MaxEntries); // max(50, default minEntries 10) = 50
        Assert.Null(opts.Until);
    }

    [Fact]
    public void VcsLogOptions_DefaultPastWeek_WithHighMinEntries_UsesMinEntries()
    {
        var opts = VcsLogOptions.DefaultPastWeek(minEntries: 100);

        Assert.Equal(100, opts.MaxEntries);
    }

    [Fact]
    public void VcsLogOptions_Properties_CanBeSet()
    {
        var since = DateTimeOffset.Now.AddDays(-30);
        var until = DateTimeOffset.Now;
        var opts = new VcsLogOptions
        {
            MaxEntries = 25,
            Since = since,
            Until = until,
            Branch = "main"
        };

        Assert.Equal(25, opts.MaxEntries);
        Assert.Equal(since, opts.Since);
        Assert.Equal(until, opts.Until);
        Assert.Equal("main", opts.Branch);
    }

    #endregion

    #region GetLogEntries Tests

    [Fact]
    public void GetLogEntries_WithInvalidRepo_ReturnsEmpty()
    {
        var result = _git.GetLogEntries(NewTempPath());

        Assert.Empty(result);
    }

    [Fact]
    public void GetLogEntries_WithSingleCommit_ReturnsEntry()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["README.md"] = "Hello" });
        using (repo) { }

        var entries = _git.GetLogEntries(repoPath);

        Assert.Single(entries);
        Assert.Equal(40, entries[0].Revision.Length);
        Assert.Equal(7, entries[0].ShortRevision.Length);
        Assert.Equal("Initial commit", entries[0].MessageShort);
        Assert.Equal("Test User", entries[0].Author);
        Assert.Equal("test@example.com", entries[0].AuthorEmail);
    }

    [Fact]
    public void GetLogEntries_WithMultipleCommits_ReturnsAllInOrder()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["a.mo"] = "v1" }, "First");
        using (repo)
        {
            AddCommit(repo, repoPath, new() { ["a.mo"] = "v2" }, "Second");
            AddCommit(repo, repoPath, new() { ["a.mo"] = "v3" }, "Third");
        }

        var entries = _git.GetLogEntries(repoPath);

        Assert.Equal(3, entries.Count);
        Assert.Equal("Third", entries[0].MessageShort);
        Assert.Equal("First", entries[2].MessageShort);
        Assert.NotEmpty(entries[0].ParentRevisions);
    }

    [Fact]
    public void GetLogEntries_WithMaxEntries_LimitsResults()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" }, "c1");
        using (repo)
        {
            AddCommit(repo, repoPath, new() { ["f.mo"] = "v2" }, "c2");
            AddCommit(repo, repoPath, new() { ["f.mo"] = "v3" }, "c3");
        }

        var entries = _git.GetLogEntries(repoPath, new VcsLogOptions { MaxEntries = 2 });

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void GetLogEntries_WithUntilFilter_ExcludesFutureCommits()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" }, "past");
        using (repo) { }

        // Until = 1 hour ago means our commit (just now) should be excluded
        var pastTime = DateTimeOffset.Now.AddHours(-1);
        var entries = _git.GetLogEntries(repoPath, new VcsLogOptions { Until = pastTime });

        Assert.Empty(entries);
    }

    [Fact]
    public void GetLogEntries_WithBranchFilter_UsesBranch()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" }, "main commit");
        using (repo)
        {
            var feature = repo.CreateBranch("feature");
            Commands.Checkout(repo, feature);
            AddCommit(repo, repoPath, new() { ["f.mo"] = "v2" }, "feature commit");
        }

        var entries = _git.GetLogEntries(repoPath, new VcsLogOptions { Branch = "feature" });

        Assert.Equal(2, entries.Count); // feature commit + main commit (reachable from feature)
        Assert.Equal("feature commit", entries[0].MessageShort);
    }

    [Fact]
    public void GetLogEntries_WithNonExistentBranchFilter_ReturnsAllCommits()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" }, "commit");
        using (repo) { }

        // Non-existent branch filter is ignored, returns all commits
        var entries = _git.GetLogEntries(repoPath, new VcsLogOptions { Branch = "nonexistent" });

        Assert.NotEmpty(entries);
    }

    #endregion

    #region GetChangedFiles Tests

    [Fact]
    public void GetChangedFiles_WithInvalidRevision_ReturnsEmpty()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        var result = _git.GetChangedFiles(repoPath, "nonexistenthash");

        Assert.Empty(result);
    }

    [Fact]
    public void GetChangedFiles_InitialCommit_ShowsAllFilesAsAdded()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["pkg.mo"] = "package A end A;" });
        using var r = repo;
        var sha = r.Head.Tip.Sha;

        var files = _git.GetChangedFiles(repoPath, sha);

        Assert.Single(files);
        Assert.Equal("pkg.mo", files[0].Path);
        Assert.Equal(VcsChangeType.Added, files[0].ChangeType);
    }

    [Fact]
    public void GetChangedFiles_ModifiedFile_ShowsModified()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using var r = repo;
        AddCommit(r, repoPath, new() { ["f.mo"] = "v2" }, "modify");
        var sha = r.Head.Tip.Sha;

        var files = _git.GetChangedFiles(repoPath, sha);

        Assert.Single(files);
        Assert.Equal(VcsChangeType.Modified, files[0].ChangeType);
    }

    [Fact]
    public void GetChangedFiles_DeletedFile_ShowsDeleted()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1", ["g.mo"] = "v1" });
        using var r = repo;

        // Delete f.mo and commit
        File.Delete(Path.Combine(repoPath, "f.mo"));
        Commands.Stage(r, "f.mo");
        var sig = new Signature("Test", "t@t.com", DateTimeOffset.Now);
        r.Commit("delete", sig, sig);
        var sha = r.Head.Tip.Sha;

        var files = _git.GetChangedFiles(repoPath, sha);

        Assert.Single(files);
        Assert.Equal("f.mo", files[0].Path);
        Assert.Equal(VcsChangeType.Deleted, files[0].ChangeType);
    }

    [Fact]
    public void GetChangedFiles_RenamedFile_ShowsRenamedWithOldPath()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["old.mo"] = "content" });
        using var r = repo;

        // Git rename: delete old, add new, git detects rename
        File.Delete(Path.Combine(repoPath, "old.mo"));
        File.WriteAllText(Path.Combine(repoPath, "new.mo"), "content"); // identical content = rename detection
        Commands.Stage(r, "old.mo");
        Commands.Stage(r, "new.mo");
        var sig = new Signature("Test", "t@t.com", DateTimeOffset.Now);
        r.Commit("rename", sig, sig);
        var sha = r.Head.Tip.Sha;

        var files = _git.GetChangedFiles(repoPath, sha);

        // May be detected as rename or add+delete depending on similarity
        Assert.NotEmpty(files);
        var renamed = files.FirstOrDefault(f => f.ChangeType == VcsChangeType.Renamed);
        if (renamed != null)
        {
            Assert.NotNull(renamed.OldPath);
        }
    }

    #endregion

    #region GetWorkingCopyChanges Tests

    [Fact]
    public void GetWorkingCopyChanges_WithInvalidRepo_ReturnsEmpty()
    {
        var result = _git.GetWorkingCopyChanges(NewTempPath());

        Assert.Empty(result);
    }

    [Fact]
    public void GetWorkingCopyChanges_CleanRepo_ReturnsEmpty()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "clean" });
        using (repo) { }

        var result = _git.GetWorkingCopyChanges(repoPath);

        Assert.Empty(result);
    }

    [Fact]
    public void GetWorkingCopyChanges_WithModifiedFile_ShowsModified()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "original" });
        using (repo) { }

        File.WriteAllText(Path.Combine(repoPath, "f.mo"), "modified");

        var result = _git.GetWorkingCopyChanges(repoPath);

        Assert.Single(result);
        Assert.Equal("f.mo", result[0].Path);
        Assert.Equal(VcsFileStatus.Modified, result[0].Status);
        Assert.False(result[0].IsStaged);
    }

    [Fact]
    public void GetWorkingCopyChanges_WithUntrackedFile_ShowsUntracked()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        File.WriteAllText(Path.Combine(repoPath, "untracked.mo"), "new file");

        var result = _git.GetWorkingCopyChanges(repoPath);

        var untracked = result.FirstOrDefault(f => f.Path == "untracked.mo");
        Assert.NotNull(untracked);
        Assert.Equal(VcsFileStatus.Untracked, untracked!.Status);
        Assert.False(untracked.IsStaged);
    }

    [Fact]
    public void GetWorkingCopyChanges_WithStagedNewFile_ShowsAddedAndStaged()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using var r = repo;

        File.WriteAllText(Path.Combine(repoPath, "staged.mo"), "new");
        Commands.Stage(r, "staged.mo");

        var result = _git.GetWorkingCopyChanges(repoPath);

        var staged = result.FirstOrDefault(f => f.Path == "staged.mo");
        Assert.NotNull(staged);
        Assert.Equal(VcsFileStatus.Added, staged!.Status);
        Assert.True(staged.IsStaged);
    }

    [Fact]
    public void GetWorkingCopyChanges_WithStagedModification_ShowsModifiedAndStaged()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "original" });
        using var r = repo;

        File.WriteAllText(Path.Combine(repoPath, "f.mo"), "modified");
        Commands.Stage(r, "f.mo");

        var result = _git.GetWorkingCopyChanges(repoPath);

        var modified = result.FirstOrDefault(f => f.Path == "f.mo");
        Assert.NotNull(modified);
        Assert.Equal(VcsFileStatus.Modified, modified!.Status);
        Assert.True(modified.IsStaged);
    }

    [Fact]
    public void GetWorkingCopyChanges_WithDeletedTrackedFile_ShowsDeleted()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "content", ["g.mo"] = "content2" });
        using (repo) { }

        File.Delete(Path.Combine(repoPath, "f.mo"));

        var result = _git.GetWorkingCopyChanges(repoPath);

        var deleted = result.FirstOrDefault(f => f.Path == "f.mo");
        Assert.NotNull(deleted);
        Assert.Equal(VcsFileStatus.Deleted, deleted!.Status);
    }

    #endregion

    #region GetBranches Tests

    [Fact]
    public void GetBranches_WithInvalidRepo_ReturnsEmpty()
    {
        var result = _git.GetBranches(NewTempPath());

        Assert.Empty(result);
    }

    [Fact]
    public void GetBranches_WithNoCommits_ReturnsEmpty()
    {
        var repoPath = NewTempPath();
        Repository.Init(repoPath);

        var result = _git.GetBranches(repoPath);

        Assert.Empty(result);
    }

    [Fact]
    public void GetBranches_WithSingleBranch_ReturnsBranchMarkedAsCurrent()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        var branches = _git.GetBranches(repoPath);

        Assert.Single(branches);
        Assert.True(branches[0].IsCurrent);
        Assert.False(branches[0].IsRemote);
        Assert.NotNull(branches[0].LastCommit);
    }

    [Fact]
    public void GetBranches_WithMultipleBranches_ReturnsAllAndMarksCurrent()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using var r = repo;
        r.CreateBranch("feature-a");
        r.CreateBranch("feature-b");

        var branches = _git.GetBranches(repoPath);

        Assert.Equal(3, branches.Count);
        Assert.Single(branches, b => b.IsCurrent);
        Assert.Contains(branches, b => b.Name == "feature-a");
        Assert.Contains(branches, b => b.Name == "feature-b");
    }

    [Fact]
    public void GetBranches_ExcludesRemoteBranchesByDefault()
    {
        // Set up a local repo + remote clone
        var (sourceRepo, sourcePath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (sourceRepo) { }

        var clonePath = NewTempPath();
        Repository.Clone(sourcePath, clonePath);

        using (var cloneRepo = new Repository(clonePath))
        {
            var branches = _git.GetBranches(clonePath, includeRemote: false);
            Assert.DoesNotContain(branches, b => b.IsRemote);

            var allBranches = _git.GetBranches(clonePath, includeRemote: true);
            Assert.Contains(allBranches, b => b.IsRemote);
        }
    }

    #endregion

    #region Commit Tests

    [Fact]
    public void Commit_WithInvalidRepo_ReturnsError()
    {
        var result = _git.Commit(NewTempPath(), "test commit");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Commit_WithNoChanges_ReturnsError()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        var result = _git.Commit(repoPath, "no changes");

        Assert.False(result.Success);
        Assert.Contains("No changes to commit", result.ErrorMessage!);
    }

    [Fact]
    public void Commit_WithAllChanges_StagesAndCommits()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        File.WriteAllText(Path.Combine(repoPath, "f.mo"), "v2");
        File.WriteAllText(Path.Combine(repoPath, "new.mo"), "new file");

        var result = _git.Commit(repoPath, "update all");

        Assert.True(result.Success);
        Assert.NotNull(result.NewRevision);
        Assert.Equal(40, result.NewRevision!.Length);
    }

    [Fact]
    public void Commit_WithSpecificFiles_CommitsOnlyThoseFiles()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["a.mo"] = "v1", ["b.mo"] = "v1" });
        using (repo) { }

        File.WriteAllText(Path.Combine(repoPath, "a.mo"), "v2");
        File.WriteAllText(Path.Combine(repoPath, "b.mo"), "v2");

        var result = _git.Commit(repoPath, "only a", filesToCommit: new[] { "a.mo" });

        Assert.True(result.Success);
        // b.mo should still show as modified (unstaged)
        var changes = _git.GetWorkingCopyChanges(repoPath);
        Assert.Contains(changes, f => f.Path == "b.mo");
    }

    #endregion

    #region RevertFiles Tests

    [Fact]
    public void RevertFiles_WithInvalidRepo_ReturnsError()
    {
        var result = _git.RevertFiles(NewTempPath(), new[] { "f.mo" });

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void RevertFiles_WithModifiedTrackedFile_RevertsToOriginal()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "original content" });
        using (repo) { }

        File.WriteAllText(Path.Combine(repoPath, "f.mo"), "modified content");
        var result = _git.RevertFiles(repoPath, new[] { "f.mo" });

        Assert.True(result.Success);
        Assert.Equal("original content", File.ReadAllText(Path.Combine(repoPath, "f.mo")));
    }

    [Fact]
    public void RevertFiles_WithUntrackedFile_DeletesFile()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        var untrackedPath = Path.Combine(repoPath, "untracked.mo");
        File.WriteAllText(untrackedPath, "new file");
        Assert.True(File.Exists(untrackedPath));

        var result = _git.RevertFiles(repoPath, new[] { "untracked.mo" });

        Assert.True(result.Success);
        Assert.False(File.Exists(untrackedPath));
    }

    [Fact]
    public void RevertFiles_WithStagedNewFile_UnstagesAndDeletesFile()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using var r = repo;

        var stagedPath = Path.Combine(repoPath, "staged.mo");
        File.WriteAllText(stagedPath, "staged new file");
        Commands.Stage(r, "staged.mo");
        Assert.True(File.Exists(stagedPath));

        var result = _git.RevertFiles(repoPath, new[] { "staged.mo" });

        Assert.True(result.Success);
        Assert.False(File.Exists(stagedPath));
    }

    #endregion

    #region SwitchBranch Tests

    [Fact]
    public void SwitchBranch_WithInvalidRepo_ReturnsError()
    {
        var result = _git.SwitchBranch(NewTempPath(), "main");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void SwitchBranch_ToNonExistentBranch_ReturnsError()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        var result = _git.SwitchBranch(repoPath, "non-existent-branch");

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage!);
    }

    [Fact]
    public void SwitchBranch_ToExistingBranch_Succeeds()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using var r = repo;
        r.CreateBranch("dev");

        var result = _git.SwitchBranch(repoPath, "dev");

        Assert.True(result.Success);
        Assert.Equal("dev", _git.GetCurrentBranch(repoPath));
    }

    #endregion

    #region CreateBranch Tests

    [Fact]
    public void CreateBranch_WithInvalidRepo_ReturnsError()
    {
        var result = _git.CreateBranch(NewTempPath(), "new-branch");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void CreateBranch_NewBranch_WithSwitch_CreatesAndSwitches()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        var originalBranch = repo.Head.FriendlyName;
        using (repo) { }

        var result = _git.CreateBranch(repoPath, "feature-x", switchToBranch: true);

        Assert.True(result.Success);
        Assert.Equal("feature-x", _git.GetCurrentBranch(repoPath));
    }

    [Fact]
    public void CreateBranch_NewBranch_WithoutSwitch_StaysOnCurrentBranch()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        var originalBranch = repo.Head.FriendlyName;
        using (repo) { }

        var result = _git.CreateBranch(repoPath, "no-switch-branch", switchToBranch: false);

        Assert.True(result.Success);
        Assert.Equal(originalBranch, _git.GetCurrentBranch(repoPath));

        // Branch should exist in list
        var branches = _git.GetBranches(repoPath);
        Assert.Contains(branches, b => b.Name == "no-switch-branch");
    }

    [Fact]
    public void CreateBranch_AlreadyExists_ReturnsError()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        var branchName = repo.Head.FriendlyName;
        using (repo) { }

        var result = _git.CreateBranch(repoPath, branchName);

        Assert.False(result.Success);
        Assert.Contains("already exists", result.ErrorMessage!);
    }

    #endregion

    #region GetFileContentAtRevision Tests

    [Fact]
    public void GetFileContentAtRevision_WithInvalidRepo_ReturnsNull()
    {
        var result = _git.GetFileContentAtRevision(NewTempPath(), "f.mo", "HEAD");

        Assert.Null(result);
    }

    [Fact]
    public void GetFileContentAtRevision_AtHead_ReturnsCurrentContent()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["model.mo"] = "model Test end Test;" });
        using (repo) { }

        var content = _git.GetFileContentAtRevision(repoPath, "model.mo", "HEAD");

        Assert.Equal("model Test end Test;", content);
    }

    [Fact]
    public void GetFileContentAtRevision_AtSpecificRevision_ReturnsOldContent()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["model.mo"] = "v1 content" });
        using var r = repo;
        var firstSha = r.Head.Tip.Sha;
        AddCommit(r, repoPath, new() { ["model.mo"] = "v2 content" }, "update");

        var content = _git.GetFileContentAtRevision(repoPath, "model.mo", firstSha);

        Assert.Equal("v1 content", content);
    }

    [Fact]
    public void GetFileContentAtRevision_NonExistentFile_ReturnsNull()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        var content = _git.GetFileContentAtRevision(repoPath, "nonexistent.mo", "HEAD");

        Assert.Null(content);
    }

    [Fact]
    public void GetFileContentAtRevision_WithNullRevision_ReturnsHeadContent()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "content" });
        using (repo) { }

        var content = _git.GetFileContentAtRevision(repoPath, "f.mo", null);

        // null revision → HEAD (but this hits the null check in source, returns null)
        // The method uses repo.Lookup<Commit>(targetRevision) which with "HEAD" string returns null for LibGit2Sharp
        // So this verifies the null/HEAD handling
        Assert.NotNull(content); // "HEAD" string resolves via Lookup in LibGit2Sharp when HEAD exists
    }

    #endregion

    #region MergeBranch Tests

    [Fact]
    public void MergeBranch_WithInvalidRepo_ReturnsError()
    {
        var result = _git.MergeBranch(NewTempPath(), "feature");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void MergeBranch_BranchNotFound_ReturnsError()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        var result = _git.MergeBranch(repoPath, "nonexistent-branch");

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage!);
    }

    [Fact]
    public void MergeBranch_UpToDate_ReturnsSuccessWithNoChanges()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        var branchName = repo.Head.FriendlyName;
        using (repo) { }

        // Merge the current branch into itself → UpToDate
        var result = _git.MergeBranch(repoPath, branchName);

        Assert.True(result.Success);
        Assert.False(result.HasChanges);
        Assert.Equal(branchName, result.SourceBranch);
    }

    [Fact]
    public void MergeBranch_FastForwardMerge_HasChanges()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using var r = repo;
        var mainBranch = r.Head.FriendlyName;

        // Create feature branch and add a commit
        var feature = r.CreateBranch("feature");
        Commands.Checkout(r, feature);
        AddCommit(r, repoPath, new() { ["feature.mo"] = "new file" }, "feature work");

        // Switch back to main
        Commands.Checkout(r, r.Branches[mainBranch]);

        var result = _git.MergeBranch(repoPath, "feature");

        Assert.True(result.Success);
        Assert.True(result.HasChanges);
        Assert.NotNull(result.ModifiedFiles);
    }

    #endregion

    #region CheckoutRevision (in-place) Tests

    [Fact]
    public void CheckoutRevision_InPlace_ChecksOutSpecificRevision()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["model.mo"] = "v1 content" });
        using var r = repo;
        var firstSha = r.Head.Tip.Sha;
        AddCommit(r, repoPath, new() { ["model.mo"] = "v2 content" }, "v2");

        // In-place checkout: both paths are same repo path
        var success = _git.CheckoutRevision(repoPath, firstSha, repoPath);

        Assert.True(success);
        Assert.Equal("v1 content", File.ReadAllText(Path.Combine(repoPath, "model.mo")));

        // Restore to latest
        _git.CheckoutRevision(repoPath, "master", repoPath);
    }

    [Fact]
    public void CheckoutRevision_WithInvalidRevision_ReturnsFalse()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }
        var outputPath = NewTempPath();

        var success = _git.CheckoutRevision(repoPath, "nonexistenthash1234567890123456789012345", outputPath);

        Assert.False(success);
    }

    #endregion

    #region UpdateToLatest Tests

    [Fact]
    public void UpdateToLatest_WithInvalidRepo_ReturnsError()
    {
        var result = _git.UpdateToLatest(NewTempPath());

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void UpdateToLatest_WithDetachedHead_ReturnsError()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using var r = repo;
        // Detach HEAD
        Commands.Checkout(r, r.Head.Tip);

        var result = _git.UpdateToLatest(repoPath);

        Assert.False(result.Success);
        Assert.Contains("detached", result.ErrorMessage!.ToLower());
    }

    [Fact]
    public void UpdateToLatest_WithNoRemote_ReturnsError()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        // No remote configured → error
        var result = _git.UpdateToLatest(repoPath);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void UpdateToLatest_WhenAlreadyUpToDate_ReturnsSuccessNoChanges()
    {
        // Set up source repo and clone it
        var (sourceRepo, sourcePath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (sourceRepo) { }

        var clonePath = NewTempPath();
        Repository.Clone(sourcePath, clonePath);

        // Clone is already up-to-date with source (no new commits)
        var result = _git.UpdateToLatest(clonePath);

        // Should succeed (fetch works, already up to date)
        Assert.True(result.Success);
        Assert.False(result.HasChanges);
    }

    #endregion

    #region Push / ForcePush Tests

    [Fact]
    public void Push_WithInvalidRepo_ReturnsError()
    {
        var result = _git.Push(NewTempPath());

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Push_WithNoRemote_ReturnsError()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        var result = _git.Push(repoPath);

        Assert.False(result.Success);
    }

    [Fact]
    public void ForcePush_WithInvalidRepo_ReturnsError()
    {
        var result = _git.ForcePush(NewTempPath());

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void ForcePush_WithNoRemote_ReturnsError()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        var result = _git.ForcePush(repoPath);

        Assert.False(result.Success);
    }

    #endregion

    #region IsBranchPushed Tests

    [Fact]
    public void IsBranchPushed_WithInvalidRepo_ReturnsFalse()
    {
        var result = _git.IsBranchPushed(NewTempPath());

        Assert.False(result);
    }

    [Fact]
    public void IsBranchPushed_WithDetachedHead_ReturnsFalse()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using var r = repo;
        Commands.Checkout(r, r.Head.Tip);

        var result = _git.IsBranchPushed(repoPath);

        Assert.False(result);
    }

    [Fact]
    public void IsBranchPushed_WithNoTrackingBranch_ReturnsFalse()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        // No remote = no tracking branch
        var result = _git.IsBranchPushed(repoPath);

        Assert.False(result);
    }

    [Fact]
    public void IsBranchPushed_WhenBranchIsUpToDate_ReturnsTrue()
    {
        var (sourceRepo, sourcePath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (sourceRepo) { }

        var clonePath = NewTempPath();
        Repository.Clone(sourcePath, clonePath);

        // The clone's tracking branch should be up-to-date (AheadBy == 0)
        using var cloneRepo = new Repository(clonePath);
        var result = _git.IsBranchPushed(clonePath);

        Assert.True(result);
    }

    #endregion

    #region GetPullRequestUrl Tests

    [Fact]
    public void GetPullRequestUrl_WithInvalidRepo_ReturnsNull()
    {
        var result = _git.GetPullRequestUrl(NewTempPath());

        Assert.Null(result);
    }

    [Fact]
    public void GetPullRequestUrl_WithDetachedHead_ReturnsNull()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using var r = repo;
        Commands.Checkout(r, r.Head.Tip);

        var result = _git.GetPullRequestUrl(repoPath);

        Assert.Null(result);
    }

    [Fact]
    public void GetPullRequestUrl_WithNoRemote_ReturnsNull()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        var result = _git.GetPullRequestUrl(repoPath);

        Assert.Null(result);
    }

    [Fact]
    public void GetPullRequestUrl_WithGitHubHttpsRemote_ReturnsGitHubUrl()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using var r = repo;

        // Create a feature branch and set fake GitHub remote
        var feature = r.CreateBranch("my-feature");
        Commands.Checkout(r, feature);
        r.Network.Remotes.Add("origin", "https://github.com/myorg/myrepo.git");

        var url = _git.GetPullRequestUrl(repoPath, "main");

        Assert.NotNull(url);
        Assert.Contains("github.com/myorg/myrepo", url!);
        Assert.Contains("my-feature", url);
        Assert.Contains("main", url);
    }

    [Fact]
    public void GetPullRequestUrl_WithGitLabHttpsRemote_ReturnsGitLabUrl()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using var r = repo;

        var feature = r.CreateBranch("feature-gl");
        Commands.Checkout(r, feature);
        r.Network.Remotes.Add("origin", "https://gitlab.com/myorg/myrepo.git");

        var url = _git.GetPullRequestUrl(repoPath, "main");

        Assert.NotNull(url);
        Assert.Contains("gitlab.com", url!);
        Assert.Contains("merge_requests", url);
        Assert.Contains("feature-gl", url);
    }

    [Fact]
    public void GetPullRequestUrl_WithBitbucketRemote_ReturnsBitbucketUrl()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using var r = repo;

        var feature = r.CreateBranch("feature-bb");
        Commands.Checkout(r, feature);
        r.Network.Remotes.Add("origin", "https://bitbucket.org/myorg/myrepo.git");

        var url = _git.GetPullRequestUrl(repoPath, "main");

        Assert.NotNull(url);
        Assert.Contains("bitbucket.org", url!);
        Assert.Contains("pull-requests", url);
        Assert.Contains("feature-bb", url);
    }

    [Fact]
    public void GetPullRequestUrl_WithAzureDevOpsRemote_ReturnsAzureUrl()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using var r = repo;

        var feature = r.CreateBranch("feature-ado");
        Commands.Checkout(r, feature);
        r.Network.Remotes.Add("origin", "https://dev.azure.com/myorg/myproject/_git/myrepo");

        var url = _git.GetPullRequestUrl(repoPath, "main");

        Assert.NotNull(url);
        Assert.Contains("dev.azure.com", url!);
        Assert.Contains("pullrequestcreate", url);
    }

    [Fact]
    public void GetPullRequestUrl_WithSshRemote_ParsesAndGeneratesUrl()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using var r = repo;

        var feature = r.CreateBranch("feature-ssh");
        Commands.Checkout(r, feature);
        r.Network.Remotes.Add("origin", "git@github.com:myorg/myrepo.git");

        var url = _git.GetPullRequestUrl(repoPath, "main");

        Assert.NotNull(url);
        Assert.Contains("github.com/myorg/myrepo", url!);
    }

    [Fact]
    public void GetPullRequestUrl_WithUnknownHost_ReturnsFallbackUrl()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using var r = repo;

        var feature = r.CreateBranch("feature-custom");
        Commands.Checkout(r, feature);
        r.Network.Remotes.Add("origin", "https://git.mycompany.com/myorg/myrepo.git");

        var url = _git.GetPullRequestUrl(repoPath, "main");

        Assert.NotNull(url);
        Assert.Contains("git.mycompany.com", url!);
        Assert.Contains("feature-custom", url);
    }

    #endregion

    #region Rebase / ContinueRebase / AbortRebase Tests

    [Fact]
    public void Rebase_WithInvalidRepo_ReturnsError()
    {
        var result = _git.Rebase(NewTempPath(), "main");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Rebase_WithValidRepo_OntoCurrentBranch_Succeeds()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        var branchName = repo.Head.FriendlyName;
        using (repo) { }

        // Rebasing onto the same branch is a no-op in git and exits 0
        var result = _git.Rebase(repoPath, branchName);

        Assert.True(result.Success);
    }

    [Fact]
    public void ContinueRebase_WithInvalidRepo_ReturnsError()
    {
        var result = _git.ContinueRebase(NewTempPath());

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void ContinueRebase_WithNoRebaseInProgress_ReturnsError()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        // No rebase in progress → git rebase --continue fails
        var result = _git.ContinueRebase(repoPath);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void AbortRebase_WithInvalidRepo_ReturnsError()
    {
        var result = _git.AbortRebase(NewTempPath());

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void AbortRebase_WithNoRebaseInProgress_ReturnsError()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        // No rebase in progress → git rebase --abort fails
        var result = _git.AbortRebase(repoPath);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    #endregion

    #region GetConflictVersions Tests

    [Fact]
    public void GetConflictVersions_WithInvalidRepo_ReturnsNulls()
    {
        var (ours, theirs) = _git.GetConflictVersions(NewTempPath(), "f.mo");

        Assert.Null(ours);
        Assert.Null(theirs);
    }

    [Fact]
    public void GetConflictVersions_WithNoConflict_ReturnsNulls()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        var (ours, theirs) = _git.GetConflictVersions(repoPath, Path.Combine(repoPath, "f.mo"));

        Assert.Null(ours);
        Assert.Null(theirs);
    }

    #endregion

    #region ResolveConflict Tests

    [Fact]
    public void ResolveConflict_WithInvalidRepo_ReturnsError()
    {
        var result = _git.ResolveConflict(NewTempPath(), "f.mo", ConflictResolutionChoice.MarkResolved);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    #endregion

    #region Merge Conflict + GetConflictVersions + ResolveConflict Tests

    /// <summary>Creates a repo where merging "conflict-branch" into HEAD will conflict on f.mo.</summary>
    private (Repository repo, string repoPath) CreateConflictRepo()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "shared original line\n" });

        // Create feature branch BEFORE main diverges
        repo.CreateBranch("conflict-branch");

        // Advance main
        AddCommit(repo, repoPath, new() { ["f.mo"] = "main version\n" }, "main change");

        // Switch to conflict-branch and make an incompatible change
        Commands.Checkout(repo, repo.Branches["conflict-branch"]);
        AddCommit(repo, repoPath, new() { ["f.mo"] = "feature version\n" }, "feature change");

        // Back to main
        Commands.Checkout(repo, repo.Branches[repo.Branches
            .Where(b => !b.IsRemote && b.FriendlyName != "conflict-branch")
            .Select(b => b.FriendlyName)
            .First()]);

        return (repo, repoPath);
    }

    [Fact]
    public void MergeBranch_WithConflict_ReturnsConflicts()
    {
        var (repo, repoPath) = CreateConflictRepo();
        using (repo) { }

        var result = _git.MergeBranch(repoPath, "conflict-branch");

        Assert.True(result.Success);
        Assert.True(result.HasConflicts);
        Assert.NotEmpty(result.ConflictedFiles!);

        // Cleanup - abort the merge by reverting
        using var r = new Repository(repoPath);
        r.Reset(ResetMode.Hard);
    }

    [Fact]
    public void GetConflictVersions_WithMergeConflict_ReturnsBothVersions()
    {
        var (repo, repoPath) = CreateConflictRepo();
        using var r = repo;

        // Create the merge conflict using LibGit2Sharp directly
        var sig = new Signature("Test", "t@t.com", DateTimeOffset.Now);
        var mergeResult = r.Merge(r.Branches["conflict-branch"], sig, new MergeOptions
        {
            FastForwardStrategy = FastForwardStrategy.NoFastForward
        });

        if (mergeResult.Status != MergeStatus.Conflicts)
        {
            // No conflict created, skip
            r.Reset(ResetMode.Hard);
            return;
        }

        var filePath = Path.Combine(repoPath, "f.mo");
        var (ours, theirs) = _git.GetConflictVersions(repoPath, filePath);

        Assert.NotNull(ours);
        Assert.NotNull(theirs);
        Assert.Contains("main version", ours);
        Assert.Contains("feature version", theirs);

        // Cleanup
        r.Reset(ResetMode.Hard);
    }

    [Fact]
    public void ResolveConflict_MarkResolved_WithConflictedFile_Succeeds()
    {
        var (repo, repoPath) = CreateConflictRepo();
        using var r = repo;

        var sig = new Signature("Test", "t@t.com", DateTimeOffset.Now);
        var mergeResult = r.Merge(r.Branches["conflict-branch"], sig, new MergeOptions
        {
            FastForwardStrategy = FastForwardStrategy.NoFastForward
        });

        if (mergeResult.Status != MergeStatus.Conflicts)
        {
            r.Reset(ResetMode.Hard);
            return;
        }

        // Write a resolved version
        var filePath = Path.Combine(repoPath, "f.mo");
        File.WriteAllText(filePath, "resolved content\n");

        var result = _git.ResolveConflict(repoPath, filePath, ConflictResolutionChoice.MarkResolved);

        Assert.True(result.Success);

        // Cleanup
        r.Reset(ResetMode.Hard);
    }

    [Fact]
    public void ResolveConflict_KeepMine_WithConflictedFile_Succeeds()
    {
        var (repo, repoPath) = CreateConflictRepo();
        using var r = repo;

        var sig = new Signature("Test", "t@t.com", DateTimeOffset.Now);
        var mergeResult = r.Merge(r.Branches["conflict-branch"], sig, new MergeOptions
        {
            FastForwardStrategy = FastForwardStrategy.NoFastForward
        });

        if (mergeResult.Status != MergeStatus.Conflicts)
        {
            r.Reset(ResetMode.Hard);
            return;
        }

        var filePath = Path.Combine(repoPath, "f.mo");
        var result = _git.ResolveConflict(repoPath, filePath, ConflictResolutionChoice.KeepMine);

        Assert.True(result.Success);

        // Cleanup
        r.Reset(ResetMode.Hard);
    }

    [Fact]
    public void ResolveConflict_AcceptIncoming_WithConflictedFile_Succeeds()
    {
        var (repo, repoPath) = CreateConflictRepo();
        using var r = repo;

        var sig = new Signature("Test", "t@t.com", DateTimeOffset.Now);
        var mergeResult = r.Merge(r.Branches["conflict-branch"], sig, new MergeOptions
        {
            FastForwardStrategy = FastForwardStrategy.NoFastForward
        });

        if (mergeResult.Status != MergeStatus.Conflicts)
        {
            r.Reset(ResetMode.Hard);
            return;
        }

        var filePath = Path.Combine(repoPath, "f.mo");
        var result = _git.ResolveConflict(repoPath, filePath, ConflictResolutionChoice.AcceptIncoming);

        Assert.True(result.Success);

        // Cleanup
        r.Reset(ResetMode.Hard);
    }

    #endregion

    #region ResolveToCommit / GetRevisionDescription Tests

    [Fact]
    public void GetRevisionDescription_WithBranchName_ReturnsDescription()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" }, "test commit");
        var branchName = repo.Head.FriendlyName;
        using (repo) { }

        var desc = _git.GetRevisionDescription(repoPath, branchName);

        // Branch name resolves via branch lookup in ResolveToCommit
        Assert.NotNull(desc);
        Assert.Contains("Test User", desc);
    }

    [Fact]
    public void GetRevisionDescription_WithAnnotatedTag_ReturnsDescription()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" }, "tagged commit");
        using var r = repo;

        var sig = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
        r.ApplyTag("v1.0", r.Head.Tip.Sha, sig, "Release v1.0");

        var desc = _git.GetRevisionDescription(repoPath, "v1.0");

        Assert.NotNull(desc);
        Assert.Contains("Test User", desc);
    }

    [Fact]
    public void ResolveRevision_WithBranchName_ReturnsCommitSha()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        var expectedSha = repo.Head.Tip.Sha;
        var branchName = repo.Head.FriendlyName;
        using (repo) { }

        var sha = _git.ResolveRevision(repoPath, branchName);

        Assert.Equal(expectedSha, sha);
    }

    [Fact]
    public void ResolveRevision_WithNonExistentRef_ReturnsNull()
    {
        var (repo, repoPath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (repo) { }

        var sha = _git.ResolveRevision(repoPath, "nonexistent-branch");

        Assert.Null(sha);
    }

    #endregion

    #region UpdateToLatest Additional Tests

    [Fact]
    public void UpdateToLatest_WithInvalidRemoteUrl_FetchFails_ReturnsError()
    {
        var (sourceRepo, sourcePath) = CreateRepoWithFiles(new() { ["f.mo"] = "v1" });
        using (sourceRepo) { }

        var clonePath = NewTempPath();
        Repository.Clone(sourcePath, clonePath);

        // Change the remote URL to something invalid so fetch will fail
        using (var cloneRepo = new Repository(clonePath))
        {
            cloneRepo.Network.Remotes.Update("origin", r => r.Url = "https://this-does-not-exist.invalid/repo.git");
        }

        var result = _git.UpdateToLatest(clonePath);

        // Fetch should fail because the remote URL is invalid
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    #endregion
}
