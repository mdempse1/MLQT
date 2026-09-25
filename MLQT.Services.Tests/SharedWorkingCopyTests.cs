using MLQT.Services.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// B301 — which repositories a VCS operation on one of them reaches.
/// </summary>
/// <remarks>
/// Two libraries checked out in one Git tree are added as two repositories, and every operation acts
/// on the tree: an update, a switch or a merge rewrites both. What follows - the monitor held off,
/// the libraries reloaded, the analysis run - asks
/// <see cref="RepositoryService.GetRepositoriesSharingWorkingCopy"/> which repositories that is.
/// </remarks>
public class SharedWorkingCopyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mlqt-shared-wc", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);   // git's object files are read-only
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    private string GitRepositoryWith(string name, params string[] libraries)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        foreach (var library in libraries)
        {
            Directory.CreateDirectory(Path.Combine(dir, library));
            File.WriteAllText(Path.Combine(dir, library, "package.mo"), $"package {library}\nend {library};\n");
        }

        var psi = new System.Diagnostics.ProcessStartInfo("git", "init")
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var git = System.Diagnostics.Process.Start(psi)!;
        git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        Assert.Equal(0, git.ExitCode);
        return dir;
    }

    private static async Task<Repository> Add(RepositoryService service, string path)
    {
        var added = await service.AddRepositoryAsync(path, startMonitoring: false);
        Assert.True(added.Success, added.ErrorMessage);
        return added.Repository!;
    }

    [Fact]
    public async Task TwoLibrariesInOneCheckout_AreEachOthersWorkingCopy_AndAnotherCheckoutIsNot()
    {
        var shared = GitRepositoryWith("Shared", "LibA", "LibB");
        var elsewhere = GitRepositoryWith("Elsewhere", "LibC");

        var service = new RepositoryService(new LibraryDataService(), new InMemorySettingsService(), new FileMonitoringService());
        var a = await Add(service, Path.Combine(shared, "LibA"));
        var b = await Add(service, Path.Combine(shared, "LibB"));
        var c = await Add(service, Path.Combine(elsewhere, "LibC"));

        // The premise: the two were found to share a root, and the third was not.
        Assert.Equal(a.VcsRootPath, b.VcsRootPath);
        Assert.NotEqual(a.VcsRootPath, c.VcsRootPath);

        Assert.Equal([a.Id, b.Id], service.GetRepositoriesSharingWorkingCopy(a.Id).Select(r => r.Id));
        Assert.Equal([b.Id, a.Id], service.GetRepositoriesSharingWorkingCopy(b.Id).Select(r => r.Id));
        Assert.Equal([c.Id], service.GetRepositoriesSharingWorkingCopy(c.Id).Select(r => r.Id));
    }

    [Fact]
    public async Task ALocalRepository_IsItsOwnWorkingCopy_AndAnUnknownOneHasNone()
    {
        var folder = Path.Combine(_root, "Plain", "LibD");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "package.mo"), "package LibD\nend LibD;\n");

        var service = new RepositoryService(new LibraryDataService(), new InMemorySettingsService(), new FileMonitoringService());
        var local = await Add(service, folder);

        Assert.Equal(RepositoryVcsType.Local, local.VcsType);
        Assert.Equal([local.Id], service.GetRepositoriesSharingWorkingCopy(local.Id).Select(r => r.Id));
        Assert.Empty(service.GetRepositoriesSharingWorkingCopy("no-such-repository"));
    }
}
