using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Helpers;
using Moq;
using Xunit;

namespace MLQT.Shared.Tests.Helpers;

/// <summary>
/// B296 — the one rule for the file monitor around a VCS operation: whoever stops it starts it again,
/// unless the analysis pipeline has been handed the restart.
/// </summary>
public class MonitorPauseTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mlqt-tests", "Lib");

    private readonly Mock<IFileMonitoringService> _monitor = new();
    private readonly Repository _repository = new() { Id = "repo", Name = "Lib", LocalPath = Root, VcsRootPath = Root };

    public MonitorPauseTests() =>
        _monitor.Setup(m => m.IsMonitoringRepository(It.IsAny<string>())).Returns(true);

    [Fact]
    public void BeginningStopsTheMonitor_AndEndingStartsItAgain()
    {
        using (MonitorPause.Begin(_monitor.Object, _repository))
        {
            _monitor.Verify(m => m.StopMonitoring("repo"), Times.Once);
            _monitor.Verify(m => m.StartMonitoring(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        _monitor.Verify(m => m.StartMonitoring("repo", Root), Times.Once);
    }

    [Fact]
    public void EndedTwice_StartsTheMonitorOnce()
    {
        // A dialog ends its pause when the operation finishes and again when it is closed.
        var pause = MonitorPause.Begin(_monitor.Object, _repository);

        pause.Dispose();
        pause.Dispose();

        _monitor.Verify(m => m.StartMonitoring("repo", Root), Times.Once);
        Assert.False(pause.IsActive);
    }

    [Fact]
    public void HandedOver_LeavesTheRestartToThePipeline()
    {
        // The pipeline stops the monitor itself while it formats, so starting it here as well
        // would watch the files it is about to rewrite.
        var pause = MonitorPause.Begin(_monitor.Object, _repository);

        pause.HandOver();
        pause.Dispose();

        _monitor.Verify(m => m.StartMonitoring(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void EveryRepositoryInTheWorkingCopy_IsHeldOffAndStartedAgain()
    {
        // B301. The monitor keeps one watcher per folder with a subscriber per repository: stopping
        // one leaves the watcher running while the other still subscribes.
        var neighbour = new Repository { Id = "neighbour", Name = "Test", LocalPath = Root, VcsRootPath = Root };

        using (MonitorPause.Begin(_monitor.Object, [_repository, neighbour]))
        {
            _monitor.Verify(m => m.StopMonitoring("repo"), Times.Once);
            _monitor.Verify(m => m.StopMonitoring("neighbour"), Times.Once);
        }

        _monitor.Verify(m => m.StartMonitoring("repo", Root), Times.Once);
        _monitor.Verify(m => m.StartMonitoring("neighbour", Root), Times.Once);
    }

    [Fact]
    public void ARepositoryThatWasNotBeingWatched_IsNotStartedAfterwards()
    {
        // A reference-only repository is never monitored. A pause that restarted everything it was
        // given would start watching one.
        var reference = new Repository { Id = "reference", Name = "Vendor", LocalPath = Root, VcsRootPath = Root };
        _monitor.Setup(m => m.IsMonitoringRepository("reference")).Returns(false);

        MonitorPause.Begin(_monitor.Object, [_repository, reference]).Dispose();

        _monitor.Verify(m => m.StopMonitoring("reference"), Times.Never);
        _monitor.Verify(m => m.StartMonitoring("reference", It.IsAny<string>()), Times.Never);
        _monitor.Verify(m => m.StartMonitoring("repo", Root), Times.Once);
    }

    [Fact]
    public void ARepositoryWithNoWorkingCopy_IsNotStartedOnNothing()
    {
        var local = new Repository { Id = "local", Name = "Local", LocalPath = Root, VcsRootPath = "" };

        MonitorPause.Begin(_monitor.Object, local).Dispose();

        _monitor.Verify(m => m.StartMonitoring(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
