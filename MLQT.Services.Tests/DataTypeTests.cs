using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Services.Helpers;

namespace MLQT.Services.Tests;

/// <summary>
/// Unit tests for simple data type classes (POCOs and value objects).
/// </summary>
public class DataTypeTests
{
    // ==================== FileChangeInfo ====================

    [Fact]
    public void FileChangeInfo_IsModelicaFile_ReturnsTrueForMoExtension()
    {
        var change = new FileChangeInfo { FilePath = "C:/Models/TestModel.mo" };

        Assert.True(change.IsModelicaFile);
    }

    [Fact]
    public void FileChangeInfo_IsModelicaFile_ReturnsTrueForUppercaseMoExtension()
    {
        var change = new FileChangeInfo { FilePath = "C:/Models/TestModel.MO" };

        Assert.True(change.IsModelicaFile);
    }

    [Fact]
    public void FileChangeInfo_IsModelicaFile_ReturnsFalseForNonMoExtension()
    {
        var change = new FileChangeInfo { FilePath = "C:/Models/data.mat" };

        Assert.False(change.IsModelicaFile);
    }

    [Fact]
    public void FileChangeInfo_IsModelicaFile_ReturnsFalseForEmptyPath()
    {
        var change = new FileChangeInfo { FilePath = "" };

        Assert.False(change.IsModelicaFile);
    }

    [Fact]
    public void FileChangeInfo_IsPackageOrderFile_ReturnsTrueForPackageOrderFile()
    {
        var change = new FileChangeInfo { FilePath = "C:/Models/package.order" };

        Assert.True(change.IsPackageOrderFile);
    }

    [Fact]
    public void FileChangeInfo_IsPackageOrderFile_ReturnsTrueForUppercasePackageOrderFile()
    {
        var change = new FileChangeInfo { FilePath = "C:/Models/PACKAGE.ORDER" };

        Assert.True(change.IsPackageOrderFile);
    }

    [Fact]
    public void FileChangeInfo_IsPackageOrderFile_ReturnsFalseForOtherFiles()
    {
        var change = new FileChangeInfo { FilePath = "C:/Models/TestModel.mo" };

        Assert.False(change.IsPackageOrderFile);
    }

    [Fact]
    public void FileChangeInfo_IsPackageOrderFile_ReturnsFalseForFilesNamedPackageOrderMo()
    {
        var change = new FileChangeInfo { FilePath = "C:/Models/package.order.mo" };

        Assert.False(change.IsPackageOrderFile);
    }

    [Fact]
    public void FileChangeInfo_DefaultValues_AreCorrect()
    {
        var change = new FileChangeInfo();

        Assert.NotEmpty(change.Id); // GUID generated
        Assert.Equal("", change.FilePath);
        Assert.Equal("", change.RepositoryId);
        Assert.Null(change.OldFilePath);
        Assert.Null(change.LibraryId);
        Assert.False(change.IsDirectory);
    }

    [Fact]
    public void FileChangeInfo_AllProperties_CanBeSet()
    {
        var now = DateTime.UtcNow;
        var change = new FileChangeInfo
        {
            ChangeType = FileChangeType.Modified,
            FilePath = "C:/test.mo",
            OldFilePath = "C:/old.mo",
            RepositoryId = "repo1",
            LibraryId = "lib1",
            DetectedAt = now,
            IsDirectory = true
        };

        Assert.Equal(FileChangeType.Modified, change.ChangeType);
        Assert.Equal("C:/test.mo", change.FilePath);
        Assert.Equal("C:/old.mo", change.OldFilePath);
        Assert.Equal("repo1", change.RepositoryId);
        Assert.Equal("lib1", change.LibraryId);
        Assert.Equal(now, change.DetectedAt);
        Assert.True(change.IsDirectory);
    }

    // ==================== PendingChangesSummary ====================

    [Fact]
    public void PendingChangesSummary_TotalChanges_SumsAllCategories()
    {
        var summary = new PendingChangesSummary
        {
            AddedFiles = 1,
            ModifiedFiles = 2,
            DeletedFiles = 3,
            RenamedFiles = 4,
            AddedDirectories = 5,
            DeletedDirectories = 6
        };

        Assert.Equal(21, summary.TotalChanges);
    }

    [Fact]
    public void PendingChangesSummary_HasChanges_ReturnsFalseWhenEmpty()
    {
        var summary = new PendingChangesSummary();

        Assert.False(summary.HasChanges);
        Assert.Equal(0, summary.TotalChanges);
    }

    [Fact]
    public void PendingChangesSummary_HasChanges_ReturnsTrueWhenAddedFiles()
    {
        var summary = new PendingChangesSummary { AddedFiles = 1 };

        Assert.True(summary.HasChanges);
    }

    [Fact]
    public void PendingChangesSummary_HasChanges_ReturnsTrueWhenModifiedFiles()
    {
        var summary = new PendingChangesSummary { ModifiedFiles = 1 };

        Assert.True(summary.HasChanges);
    }

    [Fact]
    public void PendingChangesSummary_HasChanges_ReturnsTrueWhenDeletedFiles()
    {
        var summary = new PendingChangesSummary { DeletedFiles = 1 };

        Assert.True(summary.HasChanges);
    }

    [Fact]
    public void PendingChangesSummary_HasChanges_ReturnsTrueWhenRenamedFiles()
    {
        var summary = new PendingChangesSummary { RenamedFiles = 1 };

        Assert.True(summary.HasChanges);
    }

    [Fact]
    public void PendingChangesSummary_HasChanges_ReturnsTrueWhenAddedDirectories()
    {
        var summary = new PendingChangesSummary { AddedDirectories = 1 };

        Assert.True(summary.HasChanges);
    }

    [Fact]
    public void PendingChangesSummary_HasChanges_ReturnsTrueWhenDeletedDirectories()
    {
        var summary = new PendingChangesSummary { DeletedDirectories = 1 };

        Assert.True(summary.HasChanges);
    }

    // ==================== ResourceTreeNode ====================

    [Fact]
    public void ResourceTreeNode_DefaultValues_AreCorrect()
    {
        var node = new ResourceTreeNode();

        Assert.Equal("", node.Name);
        Assert.Equal("", node.FullPath);
        Assert.False(node.IsDirectory);
        Assert.Equal(DirectoryAnnotationType.None, node.AnnotationType);
        Assert.NotNull(node.ReferencingModelIds);
        Assert.Empty(node.ReferencingModelIds);
        Assert.False(node.HasWarning);
        Assert.Null(node.WarningMessage);
        Assert.Equal("", node.FileExtension);
        Assert.False(node.IsImageFile);
        Assert.Equal(0, node.ReferencingModelCount);
    }

    [Fact]
    public void ResourceTreeNode_AllProperties_CanBeSet()
    {
        var node = new ResourceTreeNode
        {
            Name = "testFile.mat",
            FullPath = "C:/Models/testFile.mat",
            IsDirectory = false,
            AnnotationType = DirectoryAnnotationType.IncludeDirectory,
            ReferencingModelIds = new List<string> { "Model1", "Model2" },
            HasWarning = true,
            WarningMessage = "File not found",
            FileExtension = ".mat",
            IsImageFile = false,
            ReferencingModelCount = 2
        };

        Assert.Equal("testFile.mat", node.Name);
        Assert.Equal("C:/Models/testFile.mat", node.FullPath);
        Assert.False(node.IsDirectory);
        Assert.Equal(DirectoryAnnotationType.IncludeDirectory, node.AnnotationType);
        Assert.Equal(2, node.ReferencingModelIds.Count);
        Assert.True(node.HasWarning);
        Assert.Equal("File not found", node.WarningMessage);
        Assert.Equal(".mat", node.FileExtension);
        Assert.False(node.IsImageFile);
        Assert.Equal(2, node.ReferencingModelCount);
    }

    [Fact]
    public void ResourceTreeNode_DirectoryAnnotationType_AllValues_AreAccessible()
    {
        Assert.Equal(DirectoryAnnotationType.None, (DirectoryAnnotationType)0);
        Assert.NotEqual(DirectoryAnnotationType.None, DirectoryAnnotationType.IncludeDirectory);
        Assert.NotEqual(DirectoryAnnotationType.None, DirectoryAnnotationType.LibraryDirectory);
        Assert.NotEqual(DirectoryAnnotationType.None, DirectoryAnnotationType.SourceDirectory);
    }

    [Fact]
    public void ResourceTreeNode_IsImageFile_CanBeSetToTrue()
    {
        var node = new ResourceTreeNode { IsImageFile = true };

        Assert.True(node.IsImageFile);
    }

    // ==================== SaveResult ====================

    [Fact]
    public void SaveResult_DefaultValues_AreCorrect()
    {
        var result = new SaveResult();

        Assert.NotNull(result.ModelIdToFilePath);
        Assert.Empty(result.ModelIdToFilePath);
        Assert.NotNull(result.WrittenFiles);
        Assert.Empty(result.WrittenFiles);
        Assert.NotNull(result.CreatedDirectories);
        Assert.Empty(result.CreatedDirectories);
    }

    [Fact]
    public void SaveResult_Collections_CanBePopulated()
    {
        var result = new SaveResult();
        result.ModelIdToFilePath["Model1"] = "C:/output/Model1.mo";
        result.WrittenFiles.Add("C:/output/Model1.mo");
        result.CreatedDirectories.Add("C:/output");

        Assert.Single(result.ModelIdToFilePath);
        Assert.Single(result.WrittenFiles);
        Assert.Single(result.CreatedDirectories);
    }

    // ==================== RepositorySettingsCollection ====================

    [Fact]
    public void RepositorySettingsCollection_DefaultValues_AreCorrect()
    {
        var collection = new RepositorySettingsCollection();

        Assert.NotNull(collection.Repositories);
        Assert.Empty(collection.Repositories);
        Assert.Null(collection.DefaultWorkspaceDirectory);
    }

    [Fact]
    public void RepositorySettingsCollection_CanSetDefaultWorkspaceDirectory()
    {
        var collection = new RepositorySettingsCollection
        {
            DefaultWorkspaceDirectory = "C:/Workspaces"
        };

        Assert.Equal("C:/Workspaces", collection.DefaultWorkspaceDirectory);
    }

    [Fact]
    public void RepositorySettingsCollection_CanAddRepositories()
    {
        var collection = new RepositorySettingsCollection();
        collection.Repositories.Add(new RepositorySettingsEntry { Name = "Repo1" });
        collection.Repositories.Add(new RepositorySettingsEntry { Name = "Repo2" });

        Assert.Equal(2, collection.Repositories.Count);
    }

    // ==================== RepositorySettingsEntry ====================

    [Fact]
    public void RepositorySettingsEntry_DefaultValues_AreCorrect()
    {
        var entry = new RepositorySettingsEntry();

        Assert.Equal("", entry.Id);
        Assert.Equal("", entry.Name);
        Assert.Equal("", entry.RemotePath);
        Assert.Equal("", entry.LocalPath);
        Assert.Equal("Local", entry.VcsType);
        Assert.Null(entry.PreferredRevision);
        Assert.True(entry.AutoLoad);
        Assert.NotNull(entry.LibraryPaths);
        Assert.Empty(entry.LibraryPaths);
    }

    [Fact]
    public void RepositorySettingsEntry_AllProperties_CanBeSet()
    {
        var entry = new RepositorySettingsEntry
        {
            Id = "id1",
            Name = "TestRepo",
            RemotePath = "https://example.com/repo.git",
            LocalPath = "C:/Repos/TestRepo",
            VcsType = "Git",
            PreferredRevision = "main",
            AutoLoad = false,
            LibraryPaths = new List<string> { "Lib1", "Lib2" }
        };

        Assert.Equal("id1", entry.Id);
        Assert.Equal("TestRepo", entry.Name);
        Assert.Equal("https://example.com/repo.git", entry.RemotePath);
        Assert.Equal("C:/Repos/TestRepo", entry.LocalPath);
        Assert.Equal("Git", entry.VcsType);
        Assert.Equal("main", entry.PreferredRevision);
        Assert.False(entry.AutoLoad);
        Assert.Equal(2, entry.LibraryPaths.Count);
    }

    // ==================== FilePickerResult ====================

    [Fact]
    public void FilePickerResult_DefaultValues_AreCorrect()
    {
        var result = new FilePickerResult();

        Assert.Null(result.FilePath);
        Assert.Equal("", result.Content);
        Assert.False(result.IsPackageFile);
        Assert.Null(result.DirectoryPath);
    }

    [Fact]
    public void FilePickerResult_AllProperties_CanBeSet()
    {
        var result = new FilePickerResult
        {
            FilePath = "C:/Models/package.mo",
            Content = "package TestPackage end TestPackage;",
            IsPackageFile = true,
            DirectoryPath = "C:/Models"
        };

        Assert.Equal("C:/Models/package.mo", result.FilePath);
        Assert.Equal("package TestPackage end TestPackage;", result.Content);
        Assert.True(result.IsPackageFile);
        Assert.Equal("C:/Models", result.DirectoryPath);
    }

    // ==================== Repository ====================

    [Fact]
    public void Repository_DefaultValues_AreCorrect()
    {
        var repo = new Repository();

        Assert.NotEmpty(repo.Id);
        Assert.Equal("", repo.Name);
        Assert.Equal("", repo.RemotePath);
        Assert.Equal("", repo.LocalPath);
        Assert.Equal("", repo.VcsRootPath);
        Assert.Equal(RepositoryVcsType.Local, repo.VcsType);
        Assert.Null(repo.CurrentRevision);
        Assert.Null(repo.CurrentBranch);
        Assert.Null(repo.RevisionDescription);
        Assert.NotNull(repo.LibraryIds);
        Assert.Empty(repo.LibraryIds);
        Assert.NotNull(repo.DiscoveredLibraries);
        Assert.Empty(repo.DiscoveredLibraries);
        Assert.False(repo.IsLoaded);
        Assert.Null(repo.LastError);
        Assert.Null(repo.LastLoadedAt);
        Assert.Null(repo.StyleSettings);
    }

    [Fact]
    public void Repository_AllProperties_CanBeSet()
    {
        var now = DateTime.UtcNow;
        var settings = new ModelicaGraph.StyleCheckingSettings();
        var repo = new Repository
        {
            Id = "repo-1",
            Name = "MyRepo",
            RemotePath = "https://example.com/repo.git",
            LocalPath = "C:/Repos/MyRepo/Library",
            VcsRootPath = "C:/Repos/MyRepo",
            VcsType = RepositoryVcsType.Git,
            CurrentRevision = "abc123",
            CurrentBranch = "main",
            RevisionDescription = "Initial commit",
            LibraryIds = new List<string> { "lib1", "lib2" },
            DiscoveredLibraries = new Dictionary<string, string> { { ".", "MyLib" } },
            IsLoaded = true,
            LastError = null,
            LastLoadedAt = now,
            StyleSettings = settings
        };

        Assert.Equal("repo-1", repo.Id);
        Assert.Equal("MyRepo", repo.Name);
        Assert.Equal("https://example.com/repo.git", repo.RemotePath);
        Assert.Equal("C:/Repos/MyRepo/Library", repo.LocalPath);
        Assert.Equal("C:/Repos/MyRepo", repo.VcsRootPath);
        Assert.Equal(RepositoryVcsType.Git, repo.VcsType);
        Assert.Equal("abc123", repo.CurrentRevision);
        Assert.Equal("main", repo.CurrentBranch);
        Assert.Equal("Initial commit", repo.RevisionDescription);
        Assert.Equal(2, repo.LibraryIds.Count);
        Assert.Single(repo.DiscoveredLibraries);
        Assert.True(repo.IsLoaded);
        Assert.Equal(now, repo.LastLoadedAt);
        Assert.Same(settings, repo.StyleSettings);
    }

    [Fact]
    public void Repository_RelativeLibraryPath_NullWhenLocalEqualsVcsRoot()
    {
        var repo = new Repository
        {
            LocalPath = "C:/Repos/MyRepo",
            VcsRootPath = "C:/Repos/MyRepo"
        };

        Assert.Null(repo.RelativeLibraryPath);
    }

    [Fact]
    public void Repository_RelativeLibraryPath_ReturnsRelativeWhenDifferent()
    {
        var repo = new Repository
        {
            LocalPath = Path.Combine("C:", "Repos", "MyRepo", "Library"),
            VcsRootPath = Path.Combine("C:", "Repos", "MyRepo")
        };

        Assert.Equal("Library", repo.RelativeLibraryPath);
    }

    [Fact]
    public void Repository_RelativeLibraryPath_NestedSubdirectory()
    {
        var repo = new Repository
        {
            LocalPath = Path.Combine("C:", "Repos", "MyRepo", "src", "lib"),
            VcsRootPath = Path.Combine("C:", "Repos", "MyRepo")
        };

        Assert.Equal(Path.Combine("src", "lib"), repo.RelativeLibraryPath);
    }

    [Fact]
    public void Repository_UniqueIds()
    {
        var repo1 = new Repository();
        var repo2 = new Repository();

        Assert.NotEqual(repo1.Id, repo2.Id);
    }

    [Fact]
    public void Repository_VcsType_AllValuesAccessible()
    {
        Assert.NotEqual(RepositoryVcsType.Local, RepositoryVcsType.Git);
        Assert.NotEqual(RepositoryVcsType.Local, RepositoryVcsType.SVN);
        Assert.NotEqual(RepositoryVcsType.Git, RepositoryVcsType.SVN);
    }

    [Fact]
    public void Repository_LibraryIds_CanBeModified()
    {
        var repo = new Repository();
        repo.LibraryIds.Add("lib1");
        repo.LibraryIds.Add("lib2");

        Assert.Equal(2, repo.LibraryIds.Count);

        repo.LibraryIds.Remove("lib1");
        Assert.Single(repo.LibraryIds);
    }

    [Fact]
    public void Repository_DiscoveredLibraries_CanBeModified()
    {
        var repo = new Repository();
        repo.DiscoveredLibraries["path1"] = "Lib1";
        repo.DiscoveredLibraries["path2"] = "Lib2";

        Assert.Equal(2, repo.DiscoveredLibraries.Count);
        Assert.Equal("Lib1", repo.DiscoveredLibraries["path1"]);
    }

    // ==================== FileMonitoringServiceHelpers ====================

    [Fact]
    public void IsInHiddenDirectory_ReturnsTrueForDotGitPath()
    {
        Assert.True(FileMonitoringServiceHelpers.IsInHiddenDirectory("C:/Repos/.git/config"));
    }

    [Fact]
    public void IsInHiddenDirectory_ReturnsTrueForDotSvnPath()
    {
        Assert.True(FileMonitoringServiceHelpers.IsInHiddenDirectory("C:/Repos/.svn/entries"));
    }

    [Fact]
    public void IsInHiddenDirectory_ReturnsFalseForNormalPath()
    {
        Assert.False(FileMonitoringServiceHelpers.IsInHiddenDirectory("C:/Repos/MyLib/Model.mo"));
    }

    [Fact]
    public void IsInHiddenDirectory_ReturnsFalseForDotSeparatedFileName()
    {
        Assert.False(FileMonitoringServiceHelpers.IsInHiddenDirectory("C:/Models/package.order"));
    }

    [Fact]
    public void IsInHiddenDirectory_ReturnsTrueForHiddenDirectoryAnywhere()
    {
        Assert.True(FileMonitoringServiceHelpers.IsInHiddenDirectory("C:/Repos/MyLib/.hidden/file.mo"));
    }
}
