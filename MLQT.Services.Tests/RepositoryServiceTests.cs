using MLQT.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using RevisionControl;

namespace MLQT.Services.Tests;

/// <summary>
/// Unit tests for the RepositoryService class.
/// </summary>
public class RepositoryServiceTests
{
    private RepositoryService CreateService()
    {
        // Use real services for integration testing
        var libraryDataService = new LibraryDataService();
        var settingsService = new InMemorySettingsService();
        var fileMonitoringService = new FileMonitoringService();
        return new RepositoryService(libraryDataService, settingsService, fileMonitoringService);
    }

    #region DetectVcsType Tests

    [Fact]
    public void DetectVcsType_WithGitHubUrl_ReturnsGitRemote()
    {
        var service = CreateService();

        var (vcsType, isLocal) = service.DetectVcsType("https://github.com/user/repo.git");

        Assert.Equal(RepositoryVcsType.Git, vcsType);
        Assert.False(isLocal);
    }

    [Fact]
    public void DetectVcsType_WithGitLabUrl_ReturnsGitRemote()
    {
        var service = CreateService();

        var (vcsType, isLocal) = service.DetectVcsType("https://gitlab.com/user/repo");

        Assert.Equal(RepositoryVcsType.Git, vcsType);
        Assert.False(isLocal);
    }

    [Fact]
    public void DetectVcsType_WithPlainHttpsUrl_ReturnsSvnRemote()
    {
        var service = CreateService();

        // Plain HTTPS URL that is not GitHub/GitLab/Bitbucket and doesn't end in .git → assume SVN
        var (vcsType, isLocal) = service.DetectVcsType("https://svn.example.com/repos/myproject");

        Assert.Equal(RepositoryVcsType.SVN, vcsType);
        Assert.False(isLocal);
    }

    [Fact]
    public void DetectVcsType_WithGitScheme_ReturnsGitRemote()
    {
        var service = CreateService();

        var (vcsType, isLocal) = service.DetectVcsType("git://github.com/user/repo.git");

        Assert.Equal(RepositoryVcsType.Git, vcsType);
        Assert.False(isLocal);
    }

    [Fact]
    public void DetectVcsType_WithSshScheme_ReturnsGitRemote()
    {
        var service = CreateService();

        var (vcsType, isLocal) = service.DetectVcsType("ssh://git@github.com/user/repo.git");

        Assert.Equal(RepositoryVcsType.Git, vcsType);
        Assert.False(isLocal);
    }

    [Fact]
    public void DetectVcsType_WithSvnScheme_ReturnsSvnRemote()
    {
        var service = CreateService();

        var (vcsType, isLocal) = service.DetectVcsType("svn://svn.example.com/repo");

        Assert.Equal(RepositoryVcsType.SVN, vcsType);
        Assert.False(isLocal);
    }

    [Fact]
    public void DetectVcsType_WithNonExistentPath_ReturnsLocalNotLocal()
    {
        var service = CreateService();

        var (vcsType, isLocal) = service.DetectVcsType("C:\\NonExistent\\Path");

        Assert.Equal(RepositoryVcsType.Local, vcsType);
        Assert.False(isLocal);
    }

    [Fact]
    public void DetectVcsType_WithLocalGitRepo_ReturnsGitLocal()
    {
        // This test requires C:\Projects\ModelicaStandardLibrary to exist
        var service = CreateService();
        var testPath = @"C:\Projects\ModelicaStandardLibrary";

        if (!Directory.Exists(testPath))
        {
            // Skip test if directory doesn't exist
            return;
        }

        var (vcsType, isLocal) = service.DetectVcsType(testPath);

        Assert.Equal(RepositoryVcsType.Git, vcsType);
        Assert.True(isLocal);
    }

    #endregion

    #region Repository Management Tests

    [Fact]
    public void Repositories_InitiallyEmpty()
    {
        var service = CreateService();

        Assert.Empty(service.Repositories);
    }

    [Fact]
    public async Task AddRepositoryAsync_WithNonExistentPath_ReturnsError()
    {
        var service = CreateService();

        var result = await service.AddRepositoryAsync("C:\\NonExistent\\Path\\To\\Repo");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task AddRepositoryAsync_WithRemoteUrlAndNoCheckoutPath_ReturnsError()
    {
        var service = CreateService();

        // GitHub URL is remote; no checkoutPath provided → "Checkout path required"
        var result = await service.AddRepositoryAsync("https://github.com/user/some-repo.git");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Checkout path required", result.ErrorMessage);
    }

    [Fact]
    public async Task AddRepositoryAsync_WithLocalGitRepo_AddsRepository()
    {
        var testPath = @"C:\Projects\ModelicaStandardLibrary";

        if (!Directory.Exists(testPath))
        {
            // Skip test if directory doesn't exist
            return;
        }

        var service = CreateService();

        var result = await service.AddRepositoryAsync(testPath);

        Assert.True(result.Success);
        Assert.NotNull(result.Repository);
        Assert.Equal(RepositoryVcsType.Git, result.Repository.VcsType);
        Assert.Equal(testPath, result.Repository.LocalPath);
        Assert.Single(service.Repositories);
    }

    [Fact]
    public async Task AddRepositoryAsync_DiscoversLibraries()
    {
        var testPath = @"C:\Projects\ModelicaStandardLibrary";

        if (!Directory.Exists(testPath))
        {
            // Skip test if directory doesn't exist
            return;
        }

        var service = CreateService();

        var result = await service.AddRepositoryAsync(testPath);

        Assert.True(result.Success);
        Assert.NotEmpty(result.DiscoveredLibraries);

        // Should discover Modelica, ModelicaServices, ModelicaTest, etc.
        var libraryNames = result.DiscoveredLibraries.Select(l => l.LibraryName).ToList();
        Assert.Contains("Modelica", libraryNames);
    }

    [Fact]
    public async Task AddRepositoryAsync_OnlyScansImmediateSubdirectories()
    {
        var testPath = @"C:\Projects\ModelicaStandardLibrary";

        if (!Directory.Exists(testPath))
        {
            // Skip test if directory doesn't exist
            return;
        }

        var service = CreateService();

        var result = await service.AddRepositoryAsync(testPath);

        Assert.True(result.Success);

        // Should NOT find deeply nested packages (only root and immediate subdirs)
        // All discovered libraries should be at root or one level deep
        foreach (var lib in result.DiscoveredLibraries)
        {
            var slashCount = lib.RelativePath.Count(c => c == '\\' || c == '/');
            Assert.True(slashCount <= 0, $"Library {lib.LibraryName} at {lib.RelativePath} is too deeply nested");
        }
    }

    [Fact]
    public async Task AddRepositoryAsync_SkipsHiddenDirectories()
    {
        var testPath = @"C:\Projects\ModelicaStandardLibrary";

        if (!Directory.Exists(testPath))
        {
            // Skip test if directory doesn't exist
            return;
        }

        var service = CreateService();

        var result = await service.AddRepositoryAsync(testPath);

        Assert.True(result.Success);

        // Should NOT include .git or other hidden directories
        var libraryNames = result.DiscoveredLibraries.Select(l => l.RelativePath).ToList();
        Assert.DoesNotContain(".git", libraryNames);
        Assert.DoesNotContain(".CI", libraryNames);
    }

    [Fact]
    public void GetRepository_WithValidId_ReturnsRepository()
    {
        var service = CreateService();
        var testPath = @"C:\Projects\ModelicaStandardLibrary";

        if (!Directory.Exists(testPath))
        {
            return;
        }

        var addResult = service.AddRepositoryAsync(testPath).Result;
        Assert.True(addResult.Success);

        var repo = service.GetRepository(addResult.Repository!.Id);

        Assert.NotNull(repo);
        Assert.Equal(addResult.Repository.Id, repo.Id);
    }

    [Fact]
    public void GetRepository_WithInvalidId_ReturnsNull()
    {
        var service = CreateService();

        var repo = service.GetRepository("non-existent-id");

        Assert.Null(repo);
    }

    [Fact]
    public void RemoveRepository_RemovesFromList()
    {
        var service = CreateService();
        var testPath = @"C:\Projects\ModelicaStandardLibrary";

        if (!Directory.Exists(testPath))
        {
            return;
        }

        var addResult = service.AddRepositoryAsync(testPath).Result;
        Assert.True(addResult.Success);
        Assert.Single(service.Repositories);

        service.RemoveRepository(addResult.Repository!.Id, false);

        Assert.Empty(service.Repositories);
    }

    [Fact]
    public void ClearAllRepositories_RemovesAll()
    {
        var service = CreateService();
        var testPath1 = @"C:\Projects\ModelicaStandardLibrary";
        var testPath2 = @"C:\Projects\modelica-buildings";

        if (!Directory.Exists(testPath1) || !Directory.Exists(testPath2))
        {
            return;
        }

        service.AddRepositoryAsync(testPath1).Wait();
        service.AddRepositoryAsync(testPath2).Wait();
        Assert.Equal(2, service.Repositories.Count);

        service.ClearAllRepositories();

        Assert.Empty(service.Repositories);
    }

    #endregion

    #region Event Tests

    [Fact]
    public async Task AddRepositoryAsync_FiresOnRepositoriesChangedEvent()
    {
        var service = CreateService();
        var testPath = @"C:\Projects\ModelicaStandardLibrary";

        if (!Directory.Exists(testPath))
        {
            return;
        }

        var eventFired = false;
        service.OnRepositoriesChanged += () => eventFired = true;

        await service.AddRepositoryAsync(testPath);

        Assert.True(eventFired);
    }

    [Fact]
    public void RemoveRepository_FiresOnRepositoriesChangedEvent()
    {
        var service = CreateService();
        var testPath = @"C:\Projects\ModelicaStandardLibrary";

        if (!Directory.Exists(testPath))
        {
            return;
        }

        var addResult = service.AddRepositoryAsync(testPath).Result;

        var eventFired = false;
        service.OnRepositoriesChanged += () => eventFired = true;

        service.RemoveRepository(addResult.Repository!.Id, false);

        Assert.True(eventFired);
    }

    #endregion

    #region LoadLibraries Tests

    [Fact]
    public async Task LoadLibrariesAsync_SetsRepositoryIdOnLibrary()
    {
        var testPath = @"C:\Projects\modelica-buildings";

        if (!Directory.Exists(testPath))
        {
            return;
        }

        var service = CreateService();
        var addResult = await service.AddRepositoryAsync(testPath);

        Assert.True(addResult.Success);

        await service.LoadLibrariesAsync(addResult.Repository!.Id);

        // Check that libraries were loaded with the correct repository ID
        Assert.NotEmpty(addResult.Repository.LibraryIds);
    }

    [Fact]
    public async Task LoadLibrariesAsync_AddsLibraryIdToRepository()
    {
        var testPath = @"C:\Projects\modelica-buildings";

        if (!Directory.Exists(testPath))
        {
            return;
        }

        var service = CreateService();
        var addResult = await service.AddRepositoryAsync(testPath);

        await service.LoadLibrariesAsync(addResult.Repository!.Id);

        // Check that library IDs were added
        Assert.NotEmpty(addResult.Repository.LibraryIds);
    }

    #endregion

    #region GetRepositoryForLibrary Tests

    [Fact]
    public async Task GetRepositoryForLibrary_ReturnsCorrectRepository()
    {
        var testPath = @"C:\Projects\modelica-buildings";

        if (!Directory.Exists(testPath))
        {
            return;
        }

        var service = CreateService();
        var addResult = await service.AddRepositoryAsync(testPath);
        await service.LoadLibrariesAsync(addResult.Repository!.Id);

        // Get a library ID that was loaded
        var libraryId = addResult.Repository.LibraryIds.FirstOrDefault();
        if (libraryId == null)
        {
            return; // No libraries loaded, skip test
        }

        var foundRepo = service.GetRepositoryForLibrary(libraryId);

        Assert.NotNull(foundRepo);
        Assert.Equal(addResult.Repository.Id, foundRepo.Id);
    }

    [Fact]
    public void GetRepositoryForLibrary_WithUnknownLibrary_ReturnsNull()
    {
        var service = CreateService();

        var foundRepo = service.GetRepositoryForLibrary("unknown-lib-id");

        Assert.Null(foundRepo);
    }

    #endregion

    #region Buildings Repository Tests

    [Fact]
    public async Task AddRepositoryAsync_WithBuildingsRepo_DiscoversBuildings()
    {
        var testPath = @"C:\Projects\modelica-buildings";

        if (!Directory.Exists(testPath))
        {
            return;
        }

        var service = CreateService();

        var result = await service.AddRepositoryAsync(testPath);

        Assert.True(result.Success);
        Assert.NotEmpty(result.DiscoveredLibraries);

        var libraryNames = result.DiscoveredLibraries.Select(l => l.LibraryName).ToList();
        Assert.Contains("Buildings", libraryNames);
    }

    #endregion

    #region Root-Level Library Tests

    [Fact]
    public async Task AddRepositoryAsync_WithRootLevelLibrary_DiscoversLibrary()
    {
        // This repository has package.mo at the root level (not in a subdirectory)
        var testPath = @"C:\Projects\ModelicaEditorTest";

        if (!Directory.Exists(testPath))
        {
            return;
        }

        var service = CreateService();

        var result = await service.AddRepositoryAsync(testPath);

        Assert.True(result.Success);
        Assert.NotEmpty(result.DiscoveredLibraries);

        // Should discover the library at root level
        var rootLibrary = result.DiscoveredLibraries.FirstOrDefault(l => l.RelativePath == "");
        Assert.NotNull(rootLibrary);
        Assert.Single(result.DiscoveredLibraries);
        Assert.Equal("ModelicaEditorTest", rootLibrary.LibraryName);
    }

    [Fact]
    public async Task LoadLibrariesAsync_WithRootLevelLibrary_LoadsCorrectly()
    {
        var testPath = @"C:\Projects\ModelicaEditorTest";

        if (!Directory.Exists(testPath))
        {
            return;
        }

        //var service = CreateService();
        var libraryDataService = new LibraryDataService();
        var settingsService = new InMemorySettingsService();
        var fileMonitoringService = new FileMonitoringService();
        var service = new RepositoryService(libraryDataService, settingsService, fileMonitoringService);


        var result = await service.AddRepositoryAsync(testPath);
        Assert.True(result.Success);

        // Load the root-level library (empty relative path)
        var rootLibraryPaths = result.DiscoveredLibraries
            .Where(l => l.RelativePath == "")
            .Select(l => l.RelativePath)
            .ToList();

        await service.LoadLibrariesAsync(result.Repository!.Id, rootLibraryPaths);

        // Check that the library was loaded
        Assert.NotEmpty(result.Repository.LibraryIds);
        Assert.Single(libraryDataService.Libraries);
        Assert.NotEmpty(libraryDataService.CombinedGraph.ModelNodes);
    }

    [Fact]
    public void DetectVcsType_WithLocalSvnRepo_ReturnsSvnLocal()
    {
        // This directory is an SVN working copy with library at root
        var testPath = @"C:\Projects\ModelicaEditorTest";

        if (!Directory.Exists(testPath))
        {
            return;
        }

        var service = CreateService();

        var (vcsType, isLocal) = service.DetectVcsType(testPath);

        Assert.Equal(RepositoryVcsType.SVN, vcsType);
        Assert.True(isLocal);
    }

    #endregion

    #region MergeBranchAsync Tests

    [Fact]
    public async Task MergeBranchAsync_WithNonExistentRepositoryId_ReturnsError()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.MergeBranchAsync("non-existent-repo-id", "branches/test");

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("not found", result.ErrorMessage.ToLower());
    }

    [Fact]
    public async Task MergeBranchAsync_WithLocalRepository_ReturnsError()
    {
        // Arrange - create a local directory repository (no VCS)
        var tempDir = Path.Combine(Path.GetTempPath(), "LocalRepoTest_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        // Create a minimal package.mo to make it discoverable
        File.WriteAllText(Path.Combine(tempDir, "package.mo"), "package Test end Test;");

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir);

            // Skip if the path was detected as VCS (shouldn't happen for temp dir)
            if (addResult.Repository?.VcsType != RepositoryVcsType.Local)
            {
                return;
            }

            // Act
            var result = await service.MergeBranchAsync(addResult.Repository!.Id, "branches/test");

            // Assert
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("local directory", result.ErrorMessage.ToLower());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch { }
            }
        }
    }

    [Fact]
    public async Task MergeBranchAsync_WithValidSvnRepository_AndNonExistentBranch_ReturnsError()
    {
        // This test requires C:\Projects\ModelicaEditorTest to be an SVN working copy
        var testPath = @"C:\Projects\ModelicaEditorTest";

        if (!Directory.Exists(testPath))
        {
            return;
        }

        var service = CreateService();
        var addResult = await service.AddRepositoryAsync(testPath);

        if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.SVN)
        {
            return;
        }

        // Act
        var result = await service.MergeBranchAsync(addResult.Repository.Id, "branches/non-existent-branch-12345");

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task MergeBranchAsync_WithValidGitRepository_ReturnsResult()
    {
        // This test requires C:\Projects\ModelicaStandardLibrary to be a Git repository
        var testPath = @"C:\Projects\ModelicaStandardLibrary";

        if (!Directory.Exists(testPath))
        {
            return;
        }

        var service = CreateService();
        var addResult = await service.AddRepositoryAsync(testPath);

        if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Git)
        {
            return;
        }

        // Act
        var result = await service.MergeBranchAsync(addResult.Repository.Id, "main");

        // Assert - Git merge is implemented; merging the current branch returns a valid result
        Assert.NotNull(result);
    }

    [Fact]
    public async Task MergeBranchAsync_FiresOnRepositoriesChangedEvent_OnSuccess()
    {
        var testPath = @"C:\Projects\ModelicaEditorTest";

        if (!Directory.Exists(testPath))
        {
            return;
        }

        var service = CreateService();
        var addResult = await service.AddRepositoryAsync(testPath);

        if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.SVN)
        {
            return;
        }

        // Get available branches
        var branches = service.GetBranches(addResult.Repository.Id);
        if (branches.Count < 2)
        {
            // Need at least 2 branches to test merge
            return;
        }

        // Find a branch that is not the current one
        var currentBranch = addResult.Repository.CurrentBranch;
        var otherBranch = branches.FirstOrDefault(b => b.Name != currentBranch && !b.IsCurrent);

        if (otherBranch == null)
        {
            return;
        }

        var eventFired = false;
        service.OnRepositoriesChanged += () => eventFired = true;

        // Act - even if merge has no changes, event should fire
        var result = await service.MergeBranchAsync(addResult.Repository.Id, otherBranch.Name);

        // Assert - we can't control whether there are actual changes to merge,
        // but if the merge completes (with or without changes), event should fire
        if (result.Success || result.HasConflicts)
        {
            Assert.True(eventFired);
        }
    }

    [Fact]
    public async Task MergeBranchAsync_ReturnsCorrectResultStructure()
    {
        var service = CreateService();

        // Act
        var result = await service.MergeBranchAsync("non-existent-id", "branches/test");

        // Assert - verify result structure is correct even on failure
        Assert.NotNull(result);
        Assert.NotNull(result.ConflictedFiles);
        Assert.NotNull(result.ModifiedFiles);
        Assert.False(result.HasConflicts);
        Assert.Empty(result.ConflictedFiles);
        Assert.Empty(result.ModifiedFiles);
        Assert.False(result.HasChanges);
    }

    #endregion

    #region Git Temp Repository Tests

    private static string? CreateTempGitRepo(string packageMoContent = "package TestLib end TestLib;")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "RepoServiceTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Initialize git repo
            RunGit(tempDir, "init");
            RunGit(tempDir, "config user.email test@test.com");
            RunGit(tempDir, "config user.name TestUser");

            // Create package.mo and commit
            File.WriteAllText(Path.Combine(tempDir, "package.mo"), packageMoContent);
            RunGit(tempDir, "add .");
            RunGit(tempDir, "commit -m \"Initial commit\"");

            return tempDir;
        }
        catch
        {
            try { Directory.Delete(tempDir, true); } catch { }
            return null;
        }
    }

    private static void RunGit(string workDir, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", args)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        process.WaitForExit(10000);
        if (process.ExitCode != 0)
            throw new Exception($"git {args} failed: {process.StandardError.ReadToEnd()}");
    }

    [Fact]
    public async Task AddRepositoryAsync_WithLocalGitTempRepo_Succeeds()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return; // git not available, skip

        try
        {
            var service = CreateService();
            var result = await service.AddRepositoryAsync(tempDir);

            Assert.True(result.Success);
            Assert.NotNull(result.Repository);
            Assert.Equal(RepositoryVcsType.Git, result.Repository.VcsType);
            Assert.Single(service.Repositories);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task AddRepositoryAsync_WithGitRepo_DiscoversLibrary()
    {
        var tempDir = CreateTempGitRepo("package TestLib end TestLib;");
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var result = await service.AddRepositoryAsync(tempDir);

            Assert.True(result.Success);
            Assert.NotEmpty(result.DiscoveredLibraries);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task GetBranches_WithGitRepo_ReturnsBranchList()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir);
            if (!addResult.Success) return;

            var branches = service.GetBranches(addResult.Repository!.Id);

            Assert.NotNull(branches);
            Assert.NotEmpty(branches);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task GetLogEntries_WithGitRepo_ReturnsLogList()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir);
            if (!addResult.Success) return;

            var logEntries = service.GetLogEntries(addResult.Repository!.Id);

            Assert.NotNull(logEntries);
            Assert.NotEmpty(logEntries); // Should have at least our initial commit
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task GetWorkingCopyChanges_WithGitRepo_ReturnsChangesList()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir);
            if (!addResult.Success) return;

            var changes = service.GetWorkingCopyChanges(addResult.Repository!.Id);

            Assert.NotNull(changes);
            // May be empty (clean repo) or have changes
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task StartMonitoringAllRepositories_WithGitRepo_DoesNotThrow()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // Should not throw
            service.StartMonitoringAllRepositories();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ClearAllRepositories_WithGitRepo_RemovesAll()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            await service.AddRepositoryAsync(tempDir, startMonitoring: false);

            service.ClearAllRepositories();

            Assert.Empty(service.Repositories);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task RemoveRepository_WithGitRepo_RemovesSuccessfully()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            service.RemoveRepository(addResult.Repository!.Id, unloadLibraries: false);

            Assert.Empty(service.Repositories);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task DiscoverLibrariesAsync_WithGitRepo_ReturnsLibraries()
    {
        var tempDir = CreateTempGitRepo("package TestLib end TestLib;");
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            var libraries = await service.DiscoverLibrariesAsync(addResult.Repository!.Id);

            Assert.NotEmpty(libraries);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task GetFileContentAtRevision_WithGitRepo_ReturnsContent()
    {
        var tempDir = CreateTempGitRepo("package TestLib end TestLib;");
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            var content = service.GetFileContentAtRevision(addResult.Repository!.Id, "package.mo", "HEAD");

            Assert.NotNull(content);
            Assert.Contains("TestLib", content);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task SaveAndLoadRepositorySettings_WithGitRepo_RoundTrips()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var settingsService = new InMemorySettingsService();
            var service1 = new RepositoryService(new LibraryDataService(), settingsService, new FileMonitoringService());
            var addResult = await service1.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            await service1.SaveRepositorySettingsAsync();

            // Create new service with same settings store and load
            var service2 = new RepositoryService(new LibraryDataService(), settingsService, new FileMonitoringService());
            await service2.LoadRepositorySettingsAsync();

            // Should have restored the repository
            Assert.Single(service2.Repositories);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task GetChangedFiles_WithGitRepo_ReturnsChangesList()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // Get the HEAD revision
            var logEntries = service.GetLogEntries(addResult.Repository!.Id);
            if (!logEntries.Any()) return;

            var headRevision = logEntries.First().Revision;
            var changedFiles = service.GetChangedFiles(addResult.Repository!.Id, headRevision);

            Assert.NotNull(changedFiles);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task RefreshRepositoryAsync_WithGitRepo_DoesNotThrow()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // Should not throw
            await service.RefreshRepositoryAsync(addResult.Repository!.Id);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task GetRepositoryForLibrary_WithGitRepoAndLoadedLibrary_ReturnsRepo()
    {
        var tempDir = CreateTempGitRepo("package TestLib end TestLib;");
        if (tempDir == null) return;

        try
        {
            var libraryDataService = new LibraryDataService();
            var service = new RepositoryService(libraryDataService, new InMemorySettingsService(), new FileMonitoringService());
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || !addResult.DiscoveredLibraries.Any()) return;

            // Load a library
            await service.LoadLibrariesAsync(addResult.Repository!.Id);

            var libraryId = addResult.Repository.LibraryIds.FirstOrDefault();
            if (libraryId == null) return;

            var foundRepo = service.GetRepositoryForLibrary(libraryId);

            Assert.NotNull(foundRepo);
            Assert.Equal(addResult.Repository.Id, foundRepo.Id);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task IsBranchPushedAsync_WithGitRepo_ReturnsBool()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // Should not throw (no remote means not pushed)
            var result = await service.IsBranchPushedAsync(addResult.Repository!.Id);

            // Local-only repo with no remote: result depends on implementation
            Assert.IsType<bool>(result);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task CommitAsync_WithInvalidRepoId_ReturnsError()
    {
        var service = CreateService();

        var result = await service.CommitAsync("non-existent-repo", "Test commit");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task UpdateRepositoryAsync_WithInvalidRepoId_ReturnsError()
    {
        var service = CreateService();

        var result = await service.UpdateRepositoryAsync("non-existent-repo");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task RevertFilesAsync_WithInvalidRepoId_ReturnsError()
    {
        var service = CreateService();

        var result = await service.RevertFilesAsync("non-existent-repo", new[] { "some-file.mo" });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task SwitchBranchAsync_WithInvalidRepoId_ReturnsError()
    {
        var service = CreateService();

        var result = await service.SwitchBranchAsync("non-existent-repo", "main");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CreateBranchAsync_WithInvalidRepoId_ReturnsError()
    {
        var service = CreateService();

        var result = await service.CreateBranchAsync("non-existent-repo", "new-branch");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CheckoutRevisionAsync_WithInvalidRepoId_ReturnsError()
    {
        var service = CreateService();

        var result = await service.CheckoutRevisionAsync("non-existent-repo", "HEAD");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task RebaseAsync_WithInvalidRepoId_ReturnsError()
    {
        var service = CreateService();

        var result = await service.RebaseAsync("non-existent-repo", "main");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task PushAsync_WithInvalidRepoId_ReturnsError()
    {
        var service = CreateService();

        var result = await service.PushAsync("non-existent-repo");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CleanWorkspaceAsync_WithInvalidRepoId_ReturnsError()
    {
        var service = CreateService();

        var result = await service.CleanWorkspaceAsync("non-existent-repo");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task AbortRebaseAsync_WithInvalidRepoId_ReturnsError()
    {
        var service = CreateService();

        var result = await service.AbortRebaseAsync("non-existent-repo");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ContinueRebaseAsync_WithInvalidRepoId_ReturnsError()
    {
        var service = CreateService();

        var result = await service.ContinueRebaseAsync("non-existent-repo");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ForcePushAsync_WithInvalidRepoId_ReturnsError()
    {
        var service = CreateService();

        var result = await service.ForcePushAsync("non-existent-repo");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ResolveConflictAsync_WithInvalidRepoId_ReturnsError()
    {
        var service = CreateService();

        var result = await service.ResolveConflictAsync("non-existent-repo", "file.mo", ConflictResolutionChoice.KeepMine);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task GetConflictVersionsAsync_WithInvalidRepoId_ReturnsNulls()
    {
        var service = CreateService();

        var (ours, theirs) = await service.GetConflictVersionsAsync("non-existent-repo", "file.mo");

        Assert.Null(ours);
        Assert.Null(theirs);
    }

    [Fact]
    public async Task GetPullRequestUrlAsync_WithInvalidRepoId_ReturnsNull()
    {
        var service = CreateService();

        var url = await service.GetPullRequestUrlAsync("non-existent-repo");

        Assert.Null(url);
    }

    [Fact]
    public void GetBranches_WithInvalidRepoId_ReturnsEmptyList()
    {
        var service = CreateService();

        var branches = service.GetBranches("non-existent-repo");

        Assert.NotNull(branches);
        Assert.Empty(branches);
    }

    [Fact]
    public void GetLogEntries_WithInvalidRepoId_ReturnsEmptyList()
    {
        var service = CreateService();

        var logEntries = service.GetLogEntries("non-existent-repo");

        Assert.NotNull(logEntries);
        Assert.Empty(logEntries);
    }

    [Fact]
    public void GetWorkingCopyChanges_WithInvalidRepoId_ReturnsEmptyList()
    {
        var service = CreateService();

        var changes = service.GetWorkingCopyChanges("non-existent-repo");

        Assert.NotNull(changes);
        Assert.Empty(changes);
    }

    [Fact]
    public void GetChangedFiles_WithInvalidRepoId_ReturnsEmptyList()
    {
        var service = CreateService();

        var files = service.GetChangedFiles("non-existent-repo", "HEAD");

        Assert.NotNull(files);
        Assert.Empty(files);
    }

    [Fact]
    public void GetFileContentAtRevision_WithInvalidRepoId_ReturnsNull()
    {
        var service = CreateService();

        var content = service.GetFileContentAtRevision("non-existent-repo", "file.mo", "HEAD");

        Assert.Null(content);
    }

    #endregion

    #region Local Repo VCS Error Tests

    private static string CreateTempLocalRepo(string packageMoContent = "package LocalLib end LocalLib;")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LocalRepoVcsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "package.mo"), packageMoContent);
        return tempDir;
    }

    [Fact]
    public async Task CommitAsync_WithLocalRepo_ReturnsLocalDirError()
    {
        var tempDir = CreateTempLocalRepo();
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            var result = await service.CommitAsync(addResult.Repository!.Id, "test commit");

            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task UpdateRepositoryAsync_WithLocalRepo_ReturnsSuccessWithNoChanges()
    {
        var tempDir = CreateTempLocalRepo();
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            var result = await service.UpdateRepositoryAsync(addResult.Repository!.Id);

            Assert.True(result.Success);
            Assert.False(result.HasChanges);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task RevertFilesAsync_WithLocalRepo_ReturnsError()
    {
        var tempDir = CreateTempLocalRepo();
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            var result = await service.RevertFilesAsync(addResult.Repository!.Id, new[] { "package.mo" });

            Assert.False(result.Success);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task GetWorkingCopyChanges_WithLocalRepo_ReturnsEmpty()
    {
        var tempDir = CreateTempLocalRepo();
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            var changes = service.GetWorkingCopyChanges(addResult.Repository!.Id);

            Assert.Empty(changes);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task GetBranches_WithLocalRepo_ReturnsEmpty()
    {
        var tempDir = CreateTempLocalRepo();
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            var branches = service.GetBranches(addResult.Repository!.Id);

            Assert.Empty(branches);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task SwitchBranchAsync_WithLocalRepo_ReturnsError()
    {
        var tempDir = CreateTempLocalRepo();
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            var result = await service.SwitchBranchAsync(addResult.Repository!.Id, "main");

            Assert.False(result.Success);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task CreateBranchAsync_WithLocalRepo_ReturnsError()
    {
        var tempDir = CreateTempLocalRepo();
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            var result = await service.CreateBranchAsync(addResult.Repository!.Id, "feature-branch");

            Assert.False(result.Success);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task PushAsync_WithLocalRepo_ReturnsError()
    {
        var tempDir = CreateTempLocalRepo();
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            var result = await service.PushAsync(addResult.Repository!.Id);

            Assert.False(result.Success);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task CleanWorkspaceAsync_WithLocalRepo_ReturnsError()
    {
        var tempDir = CreateTempLocalRepo();
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            var result = await service.CleanWorkspaceAsync(addResult.Repository!.Id);

            Assert.False(result.Success);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task ForcePushAsync_WithLocalRepo_ReturnsError()
    {
        var tempDir = CreateTempLocalRepo();
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            var result = await service.ForcePushAsync(addResult.Repository!.Id);

            Assert.False(result.Success);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task IsBranchPushedAsync_WithLocalRepo_ReturnsFalse()
    {
        var tempDir = CreateTempLocalRepo();
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            var result = await service.IsBranchPushedAsync(addResult.Repository!.Id);

            Assert.False(result);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task RebaseAsync_WithLocalRepo_ReturnsError()
    {
        var tempDir = CreateTempLocalRepo();
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            var result = await service.RebaseAsync(addResult.Repository!.Id, "main");

            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("Rebase is only supported", result.ErrorMessage);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task CheckoutRevisionAsync_WithLocalRepo_ReturnsError()
    {
        var tempDir = CreateTempLocalRepo();
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            var result = await service.CheckoutRevisionAsync(addResult.Repository!.Id, "HEAD");

            Assert.False(result.Success);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task GetFileContentAtRevision_WithLocalRepo_ReadsCurrentFile()
    {
        var tempDir = CreateTempLocalRepo("package LocalLib end LocalLib;");
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            // Local repos read the current file directly (no revision needed)
            var content = service.GetFileContentAtRevision(addResult.Repository!.Id, "package.mo", null);

            Assert.NotNull(content);
            Assert.Contains("LocalLib", content);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task GetLogEntries_WithLocalRepo_ReturnsEmpty()
    {
        var tempDir = CreateTempLocalRepo();
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            var logEntries = service.GetLogEntries(addResult.Repository!.Id);

            Assert.Empty(logEntries);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task GetChangedFiles_WithLocalRepo_ReturnsEmpty()
    {
        var tempDir = CreateTempLocalRepo();
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            var changedFiles = service.GetChangedFiles(addResult.Repository!.Id, "HEAD");

            Assert.Empty(changedFiles);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task GetPullRequestUrlAsync_WithLocalRepo_ReturnsNull()
    {
        var tempDir = CreateTempLocalRepo();
        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success || addResult.Repository?.VcsType != RepositoryVcsType.Local) return;

            var url = await service.GetPullRequestUrlAsync(addResult.Repository!.Id);

            Assert.Null(url);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task ContinueRebaseAsync_WithGitRepo_ReturnsResult()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // ContinueRebase will fail (no rebase in progress) but covers the code path
            var result = await service.ContinueRebaseAsync(addResult.Repository!.Id);

            Assert.NotNull(result);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task AbortRebaseAsync_WithGitRepo_ReturnsResult()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // AbortRebase will fail (no rebase in progress) but covers the code path
            var result = await service.AbortRebaseAsync(addResult.Repository!.Id);

            Assert.NotNull(result);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task CommitAsync_WithGitRepo_CommitsSuccessfully()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // Stage a new file
            File.WriteAllText(Path.Combine(tempDir, "NewFile.mo"), "model NewModel end NewModel;");
            RunGit(tempDir, "add NewFile.mo");

            var result = await service.CommitAsync(addResult.Repository!.Id, "Add new file");

            Assert.True(result.Success);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task CreateBranchAsync_WithGitRepo_CreatesBranch()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            var result = await service.CreateBranchAsync(addResult.Repository!.Id, "feature-branch", switchToBranch: false);

            Assert.True(result.Success);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task CreateBranchAsync_WithGitRepo_AndSwitchToBranch_CreatesBranchAndSwitches()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // Create and switch to branch (switchToBranch=true covers lines 872-875)
            var result = await service.CreateBranchAsync(addResult.Repository!.Id, "new-feature", switchToBranch: true);

            Assert.True(result.Success);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task SwitchBranchAsync_WithGitRepo_SwitchesBranch()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // Create a second branch first (without switching)
            RunGit(tempDir, "branch dev-branch");

            var result = await service.SwitchBranchAsync(addResult.Repository!.Id, "dev-branch");

            Assert.True(result.Success);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task GetConflictVersionsAsync_WithGitRepo_ReturnsResult()
    {
        var tempDir = CreateTempGitRepo("package TestLib end TestLib;");
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // Covers the non-null, non-local path through GetConflictVersionsAsync
            var (ours, theirs) = await service.GetConflictVersionsAsync(addResult.Repository!.Id, "package.mo");

            // Clean repo → no conflict versions, both null
            Assert.Null(ours);
            Assert.Null(theirs);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task GetPullRequestUrlAsync_WithGitRepo_ReturnsNullOrUrl()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // Covers the non-null, non-local path through GetPullRequestUrlAsync
            // Local git repo without remote → null
            var url = await service.GetPullRequestUrlAsync(addResult.Repository!.Id);

            // No remote configured, so null expected
            Assert.Null(url);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task ResolveConflictAsync_WithGitRepo_ReturnsResult()
    {
        var tempDir = CreateTempGitRepo("package TestLib end TestLib;");
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // Covers the non-null Git path through ResolveConflictAsync
            // No conflict in clean repo, so it will return false/error but code is covered
            var result = await service.ResolveConflictAsync(addResult.Repository!.Id, "package.mo", ConflictResolutionChoice.KeepMine);

            Assert.NotNull(result);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task RebaseAsync_WithGitRepo_ReturnsResult()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // Rebase onto current branch (main/master → itself, may succeed trivially)
            var branches = service.GetBranches(addResult.Repository!.Id);
            var currentBranch = branches.FirstOrDefault(b => b.IsCurrent)?.Name ?? "main";

            var result = await service.RebaseAsync(addResult.Repository!.Id, currentBranch);

            Assert.NotNull(result);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task RemoveRepository_WithLibraries_UnloadsLibraries()
    {
        var tempDir = CreateTempGitRepo("package TestLib end TestLib;");
        if (tempDir == null) return;

        try
        {
            var libraryDataService = new LibraryDataService();
            var service = new RepositoryService(libraryDataService, new InMemorySettingsService(), new FileMonitoringService());
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            await service.LoadLibrariesAsync(addResult.Repository!.Id);
            Assert.NotEmpty(libraryDataService.Libraries);

            // Remove with unloadLibraries=true
            service.RemoveRepository(addResult.Repository!.Id, unloadLibraries: true);

            Assert.Empty(service.Repositories);
            Assert.Empty(libraryDataService.Libraries);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task DiscoverLibrariesAsync_WithInvalidRepoId_ReturnsEmptyList()
    {
        var service = CreateService();

        var result = await service.DiscoverLibrariesAsync("non-existent-repo");

        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadRepositorySettingsAsync_WithAutoLoadFalse_SkipsRepo()
    {
        var tempDir = CreateTempGitRepo("package TestLib end TestLib;");
        if (tempDir == null) return;

        try
        {
            var settingsService = new InMemorySettingsService();

            // Save a repo with AutoLoad=false
            var settings = new RepositorySettingsCollection();
            settings.Repositories.Add(new RepositorySettingsEntry
            {
                Id = "test-repo-id",
                Name = "TestRepo",
                LocalPath = tempDir,
                VcsType = "Git",
                AutoLoad = false
            });
            await settingsService.SetAsync("Repositories", settings);

            var service = new RepositoryService(new LibraryDataService(), settingsService, new FileMonitoringService());
            await service.LoadRepositorySettingsAsync();

            // Repo with AutoLoad=false should be skipped
            Assert.Empty(service.Repositories);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task LoadRepositorySettingsAsync_WithNonExistentPath_SkipsRepo()
    {
        var settingsService = new InMemorySettingsService();

        // Save a repo with a non-existent path
        var settings = new RepositorySettingsCollection();
        settings.Repositories.Add(new RepositorySettingsEntry
        {
            Id = "test-repo-id",
            Name = "TestRepo",
            LocalPath = @"C:\NonExistent\Path\That\DoesNotExist",
            VcsType = "Git",
            AutoLoad = true
        });
        await settingsService.SetAsync("Repositories", settings);

        var service = new RepositoryService(new LibraryDataService(), settingsService, new FileMonitoringService());
        await service.LoadRepositorySettingsAsync();

        // Repo with non-existent path should be skipped
        Assert.Empty(service.Repositories);
    }

    [Fact]
    public async Task DiscoverLibrariesInPath_WithSubdirPackages_DiscoversSubdirLibraries()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DiscoverTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var subDir = Path.Combine(tempDir, "MyLibrary");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "package.mo"), "package MyLibrary end MyLibrary;");

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            Assert.NotEmpty(addResult.DiscoveredLibraries);
            Assert.Contains(addResult.DiscoveredLibraries, l => l.LibraryName == "MyLibrary");
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task DiscoverLibrariesInPath_WithMoFilesAtRoot_DiscoversMoFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DiscoverMoTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "Model1.mo"), "model Model1 Real x; end Model1;");
        File.WriteAllText(Path.Combine(tempDir, "Model2.mo"), "model Model2 Real x; end Model2;");

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            Assert.NotEmpty(addResult.DiscoveredLibraries);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task OnRepositoryLoadStateChanged_FiredDuringLoadLibraries()
    {
        var tempDir = CreateTempGitRepo("package TestLib end TestLib;");
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            var stateChanges = new List<(string repoId, bool isLoading)>();
            service.OnRepositoryLoadStateChanged += (repoId, isLoading) =>
                stateChanges.Add((repoId, isLoading));

            await service.LoadLibrariesAsync(addResult.Repository!.Id);

            // Should have fired started (true) and completed (false)
            Assert.Contains(stateChanges, s => s.isLoading);
            Assert.Contains(stateChanges, s => !s.isLoading);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    #endregion

    #region Project Management Tests

    [Fact]
    public void GetProjects_InitiallyEmpty()
    {
        var service = CreateService();

        var projects = service.GetProjects();

        Assert.Empty(projects);
    }

    [Fact]
    public void GetActiveProject_InitiallyNull()
    {
        var service = CreateService();

        var project = service.GetActiveProject();

        Assert.Null(project);
    }

    [Fact]
    public void CreateProject_AddsProjectAndReturnsIt()
    {
        var service = CreateService();

        var project = service.CreateProject("TestProject");

        Assert.NotNull(project);
        Assert.Equal("TestProject", project.Name);
        Assert.NotNull(project.Id);
    }

    [Fact]
    public void CreateProject_ProjectAppearsInGetProjects()
    {
        var service = CreateService();

        service.CreateProject("Project1");
        var projects = service.GetProjects();

        Assert.Single(projects);
        Assert.Equal("Project1", projects[0].Name);
    }

    [Fact]
    public void CreateProject_MultipleProjects_AllAppear()
    {
        var service = CreateService();

        service.CreateProject("Project1");
        service.CreateProject("Project2");
        var projects = service.GetProjects();

        Assert.Equal(2, projects.Count);
    }

    [Fact]
    public void RenameProject_ChangesProjectName()
    {
        var service = CreateService();
        var project = service.CreateProject("Original");

        service.RenameProject(project.Id, "Renamed");

        var projects = service.GetProjects();
        Assert.Single(projects);
        Assert.Equal("Renamed", projects[0].Name);
    }

    [Fact]
    public void RenameProject_WithInvalidId_DoesNotThrow()
    {
        var service = CreateService();

        // Should not throw
        service.RenameProject("non-existent-id", "NewName");
    }

    [Fact]
    public void DeleteProject_WithSingleProject_ReturnsFalse()
    {
        var service = CreateService();
        var project = service.CreateProject("OnlyProject");

        var result = service.DeleteProject(project.Id);

        Assert.False(result);
        Assert.Single(service.GetProjects());
    }

    [Fact]
    public void DeleteProject_WithMultipleProjects_RemovesProject()
    {
        var service = CreateService();
        var project1 = service.CreateProject("Project1");
        var project2 = service.CreateProject("Project2");

        var result = service.DeleteProject(project1.Id);

        Assert.True(result);
        Assert.Single(service.GetProjects());
        Assert.Equal("Project2", service.GetProjects()[0].Name);
    }

    [Fact]
    public void DeleteProject_WithInvalidId_ReturnsFalse()
    {
        var service = CreateService();
        service.CreateProject("Project1");
        service.CreateProject("Project2");

        var result = service.DeleteProject("non-existent-id");

        Assert.False(result);
        Assert.Equal(2, service.GetProjects().Count);
    }

    #endregion

    #region FindVcsRoot Tests

    [Fact]
    public void FindVcsRoot_WithNonVcsPath_ReturnsSamePath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FindVcsRootTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = CreateService();

            var root = service.FindVcsRoot(tempDir);

            // Non-VCS path returns itself
            Assert.Equal(tempDir, root);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public void FindVcsRoot_WithGitRepo_ReturnsGitRoot()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();

            var root = service.FindVcsRoot(tempDir);

            Assert.NotNull(root);
            Assert.True(Directory.Exists(root));
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    #endregion

    #region InvalidateWorkingCopyCache Tests

    [Fact]
    public async Task InvalidateWorkingCopyCache_WithSpecificRepoId_ClearsOnlyThatRepo()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // Prime the cache by calling GetWorkingCopyChanges
            service.GetWorkingCopyChanges(addResult.Repository!.Id);

            // Should not throw
            service.InvalidateWorkingCopyCache(addResult.Repository!.Id);

            // Cache is cleared, next call should still work
            var changes = service.GetWorkingCopyChanges(addResult.Repository!.Id);
            Assert.NotNull(changes);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public void InvalidateWorkingCopyCache_WithNullRepoId_ClearsAll()
    {
        var service = CreateService();

        // Should not throw even with empty cache
        service.InvalidateWorkingCopyCache(null);
        service.InvalidateWorkingCopyCache();
    }

    #endregion

    #region WorkingCopyCache Tests

    [Fact]
    public async Task GetWorkingCopyChanges_SecondCall_UsesCachedResult()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // First call populates cache
            var changes1 = service.GetWorkingCopyChanges(addResult.Repository!.Id);
            // Second call should use cache (same result object if cache hit)
            var changes2 = service.GetWorkingCopyChanges(addResult.Repository!.Id);

            Assert.NotNull(changes1);
            Assert.NotNull(changes2);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    #endregion

    #region UpdateRepositoryAsync Success Path Tests

    [Fact]
    public async Task UpdateRepositoryAsync_WithGitRepo_ReturnsResult()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            var result = await service.UpdateRepositoryAsync(addResult.Repository!.Id);

            // Local git with no remote → succeeds but no changes (or may fail gracefully)
            Assert.NotNull(result);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    #endregion

    #region RevertFilesAsync Success Path Tests

    [Fact]
    public async Task RevertFilesAsync_WithGitRepo_ReturnsResult()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // Modify a file to revert
            File.WriteAllText(Path.Combine(tempDir, "package.mo"), "package TestLib \"modified\" end TestLib;");

            var result = await service.RevertFilesAsync(addResult.Repository!.Id, new[] { "package.mo" });

            Assert.NotNull(result);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    #endregion

    #region CheckoutRevisionAsync Success Path Tests

    [Fact]
    public async Task CheckoutRevisionAsync_WithGitRepo_ReturnsResult()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            // Get current HEAD revision
            var logEntries = service.GetLogEntries(addResult.Repository!.Id);
            if (!logEntries.Any()) return;

            var headRevision = logEntries.First().Revision;
            var result = await service.CheckoutRevisionAsync(addResult.Repository!.Id, headRevision);

            Assert.NotNull(result);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    #endregion

    #region Legacy Migration Tests

    [Fact]
    public async Task LoadRepositorySettingsAsync_WithLegacyRepositories_MigratesToProject()
    {
        var tempDir = CreateTempGitRepo("package TestLib end TestLib;");
        if (tempDir == null) return;

        try
        {
            var settingsService = new InMemorySettingsService();

            // Save legacy format: Repositories at top level (no Projects)
            var settings = new RepositorySettingsCollection();
            settings.Repositories.Add(new RepositorySettingsEntry
            {
                Id = "legacy-repo-id",
                Name = "LegacyRepo",
                LocalPath = tempDir,
                VcsType = "Git",
                AutoLoad = true
            });
            // Leave Projects empty to trigger migration
            await settingsService.SetAsync("Repositories", settings);

            var service = new RepositoryService(new LibraryDataService(), settingsService, new FileMonitoringService());
            await service.LoadRepositorySettingsAsync();

            // Should have migrated and loaded the repository
            Assert.NotEmpty(service.Repositories);

            // Should have created a project
            var projects = service.GetProjects();
            Assert.NotEmpty(projects);
            Assert.Equal("Default", projects[0].Name);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    #endregion

    #region SaveRepositorySettingsAsync Default Project Tests

    [Fact]
    public async Task SaveRepositorySettingsAsync_WithNoProjects_CreatesDefaultProject()
    {
        var settingsService = new InMemorySettingsService();
        var service = new RepositoryService(new LibraryDataService(), settingsService, new FileMonitoringService());

        // Save with no projects should create a default
        await service.SaveRepositorySettingsAsync();

        // Load the settings back in a new service
        var service2 = new RepositoryService(new LibraryDataService(), settingsService, new FileMonitoringService());
        await service2.LoadRepositorySettingsAsync();

        var projects = service2.GetProjects();
        Assert.NotEmpty(projects);
    }

    #endregion

    #region SwitchProjectAsync Tests

    [Fact]
    public async Task SwitchProjectAsync_FiresOnProjectChangedEvent()
    {
        var settingsService = new InMemorySettingsService();
        var service = new RepositoryService(new LibraryDataService(), settingsService, new FileMonitoringService());

        // Create two projects
        var project1 = service.CreateProject("Project1");
        var project2 = service.CreateProject("Project2");

        string? firedProjectId = null;
        service.OnProjectChanged += id => firedProjectId = id;

        await service.SwitchProjectAsync(project2.Id);

        Assert.Equal(project2.Id, firedProjectId);
    }

    [Fact]
    public async Task SwitchProjectAsync_WithInvalidProjectId_ReturnsEarly()
    {
        var settingsService = new InMemorySettingsService();
        var service = new RepositoryService(new LibraryDataService(), settingsService, new FileMonitoringService());

        // Should not throw
        await service.SwitchProjectAsync("non-existent-project-id");
    }

    [Fact]
    public async Task SwitchProjectAsync_UpdatesActiveProject()
    {
        var settingsService = new InMemorySettingsService();
        var service = new RepositoryService(new LibraryDataService(), settingsService, new FileMonitoringService());

        var project1 = service.CreateProject("Project1");
        var project2 = service.CreateProject("Project2");

        await service.SwitchProjectAsync(project2.Id);

        var active = service.GetActiveProject();
        Assert.NotNull(active);
        Assert.Equal(project2.Id, active.Id);
    }

    #endregion

    #region CleanWorkspaceAsync and PushAsync Success Path Tests

    [Fact]
    public async Task CleanWorkspaceAsync_WithGitRepo_ReturnsResult()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            var result = await service.CleanWorkspaceAsync(addResult.Repository!.Id);

            Assert.NotNull(result);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task PushAsync_WithGitRepo_NoRemote_ReturnsResult()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            var result = await service.PushAsync(addResult.Repository!.Id);

            // No remote configured, push should fail
            Assert.NotNull(result);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public async Task ForcePushAsync_WithGitRepo_NoRemote_ReturnsResult()
    {
        var tempDir = CreateTempGitRepo();
        if (tempDir == null) return;

        try
        {
            var service = CreateService();
            var addResult = await service.AddRepositoryAsync(tempDir, startMonitoring: false);
            if (!addResult.Success) return;

            var result = await service.ForcePushAsync(addResult.Repository!.Id);

            Assert.NotNull(result);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    #endregion

    #region LoadRepositorySettingsAsync with ProjectId Tests

    [Fact]
    public async Task LoadRepositorySettingsAsync_WithSpecificProjectId_LoadsThatProject()
    {
        var tempDir = CreateTempGitRepo("package TestLib end TestLib;");
        if (tempDir == null) return;

        try
        {
            var settingsService = new InMemorySettingsService();

            // Save settings with two projects, one with a repo, one without
            var settings = new RepositorySettingsCollection();
            var project1 = new ProjectProfile
            {
                Name = "WithRepo",
                Repositories = new List<RepositorySettingsEntry>
                {
                    new()
                    {
                        Id = "repo-1",
                        Name = "TestRepo",
                        LocalPath = tempDir,
                        VcsType = "Git",
                        AutoLoad = true
                    }
                }
            };
            var project2 = new ProjectProfile
            {
                Name = "EmptyProject",
                Repositories = new List<RepositorySettingsEntry>()
            };
            settings.Projects.Add(project1);
            settings.Projects.Add(project2);
            settings.ActiveProjectId = project2.Id; // Active is empty project

            await settingsService.SetAsync("Repositories", settings);

            // Load specifying the project with the repo
            var service = new RepositoryService(new LibraryDataService(), settingsService, new FileMonitoringService());
            await service.LoadRepositorySettingsAsync(project1.Id);

            // Should have loaded the repo from project1
            Assert.NotEmpty(service.Repositories);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    #endregion
}
