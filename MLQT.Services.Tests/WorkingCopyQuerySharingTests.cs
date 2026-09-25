using MLQT.Services.DataTypes;
using RevisionControl;

namespace MLQT.Services.Tests;

/// <summary>
/// B293 — one working-copy status query per repository at a time, shared by everyone who asks.
///
/// <para>After Update on MSL, 55 LibGit2Sharp status scans of the one working copy were running at
/// once: every caller that missed the cache started its own, and a VCS operation makes them all miss
/// it together. The window froze for two and a half minutes behind them; one scan alone took under a
/// third of a second.</para>
///
/// <para>The query itself is replaced through <see cref="RepositoryService.QueryWorkingCopy"/>, so
/// these can count and hold queries without a working copy that is really slow. The repository is
/// real, and a Git one, because a local repository never reaches the query at all.</para>
/// </summary>
public class WorkingCopyQuerySharingTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "mlqt-wc-sharing", Guid.NewGuid().ToString("N"));

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

    private async Task<(RepositoryService Service, string RepositoryId)> BuildAsync()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "package.mo"), "package TestLib\nend TestLib;\n");
        Git("init");

        var service = new RepositoryService(new LibraryDataService(), new InMemorySettingsService(), new FileMonitoringService());
        var added = await service.AddRepositoryAsync(_directory, startMonitoring: false);
        Assert.True(added.Success, added.ErrorMessage);
        Assert.Equal(RepositoryVcsType.Git, added.Repository!.VcsType);

        service.InvalidateWorkingCopyCache();
        return (service, added.Repository.Id);
    }

    private void Git(string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = _directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {arguments} failed: {stderr.Result}");
    }

    [Fact]
    public async Task CallersArrivingTogether_ShareOneQuery()
    {
        var (service, repositoryId) = await BuildAsync();

        var queries = 0;
        service.QueryWorkingCopy = _ =>
        {
            Interlocked.Increment(ref queries);
            Thread.Sleep(500);
            return [new VcsWorkingCopyFile { Path = "package.mo", Status = VcsFileStatus.Modified }];
        };

        const int callers = 20;
        var answers = new List<VcsWorkingCopyFile>[callers];
        Parallel.For(0, callers, new ParallelOptions { MaxDegreeOfParallelism = callers },
            i => answers[i] = service.GetWorkingCopyChanges(repositoryId));

        Assert.Equal(1, queries);
        Assert.All(answers, answer => Assert.Same(answers[0], answer));
    }

    [Fact]
    public async Task ACallerAfterAnInvalidation_DoesNotJoinAQueryStartedBeforeIt()
    {
        // The query in flight may have read the working copy before the change the invalidation was
        // about, so joining it would hand back the state from before the change.
        var (service, repositoryId) = await BuildAsync();

        var firstStarted = new ManualResetEventSlim(false);
        var releaseFirst = new ManualResetEventSlim(false);
        var before = new List<VcsWorkingCopyFile>();
        var after = new List<VcsWorkingCopyFile> { new() { Path = "package.mo", Status = VcsFileStatus.Modified } };
        var queries = 0;

        service.QueryWorkingCopy = _ =>
        {
            if (Interlocked.Increment(ref queries) == 1)
            {
                firstStarted.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(10));
                return before;
            }
            return after;
        };

        var first = Task.Run(() => service.GetWorkingCopyChanges(repositoryId));
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(10)));

        service.InvalidateWorkingCopyCache(repositoryId);
        var second = service.GetWorkingCopyChanges(repositoryId);
        releaseFirst.Set();

        Assert.Same(after, second);
        Assert.Same(before, await first);

        // ...and the stale answer, finishing last, must not have replaced the fresh one in the cache.
        Assert.Same(after, service.GetWorkingCopyChanges(repositoryId));
        Assert.Equal(2, queries);
    }

    [Fact]
    public async Task AQueryThatFails_IsNotSharedWithTheNextCaller()
    {
        var (service, repositoryId) = await BuildAsync();

        var queries = 0;
        service.QueryWorkingCopy = _ =>
        {
            if (Interlocked.Increment(ref queries) == 1)
                throw new InvalidOperationException("the working copy could not be read");
            return [];
        };

        Assert.Throws<InvalidOperationException>(() => service.GetWorkingCopyChanges(repositoryId));
        Assert.Empty(service.GetWorkingCopyChanges(repositoryId));
        Assert.Equal(2, queries);
    }
}
