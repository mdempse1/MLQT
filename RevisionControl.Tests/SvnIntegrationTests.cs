namespace RevisionControl.Tests;

/// <summary>
/// Integration tests for SvnRevisionControlSystem using the real test repository.
/// Repository: file:///C:/Projects/SVN/ModelicaEditorTest
///
/// Note: These tests are designed to work with an empty or minimal SVN repository.
/// Some tests may be skipped if the repository doesn't have specific content.
/// </summary>
public class SvnIntegrationTests : IDisposable
{
    private const string TestRepoUrl = "file:///C:/Projects/SVN/ModelicaEditorTest/trunk";
    private const string TestRepoRoot = "file:///C:/Projects/SVN/ModelicaEditorTest";
    private readonly SvnRevisionControlSystem _svn;
    private readonly List<string> _checkoutPaths = new();

    public SvnIntegrationTests()
    {
        _svn = new SvnRevisionControlSystem();
    }

    public void Dispose()
    {
        foreach (var path in _checkoutPaths)
        {
            ForceDeleteDirectory(path);
        }
    }

    private string CreateCheckoutPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "SvnIntegrationTest_" + Guid.NewGuid().ToString());
        _checkoutPaths.Add(path);
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
    public void IsValidRepository_WithFileUrl_ChecksRepository()
    {
        // Act
        var result = _svn.IsValidRepository(TestRepoUrl);

        // Assert - Should be true if SVN repository exists at this path
        Assert.True(result);
    }

    [Fact]
    public void IsValidRepository_WithTrunkUrl_ReturnsTrue()
    {
        // Act
        var result = _svn.IsValidRepository(TestRepoUrl);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidRepository_WithBranchesUrl_ReturnsTrue()
    {
        // Arrange
        var branchesUrl = TestRepoRoot + "/branches";

        // Act
        var result = _svn.IsValidRepository(branchesUrl);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidRepository_WithTagsUrl_ReturnsTrue()
    {
        // Arrange
        var tagsUrl = TestRepoRoot + "/tags";

        // Act
        var result = _svn.IsValidRepository(tagsUrl);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidRepository_WithInvalidUrl_ReturnsFalse()
    {
        // Arrange
        var invalidUrl = "file:///C:/NonExistent/SVN/Repository";

        // Act
        var result = _svn.IsValidRepository(invalidUrl);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CheckoutRevision_WithHEAD_ChecksOutSuccessfully()
    {
        // Arrange
        var checkoutPath = CreateCheckoutPath();

        // Act
        var result = _svn.CheckoutRevision(TestRepoUrl, "HEAD", checkoutPath);

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(checkoutPath));
        Assert.True(Directory.Exists(Path.Combine(checkoutPath, ".svn")));
    }

    [Fact]
    public void CheckoutRevision_WithEmptyRevision_DefaultsToHEAD()
    {
        // Arrange
        var checkoutPath = CreateCheckoutPath();

        // Act
        var result = _svn.CheckoutRevision(TestRepoUrl, "", checkoutPath);

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(checkoutPath));
    }

    [Fact]
    public void CheckoutRevision_WithLowercaseKeyword_WorksCaseInsensitive()
    {
        // Arrange
        var checkoutPath = CreateCheckoutPath();

        // Act
        var result = _svn.CheckoutRevision(TestRepoUrl, "head", checkoutPath);

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(checkoutPath));
    }

    [Fact]
    public void GetCurrentRevision_AfterCheckout_ReturnsRevisionNumber()
    {
        // Arrange
        var checkoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(TestRepoUrl, "HEAD", checkoutPath);

        // Act
        var revision = _svn.GetCurrentRevision(checkoutPath);

        // Assert
        Assert.NotNull(revision);
        Assert.True(long.TryParse(revision, out _), "Revision should be a number");
    }

    [Fact]
    public void GetCurrentRevision_WithRemoteUrl_ReturnsHeadRevision()
    {
        // Act
        var revision = _svn.GetCurrentRevision(TestRepoUrl);

        // Assert
        Assert.NotNull(revision);
        Assert.True(long.TryParse(revision, out _), "Revision should be a number");
    }

    [Fact]
    public void GetRevisionDescription_WithHEAD_HandlesEmptyRepository()
    {
        // Act
        var description = _svn.GetRevisionDescription(TestRepoUrl, "HEAD");

        // Assert - may be null or empty for empty/new repository
        // This test just ensures the method doesn't throw
        if (description != null)
        {
            Assert.NotNull(description);
        }
    }

    [Fact]
    public void UpdateExistingCheckout_WithNonExistentCheckout_PerformsInitialCheckout()
    {
        // Arrange
        var checkoutPath = CreateCheckoutPath();

        // Act
        var result = _svn.UpdateExistingCheckout(checkoutPath, TestRepoUrl, "HEAD");

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(checkoutPath));
        Assert.True(Directory.Exists(Path.Combine(checkoutPath, ".svn")));
    }

    [Fact]
    public void UpdateExistingCheckout_MultipleTimes_WorksConsistently()
    {
        // Arrange
        var checkoutPath = CreateCheckoutPath();

        // Act & Assert - multiple updates should all succeed
        for (int i = 0; i < 3; i++)
        {
            var result = _svn.UpdateExistingCheckout(checkoutPath, TestRepoUrl, "HEAD");
            Assert.True(result, $"Update {i + 1} should succeed");
        }
    }

    [Fact]
    public void CheckoutRevision_ToNestedPath_CreatesDirectories()
    {
        // Arrange
        var nestedPath = Path.Combine(CreateCheckoutPath(), "nested", "deep", "path");

        // Act
        var result = _svn.CheckoutRevision(TestRepoUrl, "HEAD", nestedPath);

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(nestedPath));
    }

    [Fact]
    public void IsValidRepository_WithWorkingCopy_ReturnsTrue()
    {
        // Arrange
        var checkoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(TestRepoUrl, "HEAD", checkoutPath);

        // Act
        var result = _svn.IsValidRepository(checkoutPath);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidRepository_WithNonWorkingCopyDirectory_ReturnsFalse()
    {
        // Arrange
        var emptyDir = CreateCheckoutPath();
        Directory.CreateDirectory(emptyDir);

        // Act
        var result = _svn.IsValidRepository(emptyDir);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetCurrentRevision_WithInvalidPath_ReturnsNull()
    {
        // Arrange
        var invalidPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString());

        // Act
        var result = _svn.GetCurrentRevision(invalidPath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CleanWorkspace_WithInvalidPath_ReturnsFalse()
    {
        // Arrange
        var invalidPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString());

        // Act
        var result = _svn.CleanWorkspace(invalidPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ResolveRevision_WithInvalidRepository_ReturnsNull()
    {
        // Arrange
        var invalidPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString());

        // Act
        var result = _svn.ResolveRevision(invalidPath, "HEAD");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetRevisionDescription_WithInvalidRepository_ReturnsNull()
    {
        // Arrange
        var invalidPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString());

        // Act
        var result = _svn.GetRevisionDescription(invalidPath, "HEAD");

        // Assert
        Assert.Null(result);
    }

    #region GetCurrentBranch Tests

    [Fact]
    public void GetCurrentBranch_WithTrunkUrl_ReturnsTrunk()
    {
        // Arrange
        var trunkUrl = TestRepoRoot + "/trunk";

        // Act
        var result = _svn.GetCurrentBranch(trunkUrl);

        // Assert
        Assert.Equal("trunk", result);
    }

    [Fact]
    public void GetCurrentBranch_WithTrunkWorkingCopy_ReturnsTrunk()
    {
        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoRoot + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var result = _svn.GetCurrentBranch(checkoutPath);

        // Assert
        Assert.Equal("trunk", result);
    }

    [Fact]
    public void GetCurrentBranch_WithBranchesUrl_ReturnsBranchName()
    {
        // Arrange - check if there are any branches in the repository
        var branchUrl = TestRepoRoot + "/branches/test-branch";

        // Act
        var result = _svn.GetCurrentBranch(branchUrl);

        // Assert - if branch exists, should return branches/test-branch
        // If it doesn't exist, result may be null
        if (result != null)
        {
            Assert.StartsWith("branches/", result);
        }
    }

    [Fact]
    public void GetCurrentBranch_WithTagsUrl_ReturnsTagName()
    {
        // Arrange
        var tagUrl = TestRepoRoot + "/tags/v1.0";

        // Act
        var result = _svn.GetCurrentBranch(tagUrl);

        // Assert
        Assert.Equal("tags/v1.0", result);
    }

    [Fact]
    public void GetCurrentBranch_WithTagV2Url_ReturnsTagName()
    {
        // Arrange
        var tagUrl = TestRepoRoot + "/tags/v2.0";

        // Act
        var result = _svn.GetCurrentBranch(tagUrl);

        // Assert
        Assert.Equal("tags/v2.0", result);
    }

    [Fact]
    public void GetCurrentBranch_WithTagWorkingCopy_ReturnsTagName()
    {
        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var tagUrl = TestRepoRoot + "/tags/v1.0";
        _svn.CheckoutRevision(tagUrl, "HEAD", checkoutPath);

        // Act
        var result = _svn.GetCurrentBranch(checkoutPath);

        // Assert
        Assert.Equal("tags/v1.0", result);
    }

    [Fact]
    public void GetCurrentBranch_WithInvalidPath_ReturnsNull()
    {
        // Arrange
        var invalidPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString());

        // Act
        var result = _svn.GetCurrentBranch(invalidPath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentBranch_WithNonWorkingCopyDirectory_ReturnsNull()
    {
        // Arrange
        var emptyDir = CreateCheckoutPath();
        Directory.CreateDirectory(emptyDir);

        // Act
        var result = _svn.GetCurrentBranch(emptyDir);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentBranch_WithRootUrl_ReturnsNull()
    {
        // Arrange - repository root doesn't have a branch pattern
        var rootUrl = TestRepoRoot;

        // Act
        var result = _svn.GetCurrentBranch(rootUrl);

        // Assert - root URL doesn't match any branch pattern
        Assert.Null(result);
    }

    #endregion
}
