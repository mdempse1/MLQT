namespace MLQT.Shared.Helpers;

/// <summary>
/// The file monitor held off for one VCS operation, and started again however that operation ends
/// (B296).
/// </summary>
/// <remarks>
/// <para><b>The rule: whoever stops the monitor starts it again, and never by remembering to.</b> A
/// VCS operation stops the monitor so the thousands of changes it writes are not each reported, and
/// then has to start it again on every way out. Each operation did that for itself, on the paths
/// someone had thought of: cancelling the rebase or merge dialog in its conflict phase, and a
/// checkout from the history whose reload threw, left the repository unwatched for the rest of the
/// session with nothing on screen to say so. Taken with <c>using</c>, or disposed with the dialog
/// holding it, this cannot be forgotten on a path nobody thought of.</para>
///
/// <para><b>Handing over</b> is the one other way out: the analysis pipeline that
/// <see cref="AppState.VcsFilesChanged"/> starts stops the monitor itself while it formats, and starts
/// it again when it is done, so a caller that fires it passes the restart on rather than starting
/// the monitor in the middle of the formatting.</para>
/// </remarks>
public sealed class MonitorPause : IDisposable
{
    private readonly IFileMonitoringService _monitor;
    private readonly string _repositoryId;
    private readonly string _watchedPath;
    private bool _ended;

    private MonitorPause(IFileMonitoringService monitor, string repositoryId, string watchedPath)
    {
        _monitor = monitor;
        _repositoryId = repositoryId;
        _watchedPath = watchedPath;
    }

    /// <summary>Stops the repository's monitor until the returned pause ends.</summary>
    public static MonitorPause Begin(IFileMonitoringService monitor, Repository repository)
    {
        monitor.StopMonitoring(repository.Id);
        return new MonitorPause(monitor, repository.Id, repository.VcsRootPath);
    }

    /// <summary>Whether this pause still has the monitor stopped.</summary>
    public bool IsActive => !_ended;

    /// <summary>
    /// Leaves the restart to the analysis pipeline, which is about to be started with
    /// <see cref="AppState.VcsFilesChanged"/>.
    /// </summary>
    public void HandOver() => _ended = true;

    /// <summary>Starts the monitor again, unless it has already been started or handed over.</summary>
    public void Dispose()
    {
        if (_ended)
            return;

        _ended = true;
        if (!string.IsNullOrEmpty(_watchedPath))
            _monitor.StartMonitoring(_repositoryId, _watchedPath);
    }
}
