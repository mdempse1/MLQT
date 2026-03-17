using LibGit2Sharp;

namespace RevisionControl.Tests;

/// <summary>
/// Integration tests for GitRevisionControlSystem using the real test repository.
/// Repository: https://github.com/mdempse1/ModelicaEditorTests.git
///
/// Note: These tests clone a real remote repository once to test Git operations.
/// The repository is cloned in the constructor for all tests to share.
/// </summary>
public class GitIntegrationTests : IDisposable
{
    private const string TestRepoUrl = "https://github.com/mdempse1/ModelicaEditorTests.git";
    private readonly GitRevisionControlSystem _git;
    private readonly string _clonePath;
    private readonly bool _repositoryAvailable;
    private readonly List<string> _tempPaths = new();

    public GitIntegrationTests()
    {
        _git = new GitRevisionControlSystem();
        _clonePath = Path.Combine(Path.GetTempPath(), "GitIntegrationTestRepo_" + Guid.NewGuid().ToString());

        // Clone the repository for testing
        try
        {
            Repository.Clone(TestRepoUrl, _clonePath);
            _repositoryAvailable = true;
        }
        catch
        {
            _repositoryAvailable = false;
        }
    }

    public void Dispose()
    {
        ForceDeleteDirectory(_clonePath);
        foreach (var path in _tempPaths)
        {
            ForceDeleteDirectory(path);
        }
    }

    private string CreateTempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "GitIntegrationTest_" + Guid.NewGuid().ToString());
        _tempPaths.Add(path);
        return path;
    }

    private static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
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

            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void IsValidRepository_WithClonedRepository_ReturnsTrue()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Act
        var result = _git.IsValidRepository(_clonePath);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidRepository_WithInvalidPath_ReturnsFalse()
    {
        // Arrange
        var invalidPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString());

        // Act
        var result = _git.IsValidRepository(invalidPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetCurrentRevision_FromClonedRepo_ReturnsValidSha()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Act
        var sha = _git.GetCurrentRevision(_clonePath);

        // Assert
        Assert.NotNull(sha);
        Assert.Equal(40, sha.Length); // SHA-1 hash length
    }

    [Fact]
    public void ResolveRevision_WithMainBranch_ResolvesSha()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Act
        var sha = _git.ResolveRevision(_clonePath, "main");

        // Assert - main might not exist, try master
        if (sha == null)
        {
            sha = _git.ResolveRevision(_clonePath, "master");
        }

        Assert.NotNull(sha);
        Assert.Equal(40, sha.Length);
    }

    [Fact]
    public void ResolveRevision_WithHEAD_ResolvesSha()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Act
        var sha = _git.ResolveRevision(_clonePath, "HEAD");

        // Assert
        Assert.NotNull(sha);
        Assert.Equal(40, sha.Length);
    }

    [Fact]
    public void GetRevisionDescription_WithCurrentHead_ReturnsDescription()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Arrange
        var sha = _git.GetCurrentRevision(_clonePath);

        // Act
        var description = _git.GetRevisionDescription(_clonePath, sha!);

        // Assert
        Assert.NotNull(description);
        Assert.NotEmpty(description);
    }

    [Fact]
    public void CheckoutRevision_ToNewPath_CreatesCheckout()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Arrange
        var outputPath = CreateTempPath();
        var sha = _git.GetCurrentRevision(_clonePath);

        // Act
        var result = _git.CheckoutRevision(_clonePath, sha!, outputPath);

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(outputPath));
    }

    [Fact]
    public void CheckoutRevision_WithHEAD_ChecksOutLatestCommit()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Arrange
        var outputPath = CreateTempPath();

        // Act
        var result = _git.CheckoutRevision(_clonePath, "HEAD", outputPath);

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(outputPath));
    }

    [Fact]
    public void CheckoutRevision_ToNestedPath_CreatesDirectories()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Arrange
        var nestedPath = Path.Combine(CreateTempPath(), "nested", "deep", "path");

        // Act
        var result = _git.CheckoutRevision(_clonePath, "HEAD", nestedPath);

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(nestedPath));
    }

    [Fact]
    public void UpdateExistingCheckout_WithNonExistentPath_PerformsCheckout()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateTempPath();
        var sha = _git.GetCurrentRevision(_clonePath);

        // Act
        var result = _git.UpdateExistingCheckout(checkoutPath, _clonePath, sha!);

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(checkoutPath));
    }

    [Fact]
    public void UpdateExistingCheckout_WithExistingCheckout_UpdatesSuccessfully()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateTempPath();
        var sha = _git.GetCurrentRevision(_clonePath);

        // Initial checkout
        _git.UpdateExistingCheckout(checkoutPath, _clonePath, sha!);

        // Act - update again to same revision
        var result = _git.UpdateExistingCheckout(checkoutPath, _clonePath, sha!);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void UpdateExistingCheckout_MultipleTimes_WorksConsistently()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateTempPath();
        var sha = _git.GetCurrentRevision(_clonePath);

        // Act & Assert - multiple updates should all succeed
        for (int i = 0; i < 3; i++)
        {
            var result = _git.UpdateExistingCheckout(checkoutPath, _clonePath, sha!);
            Assert.True(result, $"Update {i + 1} should succeed");
        }
    }

    [Fact]
    public void CleanWorkspace_AfterAddingFiles_RemovesUntrackedFiles()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateTempPath();
        var sha = _git.GetCurrentRevision(_clonePath);
        _git.UpdateExistingCheckout(checkoutPath, _clonePath, sha!);

        var untrackedFile = Path.Combine(checkoutPath, "untracked_test.txt");
        File.WriteAllText(untrackedFile, "This file should be removed");

        // Act
        var result = _git.CleanWorkspace(checkoutPath);

        // Assert
        Assert.True(result);
        Assert.False(File.Exists(untrackedFile));
    }

    [Fact]
    public void CleanWorkspace_AfterModifyingFiles_RevertsChanges()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateTempPath();
        var sha = _git.GetCurrentRevision(_clonePath);
        _git.UpdateExistingCheckout(checkoutPath, _clonePath, sha!);

        // Try to find a file to modify (look for any file in the checkout)
        var files = Directory.GetFiles(checkoutPath, "*", SearchOption.TopDirectoryOnly)
            .Where(f => !f.Contains(".git"))
            .ToArray();

        if (files.Length == 0)
        {
            // No files to modify, skip test
            return;
        }

        var testFile = files[0];
        var originalContent = File.ReadAllText(testFile);
        File.WriteAllText(testFile, "Modified content that should be reverted");

        // Act
        var result = _git.CleanWorkspace(checkoutPath);

        // Assert
        Assert.True(result);
        var currentContent = File.ReadAllText(testFile);
        Assert.Equal(originalContent, currentContent);
    }

    [Fact]
    public void CleanWorkspace_WithInvalidPath_ReturnsFalse()
    {
        // Arrange
        var invalidPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString());

        // Act
        var result = _git.CleanWorkspace(invalidPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CheckoutRevision_WithDifferentRevisions_ChecksOutCorrectVersion()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Arrange
        var currentSha = _git.GetCurrentRevision(_clonePath);
        Assert.NotNull(currentSha);

        // Try to get parent commit
        var parentSha = _git.ResolveRevision(_clonePath, "HEAD~1");

        if (parentSha != null)
        {
            var parentPath = CreateTempPath();

            // Act
            var result = _git.CheckoutRevision(_clonePath, parentSha, parentPath);

            // Assert
            Assert.True(result);
            Assert.True(Directory.Exists(parentPath));
        }
    }

    [Fact]
    public void ResolveRevision_WithInvalidRevision_ReturnsNull()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Act
        var result = _git.ResolveRevision(_clonePath, "nonexistent-revision-12345");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetRevisionDescription_WithInvalidRevision_ReturnsNull()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Act
        var result = _git.GetRevisionDescription(_clonePath, "nonexistent-revision-12345");

        // Assert
        Assert.Null(result);
    }

    #region GetCurrentBranch Tests

    [Fact]
    public void GetCurrentBranch_WithClonedRepository_ReturnsBranchName()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Act
        var result = _git.GetCurrentBranch(_clonePath);

        // Assert - should return "main" or "master" depending on the repo's default branch
        Assert.NotNull(result);
        Assert.True(result == "main" || result == "master", $"Expected 'main' or 'master' but got '{result}'");
    }

    [Fact]
    public void GetCurrentBranch_WithInvalidPath_ReturnsNull()
    {
        // Arrange
        var invalidPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString());

        // Act
        var result = _git.GetCurrentBranch(invalidPath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentBranch_WithNonGitDirectory_ReturnsNull()
    {
        // Arrange
        var tempDir = CreateTempPath();
        Directory.CreateDirectory(tempDir);

        // Act
        var result = _git.GetCurrentBranch(tempDir);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentBranch_WithDetachedHead_ReturnsNull()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Arrange - checkout a specific commit to detach HEAD
        var checkoutPath = CreateTempPath();
        var sha = _git.GetCurrentRevision(_clonePath);
        _git.UpdateExistingCheckout(checkoutPath, _clonePath, sha!);

        // The UpdateExistingCheckout may already leave us in detached HEAD state
        // because it checks out a specific SHA

        // Act
        var result = _git.GetCurrentBranch(checkoutPath);

        // Assert - in detached HEAD state, should return null
        // Note: UpdateExistingCheckout checks out by SHA which puts repo in detached HEAD
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentBranch_WithFeatureBranch_ReturnsBranchName()
    {
        // Skip if repository not available
        if (!_repositoryAvailable) return;

        // Arrange - check if feature-test branch exists
        var branchSha = _git.ResolveRevision(_clonePath, "feature-test");

        if (branchSha != null)
        {
            // Checkout the branch by name (not SHA) to get proper branch tracking
            using var repo = new Repository(_clonePath);
            var branch = repo.Branches["feature-test"];
            if (branch != null)
            {
                Commands.Checkout(repo, branch);

                // Act
                var result = _git.GetCurrentBranch(_clonePath);

                // Assert
                Assert.Equal("feature-test", result);

                // Restore to main
                var mainBranch = repo.Branches["main"] ?? repo.Branches["master"];
                if (mainBranch != null)
                {
                    Commands.Checkout(repo, mainBranch);
                }
            }
        }
    }

    #endregion
}
