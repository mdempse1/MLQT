using Moq;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using RevisionControl;

namespace MLQT.Services.Tests;

/// <summary>
/// B293 — the baseline refresh neither holds the thread that asked for it nor runs beside itself.
///
/// <para><b>What it did to Update on MSL.</b> The refresh asks every repository for its working-copy
/// status. A repository finishing its load ran one synchronously on whoever raised the event, and a
/// load awaited from the UI raises it on the UI thread; the throttle limited how often a refresh
/// started but not how many were running, and a VCS operation had fourteen going at once beside the
/// library tree's own queries. The window sat inside one of those scans for two and a half
/// minutes.</para>
/// </summary>
public class BaselineStatusQueueTests : IDisposable
{
    private readonly string _repoDir = Path.Combine(
        Path.GetTempPath(), "mlqt-baseline-queue", Guid.NewGuid().ToString("N"));

    public BaselineStatusQueueTests() => Directory.CreateDirectory(_repoDir);

    public void Dispose()
    {
        try { Directory.Delete(_repoDir, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// A service over one repository holding one real library, so a refresh reaches the working-copy
    /// query rather than returning early for want of models — as in
    /// <see cref="BaselineStatusDoesNotBlockTests"/>, whose first version passed for that reason.
    /// </summary>
    private (BaselineStatusService Service, Mock<IRepositoryService> Repositories, string RepositoryId) Build(
        Action onQuery)
    {
        var libraries = new LibraryDataService();
        var library = libraries.AddLibraryFromFileAsync(
                Path.Combine(_repoDir, "Lib.mo"),
                "package Lib \"lib\"\n  model A \"a\"\n  end A;\nend Lib;")
            .GetAwaiter().GetResult();

        var repository = new Repository
        {
            Name = "R",
            LocalPath = _repoDir,
            VcsRootPath = _repoDir,
            VcsType = RepositoryVcsType.Git,
        };
        library.RepositoryId = repository.Id;

        var repositories = new Mock<IRepositoryService>();
        repositories.SetupGet(r => r.Repositories).Returns([repository]);
        repositories.Setup(r => r.GetWorkingCopyChanges(It.IsAny<string>()))
            .Returns(() =>
            {
                onQuery();
                return [];
            });

        var service = new BaselineStatusService(libraries, repositories.Object, new FileMonitoringService());
        return (service, repositories, repository.Id);
    }

    [Fact]
    public async Task ARepositoryFinishingItsLoad_DoesNotWaitForTheWorkingCopyQuery()
    {
        var slow = TimeSpan.FromSeconds(2);
        var queried = new ManualResetEventSlim(false);
        var (service, repositories, repositoryId) = Build(() =>
        {
            queried.Set();
            Thread.Sleep(slow);
        });

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        repositories.Raise(r => r.OnRepositoryLoadStateChanged += null, repositoryId, false);
        elapsed.Stop();

        // The premise: the refresh did reach the slow query, so "fast" means "not waiting for it".
        Assert.True(queried.Wait(TimeSpan.FromSeconds(10)), "finishing a load never refreshed the classification");
        await service.Background;

        Assert.True(elapsed.Elapsed < slow,
            $"raising the load-finished event took {elapsed.ElapsedMilliseconds}ms. A load awaited from the "
            + "UI raises it on the UI thread, and the refresh is a working-copy scan of every repository.");
    }

    [Fact]
    public async Task RefreshesNeverRunBesideEachOther_AndABurstCostsAFewRuns()
    {
        var running = 0;
        var mostAtOnce = 0;
        var runs = 0;
        var (service, _, _) = Build(() =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref mostAtOnce, now);
            Interlocked.Increment(ref runs);
            Thread.Sleep(50);
            Interlocked.Decrement(ref running);
        });

        const int requests = 40;
        var asked = new Task[requests];
        Parallel.For(0, requests, i => asked[i] = service.RefreshAsync());
        await Task.WhenAll(asked);

        Assert.Equal(1, mostAtOnce);
        Assert.InRange(runs, 1, requests / 4);
    }

    [Fact]
    public async Task ARequestMadeWhileARunIsGoing_GetsARunAfterIt()
    {
        // The other half of folding requests together: a request must not be absorbed by a run that
        // had already read the working copy before it was made.
        var firstStarted = new ManualResetEventSlim(false);
        var releaseFirst = new ManualResetEventSlim(false);
        var runs = 0;
        var (service, _, _) = Build(() =>
        {
            if (Interlocked.Increment(ref runs) == 1)
            {
                firstStarted.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(10));
            }
        });

        var first = service.RefreshAsync();
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(10)));

        var second = service.RefreshAsync();
        releaseFirst.Set();
        await Task.WhenAll(first, second);

        Assert.Equal(2, Volatile.Read(ref runs));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref target))
               && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }
}
