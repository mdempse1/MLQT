using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using DymolaInterface.Interfaces;
using static MLQT.Services.LoggingService;
using DymolaInterface;
using MLQT.Services.Helpers;

namespace MLQT.Services;

/// <summary>
/// Service for checking Modelica models using Dymola.
/// Handles background processing, progress reporting, and cancellation.
/// </summary>
public class DymolaCheckingService : IModelCheckingService
{
    private readonly IDymolaInterfaceFactory _dymolaFactory;
    private IDymolaInterface? _dymola;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isRunning;
    private ModelCheckProgress _currentProgress = new();

    // Throttling for UI updates
    private DateTime _lastProgressUpdate = DateTime.MinValue;
    private readonly TimeSpan _progressUpdateInterval = TimeSpan.FromMilliseconds(500);

    public event Action<ModelCheckProgress>? OnProgressChanged;
    public event Action<ModelCheckResult>? OnModelChecked;
    public event Action<ModelCheckProgress>? OnCheckingComplete;

    public bool IsRunning => _isRunning;
    public ModelCheckProgress CurrentProgress => _currentProgress;
    public string ToolName => "Dymola";

    /// <summary>
    /// The one failure that is not the user's model. Singled out by its text so the result can say
    /// "buy a licence" rather than "your model is broken".
    /// </summary>
    private const string DemoLicenceLimit = "Error: the model is too complex for the current license";

    /// <summary>
    /// What a timed-out command leaves Dymola doing — the part of the message that differs from omc.
    /// </summary>
    private const string StillBusy =
        "Dymola cannot be interrupted, so it is still working on it and will not answer anything else until it has finished.";

    /// <summary>The time limit the factory is applying, kept here to say it in a message.</summary>
    private TimeSpan _commandTimeout = TimeSpan.FromMinutes(5);

    public DymolaCheckingService(IDymolaInterfaceFactory dymolaFactory)
    {
        _dymolaFactory = dymolaFactory;
    }

    public void UpdateSettings(DymolaSettings settings)
    {
        _commandTimeout = settings.CommandTimeout;
        _dymolaFactory.UpdateSettings(settings);
    }

    public Task<(bool Success, string? ErrorMessage)> EnsureLibraryLoadedAsync(string filePath)
        => EnsureLibraryLoadedAsync(filePath, CancellationToken.None);

    private async Task<(bool Success, string? ErrorMessage)> EnsureLibraryLoadedAsync(
        string filePath, CancellationToken token)
    {
        try
        {
            _dymola = await _dymolaFactory.GetOrCreateAsync();

            var isOpen = await _dymola.OpenModelAsync(filePath, false, false, cancellationToken: token);
            if (!isOpen && Interrupted(filePath) is { } interrupted)
                return (false, interrupted);

            if (!isOpen)
            {
                if (File.Exists(filePath))
                {
                    // File exists so maybe Dymola already had a version open
                    await _dymola.ClearAsync();
                    isOpen = await _dymola.OpenModelAsync(filePath, false, false, cancellationToken: token);
                    if (!isOpen && Interrupted(filePath) is { } interruptedAgain)
                        return (false, interruptedAgain);

                    if (!isOpen)
                    {
                        return (false, "Could not get Dymola to open the file for this Modelica model");
                    }
                }
                else
                {
                    return (false, $"File not found: {filePath}");
                }
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            Error("DymolaCheckingService", "Error connecting to Dymola", ex);
            return (false, $"Error connecting to Dymola: {ex.Message}");
        }
    }

    /// <summary>
    /// Why an open that did not succeed was not a refusal, or null when it was one: Dymola never
    /// answered, or the user cancelled. Asked before retrying, because a retry after a timeout waits
    /// out the whole limit again against a Dymola still busy with the first attempt.
    /// </summary>
    private string? Interrupted(string filePath) => _dymola?.LastOutcome switch
    {
        CommandOutcome.Cancelled => "Cancelled",
        CommandOutcome.TimedOut => ToolTimeLimit.LoadTimedOut(ToolName, filePath, _commandTimeout, StillBusy),
        _ => null,
    };

    /// <summary>
    /// Dymola's log for the last command, or null when it cannot be read. Never throws: this is
    /// asked on the success path too, and losing a clean result because the log could not be
    /// fetched would be a worse answer than a result with no log on it.
    /// </summary>
    /// <summary>
    /// Empties Dymola's log so the next read belongs to the command that follows it. Never throws:
    /// failing to clear is not a reason to fail the check.
    /// </summary>
    private async Task SafeClearLogAsync()
    {
        try
        {
            if (_dymola is not null)
                await _dymola.ClearLogAsync();
        }
        catch (Exception ex)
        {
            Debug("DymolaCheckingService", $"Could not clear Dymola's log: {ex.Message}");
        }
    }

    private async Task<string?> SafeLastErrorAsync()
    {
        try
        {
            var log = _dymola is null ? null : await _dymola.GetLastErrorAsync();
            return string.IsNullOrWhiteSpace(log) ? null : log;
        }
        catch (Exception ex)
        {
            Debug("DymolaCheckingService", $"Could not read Dymola's log: {ex.Message}");
            return null;
        }
    }

    public async Task<ModelCheckResult> CheckModelAsync(ModelNode modelNode, DirectedGraph graph)
    {
        try
        {
            _dymola ??= await _dymolaFactory.GetOrCreateAsync();

            // The library's own package.mo, not the class's file. Dymola would find the enclosing
            // package itself from the class's file, but OpenModelica will not — and the two tools
            // answering "which file do I open?" differently is how one of them came to be broken
            // for a release while the other worked (B170).
            var rootFile = LibraryRootFile.For(graph, modelNode);
            if (rootFile != null)
            {
                var (loadSuccess, loadError) = await EnsureLibraryLoadedAsync(rootFile);
                if (!loadSuccess)
                {
                    return new ModelCheckResult
                    {
                        ModelId = modelNode.Id,
                        Success = false,
                        Summary = "Failed to load library",
                        ErrorMessage = loadError
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Error("DymolaCheckingService", $"Error preparing to check model: {modelNode.Id}", ex);
            return await FailedResultAsync(modelNode.Id, ex);
        }

        return await CheckSingleModelAsync(modelNode)
               ?? new ModelCheckResult { ModelId = modelNode.Id, Success = false, Summary = "Cancelled" };
    }

    public Task StartCheckingAsync(ModelNode modelNode, DirectedGraph graph, CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return Task.CompletedTask;
        }

        _isRunning = true;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cancellationTokenSource.Token;

        // Run on a background thread to keep UI responsive
        _ = Task.Run(async () =>
        {
            try
            {
                await RunCheckingAsync(modelNode, graph, token);
            }
            finally
            {
                _isRunning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }, token);

        // Return immediately so UI remains responsive
        return Task.CompletedTask;
    }

    private async Task RunCheckingAsync(ModelNode modelNode, DirectedGraph graph, CancellationToken token)
    {
        try
        {
            ReportStatus($"Starting {ToolName}…");
            _dymola = await _dymolaFactory.GetOrCreateAsync();

            // The library's own package.mo, not the class's file. Dymola would find the enclosing
            // package itself from the class's file, but OpenModelica will not — and the two tools
            // answering "which file do I open?" differently is how one of them came to be broken
            // for a release while the other worked (B170).
            var rootFile = LibraryRootFile.For(graph, modelNode);
            if (rootFile != null)
            {
                ReportStatus($"Opening {Path.GetFileName(rootFile)} in {ToolName}…");
                var (loadSuccess, loadError) = await EnsureLibraryLoadedAsync(rootFile, token);
                if (!loadSuccess && token.IsCancellationRequested)
                {
                    CompleteCancelled();
                    return;
                }

                if (!loadSuccess)
                {
                    var errorResult = new ModelCheckResult
                    {
                        ModelId = modelNode.Id,
                        Success = false,
                        Summary = "Failed to load library",
                        ErrorMessage = loadError
                    };
                    OnModelChecked?.Invoke(errorResult);

                    _currentProgress = new ModelCheckProgress
                    {
                        TotalModels = 0,
                        ModelsChecked = 0,
                        IsComplete = true,
                        WasCancelled = false
                    };
                    OnCheckingComplete?.Invoke(_currentProgress);
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
                if (result.TimedOut)
                {
                    _currentProgress.IsComplete = true;
                    OnProgressChanged?.Invoke(_currentProgress);
                    OnCheckingComplete?.Invoke(_currentProgress);
                    return;
                }

                // Throttle progress updates
                FireThrottledProgressUpdate();
            }

            // Always fire final progress update
            _currentProgress.IsComplete = true;
            OnProgressChanged?.Invoke(_currentProgress);
            OnCheckingComplete?.Invoke(_currentProgress);
        }
        catch (Exception ex)
        {
            // Handle unexpected errors
            Error("DymolaCheckingService", "Unexpected error during model checking", ex);
            _currentProgress.IsComplete = true;
            _currentProgress.WasCancelled = false;
            OnCheckingComplete?.Invoke(_currentProgress);
        }
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
        OnCheckingComplete?.Invoke(_currentProgress);
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

    /// <summary>
    /// Checks one class in a session that is already open, and is <b>the only place a check
    /// happens</b> — <see cref="CheckModelAsync"/> opens the library and then calls this.
    /// </summary>
    /// <remarks>
    /// <para><b>It was not always the only place (B229).</b> The two paths each had their own copy
    /// of this, and the copy the package path used had neither the clear-the-log-first step nor the
    /// read-the-log-on-success step. So the warnings B170 exists to surface were shown when the
    /// user checked one class and lost when they checked the package containing it, and the error
    /// reported against a class could be the one the class before it produced. Neither is visible
    /// from either path on its own, which is why the tests for this are a contract run against both
    /// tools rather than a test per service.</para>
    /// </remarks>
    /// <returns>Null when the check was cancelled — the user's decision, which is not a result.</returns>
    private async Task<ModelCheckResult?> CheckSingleModelAsync(ModelNode modelNode, CancellationToken token = default)
    {
        var result = new ModelCheckResult
        {
            ModelId = modelNode.Id
        };

        try
        {
            // Cleared first so that what comes back afterwards belongs to *this* check. Dymola's log
            // accumulates, so without this a model that checked cleanly could be shown the error
            // from something checked before it — a wrong answer, and a worse one than no log at all.
            await SafeClearLogAsync();

            var checkResult = await _dymola!.CheckModelAsync(modelNode.Id, false, false, cancellationToken: token);

            // False is the model's verdict only if Dymola gave it. A check that ran out of time or was
            // cancelled answered nothing - and asking for the log then would wait out the limit again,
            // against a Dymola still busy with the check (B263).
            if (!checkResult)
            {
                switch (_dymola.LastOutcome)
                {
                    case CommandOutcome.Cancelled:
                        return null;
                    case CommandOutcome.TimedOut:
                        return ToolTimeLimit.CheckTimedOut(ToolName, modelNode.Id, _commandTimeout, StillBusy);
                }
            }

            if (checkResult)
            {
                result.Success = true;

                // `checkModel` returning true means it checked, not that it had nothing to say: a
                // model that is fine and one that is fine apart from six warnings both return true,
                // and getLastError() is where the difference is. Asked on success as well so the
                // result dialog can show what Dymola actually reported (B170).
                result.Log = await SafeLastErrorAsync();
            }
            else
            {
                var error = await _dymola.GetLastErrorAsync();
                result.Success = false;
                result.Log = error;
                result.Summary = error.Contains(DemoLicenceLimit)
                    ? "Model too complex for demo license"
                    : $"{ToolName} Check Failed";
                result.ErrorMessage = error;
            }
        }
        catch (Exception ex)
        {
            Error("DymolaCheckingService", $"Error checking model: {modelNode.Id}", ex);
            return await FailedResultAsync(modelNode.Id, ex);
        }

        return result;
    }

    /// <summary>
    /// The result for a check that threw: Dymola's own account of what went wrong where it can
    /// still be asked for one, and the exception's where it cannot.
    /// </summary>
    private async Task<ModelCheckResult> FailedResultAsync(string modelId, Exception failure)
    {
        var result = new ModelCheckResult
        {
            ModelId = modelId,
            Success = false,
            Summary = $"{ToolName} Check Failed"
        };

        try
        {
            result.ErrorMessage = _dymola != null
                ? await _dymola.GetLastErrorAsync()
                : failure.Message;
        }
        catch (Exception innerEx)
        {
            Warn("DymolaCheckingService", $"Failed to get Dymola error message: {innerEx.Message}");
            result.ErrorMessage = failure.Message;
        }

        return result;
    }

    public void StopChecking()
    {
        _cancellationTokenSource?.Cancel();
    }

    public async Task ResetAsync()
    {
        StopChecking();
        _dymola = null;
        await _dymolaFactory.ResetAsync();
    }
}
