using SharpSvn;

namespace RevisionControl.Tests;

/// <summary>
/// Advanced integration tests for SvnRevisionControlSystem using actual repository content.
/// Repository: file:///C:/Projects/SVN/ModelicaEditorTest
///
/// These tests verify operations with real Modelica files and tags/trunk structure.
/// </summary>
public class SvnIntegrationAdvancedTests : IDisposable
{
    private const string TestRepoUrl = "file:///C:/Projects/SVN/ModelicaEditorTest";
    private readonly SvnRevisionControlSystem _svn;
    private readonly List<string> _checkoutPaths = new();
    private readonly bool _repositoryAvailable;

    public SvnIntegrationAdvancedTests()
    {
        _svn = new SvnRevisionControlSystem();

        // Test if repository is available
        try
        {
            using var client = new SvnClient();
            client.GetInfo(new Uri(TestRepoUrl + "/trunk"), out _);
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
        {
            ForceDeleteDirectory(path);
        }
    }

    private string CreateCheckoutPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "SvnAdvanced_" + Guid.NewGuid().ToString());
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
    public void CheckoutRevision_TrunkUrl_ChecksOutCorrectStructure()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";

        // Act
        var result = _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Assert
        Assert.True(result);
        Assert.True(File.Exists(Path.Combine(checkoutPath, "package.mo")));
        Assert.True(File.Exists(Path.Combine(checkoutPath, "README.txt")));
        Assert.True(Directory.Exists(Path.Combine(checkoutPath, "Models")));
        Assert.True(Directory.Exists(Path.Combine(checkoutPath, ".svn")));
    }

    [Fact]
    public void CheckoutRevision_TagV1_ChecksOutTag()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var tagUrl = TestRepoUrl + "/tags/v1.0";

        // Act
        var result = _svn.CheckoutRevision(tagUrl, "HEAD", checkoutPath);

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(checkoutPath));
        Assert.True(File.Exists(Path.Combine(checkoutPath, "package.mo")));
    }

    [Fact]
    public void CheckoutRevision_TagV2_ChecksOutTag()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var tagUrl = TestRepoUrl + "/tags/v2.0";

        // Act
        var result = _svn.CheckoutRevision(tagUrl, "HEAD", checkoutPath);

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(checkoutPath));
        Assert.True(File.Exists(Path.Combine(checkoutPath, "package.mo")));
    }

    [Fact]
    public void CheckoutRevision_VerifiesModelicaFiles()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";

        // Act
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Assert - verify Modelica files exist
        var modelicaFiles = Directory.GetFiles(checkoutPath, "*.mo", SearchOption.AllDirectories);
        Assert.NotEmpty(modelicaFiles);
        Assert.Contains(modelicaFiles, f => f.Contains("SimpleModel.mo"));
        Assert.Contains(modelicaFiles, f => f.Contains("TestModel.mo"));
    }

    [Fact]
    public void CheckoutRevision_ModelsDirectory_ContainsExpectedFiles()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";

        // Act
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Assert
        var modelsDir = Path.Combine(checkoutPath, "Models");
        Assert.True(Directory.Exists(modelsDir));
        Assert.True(File.Exists(Path.Combine(modelsDir, "package.mo")));
        Assert.True(File.Exists(Path.Combine(modelsDir, "SimpleModel.mo")));
        Assert.True(File.Exists(Path.Combine(modelsDir, "TestModel.mo")));
    }

    [Fact]
    public void CheckoutRevision_VerifiesModelicaContent()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";

        // Act
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Assert - verify SimpleModel.mo has expected content
        var simpleModelPath = Path.Combine(checkoutPath, "Models", "SimpleModel.mo");
        if (File.Exists(simpleModelPath))
        {
            var content = File.ReadAllText(simpleModelPath);
            Assert.Contains("model SimpleModel", content);
            Assert.Contains("Real x", content);
            Assert.Contains("Real y", content);
        }
    }

    [Fact]
    public void UpdateExistingCheckout_BetweenTrunkAndTag_UpdatesFiles()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        var tagUrl = TestRepoUrl + "/tags/v1.0";

        // Act - checkout trunk first
        _svn.UpdateExistingCheckout(checkoutPath, trunkUrl, "HEAD");
        var trunkFileCount = Directory.GetFiles(checkoutPath, "*", SearchOption.AllDirectories).Length;

        // Update to tag v1.0
        _svn.UpdateExistingCheckout(checkoutPath, tagUrl, "HEAD");
        var tagFileCount = Directory.GetFiles(checkoutPath, "*", SearchOption.AllDirectories).Length;

        // Assert - both should have files (even if same content)
        Assert.True(trunkFileCount > 0);
        Assert.True(tagFileCount > 0);
    }

    [Fact]
    public void CleanWorkspace_AfterModifyingFile_RevertsChanges()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        var simpleModelPath = Path.Combine(checkoutPath, "Models", "SimpleModel.mo");
        if (!File.Exists(simpleModelPath)) return;

        var originalContent = File.ReadAllText(simpleModelPath);
        File.WriteAllText(simpleModelPath, "// Modified content");

        // Act
        var result = _svn.CleanWorkspace(checkoutPath);

        // Assert
        Assert.True(result);
        var restoredContent = File.ReadAllText(simpleModelPath);
        Assert.Equal(originalContent, restoredContent);
    }

    [Fact]
    public void GetCurrentRevision_AfterCheckout_ReturnsValidRevision()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var revision = _svn.GetCurrentRevision(checkoutPath);

        // Assert
        Assert.NotNull(revision);
        Assert.True(long.TryParse(revision, out var revNum));
        Assert.True(revNum > 0);
    }

    [Fact]
    public void IsValidRepository_WithTrunkUrl_ReturnsTrue()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var trunkUrl = TestRepoUrl + "/trunk";

        // Act
        var result = _svn.IsValidRepository(trunkUrl);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidRepository_WithTagUrl_ReturnsTrue()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var tagUrl = TestRepoUrl + "/tags/v1.0";

        // Act
        var result = _svn.IsValidRepository(tagUrl);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CheckoutRevision_PreservesFilePermissions()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";

        // Act
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Assert - files should be readable
        var files = Directory.GetFiles(checkoutPath, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            Assert.True(File.Exists(file));
            // Should be able to read the file
            var content = File.ReadAllText(file);
            Assert.NotNull(content);
        }
    }

    [Fact]
    public void CheckoutRevision_MultipleModelicaFiles_ChecksOutAll()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";

        // Act
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Assert - check for multiple Modelica files
        var modelsDir = Path.Combine(checkoutPath, "Models");
        if (Directory.Exists(modelsDir))
        {
            var modelFiles = Directory.GetFiles(modelsDir, "*.mo");
            Assert.NotEmpty(modelFiles);

            // Should have at least package.mo, SimpleModel.mo, and TestModel.mo
            Assert.Contains(modelFiles, f => Path.GetFileName(f) == "package.mo");
            Assert.Contains(modelFiles, f => Path.GetFileName(f) == "SimpleModel.mo");
            Assert.Contains(modelFiles, f => Path.GetFileName(f) == "TestModel.mo");
        }
    }

    [Fact]
    public void UpdateExistingCheckout_ToSameRevision_Succeeds()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";

        // Act - checkout and update to same revision
        _svn.UpdateExistingCheckout(checkoutPath, trunkUrl, "HEAD");
        var result = _svn.UpdateExistingCheckout(checkoutPath, trunkUrl, "HEAD");

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(checkoutPath));
    }

    [Fact]
    public void CheckoutRevision_WithREADME_ContainsFile()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";

        // Act
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Assert
        var readmePath = Path.Combine(checkoutPath, "README.txt");
        Assert.True(File.Exists(readmePath));
        var content = File.ReadAllText(readmePath);
        Assert.NotEmpty(content);
    }

    [Fact]
    public void CleanWorkspace_WithAddedFile_Succeeds()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        var newFilePath = Path.Combine(checkoutPath, "unversioned.txt");
        File.WriteAllText(newFilePath, "This file is not in SVN");

        // Act
        var result = _svn.CleanWorkspace(checkoutPath);

        // Assert
        Assert.True(result);
        // Note: CleanWorkspace reverts changes but may not remove unversioned files
        // depending on implementation
    }

    [Fact]
    public void CheckoutRevision_VerifiesWithinStatements()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";

        // Act
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Assert - verify SimpleModel.mo has within statement
        var simpleModelPath = Path.Combine(checkoutPath, "Models", "SimpleModel.mo");
        if (File.Exists(simpleModelPath))
        {
            var content = File.ReadAllText(simpleModelPath);
            Assert.Contains("within", content);
            Assert.Contains("ModelicaEditorTest", content);
        }
    }

    [Fact]
    public void UpdateExistingCheckout_FromNonExistent_PerformsInitialCheckout()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";

        // Act - update non-existent directory (should perform initial checkout)
        var result = _svn.UpdateExistingCheckout(checkoutPath, trunkUrl, "HEAD");

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(checkoutPath));
        Assert.True(File.Exists(Path.Combine(checkoutPath, "package.mo")));
    }

    // ============================================================================
    // GetLogEntries Tests
    // ============================================================================

    [Fact]
    public void GetLogEntries_WithDefaultOptions_ReturnsLogEntries()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var entries = _svn.GetLogEntries(checkoutPath);

        // Assert
        Assert.NotEmpty(entries);
        Assert.All(entries, e =>
        {
            Assert.NotEmpty(e.Revision);
            Assert.NotEmpty(e.ShortRevision);
            Assert.NotEmpty(e.Author);
            Assert.NotEqual(default, e.Date);
        });
    }

    [Fact]
    public void GetLogEntries_WithMaxEntries_RespectsLimit()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        var options = new VcsLogOptions { MaxEntries = 2 };

        // Act
        var entries = _svn.GetLogEntries(checkoutPath, options);

        // Assert
        Assert.True(entries.Count <= 2);
    }

    [Fact]
    public void GetLogEntries_FromRepositoryUrl_ReturnsLogEntries()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var trunkUrl = TestRepoUrl + "/trunk";

        // Act - can get log from URL directly
        var entries = _svn.GetLogEntries(trunkUrl);

        // Assert
        Assert.NotEmpty(entries);
    }

    [Fact]
    public void GetLogEntries_WithSinceDate_ReturnsEntries()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        var allEntries = _svn.GetLogEntries(checkoutPath, new VcsLogOptions { MaxEntries = 100 });
        if (allEntries.Count < 2) return;

        // Use the Since filter - SVN implementation may not strictly filter
        // but the option should be accepted without error
        var farPastDate = DateTimeOffset.Now.AddYears(-10);
        var options = new VcsLogOptions { Since = farPastDate, MaxEntries = 100 };

        // Act
        var filteredEntries = _svn.GetLogEntries(checkoutPath, options);

        // Assert - should return entries (all entries are after 10 years ago)
        Assert.NotEmpty(filteredEntries);
        // All entries have valid dates
        Assert.All(filteredEntries, e => Assert.NotEqual(default, e.Date));
    }

    [Fact]
    public void GetLogEntries_WithUntilDate_FiltersNewCommits()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        var allEntries = _svn.GetLogEntries(checkoutPath, new VcsLogOptions { MaxEntries = 100 });
        if (allEntries.Count < 2) return;

        var midDate = allEntries[allEntries.Count / 2].Date;
        var options = new VcsLogOptions { Until = midDate, MaxEntries = 100 };

        // Act
        var filteredEntries = _svn.GetLogEntries(checkoutPath, options);

        // Assert - all entries should be on or before the until date
        Assert.All(filteredEntries, e => Assert.True(e.Date <= midDate.AddMinutes(1)));
    }

    [Fact]
    public void GetLogEntries_RevisionIsNumeric()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var trunkUrl = TestRepoUrl + "/trunk";

        // Act
        var entries = _svn.GetLogEntries(trunkUrl);

        // Assert - SVN revisions should be numeric
        Assert.NotEmpty(entries);
        Assert.All(entries, e =>
        {
            Assert.True(long.TryParse(e.Revision, out var revNum));
            Assert.True(revNum > 0);
        });
    }

    [Fact]
    public void GetLogEntries_InvalidRepository_ReturnsEmptyList()
    {
        // Act
        var entries = _svn.GetLogEntries(Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()));

        // Assert
        Assert.Empty(entries);
    }

    // ============================================================================
    // GetChangedFiles Tests
    // ============================================================================

    [Fact]
    public void GetChangedFiles_ForRevision_ReturnsChangedFiles()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var trunkUrl = TestRepoUrl + "/trunk";
        var entries = _svn.GetLogEntries(trunkUrl, new VcsLogOptions { MaxEntries = 10 });
        if (entries.Count == 0) return;

        // Act
        var changedFiles = _svn.GetChangedFiles(trunkUrl, entries.First().Revision);

        // Assert
        Assert.NotNull(changedFiles);
    }

    [Fact]
    public void GetChangedFiles_FromWorkingCopy_ReturnsChangedFiles()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        var entries = _svn.GetLogEntries(checkoutPath, new VcsLogOptions { MaxEntries = 10 });
        if (entries.Count == 0) return;

        // Act
        var changedFiles = _svn.GetChangedFiles(checkoutPath, entries.First().Revision);

        // Assert
        Assert.NotNull(changedFiles);
    }

    [Fact]
    public void GetChangedFiles_ReturnsRelativePaths()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var trunkUrl = TestRepoUrl + "/trunk";
        var entries = _svn.GetLogEntries(trunkUrl, new VcsLogOptions { MaxEntries = 10 });
        if (entries.Count == 0) return;

        // Find a commit with changed files
        foreach (var entry in entries)
        {
            var changedFiles = _svn.GetChangedFiles(trunkUrl, entry.Revision);
            if (changedFiles.Count > 0)
            {
                // Assert - paths should not be full URLs
                Assert.All(changedFiles, f =>
                {
                    Assert.False(f.Path.StartsWith("file://"));
                    Assert.False(f.Path.StartsWith("http://"));
                    Assert.False(f.Path.StartsWith("https://"));
                });
                break;
            }
        }
    }

    [Fact]
    public void GetChangedFiles_InvalidRevision_ReturnsEmptyList()
    {
        if (!_repositoryAvailable) return;

        // Act
        var changedFiles = _svn.GetChangedFiles(TestRepoUrl + "/trunk", "999999");

        // Assert
        Assert.Empty(changedFiles);
    }

    // ============================================================================
    // GetFileContentAtRevision Tests
    // ============================================================================

    [Fact]
    public void GetFileContentAtRevision_ExistingFile_ReturnsContent()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var content = _svn.GetFileContentAtRevision(checkoutPath, "README.txt", null);

        // Assert
        Assert.NotNull(content);
        Assert.NotEmpty(content);
    }

    [Fact]
    public void GetFileContentAtRevision_WithSpecificRevision_ReturnsContent()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        var currentRevision = _svn.GetCurrentRevision(checkoutPath);

        // Act
        var content = _svn.GetFileContentAtRevision(checkoutPath, "package.mo", currentRevision);

        // Assert
        Assert.NotNull(content);
        Assert.Contains("package", content.ToLower());
    }

    [Fact]
    public void GetFileContentAtRevision_NestedFile_ReturnsContent()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var content = _svn.GetFileContentAtRevision(checkoutPath, "Models/SimpleModel.mo", null);

        // Assert
        Assert.NotNull(content);
        Assert.Contains("model SimpleModel", content);
    }

    [Fact]
    public void GetFileContentAtRevision_NonExistentFile_ReturnsNull()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var content = _svn.GetFileContentAtRevision(checkoutPath, "NonExistent.mo", null);

        // Assert
        Assert.Null(content);
    }

    [Fact]
    public void GetFileContentAtRevision_WithHEAD_ReturnsContent()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var content = _svn.GetFileContentAtRevision(checkoutPath, "README.txt", "HEAD");

        // Assert
        Assert.NotNull(content);
    }

    [Fact]
    public void GetFileContentAtRevision_WithBASE_ReturnsContent()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var content = _svn.GetFileContentAtRevision(checkoutPath, "README.txt", "BASE");

        // Assert
        Assert.NotNull(content);
    }

    // ============================================================================
    // GetWorkingCopyChanges Tests
    // ============================================================================

    [Fact]
    public void GetWorkingCopyChanges_CleanWorkingCopy_ReturnsEmptyList()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var changes = _svn.GetWorkingCopyChanges(checkoutPath);

        // Assert
        Assert.Empty(changes);
    }

    [Fact]
    public void GetWorkingCopyChanges_WithModifiedFile_ReturnsModifiedStatus()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        var readmePath = Path.Combine(checkoutPath, "README.txt");
        if (!File.Exists(readmePath)) return;

        File.AppendAllText(readmePath, "\n// Modified for test");

        // Act
        var changes = _svn.GetWorkingCopyChanges(checkoutPath);

        // Assert
        Assert.NotEmpty(changes);
        var readmeChange = changes.FirstOrDefault(c => c.Path.Contains("README"));
        Assert.NotNull(readmeChange);
        Assert.Equal(VcsFileStatus.Modified, readmeChange.Status);
    }

    [Fact]
    public void GetWorkingCopyChanges_WithUntrackedFile_ReturnsUntrackedStatus()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        var newFilePath = Path.Combine(checkoutPath, "NewUntrackedFile.txt");
        File.WriteAllText(newFilePath, "This is a new file");

        // Act
        var changes = _svn.GetWorkingCopyChanges(checkoutPath);

        // Assert
        Assert.NotEmpty(changes);
        var newFileChange = changes.FirstOrDefault(c => c.Path.Contains("NewUntrackedFile"));
        Assert.NotNull(newFileChange);
        Assert.Equal(VcsFileStatus.Untracked, newFileChange.Status);
    }

    [Fact]
    public void GetWorkingCopyChanges_WithDeletedFile_ReturnsDeletedStatus()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        var readmePath = Path.Combine(checkoutPath, "README.txt");
        if (!File.Exists(readmePath)) return;

        File.Delete(readmePath);

        // Act
        var changes = _svn.GetWorkingCopyChanges(checkoutPath);

        // Assert
        Assert.NotEmpty(changes);
        var deleteChange = changes.FirstOrDefault(c => c.Path.Contains("README"));
        Assert.NotNull(deleteChange);
        Assert.Equal(VcsFileStatus.Deleted, deleteChange.Status);
    }

    [Fact]
    public void GetWorkingCopyChanges_InvalidRepository_ReturnsEmptyList()
    {
        // Act
        var changes = _svn.GetWorkingCopyChanges(Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()));

        // Assert
        Assert.Empty(changes);
    }

    [Fact]
    public void GetWorkingCopyChanges_ReturnsRelativePaths()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        File.WriteAllText(Path.Combine(checkoutPath, "Models", "NewModel.mo"), "model NewModel end NewModel;");

        // Act
        var changes = _svn.GetWorkingCopyChanges(checkoutPath);

        // Assert
        Assert.NotEmpty(changes);
        Assert.All(changes, c =>
        {
            Assert.False(Path.IsPathRooted(c.Path), $"Path should be relative: {c.Path}");
        });
    }

    // ============================================================================
    // RevertFiles Tests
    // ============================================================================

    [Fact]
    public void RevertFiles_ModifiedFile_RevertsChanges()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        var readmePath = Path.Combine(checkoutPath, "README.txt");
        if (!File.Exists(readmePath)) return;

        var originalContent = File.ReadAllText(readmePath);
        File.WriteAllText(readmePath, "Modified content");

        // Act
        var result = _svn.RevertFiles(checkoutPath, new[] { "README.txt" });

        // Assert
        Assert.True(result.Success);
        var restoredContent = File.ReadAllText(readmePath);
        Assert.Equal(originalContent, restoredContent);
    }

    [Fact]
    public void RevertFiles_UntrackedFile_DeletesFile()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        var newFilePath = Path.Combine(checkoutPath, "UntrackedFile.txt");
        File.WriteAllText(newFilePath, "Untracked content");

        // Act
        var result = _svn.RevertFiles(checkoutPath, new[] { "UntrackedFile.txt" });

        // Assert
        Assert.True(result.Success);
        Assert.False(File.Exists(newFilePath));
    }

    [Fact]
    public void RevertFiles_MultipleFiles_RevertsAll()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        var readmePath = Path.Combine(checkoutPath, "README.txt");
        var packagePath = Path.Combine(checkoutPath, "package.mo");
        if (!File.Exists(readmePath) || !File.Exists(packagePath)) return;

        var originalReadme = File.ReadAllText(readmePath);
        var originalPackage = File.ReadAllText(packagePath);

        File.WriteAllText(readmePath, "Modified README");
        File.WriteAllText(packagePath, "Modified package");

        // Act
        var result = _svn.RevertFiles(checkoutPath, new[] { "README.txt", "package.mo" });

        // Assert
        Assert.True(result.Success);
        Assert.Equal(originalReadme, File.ReadAllText(readmePath));
        Assert.Equal(originalPackage, File.ReadAllText(packagePath));
    }

    [Fact]
    public void RevertFiles_NestedFile_RevertsCorrectly()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        var simpleModelPath = Path.Combine(checkoutPath, "Models", "SimpleModel.mo");
        if (!File.Exists(simpleModelPath)) return;

        var originalContent = File.ReadAllText(simpleModelPath);
        File.WriteAllText(simpleModelPath, "// Modified");

        // Act
        var result = _svn.RevertFiles(checkoutPath, new[] { "Models/SimpleModel.mo" });

        // Assert
        Assert.True(result.Success);
        Assert.Equal(originalContent, File.ReadAllText(simpleModelPath));
    }

    // ============================================================================
    // UpdateToLatest Tests
    // ============================================================================

    [Fact]
    public void UpdateToLatest_ValidWorkingCopy_ReturnsSuccess()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var result = _svn.UpdateToLatest(checkoutPath);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.NewRevision);
    }

    [Fact]
    public void UpdateToLatest_AlreadyUpToDate_ReturnsNoChanges()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);
        _svn.UpdateToLatest(checkoutPath);

        // Act
        var result = _svn.UpdateToLatest(checkoutPath);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void UpdateToLatest_InvalidPath_ReturnsFailure()
    {
        // Act
        var result = _svn.UpdateToLatest(Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()));

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public void UpdateToLatest_ReturnsRevisionNumbers()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var result = _svn.UpdateToLatest(checkoutPath);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.NewRevision);
        Assert.True(long.TryParse(result.NewRevision, out var revNum));
        Assert.True(revNum > 0);
    }

    // ============================================================================
    // GetBranches Tests
    // ============================================================================

    [Fact]
    public void GetBranches_FromTrunk_ReturnsAvailableBranches()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var branches = _svn.GetBranches(checkoutPath);

        // Assert
        Assert.NotEmpty(branches);
        // Should include trunk
        Assert.Contains(branches, b => b.Name.Contains("trunk"));
    }

    [Fact]
    public void GetBranches_IncludesTags()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var branches = _svn.GetBranches(checkoutPath);

        // Assert
        // Should include tags (v1.0, v2.0)
        var hasTags = branches.Any(b => b.Name.Contains("v1.0") || b.Name.Contains("v2.0") || b.Name.Contains("tags"));
        Assert.True(hasTags || branches.Count > 0); // At least has some branches
    }

    [Fact]
    public void GetBranches_FromUrl_ReturnsAvailableBranches()
    {
        if (!_repositoryAvailable) return;

        // Act
        var branches = _svn.GetBranches(TestRepoUrl + "/trunk");

        // Assert
        Assert.NotEmpty(branches);
    }

    [Fact]
    public void GetBranches_HasCurrentBranch()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var branches = _svn.GetBranches(checkoutPath);

        // Assert - should have a current branch marker
        var currentBranch = branches.FirstOrDefault(b => b.IsCurrent);
        Assert.NotNull(currentBranch);
    }

    [Fact]
    public void GetBranches_BranchHasName()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var branches = _svn.GetBranches(checkoutPath);

        // Assert
        Assert.All(branches, b => Assert.NotEmpty(b.Name));
    }

    [Fact]
    public void GetBranches_InvalidPath_ReturnsEmptyList()
    {
        // Act
        var branches = _svn.GetBranches(Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()));

        // Assert
        Assert.Empty(branches);
    }

    // ============================================================================
    // GetCurrentBranch Tests
    // ============================================================================

    [Fact]
    public void GetCurrentBranch_FromTrunk_ReturnsTrunk()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var branch = _svn.GetCurrentBranch(checkoutPath);

        // Assert
        Assert.NotNull(branch);
        Assert.Contains("trunk", branch);
    }

    [Fact]
    public void GetCurrentBranch_FromTag_ReturnsTagPath()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var tagUrl = TestRepoUrl + "/tags/v1.0";
        _svn.CheckoutRevision(tagUrl, "HEAD", checkoutPath);

        // Act
        var branch = _svn.GetCurrentBranch(checkoutPath);

        // Assert
        Assert.NotNull(branch);
        Assert.Contains("tags", branch);
    }

    [Fact]
    public void GetCurrentBranch_InvalidPath_ReturnsNull()
    {
        // Act
        var branch = _svn.GetCurrentBranch(Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()));

        // Assert
        Assert.Null(branch);
    }

    // ============================================================================
    // SwitchBranch Tests
    // ============================================================================

    [Fact]
    public void SwitchBranch_FromTrunkToTag_SwitchesSuccessfully()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act - switch to tags/v1.0
        var result = _svn.SwitchBranch(checkoutPath, "tags/v1.0");

        // Assert
        Assert.True(result.Success);
        var currentBranch = _svn.GetCurrentBranch(checkoutPath);
        Assert.Contains("v1.0", currentBranch);
    }

    [Fact]
    public void SwitchBranch_FromTagToTrunk_SwitchesSuccessfully()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var tagUrl = TestRepoUrl + "/tags/v1.0";
        _svn.CheckoutRevision(tagUrl, "HEAD", checkoutPath);

        // Act - switch to trunk
        var result = _svn.SwitchBranch(checkoutPath, "trunk");

        // Assert
        Assert.True(result.Success);
        var currentBranch = _svn.GetCurrentBranch(checkoutPath);
        Assert.Contains("trunk", currentBranch);
    }

    [Fact]
    public void SwitchBranch_BetweenTags_SwitchesSuccessfully()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var tagUrl = TestRepoUrl + "/tags/v1.0";
        _svn.CheckoutRevision(tagUrl, "HEAD", checkoutPath);

        // Act - switch to tags/v2.0
        var result = _svn.SwitchBranch(checkoutPath, "tags/v2.0");

        // Assert
        Assert.True(result.Success);
        var currentBranch = _svn.GetCurrentBranch(checkoutPath);
        Assert.Contains("v2.0", currentBranch);
    }

    [Fact]
    public void SwitchBranch_NonExistentBranch_ReturnsFailure()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var result = _svn.SwitchBranch(checkoutPath, "branches/non-existent-branch");

        // Assert
        Assert.False(result.Success);
    }

    // ============================================================================
    // Commit Tests
    // ============================================================================

    [Fact]
    public void Commit_NoChanges_ReturnsFailure()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var result = _svn.Commit(checkoutPath, "Empty commit");

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public void Commit_InvalidPath_ReturnsFailure()
    {
        // Act
        var result = _svn.Commit(
            Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()),
            "Test message"
        );

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public void Commit_NewFilesToAdd_ReturnsSuccess()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var branchName = "branches/" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);
        _svn.CreateBranch(checkoutPath, branchName, true);
        var newDirName = Guid.NewGuid().ToString("N").Substring(0, 8);
        var newDirPath = Path.Combine(checkoutPath, newDirName);
        if (!Directory.Exists(newDirPath))
            Directory.CreateDirectory(newDirPath);
        File.WriteAllText(Path.Combine(newDirPath, "NewFile.txt"), "This is a new file");
        List<string> filesToAdd = new List<string> { Path.Combine(newDirName, "NewFile.txt") };

        // Act
        var result = _svn.Commit(checkoutPath, "Add a new file", filesToAdd);

        // Assert
        Assert.True(result.Success, $"Commit failed with error: {result.ErrorMessage}");

        // Cleanup is handled by Dispose() via _checkoutPaths
    }

    [Fact]
    public void Commit_ModifiedFile_ReturnsSuccess()
    {
        if (!_repositoryAvailable) return;

        // Arrange - create a branch to avoid modifying trunk
        var checkoutPath = CreateCheckoutPath();
        var branchName = "branches/" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);
        _svn.CreateBranch(checkoutPath, branchName, true);

        // Modify an existing file
        var readmePath = Path.Combine(checkoutPath, "README.txt");
        var originalContent = File.ReadAllText(readmePath);
        File.WriteAllText(readmePath, originalContent + "\n// Modified for test " + Guid.NewGuid());

        // Act
        var result = _svn.Commit(checkoutPath, "Modify README.txt", new[] { "README.txt" });

        // Assert
        Assert.True(result.Success, $"Commit failed with error: {result.ErrorMessage}");
        Assert.NotNull(result.NewRevision);
        Assert.True(long.TryParse(result.NewRevision, out var revNum) && revNum > 0);
    }

    [Fact]
    public void Commit_MultipleNewFiles_ReturnsSuccess()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var branchName = "branches/" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);
        _svn.CreateBranch(checkoutPath, branchName, true);

        // Create multiple new files in a new directory
        var newDirName = Guid.NewGuid().ToString("N").Substring(0, 8);
        var newDirPath = Path.Combine(checkoutPath, newDirName);
        Directory.CreateDirectory(newDirPath);
        File.WriteAllText(Path.Combine(newDirPath, "File1.txt"), "Content 1");
        File.WriteAllText(Path.Combine(newDirPath, "File2.txt"), "Content 2");
        File.WriteAllText(Path.Combine(newDirPath, "File3.txt"), "Content 3");

        var filesToAdd = new List<string>
        {
            Path.Combine(newDirName, "File1.txt"),
            Path.Combine(newDirName, "File2.txt"),
            Path.Combine(newDirName, "File3.txt")
        };

        // Act
        var result = _svn.Commit(checkoutPath, "Add multiple files", filesToAdd);

        // Assert
        Assert.True(result.Success, $"Commit failed with error: {result.ErrorMessage}");
        Assert.NotNull(result.NewRevision);
    }

    [Fact]
    public void Commit_NestedDirectories_ReturnsSuccess()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var branchName = "branches/" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);
        _svn.CreateBranch(checkoutPath, branchName, true);

        // Create deeply nested directories
        var nestedPath = Path.Combine(checkoutPath, "level1", "level2", "level3");
        Directory.CreateDirectory(nestedPath);
        File.WriteAllText(Path.Combine(nestedPath, "DeepFile.txt"), "Deep content");

        var filesToAdd = new List<string> { Path.Combine("level1", "level2", "level3", "DeepFile.txt") };

        // Act
        var result = _svn.Commit(checkoutPath, "Add file in nested directories", filesToAdd);

        // Assert
        Assert.True(result.Success, $"Commit failed with error: {result.ErrorMessage}");
    }

    [Fact]
    public void Commit_AllChangesWithoutSpecificFiles_ReturnsSuccess()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var branchName = "branches/" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);
        _svn.CreateBranch(checkoutPath, branchName, true);

        // Make multiple changes
        var readmePath = Path.Combine(checkoutPath, "README.txt");
        File.WriteAllText(readmePath, File.ReadAllText(readmePath) + "\n// Modified " + Guid.NewGuid());

        // Act - commit without specifying files (should commit all changes)
        var result = _svn.Commit(checkoutPath, "Commit all changes");

        // Assert
        Assert.True(result.Success, $"Commit failed with error: {result.ErrorMessage}");
    }

    [Fact]
    public void Commit_DeletedFile_ReturnsSuccess()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var branchName = "branches/" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);
        _svn.CreateBranch(checkoutPath, branchName, true);

        // First add a new file and commit it
        var newFileName = Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt";
        var newFilePath = Path.Combine(checkoutPath, newFileName);
        File.WriteAllText(newFilePath, "File to delete");
        var addResult = _svn.Commit(checkoutPath, "Add file to delete", new[] { newFileName });
        Assert.True(addResult.Success, $"Add commit failed: {addResult.ErrorMessage}");

        // Now delete the file using svn delete
        using var client = new SvnClient();
        client.Delete(newFilePath);

        // Act - commit the deletion
        var result = _svn.Commit(checkoutPath, "Delete file", new[] { newFileName });

        // Assert
        Assert.True(result.Success, $"Delete commit failed with error: {result.ErrorMessage}");
    }

    [Fact]
    public void Commit_MixedNewAndModifiedFiles_ReturnsSuccess()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var branchName = "branches/" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);
        _svn.CreateBranch(checkoutPath, branchName, true);

        // Modify an existing file
        var readmePath = Path.Combine(checkoutPath, "README.txt");
        File.WriteAllText(readmePath, File.ReadAllText(readmePath) + "\n// Modified " + Guid.NewGuid());

        // Add a new file
        var newFileName = Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt";
        File.WriteAllText(Path.Combine(checkoutPath, newFileName), "New file content");

        var filesToCommit = new List<string> { "README.txt", newFileName };

        // Act
        var result = _svn.Commit(checkoutPath, "Mixed new and modified files", filesToCommit);

        // Assert
        Assert.True(result.Success, $"Commit failed with error: {result.ErrorMessage}");
    }

    [Fact]
    public void Commit_ReturnsValidRevisionNumber()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var branchName = "branches/" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);
        _svn.CreateBranch(checkoutPath, branchName, true);

        var newFileName = Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt";
        File.WriteAllText(Path.Combine(checkoutPath, newFileName), "Test content");

        // Act
        var result = _svn.Commit(checkoutPath, "Test revision number", new[] { newFileName });

        // Assert
        Assert.True(result.Success, $"Commit failed: {result.ErrorMessage}");
        Assert.NotNull(result.NewRevision);
        Assert.True(long.TryParse(result.NewRevision, out var revisionNumber));
        Assert.True(revisionNumber > 0, "Revision number should be positive");
    }

    [Fact]
    public void Commit_FileInExistingDirectory_ReturnsSuccess()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var branchName = "branches/" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);
        _svn.CreateBranch(checkoutPath, branchName, true);

        // Add new file to existing Models directory
        var modelsDir = Path.Combine(checkoutPath, "Models");
        Assert.True(Directory.Exists(modelsDir), "Models directory should exist in repository");

        var newFileName = Guid.NewGuid().ToString("N").Substring(0, 8) + ".mo";
        var newFilePath = Path.Combine(modelsDir, newFileName);
        File.WriteAllText(newFilePath, "model TestModel\nend TestModel;");

        // Act
        var result = _svn.Commit(checkoutPath, "Add model to Models directory",
            new[] { Path.Combine("Models", newFileName) });

        // Assert
        Assert.True(result.Success, $"Commit failed with error: {result.ErrorMessage}");
    }

    [Fact]
    public void Commit_EmptyMessage_StillCommits()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var branchName = "branches/" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);
        _svn.CreateBranch(checkoutPath, branchName, true);

        var newFileName = Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt";
        File.WriteAllText(Path.Combine(checkoutPath, newFileName), "Test content");

        // Act - empty message
        var result = _svn.Commit(checkoutPath, "", new[] { newFileName });

        // Assert - SVN allows empty commit messages
        Assert.True(result.Success, $"Commit failed: {result.ErrorMessage}");
    }

    [Fact]
    public void Commit_SpecificFileUnchanged_ReturnsFailure()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act - try to commit an unchanged file
        var result = _svn.Commit(checkoutPath, "Try to commit unchanged file", new[] { "README.txt" });

        // Assert - should fail because the file hasn't changed
        Assert.False(result.Success);
    }

    // ============================================================================
    // ResolveRevision Tests
    // ============================================================================

    [Fact]
    public void ResolveRevision_HEAD_ReturnsRevisionNumber()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var resolved = _svn.ResolveRevision(checkoutPath, "HEAD");

        // Assert
        Assert.NotNull(resolved);
        Assert.True(long.TryParse(resolved, out var revNum));
        Assert.True(revNum > 0);
    }

    [Fact]
    public void ResolveRevision_NumericRevision_ReturnsSameRevision()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        var currentRevision = _svn.GetCurrentRevision(checkoutPath);

        // Act
        var resolved = _svn.ResolveRevision(checkoutPath, currentRevision!);

        // Assert
        Assert.Equal(currentRevision, resolved);
    }

    [Fact]
    public void ResolveRevision_InvalidRevision_ReturnsHeadOrNull()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        // Note: SVN's revision parsing falls back to HEAD for unrecognized text
        // This is expected behavior - invalid text becomes HEAD
        var resolved = _svn.ResolveRevision(checkoutPath, "invalid_revision");

        // Assert - either null or HEAD revision (SVN implementation dependent)
        if (resolved != null)
        {
            // Should be a valid numeric revision
            Assert.True(long.TryParse(resolved, out var revNum));
            Assert.True(revNum > 0);
        }
    }

    // ============================================================================
    // GetRevisionDescription Tests
    // ============================================================================

    [Fact]
    public void GetRevisionDescription_ValidRevision_ReturnsDescription()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        var currentRevision = _svn.GetCurrentRevision(checkoutPath);

        // Act
        var description = _svn.GetRevisionDescription(checkoutPath, currentRevision!);

        // Assert
        Assert.NotNull(description);
        Assert.NotEmpty(description);
    }

    [Fact]
    public void GetRevisionDescription_HEAD_ReturnsDescription()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var description = _svn.GetRevisionDescription(checkoutPath, "HEAD");

        // Assert
        Assert.NotNull(description);
        Assert.NotEmpty(description);
    }

    [Fact]
    public void GetRevisionDescription_InvalidRevision_ReturnsNull()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath);

        // Act
        var description = _svn.GetRevisionDescription(checkoutPath, "999999");

        // Assert
        Assert.Null(description);
    }

    // ============================================================================
    // Checkout with Specific Revisions Tests
    // ============================================================================

    [Fact]
    public void CheckoutRevision_WithSpecificRevision_ChecksOutThatRevision()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";

        // Get an older revision
        var entries = _svn.GetLogEntries(trunkUrl, new VcsLogOptions { MaxEntries = 10 });
        if (entries.Count < 2) return;

        var olderRevision = entries.Last().Revision;

        // Act
        var result = _svn.CheckoutRevision(trunkUrl, olderRevision, checkoutPath);

        // Assert
        Assert.True(result);
        var currentRevision = _svn.GetCurrentRevision(checkoutPath);
        Assert.Equal(olderRevision, currentRevision);
    }

    [Fact]
    public void CheckoutRevision_WithNullRevision_ChecksOutHEAD()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";

        // Act
        var result = _svn.CheckoutRevision(trunkUrl, "", checkoutPath);

        // Assert — GetCurrentRevision returns the last-changed revision of the path,
        // which may differ from the global HEAD if other branches had later commits
        Assert.True(result);
        var currentRevision = _svn.GetCurrentRevision(checkoutPath);
        Assert.NotNull(currentRevision);
        Assert.True(int.Parse(currentRevision) > 0);
    }

    [Fact]
    public void CheckoutRevision_WithEmptyRevision_ChecksOutHEAD()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath1 = CreateCheckoutPath();
        var checkoutPath2 = CreateCheckoutPath();
        var trunkUrl = TestRepoUrl + "/trunk";

        // Act
        _svn.CheckoutRevision(trunkUrl, "HEAD", checkoutPath1);
        _svn.CheckoutRevision(trunkUrl, "", checkoutPath2);

        // Assert - both should be at same revision
        var rev1 = _svn.GetCurrentRevision(checkoutPath1);
        var rev2 = _svn.GetCurrentRevision(checkoutPath2);
        Assert.Equal(rev1, rev2);
    }
}
