using MLQT.Services.DataTypes;
using MLQT.Services.Helpers;
using MLQT.Services.Interfaces;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using static MLQT.Services.LoggingService;

namespace MLQT.Services;

/// <summary>
/// What <see cref="DymolaCheckingService"/> and <see cref="OpenModelicaCheckingService"/> do the same
/// way, written once: the run - one at a time, on the thread pool, reporting progress, stopping at
/// the first class the tool could not answer for - and the session it runs against.
/// </summary>
/// <remarks>
/// <para><b>Why a base class (B398).</b> The two services were the same ~500 lines twice, and the
/// sibling shape is how a question answered for one tool came to be left unanswered for the other,
/// more than once (B170, B229, B332-B336). <c>ModelCheckingServiceContract</c> asserted the shared
/// promises against both, but a contract can only catch a copy that drifted; here there is no copy.
/// What is left in each service is what differs: how a session is had, how a library is opened, and
/// what the tool does when a command runs out of time or is cancelled.</para>
///
/// <para>The hooks are <c>private protected</c> because the types they speak in
/// (<see cref="LibraryLoad"/>) are internal to this assembly - and because nothing outside it has any
/// business adding a third tool without the contract being run against it.</para>
/// </remarks>
/// <typeparam name="TSession">The tool's session interface, as its factory hands it out.</typeparam>
public abstract class ModelCheckingServiceBase<TSession> : IModelCheckingService
    where TSession : class
{
    private readonly CheckRunGate _runs = new();
    private ModelCheckProgress _currentProgress = new();

    // Throttling for UI updates
    private DateTime _lastProgressUpdate = DateTime.MinValue;
    private readonly TimeSpan _progressUpdateInterval = TimeSpan.FromMilliseconds(500);

    public event Action<ModelCheckProgress>? OnProgressChanged;
    public event Action<ModelCheckResult>? OnModelChecked;
    public event Action<ModelCheckProgress>? OnCheckingComplete;

    public bool IsRunning => _runs.IsRunning;
    public ModelCheckProgress CurrentProgress => _currentProgress;
    public abstract string ToolName { get; }

    /// <summary>The session the last command went to, or null when there is none to ask.</summary>
    private protected TSession? Session { get; set; }

    /// <summary>What log entries are filed under: the concrete service's name.</summary>
    private protected string LogSource => GetType().Name;

    private protected ModelCheckingServiceBase()
    {
    }

    // ── what each tool supplies ─────────────────────────────────────────────────

    /// <summary>The factory's session, started if it has to be. Throws when the tool cannot be had.</summary>
    private protected abstract Task<TSession> GetSessionAsync(CancellationToken token);

    /// <summary>Discards the factory's session, so the next request starts a new one.</summary>
    private protected abstract Task ResetSessionAsync();

    /// <summary>Opens <paramref name="filePath"/> in the tool, taking the session first.</summary>
    private protected abstract Task<LibraryLoad> LoadLibraryAsync(string filePath, CancellationToken token);

    /// <summary>
    /// Checks one class in a session that is already open, and is <b>the only place a check
    /// happens</b> - <see cref="IModelCheckingService.CheckModelAsync"/> opens the library and then
    /// calls this.
    /// </summary>
    /// <returns>Null when the check was cancelled - the user's decision, which is not a result.</returns>
    private protected abstract Task<ModelCheckResult?> CheckSingleModelAsync(ModelNode modelNode,
        CancellationToken token = default);

    public abstract Task<ModelCheckResult> CheckModelAsync(ModelNode modelNode, DirectedGraph graph,
        CancellationToken cancellationToken = default);

    public abstract Task<(bool Success, string? ErrorMessage)> EnsureLibraryLoadedAsync(string filePath);

    // ── the run ─────────────────────────────────────────────────────────────────

    public Task StartCheckingAsync(ModelNode modelNode, DirectedGraph graph, CancellationToken cancellationToken = default)
    {
        if (_runs.TryBegin(cancellationToken) is not { } run)
        {
            return Task.CompletedTask;
        }

        var token = run.Token;

        // Run on a background thread to keep UI responsive. Not given the token: a run cancelled
        // before it started would then never run at all, and never end.
        _ = Task.Run(async () =>
        {
            try
            {
                await RunCheckingAsync(modelNode, graph, token);
            }
            finally
            {
                _runs.End(run);
            }
        });

        // Return immediately so UI remains responsive
        return Task.CompletedTask;
    }

    private async Task RunCheckingAsync(ModelNode modelNode, DirectedGraph graph, CancellationToken token)
    {
        try
        {
            ReportStatus($"Starting {ToolName}…");
            if (!await ConnectAsync(modelNode, token))
                return;

            // The library's own package.mo, not the class's file. OpenModelica will not load a
            // class out of the middle of a package: handed Integrator.mo it sees a class called
            // Integrator with nothing to resolve its `within` against, and refuses. Dymola accepts
            // the file and finds the enclosing package itself - and the two tools answering "which
            // file do I open?" differently is how one of them came to be broken for a release
            // while the other worked (B170).
            var rootFile = LibraryRootFile.For(graph, modelNode);
            if (rootFile != null)
            {
                ReportStatus($"Opening {Path.GetFileName(rootFile)} in {ToolName}…");
                var load = await LoadLibraryAsync(rootFile, token);
                if (!load.Success && token.IsCancellationRequested)
                {
                    CompleteCancelled();
                    return;
                }

                if (!load.Success)
                {
                    EndWithoutChecking(load.FailureFor(modelNode.Id, ToolName));
                    return;
                }
            }

            // Determine models to check
            List<ModelNode> modelsToCheck;
            if (modelNode.ClassType == "package")
            {
                modelsToCheck = graph.ModelNodes
                    .Where(m => ModelicaName.IsStrictlyInside(m.Id, modelNode.Id) &&
                                m.ClassType != "package")
                    .ToList();
            }
            else
            {
                modelsToCheck = new List<ModelNode> { modelNode };
            }

            _currentProgress = new ModelCheckProgress
            {
                TotalModels = modelsToCheck.Count,
                ModelsChecked = 0,
                IsComplete = false,
                WasCancelled = false
            };

            // Always fire initial progress
            _lastProgressUpdate = DateTime.UtcNow;
            OnProgressChanged?.Invoke(_currentProgress);

            foreach (var model in modelsToCheck)
            {
                if (token.IsCancellationRequested)
                {
                    CompleteCancelled();
                    return;
                }

                _currentProgress.CurrentModel = model.Id;

                // Throttle progress updates to avoid overwhelming the UI
                FireThrottledProgressUpdate();

                // The token reaches the check itself now, so Cancel ends the one in flight rather
                // than waiting for it to finish and stopping before the next (B262).
                var result = await CheckSingleModelAsync(model, token);
                if (result is null)
                {
                    CompleteCancelled();
                    return;
                }

                // Every result, not only the failures. Reporting just the failures kept the UI quiet
                // — and left a clean run with nothing at all to show, so the dialog that reports
                // what the tool said correctly concluded it had checked nothing (B170). The
                // subscriber batches its own re-renders, so the saving was not one worth having.
                OnModelChecked?.Invoke(result);

                _currentProgress.ModelsChecked++;

                // Stopped at the first class to run out of time: the tool is still busy with it, or
                // has been restarted, so every class after it would wait out the same limit (B263).
                // Likewise at a tool that has gone: every class after it would be asked of nothing,
                // and each came back as an empty "Check Failed" (B334).
                if (result.TimedOut || result.ToolUnavailable)
                {
                    _currentProgress.IsComplete = true;
                    OnProgressChanged?.Invoke(_currentProgress);
                    Complete();
                    return;
                }

                // Throttle progress updates
                FireThrottledProgressUpdate();
            }

            // Always fire final progress update
            _currentProgress.IsComplete = true;
            OnProgressChanged?.Invoke(_currentProgress);
            Complete();
        }
        catch (Exception ex)
        {
            // Handle unexpected errors
            Error(LogSource, "Unexpected error during model checking", ex);
            _currentProgress.IsComplete = true;
            _currentProgress.WasCancelled = false;
            Complete();
        }
    }

    /// <summary>
    /// Takes the session for a run; false when the tool could not be started or reached, or the user
    /// stopped it starting - in which case the run has already been ended, saying which.
    /// </summary>
    /// <remarks>
    /// Asked outside <see cref="LoadLibraryAsync"/>'s handling, so a failure here used to land in the
    /// run's outer catch, which raised only the completion event: the dialog said "<i>Tool</i> checked
    /// nothing." in a success-coloured alert, and "Dymola path not specified" or "did not start within
    /// the expected time" was in the log file only (B332).
    /// </remarks>
    private async Task<bool> ConnectAsync(ModelNode modelNode, CancellationToken token)
    {
        try
        {
            Session = await GetSessionAsync(token);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Stopped while the tool was starting - the user's decision, not a tool that failed (B335).
            CompleteCancelled();
            return false;
        }
        catch (Exception ex)
        {
            Error(LogSource, $"Could not start or connect to {ToolName}", ex);
            EndWithoutChecking(UnavailableTool.CouldNotStart(ToolName, modelNode.Id, ex.Message));
            return false;
        }
    }

    /// <summary>A run that ends before any class is checked, with the one result that says why.</summary>
    private void EndWithoutChecking(ModelCheckResult reason)
    {
        OnModelChecked?.Invoke(reason);
        _currentProgress = new ModelCheckProgress { IsComplete = true };
        Complete();
    }

    /// <summary>
    /// Ends the run and then says so: a subscriber that starts another check from the completion
    /// handler finds the service free, where it used to be refused without a word (B335).
    /// </summary>
    private void Complete()
    {
        _runs.EndCurrent();
        OnCheckingComplete?.Invoke(_currentProgress);
    }

    /// <summary>
    /// Says what is happening before there is anything to count.
    /// </summary>
    /// <remarks>
    /// Starting the tool and opening the library is most of the wait on a large library, and
    /// both counts are zero throughout it - so a progress dialog shown during it had nothing in
    /// it, which reads as a stuck application rather than as a slow one (B259). Sent directly
    /// rather than through the throttle: there are two of these in a whole run, and the first
    /// is the one the user is waiting for.
    /// </remarks>
    private void ReportStatus(string status)
    {
        _currentProgress = new ModelCheckProgress { Status = status };
        _lastProgressUpdate = DateTime.UtcNow;
        OnProgressChanged?.Invoke(_currentProgress);
    }

    /// <summary>The run ends because the user cancelled it, before or during a check.</summary>
    private void CompleteCancelled()
    {
        _currentProgress.WasCancelled = true;
        _currentProgress.IsComplete = true;
        Complete();
    }

    private void FireThrottledProgressUpdate()
    {
        var now = DateTime.UtcNow;
        if (now - _lastProgressUpdate >= _progressUpdateInterval)
        {
            _lastProgressUpdate = now;
            OnProgressChanged?.Invoke(_currentProgress);
        }
    }

    public void StopChecking()
    {
        _runs.Cancel();
    }

    public async Task ResetAsync()
    {
        StopChecking();
        Session = null;
        await ResetSessionAsync();
    }
}
