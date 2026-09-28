using LibGit2Sharp;

namespace RevisionControl.Tests;

/// <summary>
/// Integration tests for GitRevisionControlSystem against a real repository with history, tags
/// and a second branch.
/// </summary>
/// <remarks>
/// Each test builds its own repository (<see cref="GitTestRepositoryFixture"/>, which throws when it
/// cannot), because some of them switch its branch. They used to clone
/// <c>https://github.com/mdempse1/ModelicaEditorTests.git</c> for every test and return early when the
/// clone failed, so offline, or with that repository gone, 21 tests passed having asserted nothing
/// (B481). Nothing here needs that repository in particular: the fixture builds the same layout.
/// </remarks>
public class GitIntegrationTests : IDisposable
{
    private readonly GitRevisionControlSystem _git;
    private readonly GitTestRepositoryFixture _repository;
    private readonly string _clonePath;
    private readonly List<string> _tempPaths = new();

    public GitIntegrationTests()
    {
        _git = new GitRevisionControlSystem();
        _repository = new GitTestRepositoryFixture();
        _clonePath = _repository.ClonePath;
    }

    public void Dispose()
    {
        _repository.Dispose();
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
        // Act
        var sha = _git.GetCurrentRevision(_clonePath);

        // Assert
        Assert.NotNull(sha);
        Assert.Equal(40, sha.Length); // SHA-1 hash length
    }

    [Fact]
    public void ResolveRevision_WithMainBranch_ResolvesSha()
    {
        // Act
        var sha = _git.ResolveRevision(_clonePath, "main");

        // Assert
        Assert.NotNull(sha);
        Assert.Equal(40, sha.Length);
    }

    [Fact]
    public void ResolveRevision_WithHEAD_ResolvesSha()
    {
        // Act
        var sha = _git.ResolveRevision(_clonePath, "HEAD");

        // Assert
        Assert.NotNull(sha);
        Assert.Equal(40, sha.Length);
    }

    [Fact]
    public void GetRevisionDescription_WithCurrentHead_ReturnsDescription()
    {
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
    public void UpdateRevisionInPlace_WithNonExistentPath_PerformsCheckout()
    {
        // Git's fast path currently delegates to UpdateExistingCheckout (LibGit2Sharp's
        // Checkout is already narrow), so this is a smoke test that the new interface
        // method is wired through for Git too.
        var checkoutPath = CreateTempPath();
        var sha = _git.GetCurrentRevision(_clonePath);

        var result = _git.UpdateRevisionInPlace(checkoutPath, _clonePath, sha!);

        Assert.True(result);
        Assert.True(Directory.Exists(checkoutPath));
    }

    [Fact]
    public void UpdateRevisionInPlace_WithExistingCheckout_UpdatesSuccessfully()
    {
        var checkoutPath = CreateTempPath();
        var sha = _git.GetCurrentRevision(_clonePath);
        _git.UpdateExistingCheckout(checkoutPath, _clonePath, sha!);

        var result = _git.UpdateRevisionInPlace(checkoutPath, _clonePath, sha!);

        Assert.True(result);
    }

    [Fact]
    public void UpdateExistingCheckout_MultipleTimes_WorksConsistently()
    {
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
        // Arrange
        var checkoutPath = CreateTempPath();
        var sha = _git.GetCurrentRevision(_clonePath);
        _git.UpdateExistingCheckout(checkoutPath, _clonePath, sha!);

        var testFile = Path.Combine(checkoutPath, "README.md");
        Assert.True(File.Exists(testFile), testFile);
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
        // Arrange
        var currentSha = _git.GetCurrentRevision(_clonePath);
        Assert.NotNull(currentSha);

        var parentSha = _git.ResolveRevision(_clonePath, "HEAD~1");
        Assert.NotNull(parentSha);
        Assert.NotEqual(currentSha, parentSha);
        var parentPath = CreateTempPath();

        // Act
        var result = _git.CheckoutRevision(_clonePath, parentSha, parentPath);

        // Assert - HEAD~1 is the commit before README.md gained its "Updated" section
        Assert.True(result);
        Assert.True(Directory.Exists(parentPath));
        Assert.DoesNotContain("## Updated", File.ReadAllText(Path.Combine(parentPath, "README.md")));
    }

    [Fact]
    public void ResolveRevision_WithInvalidRevision_ReturnsNull()
    {
        // Act
        var result = _git.ResolveRevision(_clonePath, "nonexistent-revision-12345");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetRevisionDescription_WithInvalidRevision_ReturnsNull()
    {
        // Act
        var result = _git.GetRevisionDescription(_clonePath, "nonexistent-revision-12345");

        // Assert
        Assert.Null(result);
    }

    #region GetCurrentBranch Tests

    [Fact]
    public void GetCurrentBranch_WithClonedRepository_ReturnsBranchName()
    {
        // Act
        var result = _git.GetCurrentBranch(_clonePath);

        // Assert
        Assert.Equal("main", result);
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
        // Arrange - check the branch out by name (not SHA) to get proper branch tracking. The
        // repository is this test's own, so it is not switched back.
        using (var repo = new Repository(_clonePath))
        {
            var branch = repo.Branches["feature-test"];
            Assert.NotNull(branch);
            Commands.Checkout(repo, branch);
        }

        // Act
        var result = _git.GetCurrentBranch(_clonePath);

        // Assert
        Assert.Equal("feature-test", result);
    }

    #endregion
}
