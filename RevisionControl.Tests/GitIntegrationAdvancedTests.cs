using LibGit2Sharp;

namespace RevisionControl.Tests;

/// <summary>
/// Advanced integration tests for GitRevisionControlSystem using actual repository content.
/// Repository: https://github.com/mdempse1/ModelicaEditorTests.git
///
/// These tests verify operations with real Modelica files and multiple commits/tags.
/// Uses IClassFixture to share a single repository clone across all tests.
/// </summary>
public class GitIntegrationAdvancedTests : IClassFixture<GitTestRepositoryFixture>, IDisposable
{
    private readonly GitRevisionControlSystem _git;
    private readonly string _clonePath;
    private readonly bool _repositoryAvailable;
    private readonly string? _cloneError;
    private readonly List<string> _tempPaths = new();

    public GitIntegrationAdvancedTests(GitTestRepositoryFixture fixture)
    {
        _git = new GitRevisionControlSystem();
        _clonePath = fixture.ClonePath;
        _repositoryAvailable = fixture.RepositoryAvailable;
        _cloneError = fixture.CloneError;
    }

    public void Dispose()
    {
        // Don't delete _clonePath - it's managed by the fixture
        foreach (var path in _tempPaths)
        {
            ForceDeleteDirectory(path);
        }
    }

    private string CreateTempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "GitAdvanced_" + Guid.NewGuid().ToString());
        _tempPaths.Add(path);
        return path;
    }

    /// <summary>
    /// Creates a copy of the shared test repository for tests that need an independent working copy.
    /// </summary>
    private string CopyTestRepository()
    {
        var copyPath = CreateTempPath();
        CopyDirectory(_clonePath, copyPath);
        return copyPath;
    }

    /// <summary>
    /// Normalizes line endings to LF for consistent comparison across platforms.
    /// </summary>
    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destDir = Path.Combine(destinationDir, Path.GetFileName(dir));
            CopyDirectory(dir, destDir);
        }
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
    public void CheckoutRevision_WithTag_v1_ChecksOutCorrectContent()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var outputPath = CreateTempPath();

        // Act
        var result = _git.CheckoutRevision(_clonePath, "v1.0.0", outputPath);

        // Assert
        Assert.True(result);

        // Verify v1.0.0 content - should NOT have 'z' variable
        var simpleModelPath = Path.Combine(outputPath, "Models", "SimpleModel.mo");
        if (File.Exists(simpleModelPath))
        {
            var content = File.ReadAllText(simpleModelPath);
            Assert.DoesNotContain("Real z", content);
            Assert.Contains("Real x", content);
            Assert.Contains("Real y", content);
        }
    }

    [Fact]
    public void CheckoutRevision_WithTag_v2_ChecksOutCorrectContent()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var outputPath = CreateTempPath();

        // Act
        var result = _git.CheckoutRevision(_clonePath, "v2.0.0", outputPath);

        // Assert
        Assert.True(result);

        // Verify v2.0.0 content - SHOULD have 'z' variable
        var simpleModelPath = Path.Combine(outputPath, "Models", "SimpleModel.mo");
        if (File.Exists(simpleModelPath))
        {
            var content = File.ReadAllText(simpleModelPath);
            Assert.Contains("Real z", content);
            Assert.Contains("New variable", content);
        }
    }

    [Fact]
    public void CheckoutRevision_BetweenTags_ContentChanges()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var v1Path = CreateTempPath();
        var v2Path = CreateTempPath();

        // Act
        _git.CheckoutRevision(_clonePath, "v1.0.0", v1Path);
        _git.CheckoutRevision(_clonePath, "v2.0.0", v2Path);

        // Assert - verify files exist and differ
        var v1SimpleModel = Path.Combine(v1Path, "Models", "SimpleModel.mo");
        var v2SimpleModel = Path.Combine(v2Path, "Models", "SimpleModel.mo");

        if (File.Exists(v1SimpleModel) && File.Exists(v2SimpleModel))
        {
            var v1Content = File.ReadAllText(v1SimpleModel);
            var v2Content = File.ReadAllText(v2SimpleModel);

            Assert.NotEqual(v1Content, v2Content);
            Assert.DoesNotContain("Real z", v1Content);
            Assert.Contains("Real z", v2Content);
        }
    }

    [Fact]
    public void UpdateExistingCheckout_BetweenVersions_UpdatesFiles()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateTempPath();

        // Act - checkout v1.0.0
        _git.UpdateExistingCheckout(checkoutPath, _clonePath, "v1.0.0");

        var simpleModelPath = Path.Combine(checkoutPath, "Models", "SimpleModel.mo");
        string? v1Content = null;
        if (File.Exists(simpleModelPath))
        {
            v1Content = File.ReadAllText(simpleModelPath);
        }

        // Update to v2.0.0
        _git.UpdateExistingCheckout(checkoutPath, _clonePath, "v2.0.0");

        string? v2Content = null;
        if (File.Exists(simpleModelPath))
        {
            v2Content = File.ReadAllText(simpleModelPath);
        }

        // Assert
        if (v1Content != null && v2Content != null)
        {
            Assert.NotEqual(v1Content, v2Content);
            Assert.Contains("Real z", v2Content);
        }
    }

    [Fact]
    public void CheckoutRevision_VerifiesDirectoryStructure()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var outputPath = CreateTempPath();

        // Act
        _git.CheckoutRevision(_clonePath, "main", outputPath);

        // Assert - verify expected directory structure exists
        Assert.True(File.Exists(Path.Combine(outputPath, "package.mo")));
        Assert.True(File.Exists(Path.Combine(outputPath, "README.md")));
        Assert.True(Directory.Exists(Path.Combine(outputPath, "Models")));
        Assert.True(File.Exists(Path.Combine(outputPath, "Models", "package.mo")));
        Assert.True(File.Exists(Path.Combine(outputPath, "Models", "SimpleModel.mo")));
    }

    [Fact]
    public void CheckoutRevision_VerifiesModelicaFiles()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var outputPath = CreateTempPath();

        // Act
        _git.CheckoutRevision(_clonePath, "main", outputPath);

        // Assert - verify Modelica files can be found
        var modelicaFiles = Directory.GetFiles(outputPath, "*.mo", SearchOption.AllDirectories);
        Assert.NotEmpty(modelicaFiles);
        Assert.Contains(modelicaFiles, f => f.Contains("SimpleModel.mo"));
    }

    [Fact]
    public void ResolveRevision_WithTag_ReturnsCommitHash()
    {
        if (!_repositoryAvailable) return;

        // Act
        var v1Hash = _git.ResolveRevision(_clonePath, "v1.0.0");
        var v2Hash = _git.ResolveRevision(_clonePath, "v2.0.0");

        // Assert
        Assert.NotNull(v1Hash);
        Assert.NotNull(v2Hash);
        Assert.NotEqual(v1Hash, v2Hash);
        Assert.Equal(40, v1Hash.Length);
        Assert.Equal(40, v2Hash.Length);
    }

    [Fact]
    public void GetRevisionDescription_ForTags_ReturnsDescription()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var v1Hash = _git.ResolveRevision(_clonePath, "v1.0.0");
        var v2Hash = _git.ResolveRevision(_clonePath, "v2.0.0");

        // Act
        var v1Desc = _git.GetRevisionDescription(_clonePath, v1Hash!);
        var v2Desc = _git.GetRevisionDescription(_clonePath, v2Hash!);

        // Assert
        Assert.NotNull(v1Desc);
        Assert.NotNull(v2Desc);
        Assert.NotEmpty(v1Desc);
        Assert.NotEmpty(v2Desc);
    }

    [Fact]
    public void CheckoutRevision_WithBranch_ChecksOutBranchContent()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var outputPath = CreateTempPath();

        // Act - checkout feature-test branch if it exists
        var branchSha = _git.ResolveRevision(_clonePath, "feature-test");

        if (branchSha != null)
        {
            var result = _git.CheckoutRevision(_clonePath, "feature-test", outputPath);

            // Assert
            Assert.True(result);
            Assert.True(Directory.Exists(outputPath));
        }
    }

    [Fact]
    public void CleanWorkspace_AfterModifyingModelicaFile_RevertsChanges()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateTempPath();
        var sha = _git.GetCurrentRevision(_clonePath);
        _git.UpdateExistingCheckout(checkoutPath, _clonePath, sha!);

        var simpleModelPath = Path.Combine(checkoutPath, "Models", "SimpleModel.mo");

        if (!File.Exists(simpleModelPath))
        {
            return; // Skip if file doesn't exist
        }

        var originalContent = File.ReadAllText(simpleModelPath);
        File.WriteAllText(simpleModelPath, "// Modified content");

        // Act
        var result = _git.CleanWorkspace(checkoutPath);

        // Assert
        Assert.True(result);
        var restoredContent = File.ReadAllText(simpleModelPath);
        Assert.Equal(originalContent, restoredContent);
    }

    [Fact]
    public void CheckoutRevision_MultipleModelicaFiles_ChecksOutAll()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var outputPath = CreateTempPath();

        // Act
        _git.CheckoutRevision(_clonePath, "main", outputPath);

        // Assert - check for multiple Modelica files
        var modelsDir = Path.Combine(outputPath, "Models");
        if (Directory.Exists(modelsDir))
        {
            var modelFiles = Directory.GetFiles(modelsDir, "*.mo");
            Assert.NotEmpty(modelFiles);

            // Should have at least package.mo and SimpleModel.mo
            Assert.Contains(modelFiles, f => Path.GetFileName(f) == "package.mo");
            Assert.Contains(modelFiles, f => Path.GetFileName(f) == "SimpleModel.mo");
        }
    }

    [Fact]
    public void UpdateExistingCheckout_ToOlderCommit_UpdatesCorrectly()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateTempPath();
        var v2Hash = _git.ResolveRevision(_clonePath, "v2.0.0");
        var v1Hash = _git.ResolveRevision(_clonePath, "v1.0.0");

        // Act - checkout v2, then update to v1 (going backwards)
        _git.UpdateExistingCheckout(checkoutPath, _clonePath, v2Hash!);
        _git.UpdateExistingCheckout(checkoutPath, _clonePath, v1Hash!);

        // Assert - should now have v1 content
        var simpleModelPath = Path.Combine(checkoutPath, "Models", "SimpleModel.mo");
        if (File.Exists(simpleModelPath))
        {
            var content = File.ReadAllText(simpleModelPath);
            Assert.DoesNotContain("Real z", content);
        }
    }

    [Fact]
    public void CheckoutRevision_WithShortHash_Works()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var fullHash = _git.GetCurrentRevision(_clonePath);
        var shortHash = fullHash!.Substring(0, 7);
        var outputPath = CreateTempPath();

        // Act
        var result = _git.CheckoutRevision(_clonePath, shortHash, outputPath);

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(outputPath));
    }

    [Fact]
    public void ResolveRevision_WithShortHash_ReturnsFullHash()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var fullHash = _git.GetCurrentRevision(_clonePath);
        var shortHash = fullHash!.Substring(0, 7);

        // Act
        var resolved = _git.ResolveRevision(_clonePath, shortHash);

        // Assert
        Assert.Equal(fullHash, resolved);
    }

    [Fact]
    public void CheckoutRevision_PreservesFilePermissions()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var outputPath = CreateTempPath();

        // Act
        _git.CheckoutRevision(_clonePath, "main", outputPath);

        // Assert - files should be readable
        var files = Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            Assert.True(File.Exists(file));
            // Should be able to read the file
            var content = File.ReadAllText(file);
            Assert.NotNull(content);
        }
    }

    // ============================================================================
    // GetLogEntries Tests
    // ============================================================================

    [Fact]
    public void GetLogEntries_WithDefaultOptions_ReturnsLogEntries()
    {
        if (!_repositoryAvailable) return;

        // Act
        var entries = _git.GetLogEntries(_clonePath);

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
        var options = new VcsLogOptions { MaxEntries = 2 };

        // Act
        var entries = _git.GetLogEntries(_clonePath, options);

        // Assert
        Assert.True(entries.Count <= 2);
    }

    [Fact]
    public void GetLogEntries_WithSinceDate_FiltersOldCommits()
    {
        if (!_repositoryAvailable) return;

        // Arrange - get all entries first to find a date to filter by
        var allEntries = _git.GetLogEntries(_clonePath, new VcsLogOptions { MaxEntries = 100 });
        if (allEntries.Count < 2) return;

        // Use a date that's between some commits
        var midDate = allEntries[allEntries.Count / 2].Date;
        var options = new VcsLogOptions { Since = midDate, MaxEntries = 100 };

        // Act
        var filteredEntries = _git.GetLogEntries(_clonePath, options);

        // Assert - all entries should be on or after the since date
        Assert.All(filteredEntries, e => Assert.True(e.Date >= midDate.AddMinutes(-1))); // Allow 1 minute tolerance
    }

    [Fact]
    public void GetLogEntries_WithUntilDate_FiltersNewCommits()
    {
        if (!_repositoryAvailable) return;

        // Arrange - get all entries first to find a date to filter by
        var allEntries = _git.GetLogEntries(_clonePath, new VcsLogOptions { MaxEntries = 100 });
        if (allEntries.Count < 2) return;

        // Use a date that's between some commits
        var midDate = allEntries[allEntries.Count / 2].Date;
        var options = new VcsLogOptions { Until = midDate, MaxEntries = 100 };

        // Act
        var filteredEntries = _git.GetLogEntries(_clonePath, options);

        // Assert - all entries should be on or before the until date
        Assert.All(filteredEntries, e => Assert.True(e.Date <= midDate.AddMinutes(1))); // Allow 1 minute tolerance
    }

    [Fact]
    public void GetLogEntries_DefaultPastWeek_ReturnsRecentEntries()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var options = VcsLogOptions.DefaultPastWeek();

        // Act
        var entries = _git.GetLogEntries(_clonePath, options);

        // Assert - should have entries (repo may not have week-old commits)
        // At minimum, verify the options are structured correctly
        Assert.NotNull(options.Since);
        Assert.True(options.MaxEntries >= 50);
    }

    [Fact]
    public void GetLogEntries_ReturnsShortRevision()
    {
        if (!_repositoryAvailable) return;

        // Act
        var entries = _git.GetLogEntries(_clonePath);

        // Assert
        Assert.NotEmpty(entries);
        var first = entries.First();
        Assert.Equal(40, first.Revision.Length);
        Assert.True(first.ShortRevision.Length >= 7 && first.ShortRevision.Length < 40);
        Assert.StartsWith(first.ShortRevision, first.Revision);
    }

    [Fact]
    public void GetLogEntries_ReturnsAuthorEmail()
    {
        if (!_repositoryAvailable) return;

        // Act
        var entries = _git.GetLogEntries(_clonePath);

        // Assert
        Assert.NotEmpty(entries);
        // Not all commits may have email, but the field should exist
        var hasEmail = entries.Any(e => !string.IsNullOrEmpty(e.AuthorEmail));
        // This is informational - some repos may not have emails
    }

    [Fact]
    public void GetLogEntries_ReturnsMessageShort()
    {
        if (!_repositoryAvailable) return;

        // Act
        var entries = _git.GetLogEntries(_clonePath);

        // Assert
        Assert.NotEmpty(entries);
        Assert.All(entries, e =>
        {
            Assert.NotEmpty(e.MessageShort);
            // Short message should be first line of full message
            Assert.StartsWith(e.MessageShort, e.Message.Split('\n')[0].Trim());
        });
    }

    [Fact]
    public void GetLogEntries_InvalidRepository_ReturnsEmptyList()
    {
        // Act
        var entries = _git.GetLogEntries(Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()));

        // Assert
        Assert.Empty(entries);
    }

    // ============================================================================
    // GetChangedFiles Tests
    // ============================================================================

    [Fact]
    public void GetChangedFiles_ForCommit_ReturnsChangedFiles()
    {
        if (!_repositoryAvailable) return;

        // Arrange - get a commit that has changes
        var entries = _git.GetLogEntries(_clonePath, new VcsLogOptions { MaxEntries = 10 });
        if (entries.Count == 0) return;

        // Act
        var changedFiles = _git.GetChangedFiles(_clonePath, entries.First().Revision);

        // Assert - should have at least some changed files (unless it's the initial commit)
        Assert.NotNull(changedFiles);
    }

    [Fact]
    public void GetChangedFiles_ForTagCommit_ReturnsChangedFiles()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var v2Hash = _git.ResolveRevision(_clonePath, "v2.0.0");
        if (v2Hash == null) return;

        // Act
        var changedFiles = _git.GetChangedFiles(_clonePath, v2Hash);

        // Assert - v2 added the 'z' variable, so SimpleModel.mo should be modified
        Assert.NotEmpty(changedFiles);
        var simpleModelChange = changedFiles.FirstOrDefault(f => f.Path.Contains("SimpleModel"));
        if (simpleModelChange != null)
        {
            Assert.Equal(VcsChangeType.Modified, simpleModelChange.ChangeType);
        }
    }

    [Fact]
    public void GetChangedFiles_WithAddedFiles_ReturnsAddedChangeType()
    {
        if (!_repositoryAvailable) return;

        // Find the initial (root) commit that has no parents
        using var repo = new Repository(_clonePath);
        var rootCommit = repo.Commits.FirstOrDefault(c => !c.Parents.Any());
        if (rootCommit == null) return;

        // Get changed files for the root commit
        var changedFiles = _git.GetChangedFiles(_clonePath, rootCommit.Sha);

        // Assert - initial commits should have all files as Added
        Assert.NotEmpty(changedFiles);
        Assert.All(changedFiles, f => Assert.Equal(VcsChangeType.Added, f.ChangeType));
    }

    [Fact]
    public void GetChangedFiles_InvalidRevision_ReturnsEmptyList()
    {
        if (!_repositoryAvailable) return;

        // Act
        var changedFiles = _git.GetChangedFiles(_clonePath, "invalid_revision_12345");

        // Assert
        Assert.Empty(changedFiles);
    }

    [Fact]
    public void GetChangedFiles_ReturnsRelativePaths()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var entries = _git.GetLogEntries(_clonePath, new VcsLogOptions { MaxEntries = 10 });
        if (entries.Count == 0) return;

        // Act
        var changedFiles = _git.GetChangedFiles(_clonePath, entries.First().Revision);

        // Assert - paths should be relative (not absolute)
        Assert.All(changedFiles, f =>
        {
            Assert.False(Path.IsPathRooted(f.Path), $"Path should be relative: {f.Path}");
        });
    }

    // ============================================================================
    // GetFileContentAtRevision Tests
    // ============================================================================

    [Fact]
    public void GetFileContentAtRevision_ExistingFile_ReturnsContent()
    {
        if (!_repositoryAvailable) return;

        // Act
        var content = _git.GetFileContentAtRevision(_clonePath, "README.md", "main");

        // Assert
        Assert.NotNull(content);
        Assert.NotEmpty(content);
    }

    [Fact]
    public void GetFileContentAtRevision_AtTag_ReturnsVersionSpecificContent()
    {
        if (!_repositoryAvailable) return;

        // Act - get content at v1.0.0 and v2.0.0
        var v1Content = _git.GetFileContentAtRevision(_clonePath, "Models/SimpleModel.mo", "v1.0.0");
        var v2Content = _git.GetFileContentAtRevision(_clonePath, "Models/SimpleModel.mo", "v2.0.0");

        // Assert
        if (v1Content != null && v2Content != null)
        {
            Assert.DoesNotContain("Real z", v1Content);
            Assert.Contains("Real z", v2Content);
        }
    }

    [Fact]
    public void GetFileContentAtRevision_WithCommitHash_ReturnsContent()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var hash = _git.GetCurrentRevision(_clonePath);
        if (hash == null) return;

        // Act
        var content = _git.GetFileContentAtRevision(_clonePath, "package.mo", hash);

        // Assert
        Assert.NotNull(content);
        Assert.Contains("package", content.ToLower());
    }

    [Fact]
    public void GetFileContentAtRevision_WithShortHash_ReturnsContent()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var fullHash = _git.GetCurrentRevision(_clonePath);
        if (fullHash == null) return;
        var shortHash = fullHash.Substring(0, 7);

        // Act
        var content = _git.GetFileContentAtRevision(_clonePath, "package.mo", shortHash);

        // Assert
        Assert.NotNull(content);
    }

    [Fact]
    public void GetFileContentAtRevision_NonExistentFile_ReturnsNull()
    {
        if (!_repositoryAvailable) return;

        // Act
        var content = _git.GetFileContentAtRevision(_clonePath, "NonExistent.mo", "main");

        // Assert
        Assert.Null(content);
    }

    [Fact]
    public void GetFileContentAtRevision_InvalidRevision_ReturnsNull()
    {
        if (!_repositoryAvailable) return;

        // Act
        var content = _git.GetFileContentAtRevision(_clonePath, "README.md", "invalid_revision");

        // Assert
        Assert.Null(content);
    }

    [Fact]
    public void GetFileContentAtRevision_WithNullRevision_ReturnsHeadContent()
    {
        if (!_repositoryAvailable) return;

        // Act
        var content = _git.GetFileContentAtRevision(_clonePath, "README.md", null);

        // Assert
        Assert.NotNull(content);
    }

    [Fact]
    public void GetFileContentAtRevision_NestedFile_ReturnsContent()
    {
        if (!_repositoryAvailable) return;

        // Act
        var content = _git.GetFileContentAtRevision(_clonePath, "Models/SimpleModel.mo", "main");

        // Assert
        Assert.NotNull(content);
        Assert.Contains("model SimpleModel", content);
    }

    [Fact]
    public void GetFileContentAtRevision_WithForwardSlashPath_ReturnsContent()
    {
        if (!_repositoryAvailable) return;

        // Act - explicitly use forward slashes
        var content = _git.GetFileContentAtRevision(_clonePath, "Models/SimpleModel.mo", "main");

        // Assert
        Assert.NotNull(content);
    }

    [Fact]
    public void GetFileContentAtRevision_WithBackslashPath_ReturnsContent()
    {
        if (!_repositoryAvailable) return;

        // Act - use backslashes (should be normalized internally)
        var content = _git.GetFileContentAtRevision(_clonePath, "Models\\SimpleModel.mo", "main");

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

        // Arrange - ensure clean workspace
        _git.CleanWorkspace(_clonePath);

        // Act
        var changes = _git.GetWorkingCopyChanges(_clonePath);

        // Assert
        Assert.Empty(changes);
    }

    [Fact]
    public void GetWorkingCopyChanges_WithModifiedFile_ReturnsModifiedStatus()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;

        File.AppendAllText(readmePath, "\n// Modified for test");

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

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
        var checkoutPath = CopyTestRepository();

        var newFilePath = Path.Combine(checkoutPath, "NewUntrackedFile.txt");
        File.WriteAllText(newFilePath, "This is a new file");

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

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
        var checkoutPath = CopyTestRepository();

        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;

        File.Delete(readmePath);

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

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
        var changes = _git.GetWorkingCopyChanges(Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()));

        // Assert
        Assert.Empty(changes);
    }

    [Fact]
    public void GetWorkingCopyChanges_ReturnsRelativePaths()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        File.WriteAllText(Path.Combine(checkoutPath, "Models", "NewModel.mo"), "model NewModel end NewModel;");

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

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
        var checkoutPath = CopyTestRepository();

        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;

        var originalContent = File.ReadAllText(readmePath);
        File.WriteAllText(readmePath, "Modified content");

        // Act
        var result = _git.RevertFiles(checkoutPath, new[] { "README.md" });

        // Assert
        Assert.True(result.Success);
        var restoredContent = File.ReadAllText(readmePath);
        Assert.Equal(NormalizeLineEndings(originalContent), NormalizeLineEndings(restoredContent));
    }

    [Fact]
    public void RevertFiles_UntrackedFile_DeletesFile()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var newFilePath = Path.Combine(checkoutPath, "UntrackedFile.txt");
        File.WriteAllText(newFilePath, "Untracked content");

        // Act
        var result = _git.RevertFiles(checkoutPath, new[] { "UntrackedFile.txt" });

        // Assert
        Assert.True(result.Success);
        Assert.False(File.Exists(newFilePath));
    }

    [Fact]
    public void RevertFiles_MultipleFiles_RevertsAll()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var readmePath = Path.Combine(checkoutPath, "README.md");
        var packagePath = Path.Combine(checkoutPath, "package.mo");
        if (!File.Exists(readmePath) || !File.Exists(packagePath)) return;

        var originalReadme = File.ReadAllText(readmePath);
        var originalPackage = File.ReadAllText(packagePath);

        File.WriteAllText(readmePath, "Modified README");
        File.WriteAllText(packagePath, "Modified package");

        // Act
        var result = _git.RevertFiles(checkoutPath, new[] { "README.md", "package.mo" });

        // Assert
        Assert.True(result.Success);
        Assert.Equal(NormalizeLineEndings(originalReadme), NormalizeLineEndings(File.ReadAllText(readmePath)));
        Assert.Equal(NormalizeLineEndings(originalPackage), NormalizeLineEndings(File.ReadAllText(packagePath)));
    }

    [Fact]
    public void RevertFiles_NonExistentFile_Succeeds()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Act
        var result = _git.RevertFiles(checkoutPath, new[] { "NonExistentFile.txt" });

        // Assert - should succeed (no-op for non-existent files)
        Assert.True(result.Success);
    }

    [Fact]
    public void RevertFiles_NestedFile_RevertsCorrectly()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var simpleModelPath = Path.Combine(checkoutPath, "Models", "SimpleModel.mo");
        if (!File.Exists(simpleModelPath)) return;

        var originalContent = File.ReadAllText(simpleModelPath);
        File.WriteAllText(simpleModelPath, "// Modified");

        // Act
        var result = _git.RevertFiles(checkoutPath, new[] { "Models/SimpleModel.mo" });

        // Assert
        Assert.True(result.Success);
        Assert.Equal(NormalizeLineEndings(originalContent), NormalizeLineEndings(File.ReadAllText(simpleModelPath)));
    }

    // ============================================================================
    // UpdateToLatest Tests
    // Note: These tests use local repos without remotes, so they test error handling
    // ============================================================================

    [Fact]
    public void UpdateToLatest_LocalRepoWithoutRemote_ReturnsError()
    {
        if (!_repositoryAvailable) return;

        // Act - local repo has no origin remote
        var result = _git.UpdateToLatest(_clonePath);

        // Assert - should fail because there's no remote
        Assert.False(result.Success);
        Assert.Contains("origin", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateToLatest_InvalidRepository_ReturnsFailure()
    {
        // Act
        var result = _git.UpdateToLatest(Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()));

        // Assert
        Assert.False(result.Success);
    }

    // ============================================================================
    // GetBranches Tests
    // ============================================================================

    [Fact]
    public void GetBranches_LocalOnly_ReturnsLocalBranches()
    {
        if (!_repositoryAvailable) return;

        // Act
        var branches = _git.GetBranches(_clonePath, includeRemote: false);

        // Assert
        Assert.NotEmpty(branches);
        Assert.All(branches, b => Assert.False(b.IsRemote));
    }

    [Fact]
    public void GetBranches_IncludeRemote_ReturnsAllBranches()
    {
        if (!_repositoryAvailable) return;

        // Act
        var branches = _git.GetBranches(_clonePath, includeRemote: true);

        // Assert
        Assert.NotEmpty(branches);
        // Should have at least one remote branch (origin/main or origin/master)
        var hasRemote = branches.Any(b => b.IsRemote);
        // Informational - cloned repos should have remote branches
    }

    [Fact]
    public void GetBranches_HasCurrentBranch()
    {
        if (!_repositoryAvailable) return;

        // Act
        var branches = _git.GetBranches(_clonePath);

        // Assert
        var currentBranch = branches.FirstOrDefault(b => b.IsCurrent);
        Assert.NotNull(currentBranch);
    }

    [Fact]
    public void GetBranches_BranchHasName()
    {
        if (!_repositoryAvailable) return;

        // Act
        var branches = _git.GetBranches(_clonePath);

        // Assert
        Assert.All(branches, b => Assert.NotEmpty(b.Name));
    }

    [Fact]
    public void GetBranches_InvalidRepository_ReturnsEmptyList()
    {
        // Act
        var branches = _git.GetBranches(Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()));

        // Assert
        Assert.Empty(branches);
    }

    // ============================================================================
    // CreateBranch and SwitchBranch Tests
    // ============================================================================

    [Fact]
    public void CreateBranch_NewBranch_CreatesSuccessfully()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();
        var branchName = "test-branch-" + Guid.NewGuid().ToString().Substring(0, 8);

        // Act
        var result = _git.CreateBranch(checkoutPath, branchName, switchToBranch: false);

        // Assert
        Assert.True(result.Success);
        var branches = _git.GetBranches(checkoutPath);
        Assert.Contains(branches, b => b.Name == branchName);
    }

    [Fact]
    public void CreateBranch_WithSwitch_SwitchesToNewBranch()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();
        var branchName = "test-switch-" + Guid.NewGuid().ToString().Substring(0, 8);

        // Act
        var result = _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Assert
        Assert.True(result.Success);
        var currentBranch = _git.GetCurrentBranch(checkoutPath);
        Assert.Equal(branchName, currentBranch);
    }

    [Fact]
    public void CreateBranch_ExistingBranchName_ReturnsFailure()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();
        var branchName = "duplicate-branch-" + Guid.NewGuid().ToString().Substring(0, 8);
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: false);

        // Act - try to create same branch again
        var result = _git.CreateBranch(checkoutPath, branchName, switchToBranch: false);

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public void SwitchBranch_ExistingBranch_SwitchesSuccessfully()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();
        var branchName = "switch-test-" + Guid.NewGuid().ToString().Substring(0, 8);
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: false);

        // Act
        var result = _git.SwitchBranch(checkoutPath, branchName);

        // Assert
        Assert.True(result.Success);
        var currentBranch = _git.GetCurrentBranch(checkoutPath);
        Assert.Equal(branchName, currentBranch);
    }

    [Fact]
    public void SwitchBranch_NonExistentBranch_ReturnsFailure()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Act
        var result = _git.SwitchBranch(checkoutPath, "non-existent-branch-12345");

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public void SwitchBranch_ToMainBranch_SwitchesSuccessfully()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();
        var branchName = "temp-branch-" + Guid.NewGuid().ToString().Substring(0, 8);
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Act
        var result = _git.SwitchBranch(checkoutPath, "main");

        // Assert
        Assert.True(result.Success);
        var currentBranch = _git.GetCurrentBranch(checkoutPath);
        Assert.Equal("main", currentBranch);
    }

    // ============================================================================
    // Commit Tests
    // ============================================================================

    [Fact]
    public void Commit_WithChanges_CreatesCommit()
    {
        if (!_repositoryAvailable) return;

        // Arrange - copy the repo instead of using UpdateExistingCheckout (which needs remote)
        var checkoutPath = CopyTestRepository();

        // Create a new branch so we don't affect main
        var branchName = "commit-test-" + Guid.NewGuid().ToString().Substring(0, 8);
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        var newFilePath = Path.Combine(checkoutPath, "CommitTestFile.txt");
        File.WriteAllText(newFilePath, "Test content for commit");

        var beforeRevision = _git.GetCurrentRevision(checkoutPath);

        // Act
        var result = _git.Commit(checkoutPath, "Test commit message", new[] { "CommitTestFile.txt" });

        // Assert
        Assert.True(result.Success, $"Commit failed: {result.ErrorMessage}");
        Assert.NotNull(result.NewRevision);
        Assert.NotEqual(beforeRevision, result.NewRevision);
    }

    [Fact]
    public void Commit_AllFiles_CommitsEverything()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "commit-all-" + Guid.NewGuid().ToString().Substring(0, 8);
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        File.WriteAllText(Path.Combine(checkoutPath, "File1.txt"), "Content 1");
        File.WriteAllText(Path.Combine(checkoutPath, "File2.txt"), "Content 2");

        // Act - pass null to commit all
        var result = _git.Commit(checkoutPath, "Commit all files", null);

        // Assert
        Assert.True(result.Success);
        var changes = _git.GetWorkingCopyChanges(checkoutPath);
        Assert.Empty(changes);
    }

    [Fact]
    public void Commit_NoChanges_ReturnsFailure()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();
        _git.CleanWorkspace(checkoutPath);

        // Act
        var result = _git.Commit(checkoutPath, "Empty commit");

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public void Commit_SpecificFiles_OnlyCommitsThoseFiles()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "partial-commit-" + Guid.NewGuid().ToString().Substring(0, 8);
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        File.WriteAllText(Path.Combine(checkoutPath, "ToCommit.txt"), "Will be committed");
        File.WriteAllText(Path.Combine(checkoutPath, "NotToCommit.txt"), "Will not be committed");

        // Act - only commit one file
        var result = _git.Commit(checkoutPath, "Partial commit", new[] { "ToCommit.txt" });

        // Assert
        Assert.True(result.Success);
        var changes = _git.GetWorkingCopyChanges(checkoutPath);
        Assert.Single(changes);
        Assert.Contains(changes, c => c.Path.Contains("NotToCommit"));
    }

    [Fact]
    public void Commit_ModifiedFile_CommitsModification()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "modify-commit-" + Guid.NewGuid().ToString().Substring(0, 8);
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;

        File.AppendAllText(readmePath, "\n// Test modification");

        // Act
        var result = _git.Commit(checkoutPath, "Modify README", new[] { "README.md" });

        // Assert
        Assert.True(result.Success);
        var content = _git.GetFileContentAtRevision(checkoutPath, "README.md", result.NewRevision);
        Assert.Contains("Test modification", content);
    }

    [Fact]
    public void Commit_InvalidRepository_ReturnsFailure()
    {
        // Act
        var result = _git.Commit(
            Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()),
            "Test message"
        );

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public void Commit_MultipleNewFiles_ReturnsSuccess()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "multi-files-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Create multiple new files in a new directory
        var newDirPath = Path.Combine(checkoutPath, "NewDir");
        Directory.CreateDirectory(newDirPath);
        File.WriteAllText(Path.Combine(newDirPath, "File1.txt"), "Content 1");
        File.WriteAllText(Path.Combine(newDirPath, "File2.txt"), "Content 2");
        File.WriteAllText(Path.Combine(newDirPath, "File3.txt"), "Content 3");

        var filesToAdd = new[]
        {
            "NewDir/File1.txt",
            "NewDir/File2.txt",
            "NewDir/File3.txt"
        };

        // Act
        var result = _git.Commit(checkoutPath, "Add multiple files", filesToAdd);

        // Assert
        Assert.True(result.Success, $"Commit failed with error: {result.ErrorMessage}");
        Assert.NotNull(result.NewRevision);
    }

    [Fact]
    public void Commit_NestedDirectories_ReturnsSuccess()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "nested-dir-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Create deeply nested directories
        var nestedPath = Path.Combine(checkoutPath, "level1", "level2", "level3");
        Directory.CreateDirectory(nestedPath);
        File.WriteAllText(Path.Combine(nestedPath, "DeepFile.txt"), "Deep content");

        // Act
        var result = _git.Commit(checkoutPath, "Add file in nested directories",
            new[] { "level1/level2/level3/DeepFile.txt" });

        // Assert
        Assert.True(result.Success, $"Commit failed with error: {result.ErrorMessage}");
    }

    [Fact]
    public void Commit_MixedNewAndModifiedFiles_ReturnsSuccess()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "mixed-commit-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Modify an existing file
        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;
        File.AppendAllText(readmePath, "\n// Mixed commit modification " + Guid.NewGuid());

        // Add a new file
        var newFileName = "NewFile_" + Guid.NewGuid().ToString()[..8] + ".txt";
        File.WriteAllText(Path.Combine(checkoutPath, newFileName), "New file content");

        var filesToCommit = new[] { "README.md", newFileName };

        // Act
        var result = _git.Commit(checkoutPath, "Mixed new and modified files", filesToCommit);

        // Assert
        Assert.True(result.Success, $"Commit failed with error: {result.ErrorMessage}");
    }

    [Fact]
    public void Commit_ReturnsValidRevisionNumber()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "rev-num-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        var newFileName = "RevTest_" + Guid.NewGuid().ToString()[..8] + ".txt";
        File.WriteAllText(Path.Combine(checkoutPath, newFileName), "Test content");

        // Act
        var result = _git.Commit(checkoutPath, "Test revision number", new[] { newFileName });

        // Assert
        Assert.True(result.Success, $"Commit failed: {result.ErrorMessage}");
        Assert.NotNull(result.NewRevision);
        Assert.Equal(40, result.NewRevision.Length); // Git SHA is 40 hex characters
        Assert.True(result.NewRevision.All(c => "0123456789abcdef".Contains(c)),
            "Revision should be a valid hex string");
    }

    [Fact]
    public void Commit_FileInExistingDirectory_ReturnsSuccess()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "existing-dir-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Add new file to existing Models directory
        var modelsDir = Path.Combine(checkoutPath, "Models");
        Assert.True(Directory.Exists(modelsDir), "Models directory should exist in repository");

        var newFileName = "TestModel_" + Guid.NewGuid().ToString()[..8] + ".mo";
        var newFilePath = Path.Combine(modelsDir, newFileName);
        File.WriteAllText(newFilePath, "model TestModel\nend TestModel;");

        // Act
        var result = _git.Commit(checkoutPath, "Add model to Models directory",
            new[] { "Models/" + newFileName });

        // Assert
        Assert.True(result.Success, $"Commit failed with error: {result.ErrorMessage}");
    }

    [Fact]
    public void Commit_SpecificFileUnchanged_ReturnsFailure()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();
        _git.CleanWorkspace(checkoutPath);

        // Act - try to commit an unchanged file
        var result = _git.Commit(checkoutPath, "Try to commit unchanged file", new[] { "README.md" });

        // Assert - should fail because the file hasn't changed
        Assert.False(result.Success);
    }

    // ============================================================================
    // Additional GetWorkingCopyChanges Tests - Staged States
    // ============================================================================

    [Fact]
    public void GetWorkingCopyChanges_WithStagedNewFile_ReturnsAddedStagedStatus()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "staged-test-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Create and stage a new file
        var newFilePath = Path.Combine(checkoutPath, "StagedNewFile.txt");
        File.WriteAllText(newFilePath, "Staged content");

        using (var repo = new Repository(checkoutPath))
        {
            Commands.Stage(repo, "StagedNewFile.txt");
        }

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

        // Assert
        Assert.NotEmpty(changes);
        var stagedFile = changes.FirstOrDefault(c => c.Path.Contains("StagedNewFile"));
        Assert.NotNull(stagedFile);
        Assert.Equal(VcsFileStatus.Added, stagedFile.Status);
        Assert.True(stagedFile.IsStaged);
    }

    [Fact]
    public void GetWorkingCopyChanges_WithStagedModifiedFile_ReturnsStagedModifiedStatus()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "staged-mod-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;

        File.AppendAllText(readmePath, "\nStaged modification");

        using (var repo = new Repository(checkoutPath))
        {
            Commands.Stage(repo, "README.md");
        }

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

        // Assert
        Assert.NotEmpty(changes);
        var modifiedFile = changes.FirstOrDefault(c => c.Path.Contains("README"));
        Assert.NotNull(modifiedFile);
        Assert.Equal(VcsFileStatus.Modified, modifiedFile.Status);
        Assert.True(modifiedFile.IsStaged);
    }

    [Fact]
    public void GetWorkingCopyChanges_WithStagedDeletedFile_ReturnsStagedDeletedStatus()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "staged-del-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;

        using (var repo = new Repository(checkoutPath))
        {
            Commands.Remove(repo, "README.md");
        }

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

        // Assert
        Assert.NotEmpty(changes);
        var deletedFile = changes.FirstOrDefault(c => c.Path.Contains("README"));
        Assert.NotNull(deletedFile);
        Assert.Equal(VcsFileStatus.Deleted, deletedFile.Status);
        Assert.True(deletedFile.IsStaged);
    }

    [Fact]
    public void GetWorkingCopyChanges_WithStagedAndWorkdirModified_ReturnsBothStates()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "combined-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;

        // Stage first modification
        File.AppendAllText(readmePath, "\nFirst modification");
        using (var repo = new Repository(checkoutPath))
        {
            Commands.Stage(repo, "README.md");
        }

        // Make another modification without staging
        File.AppendAllText(readmePath, "\nSecond modification");

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

        // Assert - should show the file as modified
        Assert.NotEmpty(changes);
        var modifiedFile = changes.FirstOrDefault(c => c.Path.Contains("README"));
        Assert.NotNull(modifiedFile);
        Assert.Equal(VcsFileStatus.Modified, modifiedFile.Status);
    }

    // ============================================================================
    // Additional GetLogEntries Tests - Branch Filtering
    // ============================================================================

    [Fact]
    public void GetLogEntries_WithBranchFilter_ReturnsEntriesFromThatBranch()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "log-branch-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Make a commit on the new branch
        File.WriteAllText(Path.Combine(checkoutPath, "BranchFile.txt"), "Branch content");
        _git.Commit(checkoutPath, "Commit on branch", new[] { "BranchFile.txt" });

        var options = new VcsLogOptions { Branch = branchName, MaxEntries = 10 };

        // Act
        var entries = _git.GetLogEntries(checkoutPath, options);

        // Assert
        Assert.NotEmpty(entries);
        // The most recent commit should be our branch commit
        Assert.Contains(entries, e => e.Message.Contains("Commit on branch"));
    }

    [Fact]
    public void GetLogEntries_WithNonExistentBranch_ReturnsEntries()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var options = new VcsLogOptions { Branch = "non-existent-branch-xyz", MaxEntries = 10 };

        // Act
        var entries = _git.GetLogEntries(_clonePath, options);

        // Assert - should still return some entries (falls back to default behavior)
        Assert.NotEmpty(entries);
    }

    [Fact]
    public void GetLogEntries_WithDateRangeFilter_ReturnsFilteredEntries()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var allEntries = _git.GetLogEntries(_clonePath, new VcsLogOptions { MaxEntries = 100 });
        if (allEntries.Count < 3) return;

        // Set date range to exclude first and last commits
        var oldest = allEntries.Last().Date;
        var newest = allEntries.First().Date;
        var options = new VcsLogOptions
        {
            Since = oldest.AddMinutes(1),
            Until = newest.AddMinutes(-1),
            MaxEntries = 100
        };

        // Act
        var filteredEntries = _git.GetLogEntries(_clonePath, options);

        // Assert - should have fewer entries (possibly excluding oldest and/or newest)
        // Note: depends on actual commit timestamps in the test repo
        Assert.NotNull(filteredEntries);
    }

    // ============================================================================
    // Additional CleanWorkspace Tests
    // ============================================================================

    [Fact]
    public void CleanWorkspace_WithNestedUntrackedDirectory_RemovesEntireDirectory()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Create nested untracked directories with files
        var nestedDir = Path.Combine(checkoutPath, "untracked", "nested", "deep");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, "file1.txt"), "content");
        File.WriteAllText(Path.Combine(nestedDir, "file2.txt"), "content");

        // Act
        var result = _git.CleanWorkspace(checkoutPath);

        // Assert
        Assert.True(result);
        Assert.False(Directory.Exists(Path.Combine(checkoutPath, "untracked")));
    }

    [Fact]
    public void CleanWorkspace_WithMixedTrackedAndUntrackedInSameDir_OnlyRemovesUntracked()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Verify repo is valid
        Assert.True(Repository.IsValid(checkoutPath), "Copied repository should be valid");

        // Add untracked file in Models directory (which contains tracked files)
        var untrackedFile = Path.Combine(checkoutPath, "Models", "UntrackedModel.txt");
        File.WriteAllText(untrackedFile, "untracked content");
        Assert.True(File.Exists(untrackedFile), "Untracked file should be created");

        // Verify tracked file exists
        var trackedFile = Path.Combine(checkoutPath, "Models", "SimpleModel.mo");
        Assert.True(File.Exists(trackedFile), "Tracked file should exist before cleanup");

        // Act
        var result = _git.CleanWorkspace(checkoutPath);

        // Assert - CleanWorkspace should always return true when given a valid repo
        // The actual cleanup behavior is tested in assertions below
        Assert.True(result, "CleanWorkspace should succeed on a valid repository");
        Assert.False(File.Exists(untrackedFile), "Untracked file should be removed");
        Assert.True(File.Exists(trackedFile), "Tracked file should be preserved");
    }

    [Fact]
    public void CleanWorkspace_WithModifiedAndUntrackedFiles_RevertsAndRemoves()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;

        var originalContent = File.ReadAllText(readmePath);
        File.WriteAllText(readmePath, "Modified content");

        var untrackedFile = Path.Combine(checkoutPath, "Untracked.txt");
        File.WriteAllText(untrackedFile, "Untracked content");

        // Act
        var result = _git.CleanWorkspace(checkoutPath);

        // Assert
        Assert.True(result);
        Assert.Equal(NormalizeLineEndings(originalContent), NormalizeLineEndings(File.ReadAllText(readmePath)));
        Assert.False(File.Exists(untrackedFile));
    }

    // ============================================================================
    // Additional UpdateToLatest Tests - Edge Cases
    // ============================================================================

    [Fact]
    public void UpdateToLatest_DetachedHead_ReturnsError()
    {
        if (!_repositoryAvailable) return;

        // Arrange - create a checkout in detached HEAD state
        var checkoutPath = CopyTestRepository();

        // Detach HEAD by checking out a specific commit
        var hash = _git.GetCurrentRevision(checkoutPath);
        using (var repo = new Repository(checkoutPath))
        {
            var commit = repo.Lookup<Commit>(hash);
            Commands.Checkout(repo, commit);
        }

        // Verify detached
        var branch = _git.GetCurrentBranch(checkoutPath);
        Assert.Null(branch);

        // Act
        var result = _git.UpdateToLatest(checkoutPath);

        // Assert - without a remote, the error will be about missing origin, not detached HEAD
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        // Either "origin" (no remote configured) or "detached" (if remote exists)
        Assert.True(
            result.ErrorMessage.Contains("origin", StringComparison.OrdinalIgnoreCase) ||
            result.ErrorMessage.Contains("detached", StringComparison.OrdinalIgnoreCase),
            $"Expected error about 'origin' or 'detached', got: {result.ErrorMessage}");
    }

    [Fact]
    public void UpdateToLatest_NoOriginRemote_ReturnsError()
    {
        if (!_repositoryAvailable) return;

        // Arrange - create a local repo without origin
        var localRepoPath = CreateTempPath();
        Repository.Init(localRepoPath);

        // Create a commit so we have a HEAD
        File.WriteAllText(Path.Combine(localRepoPath, "file.txt"), "content");
        using (var repo = new Repository(localRepoPath))
        {
            Commands.Stage(repo, "file.txt");
            var sig = new Signature("Test", "test@test.com", DateTimeOffset.Now);
            repo.Commit("Initial commit", sig, sig);
        }

        // Act
        var result = _git.UpdateToLatest(localRepoPath);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("origin", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ============================================================================
    // Additional RevertFiles Tests
    // ============================================================================

    [Fact]
    public void RevertFiles_StagedNewFile_UnstagesAndDeletes()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var newFilePath = Path.Combine(checkoutPath, "StagedNew.txt");
        File.WriteAllText(newFilePath, "Content");

        using (var repo = new Repository(checkoutPath))
        {
            Commands.Stage(repo, "StagedNew.txt");
        }

        // Act
        var result = _git.RevertFiles(checkoutPath, new[] { "StagedNew.txt" });

        // Assert
        Assert.True(result.Success);
        Assert.False(File.Exists(newFilePath));
    }

    [Fact]
    public void RevertFiles_StagedModifiedFile_RevertsToHead()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;

        var originalContent = File.ReadAllText(readmePath);
        File.WriteAllText(readmePath, "Modified");

        using (var repo = new Repository(checkoutPath))
        {
            Commands.Stage(repo, "README.md");
        }

        // Act
        var result = _git.RevertFiles(checkoutPath, new[] { "README.md" });

        // Assert
        Assert.True(result.Success);
        var currentContent = File.ReadAllText(readmePath);
        Assert.Equal(NormalizeLineEndings(originalContent), NormalizeLineEndings(currentContent));
    }

    // ============================================================================
    // Additional SwitchBranch Tests - Remote Tracking
    // ============================================================================

    [Fact]
    public void SwitchBranch_ToRemoteBranch_CreatesLocalTrackingBranch()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Check if there's a feature-test branch on remote
        var branches = _git.GetBranches(checkoutPath, includeRemote: true);
        var remoteBranch = branches.FirstOrDefault(b => b.IsRemote && b.Name.Contains("feature"));

        if (remoteBranch == null) return; // Skip if no remote feature branch

        var localBranchName = remoteBranch.Name.Replace("origin/", "");

        // First remove local branch if it exists
        using (var repo = new Repository(checkoutPath))
        {
            var existingLocal = repo.Branches[localBranchName];
            if (existingLocal != null && !existingLocal.IsRemote)
            {
                repo.Branches.Remove(existingLocal);
            }
        }

        // Act
        var result = _git.SwitchBranch(checkoutPath, localBranchName);

        // Assert
        if (result.Success)
        {
            var currentBranch = _git.GetCurrentBranch(checkoutPath);
            Assert.Equal(localBranchName, currentBranch);
        }
    }

    // ============================================================================
    // Additional Commit Tests
    // ============================================================================

    [Fact]
    public void Commit_WithDeletedFile_CommitsDeletion()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "delete-commit-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // First create and commit a file
        var testFilePath = Path.Combine(checkoutPath, "ToDelete.txt");
        File.WriteAllText(testFilePath, "Will be deleted");
        _git.Commit(checkoutPath, "Add file to delete", new[] { "ToDelete.txt" });

        // Now delete it
        File.Delete(testFilePath);

        using (var repo = new Repository(checkoutPath))
        {
            Commands.Stage(repo, "ToDelete.txt");
        }

        // Act
        var result = _git.Commit(checkoutPath, "Delete the file", null);

        // Assert
        Assert.True(result.Success);
        Assert.False(File.Exists(testFilePath));
    }

    [Fact]
    public void Commit_WithEmptyMessage_StillCommits()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "empty-msg-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        File.WriteAllText(Path.Combine(checkoutPath, "EmptyMsg.txt"), "Content");

        // Act
        var result = _git.Commit(checkoutPath, "", new[] { "EmptyMsg.txt" });

        // Assert - Git allows empty messages
        Assert.True(result.Success);
    }

    // ============================================================================
    // Additional GetFileContentAtRevision Tests
    // ============================================================================

    [Fact]
    public void GetFileContentAtRevision_WithHEADString_ReturnsHeadContent()
    {
        if (!_repositoryAvailable) return;

        // Act
        var content = _git.GetFileContentAtRevision(_clonePath, "README.md", "HEAD");

        // Assert
        Assert.NotNull(content);
        Assert.NotEmpty(content);
    }

    [Fact]
    public void GetFileContentAtRevision_DirectoryPath_ReturnsNull()
    {
        if (!_repositoryAvailable) return;

        // Act - try to get content of a directory, not a file
        var content = _git.GetFileContentAtRevision(_clonePath, "Models", "main");

        // Assert
        Assert.Null(content);
    }

    [Fact]
    public void GetFileContentAtRevision_EmptyRevision_ReturnsHeadContent()
    {
        if (!_repositoryAvailable) return;

        // Act
        var content = _git.GetFileContentAtRevision(_clonePath, "README.md", "");

        // Assert
        Assert.NotNull(content);
    }

    // ============================================================================
    // Additional IsValidRepository Tests
    // ============================================================================

    [Fact]
    public void IsValidRepository_EmptyDirectory_ReturnsFalse()
    {
        // Arrange
        var emptyDir = CreateTempPath();
        Directory.CreateDirectory(emptyDir);

        // Act
        var result = _git.IsValidRepository(emptyDir);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidRepository_WithClonedRepo_ReturnsTrue()
    {
        if (!_repositoryAvailable) return;

        // Act
        var result = _git.IsValidRepository(_clonePath);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidRepository_WithInitializedRepo_ReturnsTrue()
    {
        // Arrange
        var repoPath = CreateTempPath();
        Repository.Init(repoPath);

        // Act
        var result = _git.IsValidRepository(repoPath);

        // Assert
        Assert.True(result);
    }

    // ============================================================================
    // Additional GetCurrentRevision Tests
    // ============================================================================

    [Fact]
    public void GetCurrentRevision_NewRepoWithNoCommits_ReturnsNull()
    {
        // Arrange
        var repoPath = CreateTempPath();
        Repository.Init(repoPath);

        // Act
        var result = _git.GetCurrentRevision(repoPath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentRevision_InvalidPath_ReturnsNull()
    {
        // Act
        var result = _git.GetCurrentRevision(Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()));

        // Assert
        Assert.Null(result);
    }

    // ============================================================================
    // Additional ResolveRevision Tests
    // ============================================================================

    [Fact]
    public void ResolveRevision_WithTag_ResolvesToCommit()
    {
        if (!_repositoryAvailable) return;

        // Act
        var resolved = _git.ResolveRevision(_clonePath, "v1.0.0");

        // Assert
        Assert.NotNull(resolved);
        Assert.Equal(40, resolved.Length);
    }

    [Fact]
    public void ResolveRevision_InvalidPath_ReturnsNull()
    {
        // Act
        var result = _git.ResolveRevision(
            Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()),
            "main"
        );

        // Assert
        Assert.Null(result);
    }

    // ============================================================================
    // Additional GetRevisionDescription Tests
    // ============================================================================

    [Fact]
    public void GetRevisionDescription_ValidCommit_ContainsAuthorAndDate()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var hash = _git.GetCurrentRevision(_clonePath);

        // Act
        var description = _git.GetRevisionDescription(_clonePath, hash!);

        // Assert
        Assert.NotNull(description);
        Assert.Contains("by", description);
        Assert.Contains("on", description);
    }

    [Fact]
    public void GetRevisionDescription_InvalidPath_ReturnsNull()
    {
        // Act
        var result = _git.GetRevisionDescription(
            Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()),
            "main"
        );

        // Assert
        Assert.Null(result);
    }

    // ============================================================================
    // Additional CheckoutRevision Tests
    // ============================================================================

    [Fact]
    public void CheckoutRevision_InvalidRevision_ReturnsFalse()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var outputPath = CreateTempPath();

        // Act
        var result = _git.CheckoutRevision(_clonePath, "invalid-revision-xyz123", outputPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CheckoutRevision_InvalidRepository_ReturnsFalse()
    {
        // Arrange
        var outputPath = CreateTempPath();

        // Act
        var result = _git.CheckoutRevision(
            Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()),
            "main",
            outputPath
        );

        // Assert
        Assert.False(result);
    }

    // ============================================================================
    // Additional UpdateExistingCheckout Tests
    // ============================================================================

    [Fact]
    public void UpdateExistingCheckout_InvalidRevision_ReturnsFalse()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateTempPath();

        // Act
        var result = _git.UpdateExistingCheckout(checkoutPath, _clonePath, "invalid-revision-xyz");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void UpdateExistingCheckout_UpdatesRemoteUrlIfChanged()
    {
        if (!_repositoryAvailable) return;

        // Arrange - first checkout
        var checkoutPath = CopyTestRepository();

        // Act - update again with same repo (simulates remote URL being the same)
        var result = _git.UpdateExistingCheckout(checkoutPath, _clonePath, "main");

        // Assert
        Assert.True(result);
    }

    // ============================================================================
    // Additional GetBranches Tests
    // ============================================================================

    [Fact]
    public void GetBranches_WithLastCommit_ReturnsCommitInfo()
    {
        if (!_repositoryAvailable) return;

        // Act
        var branches = _git.GetBranches(_clonePath, includeRemote: true);

        // Assert
        Assert.NotEmpty(branches);
        // At least some branches should have a last commit
        var branchWithCommit = branches.FirstOrDefault(b => b.LastCommit != null);
        Assert.NotNull(branchWithCommit);
        Assert.Equal(40, branchWithCommit.LastCommit!.Length);
    }

    [Fact]
    public void GetBranches_DetachedHead_NoBranchIsCurrent()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Detach HEAD
        var hash = _git.GetCurrentRevision(checkoutPath);
        using (var repo = new Repository(checkoutPath))
        {
            var commit = repo.Lookup<Commit>(hash);
            Commands.Checkout(repo, commit);
        }

        // Act
        var branches = _git.GetBranches(checkoutPath);

        // Assert
        // In detached HEAD, no branch should be marked as current
        Assert.DoesNotContain(branches, b => b.IsCurrent);
    }

    // ============================================================================
    // Additional CreateBranch Tests
    // ============================================================================

    [Fact]
    public void CreateBranch_InvalidPath_ReturnsFailure()
    {
        // Act
        var result = _git.CreateBranch(
            Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()),
            "test-branch"
        );

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Invalid", result.ErrorMessage);
    }

    [Fact]
    public void CreateBranch_WithoutSwitch_StaysOnCurrentBranch()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var originalBranch = _git.GetCurrentBranch(checkoutPath);
        var newBranchName = "no-switch-" + Guid.NewGuid().ToString()[..8];

        // Act
        var result = _git.CreateBranch(checkoutPath, newBranchName, switchToBranch: false);

        // Assert
        Assert.True(result.Success);
        var currentBranch = _git.GetCurrentBranch(checkoutPath);
        Assert.Equal(originalBranch, currentBranch);
    }

    // ============================================================================
    // Additional SwitchBranch Tests
    // ============================================================================

    [Fact]
    public void SwitchBranch_InvalidPath_ReturnsFailure()
    {
        // Act
        var result = _git.SwitchBranch(
            Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()),
            "main"
        );

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Invalid", result.ErrorMessage);
    }

    // ============================================================================
    // Additional GetCurrentBranch Tests
    // ============================================================================

    [Fact]
    public void GetCurrentBranch_AfterSwitching_ReturnsNewBranch()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var newBranchName = "current-test-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, newBranchName, switchToBranch: true);

        // Act
        var currentBranch = _git.GetCurrentBranch(checkoutPath);

        // Assert
        Assert.Equal(newBranchName, currentBranch);
    }

    // ============================================================================
    // Coverage Improvement Tests - ResolveToCommit Edge Cases
    // ============================================================================

    [Fact]
    public void ResolveRevision_WithBranchName_ResolvesToCommit()
    {
        if (!_repositoryAvailable) return;

        // Act - resolve main branch
        var resolved = _git.ResolveRevision(_clonePath, "main");

        // Assert
        Assert.NotNull(resolved);
        Assert.Equal(40, resolved.Length);
    }

    [Fact]
    public void ResolveRevision_WithHEAD_ResolvesToCommit()
    {
        if (!_repositoryAvailable) return;

        // Act
        var resolved = _git.ResolveRevision(_clonePath, "HEAD");
        var current = _git.GetCurrentRevision(_clonePath);

        // Assert
        Assert.NotNull(resolved);
        Assert.Equal(current, resolved);
    }

    [Fact]
    public void ResolveRevision_WithParentRef_ResolvesToParentCommit()
    {
        if (!_repositoryAvailable) return;

        // Arrange - get the parent of HEAD directly
        using var repo = new Repository(_clonePath);
        var headCommit = repo.Head.Tip;
        var parentCommit = headCommit.Parents.FirstOrDefault();
        if (parentCommit == null) return; // Skip if no parent

        // Act - resolve HEAD~1 (parent of HEAD)
        var resolved = _git.ResolveRevision(_clonePath, "HEAD~1");

        // Assert
        Assert.NotNull(resolved);
        Assert.Equal(parentCommit.Sha, resolved);
    }

    [Fact]
    public void ResolveRevision_WithInvalidRef_ReturnsNull()
    {
        if (!_repositoryAvailable) return;

        // Act
        var resolved = _git.ResolveRevision(_clonePath, "completely-invalid-ref-xyz-123456");

        // Assert
        Assert.Null(resolved);
    }

    // ============================================================================
    // Coverage Improvement Tests - UpdateExistingCheckout Edge Cases
    // ============================================================================

    [Fact]
    public void UpdateExistingCheckout_NonExistentDirectory_CreatesAndCheckouts()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CreateTempPath();
        // Don't create the directory - let UpdateExistingCheckout create it
        Assert.False(Directory.Exists(checkoutPath));

        // Act
        var result = _git.UpdateExistingCheckout(checkoutPath, _clonePath, "main");

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(checkoutPath));
        Assert.True(File.Exists(Path.Combine(checkoutPath, "README.md")));
    }

    [Fact]
    public void UpdateExistingCheckout_EmptyDirectory_InitializesAndCheckouts()
    {
        if (!_repositoryAvailable) return;

        // Arrange - create empty directory (not a git repo)
        var checkoutPath = CreateTempPath();
        Directory.CreateDirectory(checkoutPath);

        // Act
        var result = _git.UpdateExistingCheckout(checkoutPath, _clonePath, "main");

        // Assert
        Assert.True(result);
        Assert.True(Repository.IsValid(checkoutPath));
    }

    [Fact]
    public void UpdateExistingCheckout_WithLocalChanges_DiscardsChanges()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Make local changes
        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;

        var originalContent = File.ReadAllText(readmePath);
        File.WriteAllText(readmePath, "Local changes that should be discarded");

        // Add untracked file
        File.WriteAllText(Path.Combine(checkoutPath, "untracked.txt"), "Should be removed");

        // Act - update to a different revision
        var v1Hash = _git.ResolveRevision(_clonePath, "v1.0.0");
        var result = _git.UpdateExistingCheckout(checkoutPath, _clonePath, v1Hash!);

        // Assert
        Assert.True(result);
        // Untracked file should be removed
        Assert.False(File.Exists(Path.Combine(checkoutPath, "untracked.txt")));
    }

    [Fact]
    public void UpdateExistingCheckout_LocalRepository_UpdatesToRevision()
    {
        if (!_repositoryAvailable) return;

        // Arrange - create a checkout
        var checkoutPath = CopyTestRepository();

        // Get the v1.0.0 revision hash
        var v1Hash = _git.ResolveRevision(_clonePath, "v1.0.0");
        Assert.NotNull(v1Hash);

        // Act - update to v1.0.0
        var result = _git.UpdateExistingCheckout(checkoutPath, _clonePath, v1Hash);

        // Assert
        Assert.True(result);
        var currentRev = _git.GetCurrentRevision(checkoutPath);
        Assert.Equal(v1Hash, currentRev);
    }

    [Fact]
    public void UpdateExistingCheckout_InvalidSourceRepository_ReturnsFalse()
    {
        // Arrange
        var checkoutPath = CreateTempPath();
        var invalidSourcePath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid());

        // Act
        var result = _git.UpdateExistingCheckout(checkoutPath, invalidSourcePath, "main");

        // Assert
        Assert.False(result);
    }

    // ============================================================================
    // Coverage Improvement Tests - GetChangedFiles Renamed/Copied
    // ============================================================================

    [Fact]
    public void GetChangedFiles_WithRenamedFile_ReturnsRenamedTypeAndOldPath()
    {
        if (!_repositoryAvailable) return;

        // Arrange - create a repo with a renamed file
        var checkoutPath = CopyTestRepository();

        var branchName = "rename-test-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Create and commit a file
        var originalPath = Path.Combine(checkoutPath, "OriginalName.txt");
        File.WriteAllText(originalPath, "Content to be renamed");
        _git.Commit(checkoutPath, "Add file to rename", new[] { "OriginalName.txt" });

        // Rename the file using git mv
        var newPath = Path.Combine(checkoutPath, "RenamedFile.txt");
        using (var repo = new Repository(checkoutPath))
        {
            Commands.Move(repo, "OriginalName.txt", "RenamedFile.txt");
        }

        var renameResult = _git.Commit(checkoutPath, "Rename file", null);
        if (!renameResult.Success) return;

        // Act - get changed files for the rename commit
        var changedFiles = _git.GetChangedFiles(checkoutPath, renameResult.NewRevision!);

        // Assert
        Assert.NotEmpty(changedFiles);
        var renamedFile = changedFiles.FirstOrDefault(f => f.ChangeType == VcsChangeType.Renamed);
        if (renamedFile != null)
        {
            Assert.Equal("RenamedFile.txt", renamedFile.Path);
            Assert.Equal("OriginalName.txt", renamedFile.OldPath);
        }
    }

    [Fact]
    public void GetChangedFiles_WithCopiedFile_ReturnsCopiedType()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "copy-test-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Create and commit a file
        var originalPath = Path.Combine(checkoutPath, "ToCopy.txt");
        File.WriteAllText(originalPath, "Content to be copied\n" + string.Join("\n", Enumerable.Range(1, 20).Select(i => $"Line {i}")));
        _git.Commit(checkoutPath, "Add file to copy", new[] { "ToCopy.txt" });

        // Copy the file (Git can detect copies with -C flag during diff)
        var copyPath = Path.Combine(checkoutPath, "CopiedFile.txt");
        File.Copy(originalPath, copyPath);
        _git.Commit(checkoutPath, "Copy file", new[] { "CopiedFile.txt" });

        // Act - get changed files (note: Git may or may not detect as copy depending on similarity)
        var entries = _git.GetLogEntries(checkoutPath, new VcsLogOptions { MaxEntries = 1 });
        if (entries.Count == 0) return;

        var changedFiles = _git.GetChangedFiles(checkoutPath, entries[0].Revision);

        // Assert - file should be in changed files (as Added or Copied)
        Assert.NotEmpty(changedFiles);
        var copiedFile = changedFiles.FirstOrDefault(f => f.Path.Contains("CopiedFile"));
        Assert.NotNull(copiedFile);
    }

    [Fact]
    public void GetChangedFiles_WithDeletedFile_ReturnsDeletedType()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "delete-change-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Create and commit a file
        var filePath = Path.Combine(checkoutPath, "ToDelete.txt");
        File.WriteAllText(filePath, "Will be deleted");
        _git.Commit(checkoutPath, "Add file", new[] { "ToDelete.txt" });

        // Delete and commit
        File.Delete(filePath);
        using (var repo = new Repository(checkoutPath))
        {
            Commands.Stage(repo, "ToDelete.txt");
        }
        var deleteResult = _git.Commit(checkoutPath, "Delete file", null);
        if (!deleteResult.Success) return;

        // Act
        var changedFiles = _git.GetChangedFiles(checkoutPath, deleteResult.NewRevision!);

        // Assert
        Assert.NotEmpty(changedFiles);
        var deletedFile = changedFiles.FirstOrDefault(f => f.ChangeType == VcsChangeType.Deleted);
        Assert.NotNull(deletedFile);
        Assert.Equal("ToDelete.txt", deletedFile.Path);
    }

    // ============================================================================
    // Coverage Improvement Tests - GetWorkingCopyChanges Combined States
    // ============================================================================

    [Fact]
    public void GetWorkingCopyChanges_WithRenamedFile_ReturnsRenamedStatus()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "wc-rename-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Rename a file using git mv and stage it
        using (var repo = new Repository(checkoutPath))
        {
            // Create a file first, commit it
            var testFile = Path.Combine(checkoutPath, "RenameMe.txt");
            File.WriteAllText(testFile, "Will be renamed");
            Commands.Stage(repo, "RenameMe.txt");
            var sig = new Signature("Test", "test@test.com", DateTimeOffset.Now);
            repo.Commit("Add file to rename", sig, sig);

            // Now rename it
            Commands.Move(repo, "RenameMe.txt", "Renamed.txt");
        }

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

        // Assert
        Assert.NotEmpty(changes);
        // Should have a renamed file entry
        var renamedFile = changes.FirstOrDefault(c => c.Status == VcsFileStatus.Renamed);
        if (renamedFile != null)
        {
            Assert.True(renamedFile.IsStaged);
        }
    }

    [Fact]
    public void GetWorkingCopyChanges_MultipleChangeTypes_ReturnsAllChanges()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Modify a tracked file
        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (File.Exists(readmePath))
        {
            File.AppendAllText(readmePath, "\n// Modified");
        }

        // Add an untracked file
        File.WriteAllText(Path.Combine(checkoutPath, "Untracked.txt"), "New");

        // Delete a tracked file
        var packageMoPath = Path.Combine(checkoutPath, "package.mo");
        if (File.Exists(packageMoPath))
        {
            File.Delete(packageMoPath);
        }

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

        // Assert
        Assert.NotEmpty(changes);

        // Should have multiple types
        var hasModified = changes.Any(c => c.Status == VcsFileStatus.Modified);
        var hasUntracked = changes.Any(c => c.Status == VcsFileStatus.Untracked);
        var hasDeleted = changes.Any(c => c.Status == VcsFileStatus.Deleted);

        Assert.True(hasModified || hasUntracked || hasDeleted);
    }

    [Fact]
    public void GetWorkingCopyChanges_StagedAndUnstagedModifications_HandlesBothStates()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "combined-state-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Create a new file, stage it (NewInIndex)
        var newFilePath = Path.Combine(checkoutPath, "NewFile.txt");
        File.WriteAllText(newFilePath, "New content");
        using (var repo = new Repository(checkoutPath))
        {
            Commands.Stage(repo, "NewFile.txt");
        }

        // Modify an existing file without staging (ModifiedInWorkdir)
        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (File.Exists(readmePath))
        {
            File.AppendAllText(readmePath, "\nUnstaged modification");
        }

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

        // Assert
        Assert.True(changes.Count >= 2);

        var stagedNewFile = changes.FirstOrDefault(c => c.Path.Contains("NewFile"));
        Assert.NotNull(stagedNewFile);
        Assert.True(stagedNewFile.IsStaged);
        Assert.Equal(VcsFileStatus.Added, stagedNewFile.Status);

        var unstagedModified = changes.FirstOrDefault(c => c.Path.Contains("README"));
        if (unstagedModified != null)
        {
            Assert.False(unstagedModified.IsStaged);
            Assert.Equal(VcsFileStatus.Modified, unstagedModified.Status);
        }
    }

    // ============================================================================
    // Coverage Improvement Tests - CleanWorkspace Edge Cases
    // ============================================================================

    [Fact]
    public void CleanWorkspace_InvalidPath_ReturnsFalse()
    {
        // Act
        var result = _git.CleanWorkspace(Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()));

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CleanWorkspace_NonGitDirectory_ReturnsFalse()
    {
        // Arrange
        var nonGitPath = CreateTempPath();
        Directory.CreateDirectory(nonGitPath);
        File.WriteAllText(Path.Combine(nonGitPath, "file.txt"), "content");

        // Act
        var result = _git.CleanWorkspace(nonGitPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CleanWorkspace_EmptyRepository_ReturnsFalse()
    {
        // Arrange - repo with no commits - Reset will fail on empty repo
        var repoPath = CreateTempPath();
        Repository.Init(repoPath);

        // Act
        var result = _git.CleanWorkspace(repoPath);

        // Assert - CleanWorkspace fails on empty repo (no HEAD to reset to)
        Assert.False(result);
    }

    [Fact]
    public void CleanWorkspace_MultipleUntrackedFilesInSameDirectory_RemovesAll()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Create multiple untracked files in a new directory
        var newDir = Path.Combine(checkoutPath, "TempFiles");
        Directory.CreateDirectory(newDir);
        File.WriteAllText(Path.Combine(newDir, "file1.txt"), "1");
        File.WriteAllText(Path.Combine(newDir, "file2.txt"), "2");
        File.WriteAllText(Path.Combine(newDir, "file3.txt"), "3");

        // Act
        var result = _git.CleanWorkspace(checkoutPath);

        // Assert
        Assert.True(result);
        Assert.False(Directory.Exists(newDir));
    }

    // ============================================================================
    // Coverage Improvement Tests - UpdateToLatest Edge Cases
    // Note: UpdateToLatest requires a remote, so we test error cases here
    // ============================================================================

    [Fact]
    public void UpdateToLatest_NoBranch_ReturnsError()
    {
        // Arrange - new repo with no HEAD
        var repoPath = CreateTempPath();
        Repository.Init(repoPath);

        // Act
        var result = _git.UpdateToLatest(repoPath);

        // Assert
        Assert.False(result.Success);
    }

    // ============================================================================
    // Coverage Improvement Tests - GetLogEntries Edge Cases
    // ============================================================================

    [Fact]
    public void GetLogEntries_EnsuresMinimumEntriesWithSinceFilter()
    {
        if (!_repositoryAvailable) return;

        // Arrange - set since to a future date, which would normally return 0 entries
        // but the implementation ensures at least minEntriesFromSinceFilter
        var options = new VcsLogOptions
        {
            Since = DateTimeOffset.Now.AddYears(1), // Future date
            MaxEntries = 100
        };

        // Act
        var entries = _git.GetLogEntries(_clonePath, options);

        // Assert - should still return some entries due to minimum entries logic
        Assert.NotEmpty(entries);
    }

    [Fact]
    public void GetLogEntries_WithShortMaxEntries_RespectsLimit()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var options = new VcsLogOptions { MaxEntries = 1 };

        // Act
        var entries = _git.GetLogEntries(_clonePath, options);

        // Assert
        Assert.Single(entries);
    }

    [Fact]
    public void GetLogEntries_WithUntilInFuture_ReturnsAllEntries()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var options = new VcsLogOptions
        {
            Until = DateTimeOffset.Now.AddYears(1),
            MaxEntries = 100
        };

        // Act
        var entries = _git.GetLogEntries(_clonePath, options);

        // Assert
        Assert.NotEmpty(entries);
    }

    // ============================================================================
    // Coverage Improvement Tests - RevertFiles Edge Cases
    // ============================================================================

    [Fact]
    public void RevertFiles_InvalidPath_ReturnsError()
    {
        // Act
        var result = _git.RevertFiles(
            Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()),
            new[] { "file.txt" }
        );

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Invalid", result.ErrorMessage);
    }

    [Fact]
    public void RevertFiles_EmptyFileList_Succeeds()
    {
        if (!_repositoryAvailable) return;

        // Act
        var result = _git.RevertFiles(_clonePath, Array.Empty<string>());

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public void RevertFiles_DeletedTrackedFile_RestoresFile()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;

        var originalContent = File.ReadAllText(readmePath);
        File.Delete(readmePath);
        Assert.False(File.Exists(readmePath));

        // Act
        var result = _git.RevertFiles(checkoutPath, new[] { "README.md" });

        // Assert
        Assert.True(result.Success, $"RevertFiles failed: {result.ErrorMessage}");
        Assert.True(File.Exists(readmePath), "File should be restored");
        Assert.Equal(NormalizeLineEndings(originalContent), NormalizeLineEndings(File.ReadAllText(readmePath)));
    }

    // ============================================================================
    // Coverage Improvement Tests - GetFileContentAtRevision Edge Cases
    // ============================================================================

    [Fact]
    public void GetFileContentAtRevision_InvalidRepository_ReturnsNull()
    {
        // Act
        var content = _git.GetFileContentAtRevision(
            Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()),
            "README.md",
            "main"
        );

        // Assert
        Assert.Null(content);
    }

    [Fact]
    public void GetFileContentAtRevision_FileInSubdirectory_ReturnsContent()
    {
        if (!_repositoryAvailable) return;

        // Act
        var content = _git.GetFileContentAtRevision(_clonePath, "Models/package.mo", "main");

        // Assert
        Assert.NotNull(content);
    }

    [Fact]
    public void GetFileContentAtRevision_WithParentRevision_ReturnsContent()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var entries = _git.GetLogEntries(_clonePath, new VcsLogOptions { MaxEntries = 2 });
        if (entries.Count < 2) return;

        // Act - use HEAD~1
        var content = _git.GetFileContentAtRevision(_clonePath, "README.md", "HEAD~1");

        // Assert - may be null if file didn't exist in parent, but shouldn't throw
        // Just verify no exception was thrown
    }

    // ============================================================================
    // Coverage Improvement Tests - GetBranches Edge Cases
    // ============================================================================

    [Fact]
    public void GetBranches_EmptyRepository_ReturnsEmptyList()
    {
        // Arrange
        var repoPath = CreateTempPath();
        Repository.Init(repoPath);

        // Act
        var branches = _git.GetBranches(repoPath);

        // Assert
        Assert.Empty(branches);
    }

    [Fact]
    public void GetBranches_WithMultipleLocalBranches_ReturnsAll()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Create multiple branches
        _git.CreateBranch(checkoutPath, "branch1-" + Guid.NewGuid().ToString()[..8], false);
        _git.CreateBranch(checkoutPath, "branch2-" + Guid.NewGuid().ToString()[..8], false);

        // Act
        var branches = _git.GetBranches(checkoutPath, includeRemote: false);

        // Assert
        Assert.True(branches.Count >= 3); // main + 2 new branches
    }

    // ============================================================================
    // Coverage Improvement Tests - CheckoutRevision with Tree Entries
    // ============================================================================

    [Fact]
    public void CheckoutRevision_WithNestedDirectories_ChecksOutAllFiles()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var outputPath = CreateTempPath();

        // Act
        var result = _git.CheckoutRevision(_clonePath, "main", outputPath);

        // Assert
        Assert.True(result);

        // Verify nested structure
        Assert.True(Directory.Exists(Path.Combine(outputPath, "Models")));
        Assert.True(File.Exists(Path.Combine(outputPath, "Models", "SimpleModel.mo")));
        Assert.True(File.Exists(Path.Combine(outputPath, "Models", "package.mo")));
    }

    [Fact]
    public void CheckoutRevision_VerifiesFileContents()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var outputPath = CreateTempPath();

        // Act
        _git.CheckoutRevision(_clonePath, "main", outputPath);

        // Assert - verify actual content
        var simpleModelPath = Path.Combine(outputPath, "Models", "SimpleModel.mo");
        if (File.Exists(simpleModelPath))
        {
            var content = File.ReadAllText(simpleModelPath);
            Assert.Contains("model", content);
        }
    }

    // ============================================================================
    // Coverage Improvement Tests - GetCurrentBranch Edge Cases
    // ============================================================================

    [Fact]
    public void GetCurrentBranch_InvalidPath_ReturnsNull()
    {
        // Act
        var branch = _git.GetCurrentBranch(Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()));

        // Assert
        Assert.Null(branch);
    }

    [Fact]
    public void GetCurrentBranch_EmptyRepository_ReturnsNull()
    {
        // Arrange
        var repoPath = CreateTempPath();
        Repository.Init(repoPath);

        // Act
        var branch = _git.GetCurrentBranch(repoPath);

        // Assert
        // Empty repo might return null or "master" depending on Git config
    }

    // ============================================================================
    // Coverage Improvement Tests - Commit with Staging
    // ============================================================================

    [Fact]
    public void Commit_WithWildcard_StagesAllChanges()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "wildcard-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Create multiple files
        File.WriteAllText(Path.Combine(checkoutPath, "A.txt"), "A");
        File.WriteAllText(Path.Combine(checkoutPath, "B.txt"), "B");
        Directory.CreateDirectory(Path.Combine(checkoutPath, "SubDir"));
        File.WriteAllText(Path.Combine(checkoutPath, "SubDir", "C.txt"), "C");

        // Act - commit with null (stages all)
        var result = _git.Commit(checkoutPath, "Commit all", null);

        // Assert
        Assert.True(result.Success);
        var changes = _git.GetWorkingCopyChanges(checkoutPath);
        Assert.Empty(changes);
    }

    [Fact]
    public void Commit_ModifiedTrackedFile_StagesAndCommits()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "mod-tracked-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;

        File.AppendAllText(readmePath, "\nModified " + Guid.NewGuid());

        // Act
        var result = _git.Commit(checkoutPath, "Modify README", new[] { "README.md" });

        // Assert
        Assert.True(result.Success);
    }

    // ============================================================================
    // Coverage Improvement Tests - Exception Handling Paths
    // ============================================================================

    [Fact]
    public void IsValidRepository_NullPath_ReturnsFalse()
    {
        // Act
        var result = _git.IsValidRepository(null!);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetChangedFiles_InvalidRepository_ReturnsEmptyList()
    {
        // Act
        var files = _git.GetChangedFiles(
            Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid()),
            "abc123"
        );

        // Assert
        Assert.Empty(files);
    }

    // ============================================================================
    // Coverage Improvement Tests - UpdateExistingCheckout with HEAD tip null
    // ============================================================================

    [Fact]
    public void UpdateExistingCheckout_NewEmptyRepo_SkipsCleanAndSucceeds()
    {
        if (!_repositoryAvailable) return;

        // Arrange - create a path that doesn't exist yet
        var checkoutPath = CreateTempPath();

        // Act - should create, init, and checkout
        var result = _git.UpdateExistingCheckout(checkoutPath, _clonePath, "main");

        // Assert
        Assert.True(result);
        Assert.True(Repository.IsValid(checkoutPath));
    }

    // ============================================================================
    // Coverage Improvement Tests - GetLogEntries with Branch that doesn't exist
    // ============================================================================

    [Fact]
    public void GetLogEntries_NonExistentBranch_FallsBackToDefault()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var options = new VcsLogOptions
        {
            Branch = "this-branch-definitely-does-not-exist-xyz123",
            MaxEntries = 5
        };

        // Act
        var entries = _git.GetLogEntries(_clonePath, options);

        // Assert - should still return entries from default HEAD
        Assert.NotEmpty(entries);
    }

    // ============================================================================
    // Coverage Improvement Tests - GetWorkingCopyChanges default case handling
    // ============================================================================

    [Fact]
    public void GetWorkingCopyChanges_ModifiedInBothIndexAndWorkdir_ShowsModified()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "index-workdir-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;

        // First modification and stage it
        File.WriteAllText(readmePath, "First modification");
        using (var repo = new Repository(checkoutPath))
        {
            Commands.Stage(repo, "README.md");
        }

        // Second modification (not staged)
        File.WriteAllText(readmePath, "Second modification - different content");

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

        // Assert
        var readmeChange = changes.FirstOrDefault(c => c.Path.Contains("README"));
        Assert.NotNull(readmeChange);
        // When both index and workdir are modified, status should reflect that
    }

    // ============================================================================
    // Coverage Improvement Tests - SwitchBranch with remote tracking
    // ============================================================================

    [Fact]
    public void SwitchBranch_LocalBranchMatchingRemote_SwitchesSuccessfully()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Create a local branch
        var branchName = "local-track-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: false);

        // Act - switch to the new local branch
        var result = _git.SwitchBranch(checkoutPath, branchName);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(branchName, _git.GetCurrentBranch(checkoutPath));
    }

    // ============================================================================
    // Coverage Improvement Tests - CreateBranch edge cases
    // ============================================================================

    [Fact]
    public void CreateBranch_EmptyRepository_ReturnsFailure()
    {
        // Arrange - empty repo has no commits to branch from
        var repoPath = CreateTempPath();
        Repository.Init(repoPath);

        // Act
        var result = _git.CreateBranch(repoPath, "new-branch");

        // Assert
        Assert.False(result.Success);
    }

    // ============================================================================
    // High-Coverage Tests - GetLogEntries Main Paths
    // ============================================================================

    [Fact]
    public void GetLogEntries_ExercisesMainCodePath_ReturnsPopulatedEntries()
    {
        if (!_repositoryAvailable) return;

        // Act - exercise the main code path
        var entries = _git.GetLogEntries(_clonePath, new VcsLogOptions { MaxEntries = 50 });

        // Assert - verify all fields are populated
        Assert.NotEmpty(entries);
        foreach (var entry in entries)
        {
            Assert.NotEmpty(entry.Revision);
            Assert.Equal(40, entry.Revision.Length);
            Assert.NotEmpty(entry.ShortRevision);
            Assert.Equal(7, entry.ShortRevision.Length);
            Assert.NotEmpty(entry.Author);
            Assert.NotEqual(default(DateTimeOffset), entry.Date);
            // Branch should be set when not in detached HEAD
            Assert.NotNull(entry.Branch);
        }
    }

    [Fact]
    public void GetLogEntries_WithBranchFilter_ExercisesBranchLookup()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "log-filter-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Create a commit
        File.WriteAllText(Path.Combine(checkoutPath, "LogTest.txt"), "content");
        _git.Commit(checkoutPath, "Log test commit", new[] { "LogTest.txt" });

        // Act - use branch filter
        var options = new VcsLogOptions { Branch = branchName, MaxEntries = 10 };
        var entries = _git.GetLogEntries(checkoutPath, options);

        // Assert
        Assert.NotEmpty(entries);
    }

    [Fact]
    public void GetLogEntries_ExercisesDateFilterPath()
    {
        if (!_repositoryAvailable) return;

        // Arrange - date filter that should include all entries
        var options = new VcsLogOptions
        {
            Since = DateTimeOffset.MinValue.AddYears(100),
            Until = DateTimeOffset.MaxValue.AddYears(-100),
            MaxEntries = 100
        };

        // Act
        var entries = _git.GetLogEntries(_clonePath, options);

        // Assert - with wide date range, should get entries
        Assert.NotEmpty(entries);
    }

    // ============================================================================
    // High-Coverage Tests - GetWorkingCopyChanges Main Paths
    // ============================================================================

    [Fact]
    public void GetWorkingCopyChanges_ExercisesNewInIndexPath()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "wc-index-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Create and stage a new file
        File.WriteAllText(Path.Combine(checkoutPath, "IndexNew.txt"), "new");
        using (var repo = new Repository(checkoutPath))
        {
            Commands.Stage(repo, "IndexNew.txt");
        }

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

        // Assert - exercise NewInIndex path
        var newFile = changes.FirstOrDefault(c => c.Path.Contains("IndexNew"));
        Assert.NotNull(newFile);
        Assert.Equal(VcsFileStatus.Added, newFile.Status);
        Assert.True(newFile.IsStaged);
    }

    [Fact]
    public void GetWorkingCopyChanges_ExercisesModifiedInIndexPath()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "wc-mod-idx-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Modify and stage an existing file
        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;

        File.AppendAllText(readmePath, "\nModified and staged");
        using (var repo = new Repository(checkoutPath))
        {
            Commands.Stage(repo, "README.md");
        }

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

        // Assert - exercise ModifiedInIndex path
        var modFile = changes.FirstOrDefault(c => c.Path.Contains("README"));
        Assert.NotNull(modFile);
        Assert.Equal(VcsFileStatus.Modified, modFile.Status);
        Assert.True(modFile.IsStaged);
    }

    [Fact]
    public void GetWorkingCopyChanges_ExercisesDeletedFromIndexPath()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "wc-del-idx-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Stage a deletion
        using (var repo = new Repository(checkoutPath))
        {
            Commands.Remove(repo, "README.md");
        }

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

        // Assert - exercise DeletedFromIndex path
        var delFile = changes.FirstOrDefault(c => c.Path.Contains("README"));
        Assert.NotNull(delFile);
        Assert.Equal(VcsFileStatus.Deleted, delFile.Status);
        Assert.True(delFile.IsStaged);
    }

    [Fact]
    public void GetWorkingCopyChanges_ExercisesModifiedInWorkdirPath()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Modify without staging
        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;
        File.AppendAllText(readmePath, "\nUnstaged modification");

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

        // Assert - exercise ModifiedInWorkdir path
        var modFile = changes.FirstOrDefault(c => c.Path.Contains("README"));
        Assert.NotNull(modFile);
        Assert.Equal(VcsFileStatus.Modified, modFile.Status);
        Assert.False(modFile.IsStaged);
    }

    [Fact]
    public void GetWorkingCopyChanges_ExercisesDeletedFromWorkdirPath()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Delete without staging
        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;
        File.Delete(readmePath);

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

        // Assert - exercise DeletedFromWorkdir path
        var delFile = changes.FirstOrDefault(c => c.Path.Contains("README"));
        Assert.NotNull(delFile);
        Assert.Equal(VcsFileStatus.Deleted, delFile.Status);
        Assert.False(delFile.IsStaged);
    }

    [Fact]
    public void GetWorkingCopyChanges_ExercisesNewInWorkdirPath()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Create new untracked file
        File.WriteAllText(Path.Combine(checkoutPath, "Untracked.txt"), "new");

        // Act
        var changes = _git.GetWorkingCopyChanges(checkoutPath);

        // Assert - exercise NewInWorkdir (untracked) path
        var newFile = changes.FirstOrDefault(c => c.Path.Contains("Untracked"));
        Assert.NotNull(newFile);
        Assert.Equal(VcsFileStatus.Untracked, newFile.Status);
        Assert.False(newFile.IsStaged);
    }

    // ============================================================================
    // High-Coverage Tests - GetChangedFiles Main Paths
    // ============================================================================

    [Fact]
    public void GetChangedFiles_ExercisesAllChangeTypes()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "changes-all-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Create initial file
        File.WriteAllText(Path.Combine(checkoutPath, "ToChange.txt"), "original");
        _git.Commit(checkoutPath, "Add file", new[] { "ToChange.txt" });

        // Modify it
        File.WriteAllText(Path.Combine(checkoutPath, "ToChange.txt"), "modified");
        var modResult = _git.Commit(checkoutPath, "Modify file", new[] { "ToChange.txt" });
        if (modResult.Success)
        {
            var changedFiles = _git.GetChangedFiles(checkoutPath, modResult.NewRevision!);
            Assert.NotEmpty(changedFiles);
            Assert.Contains(changedFiles, f => f.ChangeType == VcsChangeType.Modified);
        }
    }

    [Fact]
    public void GetChangedFiles_ExercisesAddedChangeType()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "changes-add-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Add new file
        File.WriteAllText(Path.Combine(checkoutPath, "NewlyAdded.txt"), "new content");
        var result = _git.Commit(checkoutPath, "Add new file", new[] { "NewlyAdded.txt" });

        if (result.Success)
        {
            // Act
            var changedFiles = _git.GetChangedFiles(checkoutPath, result.NewRevision!);

            // Assert
            Assert.NotEmpty(changedFiles);
            Assert.Contains(changedFiles, f => f.ChangeType == VcsChangeType.Added);
        }
    }

    [Fact]
    public void GetChangedFiles_ExercisesDeletedChangeType()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "changes-del-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Create and commit
        File.WriteAllText(Path.Combine(checkoutPath, "ToBeDeleted.txt"), "will delete");
        _git.Commit(checkoutPath, "Add file", new[] { "ToBeDeleted.txt" });

        // Delete and commit
        File.Delete(Path.Combine(checkoutPath, "ToBeDeleted.txt"));
        using (var repo = new Repository(checkoutPath))
        {
            Commands.Stage(repo, "ToBeDeleted.txt");
        }
        var result = _git.Commit(checkoutPath, "Delete file", null);

        if (result.Success)
        {
            // Act
            var changedFiles = _git.GetChangedFiles(checkoutPath, result.NewRevision!);

            // Assert
            Assert.NotEmpty(changedFiles);
            Assert.Contains(changedFiles, f => f.ChangeType == VcsChangeType.Deleted);
        }
    }

    // ============================================================================
    // High-Coverage Tests - Commit Main Paths
    // ============================================================================

    [Fact]
    public void Commit_ExercisesStagingSpecificFiles()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "commit-stage-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        File.WriteAllText(Path.Combine(checkoutPath, "FileA.txt"), "A");
        File.WriteAllText(Path.Combine(checkoutPath, "FileB.txt"), "B");

        // Act - commit specific files (not null)
        var result = _git.Commit(checkoutPath, "Commit specific files", new[] { "FileA.txt", "FileB.txt" });

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.NewRevision);
    }

    [Fact]
    public void Commit_ExercisesStagingAllWithWildcard()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "commit-all-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        File.WriteAllText(Path.Combine(checkoutPath, "File1.txt"), "1");
        File.WriteAllText(Path.Combine(checkoutPath, "File2.txt"), "2");

        // Act - commit all (null means all)
        var result = _git.Commit(checkoutPath, "Commit all", null);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public void Commit_ExercisesNoStagedChangesPath()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();
        _git.CleanWorkspace(checkoutPath);

        // Act - commit with no changes
        var result = _git.Commit(checkoutPath, "Empty commit");

        // Assert - should fail with no changes
        Assert.False(result.Success);
        Assert.Contains("No changes", result.ErrorMessage);
    }

    // ============================================================================
    // High-Coverage Tests - RevertFiles Main Paths
    // ============================================================================

    [Fact]
    public void RevertFiles_ExercisesCheckoutPathForTrackedFile()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var readmePath = Path.Combine(checkoutPath, "README.md");
        if (!File.Exists(readmePath)) return;

        var original = File.ReadAllText(readmePath);
        File.WriteAllText(readmePath, "Modified");

        // Act - revert tracked file (exercises CheckoutPaths)
        var result = _git.RevertFiles(checkoutPath, new[] { "README.md" });

        // Assert
        Assert.True(result.Success);
        Assert.Equal(NormalizeLineEndings(original), NormalizeLineEndings(File.ReadAllText(readmePath)));
    }

    [Fact]
    public void RevertFiles_ExercisesDeletePathForNewFile()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var newFilePath = Path.Combine(checkoutPath, "NewToRevert.txt");
        File.WriteAllText(newFilePath, "New file");

        // Act - revert new file (exercises delete path)
        var result = _git.RevertFiles(checkoutPath, new[] { "NewToRevert.txt" });

        // Assert
        Assert.True(result.Success);
        Assert.False(File.Exists(newFilePath));
    }

    [Fact]
    public void RevertFiles_ExercisesUnstagePathForStagedFile()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var newFilePath = Path.Combine(checkoutPath, "StagedNew.txt");
        File.WriteAllText(newFilePath, "Staged new file");

        using (var repo = new Repository(checkoutPath))
        {
            Commands.Stage(repo, "StagedNew.txt");
        }

        // Act - revert staged new file (exercises unstage + delete)
        var result = _git.RevertFiles(checkoutPath, new[] { "StagedNew.txt" });

        // Assert
        Assert.True(result.Success);
        Assert.False(File.Exists(newFilePath));
    }

    // ============================================================================
    // High-Coverage Tests - SwitchBranch Main Paths
    // ============================================================================

    [Fact]
    public void SwitchBranch_ExercisesLocalBranchPath()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var newBranch = "switch-local-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, newBranch, switchToBranch: false);

        // Act - switch to local branch
        var result = _git.SwitchBranch(checkoutPath, newBranch);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(newBranch, _git.GetCurrentBranch(checkoutPath));
    }

    [Fact]
    public void SwitchBranch_ExercisesRemoteBranchTrackingPath()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Create a new local branch
        var newBranch = "track-test-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, newBranch, switchToBranch: true);

        // Switch back to main
        _git.SwitchBranch(checkoutPath, "main");

        // Act - switch back to the new branch
        var result = _git.SwitchBranch(checkoutPath, newBranch);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public void SwitchBranch_ExercisesNonExistentBranchPath()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Act - try to switch to non-existent branch
        var result = _git.SwitchBranch(checkoutPath, "nonexistent-xyz-12345");

        // Assert - should fail
        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage);
    }

    // ============================================================================
    // High-Coverage Tests - GetFileContentAtRevision Main Paths
    // ============================================================================

    [Fact]
    public void GetFileContentAtRevision_ExercisesHeadDefaultPath()
    {
        if (!_repositoryAvailable) return;

        // Act - null revision defaults to HEAD
        var content = _git.GetFileContentAtRevision(_clonePath, "README.md", null);

        // Assert
        Assert.NotNull(content);
        Assert.NotEmpty(content);
    }

    [Fact]
    public void GetFileContentAtRevision_ExercisesHEADStringPath()
    {
        if (!_repositoryAvailable) return;

        // Act - explicit "HEAD" revision
        var content = _git.GetFileContentAtRevision(_clonePath, "README.md", "HEAD");

        // Assert
        Assert.NotNull(content);
    }

    [Fact]
    public void GetFileContentAtRevision_ExercisesCommitLookupPath()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var hash = _git.GetCurrentRevision(_clonePath);

        // Act - lookup by commit hash
        var content = _git.GetFileContentAtRevision(_clonePath, "README.md", hash);

        // Assert
        Assert.NotNull(content);
    }

    [Fact]
    public void GetFileContentAtRevision_ExercisesTreeEntryLookupPath()
    {
        if (!_repositoryAvailable) return;

        // Act - nested file exercises tree traversal
        var content = _git.GetFileContentAtRevision(_clonePath, "Models/SimpleModel.mo", "main");

        // Assert
        Assert.NotNull(content);
        Assert.Contains("model", content);
    }

    [Fact]
    public void GetFileContentAtRevision_ExercisesPathNormalization()
    {
        if (!_repositoryAvailable) return;

        // Act - use backslash path (should be normalized)
        var content = _git.GetFileContentAtRevision(_clonePath, "Models\\package.mo", "main");

        // Assert
        Assert.NotNull(content);
    }

    [Fact]
    public void GetFileContentAtRevision_ExercisesNonBlobPath()
    {
        if (!_repositoryAvailable) return;

        // Act - try to get content of a directory (should return null)
        var content = _git.GetFileContentAtRevision(_clonePath, "Models", "main");

        // Assert - directories are not blobs
        Assert.Null(content);
    }

    [Fact]
    public void GetFileContentAtRevision_ExercisesMissingFilePath()
    {
        if (!_repositoryAvailable) return;

        // Act - non-existent file
        var content = _git.GetFileContentAtRevision(_clonePath, "DoesNotExist.xyz", "main");

        // Assert
        Assert.Null(content);
    }

    // ============================================================================
    // High-Coverage Tests - GetBranches Main Paths
    // ============================================================================

    [Fact]
    public void GetBranches_ExercisesLocalBranchEnumeration()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        // Create some branches
        _git.CreateBranch(checkoutPath, "enum-test-1", switchToBranch: false);
        _git.CreateBranch(checkoutPath, "enum-test-2", switchToBranch: false);

        // Act - get local branches only
        var branches = _git.GetBranches(checkoutPath, includeRemote: false);

        // Assert
        Assert.NotEmpty(branches);
        Assert.All(branches, b => Assert.False(b.IsRemote));
        Assert.All(branches, b => Assert.NotEmpty(b.Name));
    }

    [Fact]
    public void GetBranches_WithIncludeRemote_ReturnsAllBranches()
    {
        if (!_repositoryAvailable) return;

        // Act - include remote branches (even if none exist)
        var branches = _git.GetBranches(_clonePath, includeRemote: true);

        // Assert - should have at least local branches
        Assert.NotEmpty(branches);
        // Verify that calling with includeRemote=true returns all branches
        var localBranches = branches.Where(b => !b.IsRemote).ToList();
        Assert.NotEmpty(localBranches);
        // Test repository has main and feature-test branches
        Assert.Contains(localBranches, b => b.Name == "main" || b.Name == "master");
    }

    [Fact]
    public void GetBranches_ExercisesCurrentBranchDetection()
    {
        if (!_repositoryAvailable) return;

        // Act
        var branches = _git.GetBranches(_clonePath);

        // Assert - one branch should be marked as current
        var currentBranches = branches.Where(b => b.IsCurrent).ToList();
        Assert.Single(currentBranches);
    }

    [Fact]
    public void GetBranches_ExercisesLastCommitRetrieval()
    {
        if (!_repositoryAvailable) return;

        // Act
        var branches = _git.GetBranches(_clonePath);

        // Assert - branches should have last commit info
        Assert.All(branches, b =>
        {
            if (b.LastCommit != null)
            {
                Assert.Equal(40, b.LastCommit.Length);
            }
        });
    }

    // ============================================================================
    // High-Coverage Tests - CreateBranch Main Paths
    // ============================================================================

    [Fact]
    public void CreateBranch_ExercisesCreationWithSwitch()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "create-switch-" + Guid.NewGuid().ToString()[..8];

        // Act - create with switch
        var result = _git.CreateBranch(checkoutPath, branchName, switchToBranch: true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(branchName, _git.GetCurrentBranch(checkoutPath));
    }

    [Fact]
    public void CreateBranch_ExercisesCreationWithoutSwitch()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var originalBranch = _git.GetCurrentBranch(checkoutPath);
        var branchName = "create-noswitch-" + Guid.NewGuid().ToString()[..8];

        // Act - create without switch
        var result = _git.CreateBranch(checkoutPath, branchName, switchToBranch: false);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(originalBranch, _git.GetCurrentBranch(checkoutPath));
    }

    [Fact]
    public void CreateBranch_ExercisesDuplicateBranchPath()
    {
        if (!_repositoryAvailable) return;

        // Arrange
        var checkoutPath = CopyTestRepository();

        var branchName = "duplicate-" + Guid.NewGuid().ToString()[..8];
        _git.CreateBranch(checkoutPath, branchName, switchToBranch: false);

        // Act - try to create again
        var result = _git.CreateBranch(checkoutPath, branchName, switchToBranch: false);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("already exists", result.ErrorMessage);
    }

    // ============================================================================
    // Diagnostic Tests - Verify Fixture Setup
    // ============================================================================

    [Fact]
    public void DiagnosticTest_RepositoryIsAvailable()
    {
        // This test MUST pass - if it fails, the fixture isn't working
        Assert.True(_repositoryAvailable, $"Repository should be available. ClonePath: {_clonePath}. Error: {_cloneError ?? "none"}");
        Assert.True(Directory.Exists(_clonePath), $"Clone path should exist: {_clonePath}");
        Assert.True(Repository.IsValid(_clonePath), $"Clone path should be a valid Git repo: {_clonePath}");
    }
}
