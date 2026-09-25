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

    /// <summary>Runs git in a directory, fails the test on a non-zero exit, and returns stdout.</summary>
    private static string Git(string workingDirectory, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", args)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var git = System.Diagnostics.Process.Start(psi)!;
        var stderr = git.StandardError.ReadToEndAsync();
        var stdout = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        Assert.True(git.ExitCode == 0, $"git {args}: {stderr.Result}");
        return stdout.Trim();
    }

    private const string Identity = "-c user.email=t@t -c user.name=T";

    /// <summary>
    /// One checkout holding two libraries, committed on <c>main</c> and pushed to a bare remote -
    /// the shape B324 needs: a sibling library whose push goes somewhere real.
    /// </summary>
    private (string checkout, string remote) PushedCheckoutWith(params string[] libraries)
    {
        var remote = Path.Combine(_root, "remote.git");
        Directory.CreateDirectory(remote);
        Git(remote, "init --bare -b main");

        var checkout = GitRepositoryWith("Pushed", libraries);
        Git(checkout, "checkout -b main");
        Git(checkout, "add -A");
        Git(checkout, $"{Identity} commit -m first");
        Git(checkout, $"remote add origin \"{remote}\"");
        Git(checkout, "push -u origin main");
        return (checkout, remote);
    }

    private static void CommitAFile(string checkout, string name)
    {
        File.WriteAllText(Path.Combine(checkout, name), "x");
        Git(checkout, $"add {name}");
        Git(checkout, $"{Identity} commit -m {name}");
    }

    /// <summary>
    /// B324: Create Branch from one library moves the whole checkout, so the other library's stored
    /// branch must follow - it is what the Library Browser shows and what a push used to push.
    /// </summary>
    [Fact]
    public async Task CreateBranchFromOneLibrary_MovesTheOtherLibrarysBranchToo()
    {
        var (checkout, _) = PushedCheckoutWith("LibA", "LibB");
        var service = new RepositoryService(new LibraryDataService(), new InMemorySettingsService(), new FileMonitoringService());
        var a = await Add(service, Path.Combine(checkout, "LibA"));
        var b = await Add(service, Path.Combine(checkout, "LibB"));
        Assert.Equal("main", b.CurrentBranch);

        var created = await service.CreateBranchAsync(a.Id, "feature");

        Assert.True(created.Success, created.ErrorMessage);
        Assert.Equal("feature", a.CurrentBranch);
        Assert.Equal("feature", b.CurrentBranch);
    }

    /// <summary>
    /// B324: a commit made from one library is the other library's current revision too.
    /// </summary>
    [Fact]
    public async Task CommitFromOneLibrary_MovesTheOtherLibrarysRevisionToo()
    {
        var (checkout, _) = PushedCheckoutWith("LibA", "LibB");
        var service = new RepositoryService(new LibraryDataService(), new InMemorySettingsService(), new FileMonitoringService());
        var a = await Add(service, Path.Combine(checkout, "LibA"));
        var b = await Add(service, Path.Combine(checkout, "LibB"));
        var before = b.CurrentRevision;

        File.WriteAllText(Path.Combine(checkout, "LibA", "package.mo"), "package LibA \"changed\"\nend LibA;\n");
        var committed = await service.CommitAsync(a.Id, "change LibA");

        Assert.True(committed.Success, committed.ErrorMessage);
        Assert.NotEqual(before, b.CurrentRevision);
        Assert.Equal(committed.NewRevision, b.CurrentRevision);
    }

    /// <summary>
    /// B324, the dangerous half: a push acts on the branch HEAD is on, whatever the repository
    /// last remembered. Here the checkout is switched by another tool, so every stored
    /// <c>CurrentBranch</c> still says <c>main</c>; the force push must create <c>feature</c> on the
    /// remote and leave <c>main</c> exactly where it was. Pushing the stored name instead pushed
    /// <c>main</c> - which, after a rebase, a lease against a fresh fetch lets rewind.
    /// </summary>
    [Fact]
    public async Task PushAndForcePush_PushTheBranchHeadIsOn_NotTheStoredOne()
    {
        var (checkout, remote) = PushedCheckoutWith("LibA", "LibB");
        CommitAFile(checkout, "second.txt");
        Git(checkout, "push origin main");
        var remoteMain = Git(remote, "rev-parse main");

        var service = new RepositoryService(new LibraryDataService(), new InMemorySettingsService(), new FileMonitoringService());
        await Add(service, Path.Combine(checkout, "LibA"));
        var b = await Add(service, Path.Combine(checkout, "LibB"));

        // Another tool moves the checkout onto a branch one commit behind main.
        Git(checkout, "checkout -b feature HEAD~1");
        Assert.Equal("main", b.CurrentBranch);   // the premise: the stored name is stale

        var forced = await service.ForcePushAsync(b.Id);

        Assert.True(forced.Success, forced.ErrorMessage);
        Assert.Equal(Git(checkout, "rev-parse HEAD"), Git(remote, "rev-parse feature"));
        Assert.Equal(remoteMain, Git(remote, "rev-parse main"));
        Assert.Equal("feature", b.CurrentBranch);

        CommitAFile(checkout, "third.txt");
        var pushed = await service.PushAsync(b.Id);

        Assert.True(pushed.Success, pushed.ErrorMessage);
        Assert.Equal(Git(checkout, "rev-parse HEAD"), Git(remote, "rev-parse feature"));
        Assert.Equal(remoteMain, Git(remote, "rev-parse main"));
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
