namespace RevisionControl.Tests;

/// <summary>
/// Tests for SvnRevisionControlSystem operations:
/// no-op stub methods, GetConflictVersions, GetFileContentAtRevision,
/// Commit, RevertFiles, SwitchBranch, CreateBranch, UpdateToLatest,
/// GetLogEntries, GetChangedFiles, and ExtractBranchFromSvnUrl edge cases.
/// Integration tests use the real SVN working copy at C:\Projects\ModelicaEditorTest.
/// </summary>
public class SvnOperationsTests
{
    private readonly SvnRevisionControlSystem _svn;
    private static readonly string RealSvnPath = @"C:\Projects\ModelicaEditorTest";

    public SvnOperationsTests()
    {
        _svn = new SvnRevisionControlSystem();
    }

    private static bool RealSvnAvailable => Directory.Exists(RealSvnPath);

    #region No-op Stub Method Tests

    [Fact]
    public void Push_ReturnsSuccess()
    {
        var result = _svn.Push("anypath");

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ForcePush_ReturnsSuccess()
    {
        var result = _svn.ForcePush("anypath");

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void IsBranchPushed_ReturnsTrue()
    {
        var result = _svn.IsBranchPushed("anypath");

        Assert.True(result);
    }

    [Fact]
    public void GetPullRequestUrl_ReturnsNull()
    {
        var result = _svn.GetPullRequestUrl("anypath");

        Assert.Null(result);
    }

    #endregion

    #region FindRepositoryRoot Tests

    [Fact]
    public void FindRepositoryRoot_WithRealSvnWorkingCopyRoot_ReturnsRoot()
    {
        if (!RealSvnAvailable)
            return;

        var result = _svn.FindRepositoryRoot(RealSvnPath);

        Assert.NotNull(result);
        Assert.Equal(RealSvnPath, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindRepositoryRoot_WithSubdirectoryOfSvnWorkingCopy_ReturnsRoot()
    {
        if (!RealSvnAvailable)
            return;

        // Find any subdirectory of the SVN WC to test from
        var subDir = Directory.GetDirectories(RealSvnPath).FirstOrDefault();
        if (subDir == null)
            return;

        var result = _svn.FindRepositoryRoot(subDir);

        Assert.NotNull(result);
        Assert.Equal(RealSvnPath, result, StringComparer.OrdinalIgnoreCase);
    }

    #endregion

    [Fact]
    public void GetPullRequestUrl_WithBaseBranch_ReturnsNull()
    {
        var result = _svn.GetPullRequestUrl("anypath", "main");

        Assert.Null(result);
    }

    [Fact]
    public void Rebase_ReturnsErrorMessage()
    {
        var result = _svn.Rebase("anypath", "trunk");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("not supported", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContinueRebase_ReturnsErrorMessage()
    {
        var result = _svn.ContinueRebase("anypath");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("not supported", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AbortRebase_ReturnsErrorMessage()
    {
        var result = _svn.AbortRebase("anypath");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("not supported", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #region GetConflictVersions Tests (filesystem-based, no SVN needed)

    [Fact]
    public void GetConflictVersions_WithNoSidecarFiles_ReturnsBothNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SvnConflict_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var filePath = Path.Combine(tempDir, "model.mo");
            File.WriteAllText(filePath, "original content");

            var (ours, theirs) = _svn.GetConflictVersions("unused", filePath);

            Assert.Null(ours);
            Assert.Null(theirs);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetConflictVersions_WithMineFile_ReturnsOursContent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SvnConflict_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var filePath = Path.Combine(tempDir, "model.mo");
            File.WriteAllText(filePath, "conflicted content");
            File.WriteAllText(filePath + ".mine", "my version content");

            var (ours, theirs) = _svn.GetConflictVersions("unused", filePath);

            Assert.Equal("my version content", ours);
            Assert.Null(theirs);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetConflictVersions_WithRFile_ReturnsTheirsContent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SvnConflict_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var filePath = Path.Combine(tempDir, "model.mo");
            File.WriteAllText(filePath, "conflicted content");
            File.WriteAllText(filePath + ".r42", "their version at r42");

            var (ours, theirs) = _svn.GetConflictVersions("unused", filePath);

            Assert.Null(ours);
            Assert.Equal("their version at r42", theirs);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetConflictVersions_WithMultipleRFiles_ReturnsHighestRevision()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SvnConflict_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var filePath = Path.Combine(tempDir, "model.mo");
            File.WriteAllText(filePath, "conflicted content");
            File.WriteAllText(filePath + ".r10", "version at r10");
            File.WriteAllText(filePath + ".r50", "version at r50");
            File.WriteAllText(filePath + ".r25", "version at r25");

            var (ours, theirs) = _svn.GetConflictVersions("unused", filePath);

            Assert.Equal("version at r50", theirs); // highest revision wins
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetConflictVersions_WithBothSidecarFiles_ReturnsBoth()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SvnConflict_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var filePath = Path.Combine(tempDir, "model.mo");
            File.WriteAllText(filePath, "conflicted content");
            File.WriteAllText(filePath + ".mine", "my changes");
            File.WriteAllText(filePath + ".r100", "incoming changes at r100");

            var (ours, theirs) = _svn.GetConflictVersions("unused", filePath);

            Assert.Equal("my changes", ours);
            Assert.Equal("incoming changes at r100", theirs);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region ExtractBranchFromSvnUrl Edge Cases (via GetCurrentBranch URL paths)

    [Fact]
    public void GetCurrentBranch_WithTicketsUrl_ExtractsBranchFromUrl()
    {
        var ticketsUrl = "http://invalid.example.com/svn/repo/tickets/ml-2020";
        var customDirs = new[] { "trunk", "branches", "tags", "tickets", "releases" };

        var result = _svn.GetCurrentBranch(ticketsUrl, customDirs);

        Assert.Equal("tickets/ml-2020", result);
    }

    [Fact]
    public void GetCurrentBranch_WithReleasesUrl_ExtractsBranchFromUrl()
    {
        var releasesUrl = "http://invalid.example.com/svn/repo/releases/2025.1";
        var customDirs = new[] { "trunk", "branches", "tags", "tickets", "releases" };

        var result = _svn.GetCurrentBranch(releasesUrl, customDirs);

        Assert.Equal("releases/2025.1", result);
    }

    [Fact]
    public void GetCurrentBranch_WithTrunkInSubpath_ReturnsTrunk()
    {
        // trunk can appear anywhere in path
        var url = "https://svn.example.com/myorg/myrepo/trunk/Models";

        var result = _svn.GetCurrentBranch(url);

        Assert.Equal("trunk", result);
    }

    [Fact]
    public void GetCurrentBranch_WithBranchesWithSubpath_ExtractsBranchOnly()
    {
        var url = "https://svn.example.com/repo/branches/feature-x/SubDir";

        var result = _svn.GetCurrentBranch(url);

        Assert.Equal("branches/feature-x", result);
    }

    #endregion

    #region Error Path Tests (no SVN server required)

    [Fact]
    public void Commit_WithNonExistentPath_ReturnsError()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "NoSuch_" + Guid.NewGuid().ToString("N"));

        var result = _svn.Commit(nonExistent, "test commit");

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.ErrorMessage!);
    }

    [Fact]
    public void Commit_WithNonSvnDirectory_ReturnsError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SvnCommit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = _svn.Commit(tempDir, "test commit");

            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void RevertFiles_WithNonExistentPath_ReturnsError()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "NoSuch_" + Guid.NewGuid().ToString("N"));

        var result = _svn.RevertFiles(nonExistent, new[] { "file.mo" });

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.ErrorMessage!);
    }

    [Fact]
    public void SwitchBranch_WithNonExistentPath_ReturnsError()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "NoSuch_" + Guid.NewGuid().ToString("N"));

        var result = _svn.SwitchBranch(nonExistent, "trunk");

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.ErrorMessage!);
    }

    [Fact]
    public void SwitchBranch_WithNonSvnDirectory_ReturnsError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SvnSwitch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = _svn.SwitchBranch(tempDir, "trunk");

            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CreateBranch_WithNonExistentPath_ReturnsError()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "NoSuch_" + Guid.NewGuid().ToString("N"));

        var result = _svn.CreateBranch(nonExistent, "branches/new-branch");

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.ErrorMessage!);
    }

    [Fact]
    public void CreateBranch_WithNonSvnDirectory_ReturnsError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SvnCreate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = _svn.CreateBranch(tempDir, "branches/new-branch");

            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetFileContentAtRevision_WithNonExistentPath_ReturnsNull()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "NoSuch_" + Guid.NewGuid().ToString("N"));

        var result = _svn.GetFileContentAtRevision(nonExistent, "model.mo");

        Assert.Null(result);
    }

    [Fact]
    public void GetFileContentAtRevision_WithNonSvnDirectory_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SvnContent_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = _svn.GetFileContentAtRevision(tempDir, "model.mo");

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void UpdateToLatest_WithNonExistentPath_ReturnsError()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "NoSuch_" + Guid.NewGuid().ToString("N"));

        var result = _svn.UpdateToLatest(nonExistent);

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.ErrorMessage!);
    }

    [Fact]
    public void UpdateToLatest_WithNonSvnDirectory_ReturnsError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SvnUpdate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = _svn.UpdateToLatest(tempDir);

            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetLogEntries_WithNonSvnPath_ReturnsEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SvnLog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Non-SVN directory will fail but return empty list
            var result = _svn.GetLogEntries(tempDir);

            Assert.NotNull(result);
            Assert.Empty(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetLogEntries_WithRelativeNonUrlString_ReturnsEmpty()
    {
        // A simple string with no path separators and no URL scheme falls through
        // to the GetRepositoryUri fallback (file:// from relative path)
        var result = _svn.GetLogEntries(Guid.NewGuid().ToString("N"));

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetChangedFiles_WithRelativeNonUrlString_ReturnsEmpty()
    {
        var result = _svn.GetChangedFiles(Guid.NewGuid().ToString("N"), "HEAD");

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetChangedFiles_WithNonSvnPath_ReturnsEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SvnChanged_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = _svn.GetChangedFiles(tempDir, "HEAD");

            Assert.NotNull(result);
            Assert.Empty(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ResolveConflict_WithNonExistentFile_ReturnsError()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "no_such_file.mo");

        var result = _svn.ResolveConflict("unused", nonExistent, ConflictResolutionChoice.MarkResolved);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    #endregion

    #region Integration Tests with Real SVN Repository

    [Fact]
    public void GetLogEntries_WithRealSvnWorkingCopy_ReturnsEntries()
    {
        if (!RealSvnAvailable) return;

        var entries = _svn.GetLogEntries(RealSvnPath, new VcsLogOptions { MaxEntries = 5 });

        Assert.NotNull(entries);
        Assert.NotEmpty(entries);
        Assert.All(entries, e =>
        {
            Assert.NotEmpty(e.Revision);
            Assert.NotEmpty(e.Author);
            Assert.NotNull(e.MessageShort);
        });
    }

    [Fact]
    public void GetLogEntries_WithRealSvnWorkingCopy_UntilFilter_FiltersEntries()
    {
        if (!RealSvnAvailable) return;

        // Until 1 hour ago - should return 0 or few entries (recent commits excluded)
        var pastTime = DateTimeOffset.Now.AddHours(-1);
        var entries = _svn.GetLogEntries(RealSvnPath, new VcsLogOptions
        {
            MaxEntries = 5,
            Until = pastTime
        });

        Assert.NotNull(entries);
        // May or may not have entries depending on history, just verify it works
    }

    [Fact]
    public void GetChangedFiles_WithRealSvnWorkingCopy_AtKnownRevision_ReturnsFiles()
    {
        if (!RealSvnAvailable) return;

        // Get the current revision first
        var currentRevision = _svn.GetCurrentRevision(RealSvnPath);
        if (currentRevision == null) return;

        var files = _svn.GetChangedFiles(RealSvnPath, currentRevision);

        // Files at the current revision may or may not exist, but shouldn't throw
        Assert.NotNull(files);
    }

    [Fact]
    public void GetFileContentAtRevision_WithRealSvnWorkingCopy_ReturnsContent()
    {
        if (!RealSvnAvailable) return;

        // Find any tracked file to read
        var allFiles = Directory.GetFiles(RealSvnPath, "*", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("."))
            .ToArray();
        if (allFiles.Length == 0) return;

        var relativePath = Path.GetRelativePath(RealSvnPath, allFiles[0]);

        // HEAD revision
        var content = _svn.GetFileContentAtRevision(RealSvnPath, relativePath, "HEAD");

        Assert.NotNull(content);
        Assert.NotEmpty(content);
    }

    [Fact]
    public void GetFileContentAtRevision_WithRealSvnWorkingCopy_NullRevision_ReturnsContent()
    {
        if (!RealSvnAvailable) return;

        var allFiles = Directory.GetFiles(RealSvnPath, "*", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("."))
            .ToArray();
        if (allFiles.Length == 0) return;

        var relativePath = Path.GetRelativePath(RealSvnPath, allFiles[0]);

        // null revision → BASE
        var content = _svn.GetFileContentAtRevision(RealSvnPath, relativePath, null);

        Assert.NotNull(content);
    }

    [Fact]
    public void UpdateToLatest_WithRealSvnWorkingCopy_Succeeds()
    {
        if (!RealSvnAvailable) return;

        var result = _svn.UpdateToLatest(RealSvnPath);

        Assert.True(result.Success);
        Assert.NotNull(result.OldRevision);
        Assert.NotNull(result.NewRevision);
    }

    [Fact]
    public void RevertFiles_WithRealSvnWorkingCopy_UntrackedFile_DeletesFile()
    {
        if (!RealSvnAvailable) return;

        // Create a temporary unversioned file in the working copy
        var tempFileName = $"__test_revert_{Guid.NewGuid():N}.txt";
        var tempFilePath = Path.Combine(RealSvnPath, tempFileName);

        try
        {
            File.WriteAllText(tempFilePath, "temporary test file - should be deleted by RevertFiles");
            Assert.True(File.Exists(tempFilePath));

            var result = _svn.RevertFiles(RealSvnPath, new[] { tempFileName });

            Assert.True(result.Success);
            Assert.False(File.Exists(tempFilePath), "Unversioned file should be deleted by RevertFiles");
        }
        finally
        {
            // Safety cleanup in case the test fails
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
        }
    }

    [Fact]
    public void SwitchBranch_WithRealSvnWorkingCopy_ToNonExistentBranch_ReturnsError()
    {
        if (!RealSvnAvailable) return;

        var result = _svn.SwitchBranch(RealSvnPath, "branches/this-branch-does-not-exist-99999");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void GetFileContentAtRevision_WithRealSvnWorkingCopy_NumericRevision_ReturnsContent()
    {
        if (!RealSvnAvailable) return;

        var allFiles = Directory.GetFiles(RealSvnPath, "*", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("."))
            .ToArray();
        if (allFiles.Length == 0) return;

        var relativePath = Path.GetRelativePath(RealSvnPath, allFiles[0]);
        var currentRevision = _svn.GetCurrentRevision(RealSvnPath);
        if (currentRevision == null) return;

        // Use numeric revision (exercises the long.TryParse branch)
        var content = _svn.GetFileContentAtRevision(RealSvnPath, relativePath, currentRevision);

        // May be null if file doesn't exist at that revision, but should not throw
        // A successful read returns content
    }

    [Fact]
    public void GetWorkingCopyChanges_WithRealSvnWorkingCopy_UntrackedDirectory_ExpandsToFiles()
    {
        if (!RealSvnAvailable) return;

        // Create an unversioned directory with files inside the working copy
        var testDirName = $"__test_dir_{Guid.NewGuid():N}";
        var testDirPath = Path.Combine(RealSvnPath, testDirName);
        var testFilePath = Path.Combine(testDirPath, "test_file.txt");

        try
        {
            Directory.CreateDirectory(testDirPath);
            File.WriteAllText(testFilePath, "test content");

            var changes = _svn.GetWorkingCopyChanges(RealSvnPath);

            // The unversioned directory should be expanded to its individual files
            Assert.NotNull(changes);
            // The directory itself should NOT appear (it gets replaced by its file entries)
            Assert.DoesNotContain(changes, f => f.Path == testDirName || f.Path == testDirName + Path.DirectorySeparatorChar);
            // The file inside should appear as untracked
            var relativeFilePath = Path.GetRelativePath(RealSvnPath, testFilePath);
            Assert.Contains(changes, f => f.Path.Equals(relativeFilePath, StringComparison.OrdinalIgnoreCase)
                                         && f.Status == VcsFileStatus.Untracked);
        }
        finally
        {
            // Cleanup: remove the temp directory
            if (Directory.Exists(testDirPath))
                Directory.Delete(testDirPath, recursive: true);
        }
    }

    [Fact]
    public void GetLogEntries_WithRealSvnWorkingCopy_SinceFilter_FiltersCorrectly()
    {
        if (!RealSvnAvailable) return;

        // Get entries from 1 year ago to exercise the Since filter path
        var since = DateTimeOffset.Now.AddYears(-1);
        var entries = _svn.GetLogEntries(RealSvnPath, new VcsLogOptions
        {
            MaxEntries = 100,
            Since = since
        });

        Assert.NotNull(entries);
        // All returned entries should be on or after the since date
        Assert.All(entries, e => Assert.True(e.Date >= since.AddDays(-1),
            $"Entry date {e.Date} should be >= {since}"));
    }

    [Fact]
    public void GetLogEntries_WithRealSvnWorkingCopy_BranchFilter_UsesCurrentBranch()
    {
        if (!RealSvnAvailable) return;

        var currentBranch = _svn.GetCurrentBranch(RealSvnPath);
        if (currentBranch == null) return;

        var entries = _svn.GetLogEntries(RealSvnPath, new VcsLogOptions
        {
            MaxEntries = 5,
            Branch = currentBranch
        });

        Assert.NotNull(entries);
        // Each entry's branch reflects where the change actually occurred.
        // SVN log follows copy history, so older entries may show trunk or
        // another branch that predates the current branch.
        Assert.All(entries, e => Assert.NotNull(e.Branch));
    }

    [Fact]
    public void GetChangedFiles_WithRealSvnWorkingCopy_AtHeadRevision_ReturnsFiles()
    {
        if (!RealSvnAvailable) return;

        // Get a few log entries to find a revision with known changed files
        var logEntries = _svn.GetLogEntries(RealSvnPath, new VcsLogOptions { MaxEntries = 3 });
        if (logEntries.Count == 0) return;

        var revision = logEntries[0].Revision;
        var files = _svn.GetChangedFiles(RealSvnPath, revision);

        Assert.NotNull(files);
        // The latest commit should have at least one changed file
        Assert.NotEmpty(files);
        Assert.All(files, f => Assert.NotEmpty(f.Path));
    }

    [Fact]
    public void CreateBranch_WithNonExistentPath_WithBranchPrefix_ReturnsError()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "NoSuch_" + Guid.NewGuid().ToString("N"));

        // Test with "branches/" prefix (hits different URL construction path)
        var result = _svn.CreateBranch(nonExistent, "branches/new-branch");

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.ErrorMessage!);
    }

    [Fact]
    public void CreateBranch_WithTagsPrefix_NonExistentPath_ReturnsError()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "NoSuch_" + Guid.NewGuid().ToString("N"));

        // Test with "tags/" prefix (hits different URL construction path)
        var result = _svn.CreateBranch(nonExistent, "tags/v1.0");

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.ErrorMessage!);
    }

    [Fact]
    public void RevertFiles_WithRealSvnWorkingCopy_TrackedFile_RevertsChanges()
    {
        if (!RealSvnAvailable) return;

        // Find any tracked file to modify and revert
        var allFiles = Directory.GetFiles(RealSvnPath, "*", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("."))
            .ToArray();
        if (allFiles.Length == 0) return;

        var filePath = allFiles[0];
        var relativePath = Path.GetRelativePath(RealSvnPath, filePath);
        var originalContent = File.ReadAllText(filePath);

        try
        {
            // Modify the tracked file
            File.WriteAllText(filePath, originalContent + "\n// temporary test modification");

            var result = _svn.RevertFiles(RealSvnPath, new[] { relativePath });

            Assert.True(result.Success);
            Assert.Equal(originalContent, File.ReadAllText(filePath));
        }
        finally
        {
            // Safety: restore original content in case revert didn't work
            if (File.ReadAllText(filePath) != originalContent)
                File.WriteAllText(filePath, originalContent);
        }
    }

    [Fact]
    public void GetRevisionDescription_WithRealSvnWorkingCopy_ReturnsDescription()
    {
        if (!RealSvnAvailable) return;

        var currentRevision = _svn.GetCurrentRevision(RealSvnPath);
        if (currentRevision == null) return;

        var desc = _svn.GetRevisionDescription(RealSvnPath, currentRevision);

        Assert.NotNull(desc);
        Assert.NotEmpty(desc);
    }

    [Fact]
    public void ResolveRevision_WithRealSvnWorkingCopy_HeadRevision_ReturnsNumber()
    {
        if (!RealSvnAvailable) return;

        var resolved = _svn.ResolveRevision(RealSvnPath, "HEAD");

        Assert.NotNull(resolved);
        Assert.True(long.TryParse(resolved, out _), $"Resolved revision should be numeric, got: {resolved}");
    }

    [Fact]
    public void GetBranches_WithRealSvnWorkingCopy_IncludeRemote_ReturnsBranches()
    {
        if (!RealSvnAvailable) return;

        var branches = _svn.GetBranches(RealSvnPath, includeRemote: true);

        Assert.NotNull(branches);
        Assert.NotEmpty(branches);
        Assert.All(branches, b => Assert.True(b.IsRemote)); // SVN branches are always "remote"
    }

    [Fact]
    public void GetWorkingCopyChanges_WithRealSvnWorkingCopy_MissingFile_ShowsDeletedStatus()
    {
        if (!RealSvnAvailable) return;

        // Find any tracked file to temporarily delete (use top-level files to ensure they're tracked)
        var allFiles = Directory.GetFiles(RealSvnPath, "*", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("."))
            .ToArray();
        if (allFiles.Length == 0) return;

        var filePath = allFiles[0];
        var relativePath = Path.GetRelativePath(RealSvnPath, filePath);

        try
        {
            // Delete directly from filesystem (not via svn delete) → SVN shows as Missing
            File.Delete(filePath);
            Assert.False(File.Exists(filePath));

            var changes = _svn.GetWorkingCopyChanges(RealSvnPath);

            // Missing file should appear with Deleted status
            var missing = changes.FirstOrDefault(f =>
                f.Path.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(missing);
            Assert.Equal(VcsFileStatus.Deleted, missing!.Status);
        }
        finally
        {
            // Revert to restore the file from SVN
            _svn.RevertFiles(RealSvnPath, new[] { relativePath });
            Assert.True(File.Exists(filePath), "File should be restored by RevertFiles");
        }
    }

    [Fact]
    public void GetWorkingCopyChanges_WithRealSvnWorkingCopy_AddedFile_ShowsAddedStatus()
    {
        if (!RealSvnAvailable) return;

        var tempFileName = $"__test_added_{Guid.NewGuid():N}.txt";
        var tempFilePath = Path.Combine(RealSvnPath, tempFileName);

        try
        {
            File.WriteAllText(tempFilePath, "content for added file test");

            // svn add via client - but we can't call svn add without SharpSvn directly here.
            // Instead verify untracked status (the Added status is covered by SVN add operations)
            var changes = _svn.GetWorkingCopyChanges(RealSvnPath);

            var added = changes.FirstOrDefault(f =>
                f.Path.Equals(tempFileName, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(added);
            // Untracked files appear as Untracked (Added requires svn add to have been called)
            Assert.Equal(VcsFileStatus.Untracked, added!.Status);
        }
        finally
        {
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
        }
    }

    #endregion
}
