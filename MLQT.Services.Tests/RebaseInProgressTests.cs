using MLQT.Services.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// B382 - a rebase left stopped is known to the repository, so the browser can say so and the rebase
/// dialog can offer to continue or abort it.
/// </summary>
/// <remarks>
/// The repository is real and the rebase is git's own, stopped on a conflict, because what is being
/// held is that MLQT finds a state another tool - or an earlier dialog - left behind.
/// </remarks>
public class RebaseInProgressTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "mlqt-rebase-in-progress", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);   // git's object files are read-only
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    private int Git(string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = _directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["GIT_EDITOR"] = "true";
        using var process = System.Diagnostics.Process.Start(psi)!;
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    private void Write(string content) =>
        File.WriteAllText(Path.Combine(_directory, "package.mo"), content);

    /// <summary>A repository whose branch <c>feature</c> is part-way through a rebase onto <c>base</c>.</summary>
    private void StopARebaseOnAConflict()
    {
        Directory.CreateDirectory(_directory);
        Assert.Equal(0, Git("init -b base"));
        Assert.Equal(0, Git("config user.name Test"));
        Assert.Equal(0, Git("config user.email test@example.com"));
        Write("package TestLib\nend TestLib;\n");
        Assert.Equal(0, Git("add package.mo"));
        Assert.Equal(0, Git("commit -m first"));
        Assert.Equal(0, Git("checkout -b feature"));
        Write("package TestLib \"feature\"\nend TestLib;\n");
        Assert.Equal(0, Git("commit -am feature"));
        Assert.Equal(0, Git("checkout base"));
        Write("package TestLib \"base\"\nend TestLib;\n");
        Assert.Equal(0, Git("commit -am base"));
        Assert.Equal(0, Git("checkout feature"));
        Assert.NotEqual(0, Git("rebase base"));
    }

    private async Task<(RepositoryService Service, Repository Repository)> AddAsync()
    {
        var service = new RepositoryService(new LibraryDataService(), new InMemorySettingsService(), new FileMonitoringService());
        var added = await service.AddRepositoryAsync(_directory, startMonitoring: false);
        Assert.True(added.Success, added.ErrorMessage);
        return (service, added.Repository!);
    }

    [Fact]
    public async Task ARebaseLeftStopped_IsKnownWhenTheRepositoryIsLoaded_AndWhenAskedAgain()
    {
        StopARebaseOnAConflict();

        var (service, repository) = await AddAsync();

        Assert.Null(repository.CurrentBranch);
        Assert.Equal("feature", repository.RebaseInProgress?.Branch);

        var asked = await service.GetRebaseInProgressAsync(repository.Id);
        Assert.NotNull(asked);
        Assert.Equal([Path.Combine(_directory, "package.mo")], asked.ConflictedFiles);
    }

    [Fact]
    public async Task ARebaseAborted_IsNoLongerInProgress()
    {
        StopARebaseOnAConflict();
        var (service, repository) = await AddAsync();

        Assert.True((await service.AbortRebaseAsync(repository.Id)).Success);

        Assert.Equal("feature", repository.CurrentBranch);
        Assert.Null(repository.RebaseInProgress);
        Assert.Null(await service.GetRebaseInProgressAsync(repository.Id));
    }

    [Fact]
    public async Task ARepositoryThatIsNotGit_HasNoRebase()
    {
        Directory.CreateDirectory(_directory);
        Write("package TestLib\nend TestLib;\n");
        var (service, repository) = await AddAsync();

        Assert.Null(repository.RebaseInProgress);
        Assert.Null(await service.GetRebaseInProgressAsync(repository.Id));
        Assert.Null(await service.GetRebaseInProgressAsync("no-such-repository"));
    }
}
