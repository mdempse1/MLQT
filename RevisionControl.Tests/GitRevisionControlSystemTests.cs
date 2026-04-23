using LibGit2Sharp;

namespace RevisionControl.Tests;

/// <summary>
/// Tests for GitRevisionControlSystem.
/// These tests create a temporary Git repository and test various operations.
/// </summary>
public class GitRevisionControlSystemTests : IDisposable
{
    private readonly string _tempRepoPath;
    private readonly GitRevisionControlSystem _git;

    public GitRevisionControlSystemTests()
    {
        _git = new GitRevisionControlSystem();
        _tempRepoPath = Path.Combine(Path.GetTempPath(), "GitTest_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRepoPath);
    }

    public void Dispose()
    {
        // Clean up temp repository
        ForceDeleteDirectory(_tempRepoPath);
    }

    /// <summary>
    /// Forcefully deletes a directory, removing read-only attributes first.
    /// </summary>
    private static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            // Remove read-only attributes from all files
            var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch
                {
                    // Continue even if we can't change attributes
                }
            }

            // Delete the directory
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void IsValidRepository_WithNonExistentPath_ReturnsFalse()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString());

        // Act
        var result = _git.IsValidRepository(nonExistentPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidRepository_WithEmptyDirectory_ReturnsFalse()
    {
        // Arrange - _tempRepoPath is empty

        // Act
        var result = _git.IsValidRepository(_tempRepoPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidRepository_WithValidGitRepo_ReturnsTrue()
    {
        // Arrange
        Repository.Init(_tempRepoPath);

        // Act
        var result = _git.IsValidRepository(_tempRepoPath);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidRepository_WithSubdirectoryOfGitRepo_ReturnsTrue()
    {
        // Arrange
        Repository.Init(_tempRepoPath);
        var subDir = Path.Combine(_tempRepoPath, "Modelica");
        Directory.CreateDirectory(subDir);

        // Act
        var result = _git.IsValidRepository(subDir);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void FindRepositoryRoot_WithNonGitDirectory_ReturnsNull()
    {
        // Arrange - empty temp dir, no git repo

        // Act
        var result = _git.FindRepositoryRoot(_tempRepoPath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FindRepositoryRoot_WithGitRepoRoot_ReturnsRepoRoot()
    {
        // Arrange
        Repository.Init(_tempRepoPath);
        var expected = _tempRepoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Act
        var result = _git.FindRepositoryRoot(_tempRepoPath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expected, result.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindRepositoryRoot_WithSubdirectoryOfGitRepo_ReturnsRepoRoot()
    {
        // Arrange
        Repository.Init(_tempRepoPath);
        var subDir = Path.Combine(_tempRepoPath, "Modelica", "MyLib");
        Directory.CreateDirectory(subDir);
        var expectedRoot = _tempRepoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Act
        var result = _git.FindRepositoryRoot(subDir);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedRoot, result.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetBranchPointDate_AlwaysReturnsNull()
    {
        // Git implementation is intentionally not supported — callers (e.g. the planner)
        // log a warning and fall back to the commit date.
        var result = _git.GetBranchPointDate(_tempRepoPath);
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentRevision_WithNonGitRepo_ReturnsNull()
    {
        // Arrange - empty directory

        // Act
        var result = _git.GetCurrentRevision(_tempRepoPath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentRevision_WithInitializedRepo_ReturnsNull()
    {
        // Arrange - initialized but no commits
        Repository.Init(_tempRepoPath);

        // Act
        var result = _git.GetCurrentRevision(_tempRepoPath);

        // Assert
        Assert.Null(result); // No commits yet
    }

    [Fact]
    public void GetCurrentRevision_WithCommit_ReturnsCommitHash()
    {
        // Arrange
        var repo = CreateRepoWithCommit("Initial commit");

        // Act
        var result = _git.GetCurrentRevision(_tempRepoPath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(40, result.Length); // SHA-1 hash length
    }

    [Fact]
    public void ResolveRevision_WithNonExistentRevision_ReturnsNull()
    {
        // Arrange
        CreateRepoWithCommit("Initial commit");

        // Act
        var result = _git.ResolveRevision(_tempRepoPath, "nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ResolveRevision_WithCommitHash_ReturnsFullHash()
    {
        // Arrange
        var repo = CreateRepoWithCommit("Initial commit");
        var expectedHash = repo.Head.Tip.Sha;

        // Act - use short hash
        var result = _git.ResolveRevision(_tempRepoPath, expectedHash.Substring(0, 7));

        // Assert
        Assert.Equal(expectedHash, result);
    }

    [Fact]
    public void ResolveRevision_WithBranchName_ReturnsCommitHash()
    {
        // Arrange
        var repo = CreateRepoWithCommit("Initial commit");
        var expectedHash = repo.Head.Tip.Sha;

        // Act
        var result = _git.ResolveRevision(_tempRepoPath, "master");

        // Assert
        Assert.Equal(expectedHash, result);
    }

    [Fact]
    public void ResolveRevision_WithTag_ReturnsCommitHash()
    {
        // Arrange
        var repo = CreateRepoWithCommit("Initial commit");
        var commit = repo.Head.Tip;
        repo.Tags.Add("v1.0.0", commit);

        // Act
        var result = _git.ResolveRevision(_tempRepoPath, "v1.0.0");

        // Assert
        Assert.Equal(commit.Sha, result);
    }

    [Fact]
    public void GetRevisionDescription_WithCommit_ReturnsDescription()
    {
        // Arrange
        CreateRepoWithCommit("Test commit message");
        var currentHash = _git.GetCurrentRevision(_tempRepoPath);

        // Act
        var result = _git.GetRevisionDescription(_tempRepoPath, currentHash!);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Test commit message", result);
    }

    [Fact]
    public void CheckoutRevision_WithValidCommit_ChecksOutFiles()
    {
        // Arrange
        var repo = CreateRepoWithFile("test.txt", "Initial content");
        var firstCommit = repo.Head.Tip.Sha;

        // Modify file and create second commit
        File.WriteAllText(Path.Combine(_tempRepoPath, "test.txt"), "Updated content");
        Commands.Stage(repo, "test.txt");
        var signature = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
        repo.Commit("Update file", signature, signature);

        var outputPath = Path.Combine(Path.GetTempPath(), "GitCheckout_" + Guid.NewGuid().ToString());

        try
        {
            // Act - checkout first commit
            var success = _git.CheckoutRevision(_tempRepoPath, firstCommit, outputPath);

            // Assert
            Assert.True(success);
            Assert.True(Directory.Exists(outputPath));
            Assert.True(File.Exists(Path.Combine(outputPath, "test.txt")));
            var content = File.ReadAllText(Path.Combine(outputPath, "test.txt"));
            Assert.Equal("Initial content", content);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, recursive: true);
            }
        }
    }

    [Fact]
    public void CheckoutRevision_WithTag_ChecksOutCorrectVersion()
    {
        // Arrange
        var repo = CreateRepoWithFile("package.mo", "v1 content");
        var v1Commit = repo.Head.Tip;
        repo.Tags.Add("v1.0.0", v1Commit);

        File.WriteAllText(Path.Combine(_tempRepoPath, "package.mo"), "v2 content");
        Commands.Stage(repo, "package.mo");
        var signature = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
        repo.Commit("v2", signature, signature);

        var outputPath = Path.Combine(Path.GetTempPath(), "GitCheckout_" + Guid.NewGuid().ToString());

        try
        {
            // Act
            var success = _git.CheckoutRevision(_tempRepoPath, "v1.0.0", outputPath);

            // Assert
            Assert.True(success);
            var content = File.ReadAllText(Path.Combine(outputPath, "package.mo"));
            Assert.Equal("v1 content", content);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, recursive: true);
            }
        }
    }

    [Fact]
    public void CheckoutRevision_WithDirectoryStructure_CreatesAllDirectories()
    {
        // Arrange
        var repo = CreateRepoWithDirectoryStructure();
        var commit = repo.Head.Tip.Sha;

        var outputPath = Path.Combine(Path.GetTempPath(), "GitCheckout_" + Guid.NewGuid().ToString());

        try
        {
            // Act
            var success = _git.CheckoutRevision(_tempRepoPath, commit, outputPath);

            // Assert
            Assert.True(success);
            Assert.True(File.Exists(Path.Combine(outputPath, "package.mo")));
            Assert.True(File.Exists(Path.Combine(outputPath, "Models", "Model1.mo")));
            Assert.True(File.Exists(Path.Combine(outputPath, "Models", "SubPackage", "Model2.mo")));
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, recursive: true);
            }
        }
    }

    #region Helper Methods

    private Repository CreateRepoWithCommit(string message)
    {
        var repo = new Repository(Repository.Init(_tempRepoPath));

        // Create a dummy file
        var filePath = Path.Combine(_tempRepoPath, "README.md");
        File.WriteAllText(filePath, "Test repository");

        Commands.Stage(repo, "README.md");

        var signature = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
        repo.Commit(message, signature, signature);

        return repo;
    }

    private Repository CreateRepoWithFile(string filename, string content)
    {
        var repo = new Repository(Repository.Init(_tempRepoPath));

        var filePath = Path.Combine(_tempRepoPath, filename);
        File.WriteAllText(filePath, content);

        Commands.Stage(repo, filename);

        var signature = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
        repo.Commit("Add " + filename, signature, signature);

        return repo;
    }

    private Repository CreateRepoWithDirectoryStructure()
    {
        var repo = new Repository(Repository.Init(_tempRepoPath));

        // Create directory structure
        Directory.CreateDirectory(Path.Combine(_tempRepoPath, "Models"));
        Directory.CreateDirectory(Path.Combine(_tempRepoPath, "Models", "SubPackage"));

        File.WriteAllText(Path.Combine(_tempRepoPath, "package.mo"), "package TestLib");
        File.WriteAllText(Path.Combine(_tempRepoPath, "Models", "Model1.mo"), "model Model1");
        File.WriteAllText(Path.Combine(_tempRepoPath, "Models", "SubPackage", "Model2.mo"), "model Model2");

        Commands.Stage(repo, "*");

        var signature = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
        repo.Commit("Create directory structure", signature, signature);

        return repo;
    }

    #endregion

    #region Workspace Reuse Tests

    [Fact]
    public void UpdateExistingCheckout_WithNonExistentCheckout_PerformsInitialCheckout()
    {
        // Arrange
        using var repo = CreateRepoWithFile("test.mo", "model Test");
        var commitSha = repo.Head.Tip.Sha;

        var checkoutPath = Path.Combine(Path.GetTempPath(), "Checkout_" + Guid.NewGuid().ToString());

        try
        {
            // Act
            var result = _git.UpdateExistingCheckout(checkoutPath, _tempRepoPath, commitSha);

            // Assert
            Assert.True(result);
            Assert.True(Directory.Exists(checkoutPath));
            Assert.True(File.Exists(Path.Combine(checkoutPath, "test.mo")));
            Assert.Equal("model Test", File.ReadAllText(Path.Combine(checkoutPath, "test.mo")));
        }
        finally
        {
            ForceDeleteDirectory(checkoutPath);
        }
    }

    [Fact]
    public void UpdateExistingCheckout_WithExistingCheckout_UpdatesToNewRevision()
    {
        // Arrange
        using var repo = CreateRepoWithFile("test.mo", "model Test v1");
        var firstCommitSha = repo.Head.Tip.Sha;

        var checkoutPath = Path.Combine(Path.GetTempPath(), "Checkout_" + Guid.NewGuid().ToString());

        try
        {
            // Initial checkout
            _git.CheckoutRevision(_tempRepoPath, firstCommitSha, checkoutPath);

            // Create second commit
            File.WriteAllText(Path.Combine(_tempRepoPath, "test.mo"), "model Test v2");
            Commands.Stage(repo, "test.mo");
            var signature = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
            repo.Commit("Update to v2", signature, signature);
            var secondCommitSha = repo.Head.Tip.Sha;

            // Act
            var result = _git.UpdateExistingCheckout(checkoutPath, _tempRepoPath, secondCommitSha);

            // Assert
            Assert.True(result);
            Assert.Equal("model Test v2", File.ReadAllText(Path.Combine(checkoutPath, "test.mo")));
        }
        finally
        {
            ForceDeleteDirectory(checkoutPath);
        }
    }

    [Fact]
    public void UpdateExistingCheckout_WithLocalChanges_DiscardsChangesAndUpdates()
    {
        // Arrange
        using var repo = CreateRepoWithFile("test.mo", "model Test v1");
        var firstCommitSha = repo.Head.Tip.Sha;

        var checkoutPath = Path.Combine(Path.GetTempPath(), "Checkout_" + Guid.NewGuid().ToString());

        try
        {
            // Initial checkout
            _git.CheckoutRevision(_tempRepoPath, firstCommitSha, checkoutPath);

            // Make local changes
            File.WriteAllText(Path.Combine(checkoutPath, "test.mo"), "modified locally");

            // Create second commit in source repo
            File.WriteAllText(Path.Combine(_tempRepoPath, "test.mo"), "model Test v2");
            Commands.Stage(repo, "test.mo");
            var signature = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
            repo.Commit("Update to v2", signature, signature);
            var secondCommitSha = repo.Head.Tip.Sha;

            // Act
            var result = _git.UpdateExistingCheckout(checkoutPath, _tempRepoPath, secondCommitSha);

            // Assert
            Assert.True(result);
            Assert.Equal("model Test v2", File.ReadAllText(Path.Combine(checkoutPath, "test.mo")));
        }
        finally
        {
            ForceDeleteDirectory(checkoutPath);
        }
    }

    [Fact]
    public void CleanWorkspace_WithTrackedChanges_RevertsToHead()
    {
        // Arrange
        using var repo = CreateRepoWithFile("test.mo", "model Test original");
        var commitSha = repo.Head.Tip.Sha;

        var checkoutPath = Path.Combine(Path.GetTempPath(), "Checkout_" + Guid.NewGuid().ToString());

        try
        {
            // Use UpdateExistingCheckout to create a proper Git clone
            _git.UpdateExistingCheckout(checkoutPath, _tempRepoPath, commitSha);

            // Modify tracked file
            File.WriteAllText(Path.Combine(checkoutPath, "test.mo"), "modified content");

            // Act
            var result = _git.CleanWorkspace(checkoutPath);

            // Assert
            Assert.True(result);
            Assert.Equal("model Test original", File.ReadAllText(Path.Combine(checkoutPath, "test.mo")));
        }
        finally
        {
            ForceDeleteDirectory(checkoutPath);
        }
    }

    [Fact]
    public void CleanWorkspace_WithUntrackedFiles_RemovesUntrackedFiles()
    {
        // Arrange
        using var repo = CreateRepoWithFile("test.mo", "model Test");
        var commitSha = repo.Head.Tip.Sha;

        var checkoutPath = Path.Combine(Path.GetTempPath(), "Checkout_" + Guid.NewGuid().ToString());

        try
        {
            // Use UpdateExistingCheckout to create a proper Git clone
            _git.UpdateExistingCheckout(checkoutPath, _tempRepoPath, commitSha);

            // Add untracked file
            File.WriteAllText(Path.Combine(checkoutPath, "untracked.mo"), "untracked content");

            // Act
            var result = _git.CleanWorkspace(checkoutPath);

            // Assert
            Assert.True(result);
            Assert.False(File.Exists(Path.Combine(checkoutPath, "untracked.mo")));
            Assert.True(File.Exists(Path.Combine(checkoutPath, "test.mo")));
        }
        finally
        {
            ForceDeleteDirectory(checkoutPath);
        }
    }

    [Fact]
    public void CleanWorkspace_WithUntrackedDirectory_RemovesUntrackedDirectory()
    {
        // Arrange
        using var repo = CreateRepoWithFile("test.mo", "model Test");
        var commitSha = repo.Head.Tip.Sha;

        var checkoutPath = Path.Combine(Path.GetTempPath(), "Checkout_" + Guid.NewGuid().ToString());

        try
        {
            // Use UpdateExistingCheckout to create a proper Git clone
            _git.UpdateExistingCheckout(checkoutPath, _tempRepoPath, commitSha);

            // Add untracked directory
            var untrackedDir = Path.Combine(checkoutPath, "UntrackedDir");
            Directory.CreateDirectory(untrackedDir);
            File.WriteAllText(Path.Combine(untrackedDir, "file.mo"), "content");

            // Verify untracked directory exists before cleaning
            Assert.True(Directory.Exists(untrackedDir), "Untracked directory should exist before cleaning");

            // Act
            var result = _git.CleanWorkspace(checkoutPath);

            // Assert
            Assert.True(result, "CleanWorkspace should return true");

            // The .git directory will exist, but UntrackedDir should not
            var remainingDirs = Directory.GetDirectories(checkoutPath);
            var nonGitDirs = remainingDirs.Where(d => !Path.GetFileName(d).Equals(".git", StringComparison.OrdinalIgnoreCase)).ToArray();

            Assert.Empty(nonGitDirs);
            Assert.True(File.Exists(Path.Combine(checkoutPath, "test.mo")));
        }
        finally
        {
            ForceDeleteDirectory(checkoutPath);
        }
    }

    [Fact]
    public void CleanWorkspace_WithInvalidPath_ReturnsFalse()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString());

        // Act
        var result = _git.CleanWorkspace(nonExistentPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void UpdateExistingCheckout_SwitchingBetweenBranches_Works()
    {
        // Arrange
        using var repo = CreateRepoWithFile("test.mo", "model Test main");
        var mainSha = repo.Head.Tip.Sha;

        // Create a branch
        var branch = repo.CreateBranch("feature");
        Commands.Checkout(repo, branch);
        File.WriteAllText(Path.Combine(_tempRepoPath, "test.mo"), "model Test feature");
        Commands.Stage(repo, "test.mo");
        var signature = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
        repo.Commit("Feature change", signature, signature);
        var featureSha = repo.Head.Tip.Sha;

        var checkoutPath = Path.Combine(Path.GetTempPath(), "Checkout_" + Guid.NewGuid().ToString());

        try
        {
            // Checkout feature branch
            _git.CheckoutRevision(_tempRepoPath, featureSha, checkoutPath);
            Assert.Equal("model Test feature", File.ReadAllText(Path.Combine(checkoutPath, "test.mo")));

            // Switch to main
            var result = _git.UpdateExistingCheckout(checkoutPath, _tempRepoPath, mainSha);

            // Assert
            Assert.True(result);
            Assert.Equal("model Test main", File.ReadAllText(Path.Combine(checkoutPath, "test.mo")));
        }
        finally
        {
            ForceDeleteDirectory(checkoutPath);
        }
    }

    #endregion

    #region GetCurrentBranch Tests

    [Fact]
    public void GetCurrentBranch_WithNonExistentPath_ReturnsNull()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString());

        // Act
        var result = _git.GetCurrentBranch(nonExistentPath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentBranch_WithEmptyDirectory_ReturnsNull()
    {
        // Arrange - _tempRepoPath is empty

        // Act
        var result = _git.GetCurrentBranch(_tempRepoPath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentBranch_WithInitializedRepoNoCommits_ReturnsBranchName()
    {
        // Arrange - initialized but no commits
        Repository.Init(_tempRepoPath);

        // Act
        var result = _git.GetCurrentBranch(_tempRepoPath);

        // Assert - Git returns the default branch name even without commits
        // The branch doesn't fully exist yet, but HEAD points to it
        Assert.Equal("master", result);
    }

    [Fact]
    public void GetCurrentBranch_WithMasterBranch_ReturnsMaster()
    {
        // Arrange
        CreateRepoWithCommit("Initial commit");

        // Act
        var result = _git.GetCurrentBranch(_tempRepoPath);

        // Assert
        Assert.Equal("master", result);
    }

    [Fact]
    public void GetCurrentBranch_WithFeatureBranch_ReturnsFeatureBranchName()
    {
        // Arrange
        using var repo = CreateRepoWithCommit("Initial commit");
        var branch = repo.CreateBranch("feature/test");
        Commands.Checkout(repo, branch);

        // Act
        var result = _git.GetCurrentBranch(_tempRepoPath);

        // Assert
        Assert.Equal("feature/test", result);
    }

    [Fact]
    public void GetCurrentBranch_WithDetachedHead_ReturnsNull()
    {
        // Arrange
        using var repo = CreateRepoWithCommit("Initial commit");
        var commitSha = repo.Head.Tip.Sha;

        // Detach HEAD by checking out a specific commit
        Commands.Checkout(repo, repo.Head.Tip);

        // Act
        var result = _git.GetCurrentBranch(_tempRepoPath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentBranch_AfterSwitchingBranches_ReturnsNewBranchName()
    {
        // Arrange
        using var repo = CreateRepoWithCommit("Initial commit");
        var master = repo.Head.FriendlyName;

        // Create and switch to feature branch
        var featureBranch = repo.CreateBranch("feature/new-feature");
        Commands.Checkout(repo, featureBranch);

        // Act
        var result = _git.GetCurrentBranch(_tempRepoPath);

        // Assert
        Assert.Equal("feature/new-feature", result);

        // Switch back to master
        Commands.Checkout(repo, repo.Branches[master]);
        result = _git.GetCurrentBranch(_tempRepoPath);
        Assert.Equal(master, result);
    }

    #endregion
}
