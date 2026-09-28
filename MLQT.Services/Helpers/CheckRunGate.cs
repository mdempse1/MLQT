namespace MLQT.Services.Helpers;

/// <summary>
/// Whether an external-tool check is running, and its cancellation — one run at a time, ended
/// <b>before</b> the run says it has finished.
/// </summary>
/// <remarks>
/// <para>Both checking services cleared their running flag in a <c>finally</c> after the run had
/// raised <c>OnCheckingComplete</c>. So a page re-rendering on that event still saw the service
/// running, and a check started from it was silently refused - its progress dialog then waited for
/// a run that never began (B335). Ended here first, the completion handler sees a service that will
/// take the next run.</para>
///
/// <para>The <c>finally</c> still ends the run, for a path that never completed, but only if it is
/// still <i>its</i> run: clearing the field unconditionally would end the next run if one had
/// already begun. Shared because the two services are siblings.</para>
/// </remarks>
internal sealed class CheckRunGate
{
    private readonly object _lock = new();
    private CancellationTokenSource? _run;

    public bool IsRunning
    {
        get { lock (_lock) return _run != null; }
    }

    /// <summary>A new run linked to <paramref name="cancellationToken"/>, or null while one is going.</summary>
    public CancellationTokenSource? TryBegin(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_run != null)
                return null;
            _run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return _run;
        }
    }

    /// <summary>Ends <paramref name="run"/> if it is still the current one.</summary>
    public void End(CancellationTokenSource run)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_run, run))
                return;
            _run = null;
        }

        run.Dispose();
    }

    /// <summary>Ends whatever run is current - called by the run itself, just before it completes.</summary>
    public void EndCurrent()
    {
        CancellationTokenSource? run;
        lock (_lock)
            run = _run;
        if (run != null)
            End(run);
    }

    /// <summary>Cancels the current run, if there is one.</summary>
    public void Cancel()
    {
        lock (_lock)
        {
            try
            {
                _run?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Ended between the read and the cancel; there is nothing left to stop.
            }
        }
    }
}
