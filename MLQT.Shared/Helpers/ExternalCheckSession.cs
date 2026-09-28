using ModelicaGraph;
using ModelicaGraph.DataTypes;

namespace MLQT.Shared.Helpers;

/// <summary>
/// The Code Review page's one external-tool check at a time: which tool's run is current, what it has
/// said, and only its events passed on (B335).
/// </summary>
/// <remarks>
/// <para><b>What went wrong without it.</b> The page subscribed the same handlers to both tools and
/// kept one result list and one token, so it could not tell whose event it was handling. Clicking a
/// tool again while its run was still starting reset the results and opened a dialog the service
/// ignored - it refuses a second run - which the old run then closed with "stopped after 0 classes".
/// Clicking the other tool let the stopped run's completion close the new tool's dialog and dispose
/// its token, leaving that run with no way to stop it.</para>
///
/// <para>So a check starts only while neither tool is running - the buttons are disabled for as
/// long, from <see cref="IsAnyRunning"/> - and events from any service but the current one are
/// dropped here, before the page sees them.</para>
/// </remarks>
public sealed class ExternalCheckSession : IDisposable
{
    private readonly IReadOnlyList<IModelCheckingService> _services;
    private readonly List<(IModelCheckingService Service, Action<ModelCheckProgress> Progress,
        Action<ModelCheckResult> Checked, Action<ModelCheckProgress> Complete)> _subscriptions = [];
    private readonly List<ModelCheckResult> _results = [];
    private readonly object _lock = new();
    private CancellationTokenSource? _cancellation;

    public ExternalCheckSession(params IModelCheckingService[] services)
    {
        _services = services;
        foreach (var service in services)
        {
            Action<ModelCheckProgress> progress = p => { if (IsCurrent(service)) ProgressChanged?.Invoke(p); };
            Action<ModelCheckResult> modelChecked = r => OnModelChecked(service, r);
            Action<ModelCheckProgress> complete = p => OnComplete(service, p);

            service.OnProgressChanged += progress;
            service.OnModelChecked += modelChecked;
            service.OnCheckingComplete += complete;
            _subscriptions.Add((service, progress, modelChecked, complete));
        }
    }

    /// <summary>The tool whose run this is, or null before the first.</summary>
    public IModelCheckingService? Current { get; private set; }

    /// <summary>The current tool's name, or empty before the first run.</summary>
    public string ToolName => Current?.ToolName ?? "";

    /// <summary>What the current run's tool said about each class, so far.</summary>
    public IReadOnlyList<ModelCheckResult> Results
    {
        get { lock (_lock) return [.. _results]; }
    }

    /// <summary>Whether the current run ended because it was stopped.</summary>
    public bool WasCancelled { get; private set; }

    /// <summary>Whether either tool is running - including a run that was stopped and has not ended.</summary>
    public bool IsAnyRunning => _services.Any(s => s.IsRunning);

    /// <summary>Progress from the current run only.</summary>
    public event Action<ModelCheckProgress>? ProgressChanged;

    /// <summary>A result from the current run only, after it has been added to <see cref="Results"/>.</summary>
    public event Action<ModelCheckResult>? ModelChecked;

    /// <summary>The current run has finished, after <see cref="WasCancelled"/> has been set.</summary>
    public event Action<ModelCheckProgress>? Completed;

    /// <summary>
    /// Starts <paramref name="service"/> on <paramref name="model"/>, unless a check - by either tool
    /// - is still running, in which case nothing changes and the answer is false.
    /// </summary>
    public bool TryStart(IModelCheckingService service, ModelNode model, DirectedGraph graph)
    {
        if (IsAnyRunning)
            return false;

        lock (_lock)
        {
            _results.Clear();
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
        }

        WasCancelled = false;
        Current = service;

        // Runs on the service's own background thread and returns at once.
        _ = service.StartCheckingAsync(model, graph, _cancellation.Token);
        return true;
    }

    /// <summary>Stops the current run. It still completes, as a cancelled run.</summary>
    public void Stop()
    {
        lock (_lock)
            _cancellation?.Cancel();
        Current?.StopChecking();
    }

    private bool IsCurrent(IModelCheckingService service) => ReferenceEquals(service, Current);

    private void OnModelChecked(IModelCheckingService service, ModelCheckResult result)
    {
        if (!IsCurrent(service))
            return;

        lock (_lock)
            _results.Add(result);
        ModelChecked?.Invoke(result);
    }

    private void OnComplete(IModelCheckingService service, ModelCheckProgress progress)
    {
        if (!IsCurrent(service))
            return;

        WasCancelled = progress.WasCancelled;
        Completed?.Invoke(progress);
    }

    public void Dispose()
    {
        foreach (var (service, progress, modelChecked, complete) in _subscriptions)
        {
            service.OnProgressChanged -= progress;
            service.OnModelChecked -= modelChecked;
            service.OnCheckingComplete -= complete;
        }
        _subscriptions.Clear();

        lock (_lock)
        {
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }
}
