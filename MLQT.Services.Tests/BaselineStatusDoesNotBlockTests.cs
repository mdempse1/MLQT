using Moq;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using RevisionControl;

namespace MLQT.Services.Tests;

/// <summary>
/// B253 — the baseline refresh runs on its own thread, not on whoever raised the event.
///
/// <para><b>Where the eleven seconds went.</b> <see cref="BaselineStatusService"/> follows file
/// activity so its answer stays current, and file activity includes MLQT's own writes: saving an
/// annotation restarts the file monitor and calls <c>NotifyFileActivity</c>, which raises the event
/// synchronously. The refresh asks every repository for its working-copy status, which on a library
/// the size of the Modelica Standard Library is a scan of thousands of files — and it was charged to
/// the thread that had just written the file. The <b>Exclude from formatting</b> button took eleven
/// seconds on its first use after an idle spell.</para>
///
/// <para><b>And why it looked intermittent.</b> The throttle took the leading edge synchronously and
/// queued everything inside its window, so the first click after a pause paid for the scan and the
/// next two, seconds apart, were a sixth of a second. That reads as "sometimes slow" rather than as
/// "one call on the wrong thread", and is why two rounds of guessing preceded measuring it.</para>
/// </summary>
public class BaselineStatusDoesNotBlockTests : IDisposable
{
    /// <summary>Long enough that a synchronous refresh cannot be mistaken for a fast one.</summary>
    private static readonly TimeSpan SlowQuery = TimeSpan.FromSeconds(2);

    private readonly string _repoDir = Path.Combine(
        Path.GetTempPath(), "mlqt-baseline-block", Guid.NewGuid().ToString("N"));

    public BaselineStatusDoesNotBlockTests() => Directory.CreateDirectory(_repoDir);

    public void Dispose()
    {
        try { Directory.Delete(_repoDir, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// A service with one repository holding one real library, so a refresh actually reaches the
    /// working-copy query. Without the library it returns before asking anything, and a test built
    /// on that would pass however the refresh was scheduled — which the first version of this did.
    /// </summary>
    private (FileMonitoringService Monitor, string RepositoryId, ManualResetEventSlim Queried) Build(
        Action? onQuery = null)
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

        var queried = new ManualResetEventSlim(false);

        var repositories = new Mock<IRepositoryService>();
        repositories.SetupGet(r => r.Repositories).Returns([repository]);
        repositories.Setup(r => r.GetWorkingCopyChanges(It.IsAny<string>()))
            .Returns(() =>
            {
                queried.Set();
                onQuery?.Invoke();
                return [];
            });

        var monitor = new FileMonitoringService();

        // Kept alive by the event subscription it makes in its constructor, which is the thing under
        // test; the variable is unused on purpose.
        _ = new BaselineStatusService(libraries, repositories.Object, monitor);

        return (monitor, repository.Id, queried);
    }

    [Fact]
    public void RaisingFileActivity_DoesNotWaitForTheWorkingCopyQuery()
    {
        var (monitor, repositoryId, queried) = Build(onQuery: () => Thread.Sleep(SlowQuery));

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        monitor.NotifyFileActivity(repositoryId);
        elapsed.Stop();

        // The premise first: the refresh really did reach the slow query, so "fast" means "not
        // waiting for it" rather than "never went looking".
        Assert.True(queried.Wait(TimeSpan.FromSeconds(10)), "the refresh never queried the working copy");

        Assert.True(elapsed.Elapsed < SlowQuery,
            $"NotifyFileActivity took {elapsed.ElapsedMilliseconds}ms. It is a notification: the "
            + "refresh it triggers asks every repository for its working-copy status, and running "
            + "that on the caller's thread charges it to whoever happened to save a file.");
    }

    [Fact]
    public void TheRefreshStillHappens()
    {
        // The control, and the reason the test above is not just "do nothing": moving the work off
        // the caller's thread must not lose it. Without this, deleting the refresh entirely passes.
        var (monitor, repositoryId, queried) = Build();

        monitor.NotifyFileActivity(repositoryId);

        Assert.True(queried.Wait(TimeSpan.FromSeconds(10)),
            "the refresh never ran — moving it off the caller's thread has to keep it, not drop it");
    }
}
