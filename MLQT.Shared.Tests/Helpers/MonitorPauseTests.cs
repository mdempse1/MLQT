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
    public void ARepositoryWithNoWorkingCopy_IsNotStartedOnNothing()
    {
        var local = new Repository { Id = "local", Name = "Local", LocalPath = Root, VcsRootPath = "" };

        MonitorPause.Begin(_monitor.Object, local).Dispose();

        _monitor.Verify(m => m.StartMonitoring(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
