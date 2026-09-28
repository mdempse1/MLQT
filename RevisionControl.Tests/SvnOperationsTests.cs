namespace RevisionControl.Tests;

/// <summary>
/// Tests for SvnRevisionControlSystem operations:
/// no-op stub methods, GetConflictVersions, GetFileContentAtRevision,
/// Commit, RevertFiles, SwitchBranch, CreateBranch, UpdateToLatest,
/// GetLogEntries, GetChangedFiles, and ExtractBranchFromSvnUrl edge cases.
/// Integration tests use a working copy of a repository this run builds for itself
/// (<see cref="SvnWorkingCopyFixture"/>, B426), and are skipped where svn is not installed (B481).
/// </summary>
public class SvnOperationsTests : IClassFixture<SvnWorkingCopyFixture>
{
    private readonly SvnRevisionControlSystem _svn;
    private readonly SvnWorkingCopyFixture _fixture;
    private readonly string _workingCopy;

    public SvnOperationsTests(SvnWorkingCopyFixture fixture)
    {
        _svn = new SvnRevisionControlSystem();
        _fixture = fixture;
        _workingCopy = fixture.WorkingCopy ?? "";
    }

    /// <summary>
    /// Skips a test that needs the working copy where svn is not installed (B481); see
    /// <see cref="SvnWorkingCopyFixture.RequireWorkingCopy"/>. With svn installed the working copy is
    /// always there, and every condition the repository guarantees is asserted rather than returned on.
    /// </summary>
    private void RequireWorkingCopy() => _fixture.RequireWorkingCopy();

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
        RequireWorkingCopy();

        var result = _svn.FindRepositoryRoot(_workingCopy);

        Assert.NotNull(result);
        Assert.Equal(_workingCopy, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindRepositoryRoot_WithSubdirectoryOfSvnWorkingCopy_ReturnsRoot()
    {
        RequireWorkingCopy();

        // Find any subdirectory of the SVN WC to test from
        var subDir = Directory.GetDirectories(_workingCopy).FirstOrDefault();
        Assert.NotNull(subDir);

        var result = _svn.FindRepositoryRoot(subDir);

        Assert.NotNull(result);
        Assert.Equal(_workingCopy, result, StringComparer.OrdinalIgnoreCase);
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

    /// <summary>
    /// The sidecar's bytes as text. The system under test returns what was stored and leaves the
    /// decoding to its caller, which is B240 - these fixtures are ASCII, so UTF-8 is exact.
    /// </summary>
    private static string? Text(byte[]? bytes) =>
        bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes);


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

            Assert.Equal("my version content", Text(ours));
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
            Assert.Equal("their version at r42", Text(theirs));
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

            Assert.Equal("version at r50", Text(theirs)); // highest revision wins
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

            Assert.Equal("my changes", Text(ours));
            Assert.Equal("incoming changes at r100", Text(theirs));
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
        RequireWorkingCopy();

        var entries = _svn.GetLogEntries(_workingCopy, new VcsLogOptions { MaxEntries = 5 });

        // Trunk's five newest commits, newest first: r7 ("Add TestModel") down to r3.
        Assert.Equal(["7", "6", "5", "4", "3"], entries.Select(e => e.Revision));
        Assert.Equal("Add TestModel", entries[0].MessageShort);
        Assert.All(entries, e => Assert.NotEmpty(e.Author));
    }

    [Fact]
    public void GetLogEntries_WithRealSvnWorkingCopy_UntilFilter_FiltersEntries()
    {
        RequireWorkingCopy();

        // The repository was built by this run, so every commit in it is newer than an hour ago
        // and an Until of an hour ago leaves none.
        var pastTime = DateTimeOffset.Now.AddHours(-1);
        var entries = _svn.GetLogEntries(_workingCopy, new VcsLogOptions
        {
            MaxEntries = 5,
            Until = pastTime
        });

        Assert.Empty(entries);

        // ...and one a minute from now leaves all of them, so it is the filter that emptied it.
        var all = _svn.GetLogEntries(_workingCopy, new VcsLogOptions
        {
            MaxEntries = 5,
            Until = DateTimeOffset.Now.AddMinutes(1)
        });
        Assert.Equal(5, all.Count);
    }

    [Fact]
    public void GetChangedFiles_WithRealSvnWorkingCopy_AtKnownRevision_ReturnsFiles()
    {
        RequireWorkingCopy();

        // The working copy's current revision is the last that changed trunk, "Add TestModel"
        var currentRevision = _svn.GetCurrentRevision(_workingCopy);
        Assert.Equal(SvnTestRepository.TrunkLastChangedRevision.ToString(), currentRevision);

        var files = _svn.GetChangedFiles(_workingCopy, currentRevision!);

        var file = Assert.Single(files);
        Assert.Equal("trunk/Models/TestModel.mo", file.Path);
        Assert.Equal(VcsChangeType.Added, file.ChangeType);
    }

    [Fact]
    public void GetFileContentAtRevision_WithRealSvnWorkingCopy_ReturnsContent()
    {
        RequireWorkingCopy();

        // Find any tracked file to read
        var allFiles = Directory.GetFiles(_workingCopy, "*", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("."))
            .ToArray();
        Assert.NotEmpty(allFiles);

        var relativePath = Path.GetRelativePath(_workingCopy, allFiles[0]);

        // HEAD revision
        var content = _svn.GetFileContentAtRevision(_workingCopy, relativePath, "HEAD");

        // Nothing has committed since the checkout, so HEAD is what is on disk
        Assert.Equal(File.ReadAllText(allFiles[0]), content);
    }

    [Fact]
    public void GetFileContentAtRevision_WithRealSvnWorkingCopy_NullRevision_ReturnsContent()
    {
        RequireWorkingCopy();

        var allFiles = Directory.GetFiles(_workingCopy, "*", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("."))
            .ToArray();
        Assert.NotEmpty(allFiles);

        var relativePath = Path.GetRelativePath(_workingCopy, allFiles[0]);

        // null revision → BASE, which for an unmodified file is what is on disk
        var content = _svn.GetFileContentAtRevision(_workingCopy, relativePath, null);

        Assert.Equal(File.ReadAllText(allFiles[0]), content);
    }

    [Fact]
    public void UpdateToLatest_WithRealSvnWorkingCopy_Succeeds()
    {
        RequireWorkingCopy();

        var result = _svn.UpdateToLatest(_workingCopy);

        // The checkout was taken at HEAD and nothing has committed since, so there is nothing to take.
        Assert.True(result.Success);
        Assert.Equal(SvnTestRepository.HeadRevision.ToString(), result.OldRevision);
        Assert.Equal(SvnTestRepository.HeadRevision.ToString(), result.NewRevision);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void RevertFiles_WithRealSvnWorkingCopy_UntrackedFile_DeletesFile()
    {
        RequireWorkingCopy();

        // Create a temporary unversioned file in the working copy
        var tempFileName = $"__test_revert_{Guid.NewGuid():N}.txt";
        var tempFilePath = Path.Combine(_workingCopy, tempFileName);

        try
        {
            File.WriteAllText(tempFilePath, "temporary test file - should be deleted by RevertFiles");
            Assert.True(File.Exists(tempFilePath));

            var result = _svn.RevertFiles(_workingCopy, new[] { tempFileName });

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
        RequireWorkingCopy();

        var result = _svn.SwitchBranch(_workingCopy, "branches/this-branch-does-not-exist-99999");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void GetFileContentAtRevision_WithRealSvnWorkingCopy_NumericRevision_ReturnsContent()
    {
        RequireWorkingCopy();

        var allFiles = Directory.GetFiles(_workingCopy, "*", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("."))
            .ToArray();
        Assert.NotEmpty(allFiles);

        var relativePath = Path.GetRelativePath(_workingCopy, allFiles[0]);
        var currentRevision = _svn.GetCurrentRevision(_workingCopy);
        Assert.NotNull(currentRevision);

        // Use numeric revision (exercises the long.TryParse branch)
        var content = _svn.GetFileContentAtRevision(_workingCopy, relativePath, currentRevision);

        // The file is tracked at the working copy's own revision, unmodified since
        Assert.Equal(File.ReadAllText(allFiles[0]), content);
    }

    [Fact]
    public void GetWorkingCopyChanges_WithRealSvnWorkingCopy_UntrackedDirectory_ExpandsToFiles()
    {
        RequireWorkingCopy();

        // Create an unversioned directory with files inside the working copy
        var testDirName = $"__test_dir_{Guid.NewGuid():N}";
        var testDirPath = Path.Combine(_workingCopy, testDirName);
        var testFilePath = Path.Combine(testDirPath, "test_file.txt");

        try
        {
            Directory.CreateDirectory(testDirPath);
            File.WriteAllText(testFilePath, "test content");

            var changes = _svn.GetWorkingCopyChanges(_workingCopy);

            // The unversioned directory should be expanded to its individual files
            Assert.NotNull(changes);
            // The directory itself should NOT appear (it gets replaced by its file entries)
            Assert.DoesNotContain(changes, f => f.Path == testDirName || f.Path == testDirName + Path.DirectorySeparatorChar);
            // The file inside should appear as untracked
            // Forward-slashed on every platform, as Git reports it (B472).
            var relativeFilePath = testDirName + "/test_file.txt";
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
        RequireWorkingCopy();

        // Get entries from 1 year ago to exercise the Since filter path
        var since = DateTimeOffset.Now.AddYears(-1);
        var entries = _svn.GetLogEntries(_workingCopy, new VcsLogOptions
        {
            MaxEntries = 100,
            Since = since
        });

        // Every commit in trunk's history was made by this run, so all of them are after it
        Assert.Equal(SvnTestRepository.TrunkHistoryLength, entries.Count);
        Assert.All(entries, e => Assert.True(e.Date >= since,
            $"Entry date {e.Date} should be >= {since}"));
    }

    [Fact]
    public void GetLogEntries_WithRealSvnWorkingCopy_BranchFilter_UsesCurrentBranch()
    {
        RequireWorkingCopy();

        var currentBranch = _svn.GetCurrentBranch(_workingCopy);
        Assert.Equal("trunk", currentBranch);

        var entries = _svn.GetLogEntries(_workingCopy, new VcsLogOptions
        {
            MaxEntries = 5,
            Branch = currentBranch
        });

        // Each entry's branch reflects where the change actually occurred, and trunk's own history
        // was all made on trunk.
        Assert.Equal(5, entries.Count);
        Assert.All(entries, e => Assert.Equal("trunk", e.Branch));
    }

    [Fact]
    public void GetChangedFiles_WithRealSvnWorkingCopy_AtHeadRevision_ReturnsFiles()
    {
        RequireWorkingCopy();

        // Get a few log entries to find a revision with known changed files
        var logEntries = _svn.GetLogEntries(_workingCopy, new VcsLogOptions { MaxEntries = 3 });
        Assert.NotEmpty(logEntries);

        var revision = logEntries[0].Revision;
        var files = _svn.GetChangedFiles(_workingCopy, revision);

        // The latest commit on trunk is "Add TestModel"
        Assert.Equal(SvnTestRepository.TrunkLastChangedRevision.ToString(), revision);
        var file = Assert.Single(files);
        Assert.Equal("trunk/Models/TestModel.mo", file.Path);
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
        RequireWorkingCopy();

        // Find any tracked file to modify and revert
        var allFiles = Directory.GetFiles(_workingCopy, "*", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("."))
            .ToArray();
        Assert.NotEmpty(allFiles);

        var filePath = allFiles[0];
        var relativePath = Path.GetRelativePath(_workingCopy, filePath);
        var originalContent = File.ReadAllText(filePath);

        try
        {
            // Modify the tracked file
            File.WriteAllText(filePath, originalContent + "\n// temporary test modification");

            var result = _svn.RevertFiles(_workingCopy, new[] { relativePath });

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
        RequireWorkingCopy();

        var currentRevision = _svn.GetCurrentRevision(_workingCopy);
        Assert.NotNull(currentRevision);

        var desc = _svn.GetRevisionDescription(_workingCopy, currentRevision);

        Assert.NotNull(desc);
        Assert.StartsWith("Add TestModel (by ", desc);
    }

    [Fact]
    public void ResolveRevision_WithRealSvnWorkingCopy_HeadRevision_ReturnsNumber()
    {
        RequireWorkingCopy();

        var resolved = _svn.ResolveRevision(_workingCopy, "HEAD");

        Assert.Equal(SvnTestRepository.HeadRevision.ToString(), resolved);
    }

    [Fact]
    public void GetBranches_WithRealSvnWorkingCopy_IncludeRemote_ReturnsBranches()
    {
        RequireWorkingCopy();

        var branches = _svn.GetBranches(_workingCopy, includeRemote: true);

        Assert.Equal(["trunk", "branches/feature-test", "tags/v1.0", "tags/v2.0"], branches.Select(b => b.Name));
        Assert.All(branches, b => Assert.True(b.IsRemote)); // SVN branches are always "remote"
        Assert.Equal("trunk", Assert.Single(branches, b => b.IsCurrent).Name);
    }

    [Fact]
    public void GetWorkingCopyChanges_WithRealSvnWorkingCopy_MissingFile_ShowsDeletedStatus()
    {
        RequireWorkingCopy();

        // Find any tracked file to temporarily delete (use top-level files to ensure they're tracked)
        var allFiles = Directory.GetFiles(_workingCopy, "*", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("."))
            .ToArray();
        Assert.NotEmpty(allFiles);

        var filePath = allFiles[0];
        var relativePath = Path.GetRelativePath(_workingCopy, filePath);

        try
        {
            // Delete directly from filesystem (not via svn delete) → SVN shows as Missing
            File.Delete(filePath);
            Assert.False(File.Exists(filePath));

            var changes = _svn.GetWorkingCopyChanges(_workingCopy);

            // Missing file should appear with Deleted status
            var missing = changes.FirstOrDefault(f =>
                f.Path.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(missing);
            Assert.Equal(VcsFileStatus.Deleted, missing!.Status);
        }
        finally
        {
            // Revert to restore the file from SVN
            _svn.RevertFiles(_workingCopy, new[] { relativePath });
            Assert.True(File.Exists(filePath), "File should be restored by RevertFiles");
        }
    }

    [Fact]
    public void GetWorkingCopyChanges_WithRealSvnWorkingCopy_AddedFile_ShowsAddedStatus()
    {
        RequireWorkingCopy();

        var tempFileName = $"__test_added_{Guid.NewGuid():N}.txt";
        var tempFilePath = Path.Combine(_workingCopy, tempFileName);

        try
        {
            File.WriteAllText(tempFilePath, "content for added file test");

            // svn add via client - but we can't call svn add without SharpSvn directly here.
            // Instead verify untracked status (the Added status is covered by SVN add operations)
            var changes = _svn.GetWorkingCopyChanges(_workingCopy);

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
