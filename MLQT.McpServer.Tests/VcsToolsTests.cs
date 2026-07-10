using Moq;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using RevisionControl;

namespace MLQT.McpServer.Tests;

public class VcsToolsTests
{
    private static readonly Dictionary<string, string> LibFiles = new()
    {
        ["package.mo"] = "within;\npackage DepLib \"d\"\nend DepLib;\n",
        ["package.order"] = "Base\nMiddle\n",
        ["Base.mo"] = "within DepLib;\nmodel Base \"b\"\n  Real b \"s\";\nequation\n  b = time;\nend Base;\n",
        ["Middle.mo"] = "within DepLib;\nmodel Middle \"m\"\n  Base base1 \"a base\";\nend Middle;\n",
    };

    private static VcsTools Build(TestHost host, out string repoId, string changedFile = "Base.mo")
    {
        var dir = host.WriteLibraryDir(LibFiles);
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();

        const string id = "r1";
        repoId = id;
        var repo = new Repository { Id = id, Name = "r", LocalPath = dir, VcsRootPath = dir };

        var repoMock = new Mock<IRepositoryService>();
        repoMock.Setup(r => r.Repositories).Returns(new List<Repository> { repo });
        repoMock.Setup(r => r.GetRepository(id)).Returns(repo);
        repoMock.Setup(r => r.GetRepository(It.Is<string>(s => s != id))).Returns((Repository?)null);
        repoMock.Setup(r => r.GetWorkingCopyChanges(id))
            .Returns(new List<VcsWorkingCopyFile> { new() { Path = changedFile, Status = VcsFileStatus.Modified } });
        repoMock.Setup(r => r.GetChangedFiles(id, It.IsAny<string>()))
            .Returns(new List<VcsChangedFile> { new() { Path = changedFile, ChangeType = VcsChangeType.Modified } });

        return new VcsTools(host.Libraries, repoMock.Object, host.Impact, host.Session);
    }

    [Fact]
    public void GetChangedClasses_WorkingCopy_MapsFileToClass()
    {
        using var host = new TestHost();
        var vcs = Build(host, out var repoId);

        var res = ToolAssert.Ok<ChangedClassesResult>(vcs.GetChangedClasses(repoId));
        Assert.Equal("workingCopy", res.Revision);
        Assert.Equal(1, res.ChangedFileCount);
        Assert.Contains("DepLib.Base", res.ClassIds);
    }

    [Fact]
    public void GetChangedClasses_ByRepositoryName_Works()
    {
        using var host = new TestHost();
        var vcs = Build(host, out _); // repository Name is "r"
        var res = ToolAssert.Ok<ChangedClassesResult>(vcs.GetChangedClasses("r"));
        Assert.Contains("DepLib.Base", res.ClassIds);
    }

    [Fact]
    public void GetChangedClasses_Revision_UsesChangedFiles()
    {
        using var host = new TestHost();
        var vcs = Build(host, out var repoId);

        var res = ToolAssert.Ok<ChangedClassesResult>(vcs.GetChangedClasses(repoId, revision: "HEAD"));
        Assert.Equal("HEAD", res.Revision);
        Assert.Contains("DepLib.Base", res.ClassIds);
    }

    [Fact]
    public void GetChangedClasses_UnknownRepo_Errors()
    {
        using var host = new TestHost();
        var vcs = Build(host, out _);
        Assert.IsType<ToolError>(vcs.GetChangedClasses("nope"));
    }

    [Fact]
    public void GetChangedClasses_NoLibraryLoaded_Guides()
    {
        using var host = new TestHost();
        var repo = new Repository { Id = "r1", Name = "r", LocalPath = "x", VcsRootPath = "x" };
        var repoMock = new Mock<IRepositoryService>();
        repoMock.Setup(r => r.Repositories).Returns(new List<Repository> { repo });
        repoMock.Setup(r => r.GetRepository("r1")).Returns(repo);
        var vcs = new VcsTools(host.Libraries, repoMock.Object, host.Impact, host.Session);

        var err = ToolAssert.Error(vcs.GetChangedClasses("r1"));
        Assert.Contains("library", err.Error);
    }

    [Fact]
    public void GetChangedClasses_NonMoChange_Ignored()
    {
        using var host = new TestHost();
        var vcs = Build(host, out var repoId, changedFile: "README.txt");
        var res = ToolAssert.Ok<ChangedClassesResult>(vcs.GetChangedClasses(repoId));
        Assert.Equal(0, res.ChangedFileCount);
    }

    [Fact]
    public async Task AnalyzeChangeImpact_AfterAnalyze_ReturnsBlastRadius()
    {
        using var host = new TestHost();
        var vcs = Build(host, out var repoId);

        // Before analysis: guidance to run analyze_dependencies (get_changed_classes still works).
        var pre = ToolAssert.Error(vcs.AnalyzeChangeImpact(repoId));
        Assert.Contains("analyze_dependencies", pre.Error);

        // Run dependency analysis, then the impact appears.
        var deps = new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session);
        await deps.AnalyzeDependencies();

        var post = ToolAssert.Ok<ChangeImpactResult>(vcs.AnalyzeChangeImpact(repoId));
        Assert.True(post.DependenciesAnalyzed);
        Assert.Contains(post.ImpactDetails, d => d.ModelId == "DepLib.Middle");
    }

    [Fact]
    public void AnalyzeChangeImpact_UnknownRepo_Errors()
    {
        using var host = new TestHost();
        var vcs = Build(host, out _);
        Assert.IsType<ToolError>(vcs.AnalyzeChangeImpact("nope"));
    }
}
