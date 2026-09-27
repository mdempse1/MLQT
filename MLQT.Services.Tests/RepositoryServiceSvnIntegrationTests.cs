using MLQT.Services;
using MLQT.Services.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// <see cref="RepositoryService"/> against a real Subversion working copy: a checkout of the
/// repository this run builds for itself (<see cref="SvnTestRepository"/>), whose trunk holds the
/// library <c>ModelicaEditorTest</c> at its root and which has one branch, <c>feature-test</c>.
/// </summary>
/// <remarks>
/// <para><b>Why these are a class of their own (B471).</b> They were part of
/// <see cref="RepositoryServiceTests"/>, run against a working copy the developer kept at a fixed
/// path, and returned before asserting anything wherever that folder was absent - so they passed on
/// every machine but one, and on that one they could collide with any other run using it. They need
/// an svn client and svnadmin, and nothing else in that class does, so they are here where the
/// SVN filter can name them: CI and <c>run-all-tests.ps1</c> without svn exclude exactly this class,
/// and anywhere with svn every test asserts. <c>SvnTestFilterTests</c> holds the filter.</para>
///
/// <para>Each test checks out its own working copy, because a merge leaves one modified, and
/// every repository id goes through <see cref="RepositoryServiceTests.SandboxedId"/>: the checkout
/// is under the temp directory, so the guard every other test in the suite takes applies here too.</para>
/// </remarks>
public class RepositoryServiceSvnIntegrationTests(SvnTestRepository svn) : IClassFixture<SvnTestRepository>
{
    private static (RepositoryService Service, LibraryDataService Libraries) CreateService()
    {
        var libraryDataService = new LibraryDataService();
        var service = new RepositoryService(libraryDataService, new InMemorySettingsService(), new FileMonitoringService());
        return (service, libraryDataService);
    }

    [Fact]
    public void DetectVcsType_WithSvnWorkingCopy_ReturnsSvnLocal()
    {
        var (service, _) = CreateService();

        var (vcsType, isLocal) = service.DetectVcsType(svn.CheckOut());

        Assert.Equal(RepositoryVcsType.SVN, vcsType);
        Assert.True(isLocal);
    }

    [Fact]
    public async Task AddRepositoryAsync_WithRootLevelLibrary_DiscoversLibrary()
    {
        var (service, _) = CreateService();

        var result = await service.AddRepositoryAsync(svn.CheckOut());

        Assert.True(result.Success, result.ErrorMessage);
        RepositoryServiceTests.SandboxedId(result);
        Assert.Equal(RepositoryVcsType.SVN, result.Repository!.VcsType);

        // The library's package.mo is at the root of the working copy, not in a subdirectory.
        var library = Assert.Single(result.DiscoveredLibraries);
        Assert.Equal("", library.RelativePath);
        Assert.Equal("ModelicaEditorTest", library.LibraryName);
    }

    [Fact]
    public async Task LoadLibrariesAsync_WithRootLevelLibrary_LoadsCorrectly()
    {
        var (service, libraries) = CreateService();
        var result = await service.AddRepositoryAsync(svn.CheckOut());
        Assert.True(result.Success, result.ErrorMessage);
        var id = RepositoryServiceTests.SandboxedId(result);

        await service.LoadLibrariesAsync(id, [""]);

        Assert.NotEmpty(result.Repository!.LibraryIds);
        Assert.Single(libraries.Libraries);
        Assert.Contains(libraries.CombinedGraph.ModelNodes, m => m.Id == "ModelicaEditorTest.Models.SimpleModel");
    }

    [Fact]
    public async Task MergeBranchAsync_WithNonExistentBranch_ReturnsError()
    {
        var (service, _) = CreateService();
        var addResult = await service.AddRepositoryAsync(svn.CheckOut());
        Assert.True(addResult.Success, addResult.ErrorMessage);

        var result = await service.MergeBranchAsync(
            RepositoryServiceTests.SandboxedId(addResult), "branches/non-existent-branch-12345");

        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
    }

    [Fact]
    public async Task MergeBranchAsync_FromAnotherBranch_SucceedsAndFiresOnRepositoriesChanged()
    {
        var (service, _) = CreateService();
        var addResult = await service.AddRepositoryAsync(svn.CheckOut());
        Assert.True(addResult.Success, addResult.ErrorMessage);
        var id = RepositoryServiceTests.SandboxedId(addResult);

        var branches = service.GetBranches(id);
        var feature = Assert.Single(branches, b => !b.IsCurrent && b.Name.Contains("feature-test", StringComparison.Ordinal));

        var eventFired = false;
        service.OnRepositoriesChanged += () => eventFired = true;

        var result = await service.MergeBranchAsync(id, feature.Name);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(result.HasConflicts);
        Assert.True(eventFired);
    }
}
