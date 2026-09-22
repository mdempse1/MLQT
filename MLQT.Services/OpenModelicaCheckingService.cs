using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using OpenModelicaInterface.Interfaces;
using static MLQT.Services.LoggingService;
using OpenModelicaInterface;
using MLQT.Services.Helpers;


namespace MLQT.Services;

/// <summary>
/// Service for checking Modelica models using OpenModelica.
/// Handles background processing, progress reporting, and cancellation.
/// </summary>
public class OpenModelicaCheckingService : IModelCheckingService
{
    private readonly IOpenModelicaInterfaceFactory _omcFactory;
    private IOpenModelicaInterface? _omc;
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
    public string ToolName => "OpenModelica";

    /// <summary>
    /// The one failure that is not the user's model. Singled out by its text so the result can say
    /// "buy a licence" rather than "your model is broken".
    /// </summary>
    private const string DemoLicenceLimit = "Error: the model is too complex for the current license";

    public OpenModelicaCheckingService(IOpenModelicaInterfaceFactory omcFactory)
    {
        _omcFactory = omcFactory;
    }

    public void UpdateSettings(OpenModelicaSettings settings)
    {
        _omcFactory.UpdateSettings(settings);    
    }

    public async Task<(bool Success, string? ErrorMessage)> EnsureLibraryLoadedAsync(string filePath)
    {
        try
        {
            _omc = await _omcFactory.GetOrCreateAsync();

            var isOpen = await _omc.LoadFileAsync(filePath);
            if (!isOpen)
            {
                var error = await _omc.GetErrorStringAsync();
                if (File.Exists(filePath))
                {
                    // File exists so maybe OpenModelica already had a version open
                    await _omc.ClearAsync();
                    isOpen = await _omc.LoadFileAsync(filePath);
                    if (!isOpen)
                    {
                        return (false, "Could not get OpenModelica to open the file for this Modelica model");
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
            Error("OpenModelicaCheckingService", "Error connecting to OpenModelica", ex);
            return (false, $"Error connecting to OpenModelica: {ex.Message}");
        }
    }

    /// <summary>
    /// The tool's accumulated messages, or null when they cannot be read. Never throws: this is
    /// asked on the success path too, and losing a clean result because the log could not be
    /// fetched would be a worse answer than a result with no log on it.
    /// </summary>
    private async Task<string?> SafeErrorStringAsync()
    {
        try
        {
            var log = _omc is null ? null : await _omc.GetErrorStringAsync();
            return string.IsNullOrWhiteSpace(log) ? null : log;
        }
        catch (Exception ex)
        {
            Debug("OpenModelicaCheckingService", $"Could not read the OpenModelica log: {ex.Message}");
            return null;
        }
    }

    public async Task<ModelCheckResult> CheckModelAsync(ModelNode modelNode, DirectedGraph graph)
    {
        try
        {
            _omc ??= await _omcFactory.GetOrCreateAsync();

            // The library's own package.mo, not the class's file. OpenModelica will not load a
            // class out of the middle of a package: handed Integrator.mo it sees a class called
            // Integrator with nothing to resolve its `within` against, and refuses. Dymola accepts
            // the file and finds the enclosing package itself, which is why the same code worked
            // for one tool and not the other (B170).
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
            Error("OpenModelicaCheckingService", $"Error preparing to check model: {modelNode.Id}", ex);
            return await FailedResultAsync(modelNode.Id, ex);
        }

        return await CheckSingleModelAsync(modelNode);
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
            _omc = await _omcFactory.GetOrCreateAsync();

            // The library's own package.mo, not the class's file. OpenModelica will not load a
            // class out of the middle of a package: handed Integrator.mo it sees a class called
            // Integrator with nothing to resolve its `within` against, and refuses. Dymola accepts
            // the file and finds the enclosing package itself, which is why the same code worked
            // for one tool and not the other (B170).
            var rootFile = LibraryRootFile.For(graph, modelNode);
            if (rootFile != null)
            {
                ReportStatus($"Opening {Path.GetFileName(rootFile)} in {ToolName}…");
                var (loadSuccess, loadError) = await EnsureLibraryLoadedAsync(rootFile);
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
                    _currentProgress.WasCancelled = true;
                    _currentProgress.IsComplete = true;
                    OnCheckingComplete?.Invoke(_currentProgress);
                    return;
                }

                _currentProgress.CurrentModel = model.Id;

                // Throttle progress updates to avoid overwhelming the UI
                FireThrottledProgressUpdate();

                var result = await CheckSingleModelAsync(model);

                // Every result, not only the failures — a clean run otherwise had nothing at all
                // to show, so the dialog reporting what the tool said correctly concluded it had
                // checked nothing (B170).
                OnModelChecked?.Invoke(result);

                _currentProgress.ModelsChecked++;

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
            Error("OpenModelicaCheckingService", "Unexpected error during model checking", ex);
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
    /// of this, and the copy the package path used had neither the drain-the-log-first step nor the
    /// read-the-log-on-success step. So the warnings B170 exists to surface were shown when the
    /// user checked one class and lost when they checked the package containing it, and the error
    /// reported against a class could be the one the class before it produced. Neither is visible
    /// from either path on its own, which is why the tests for this are a contract run against both
    /// tools rather than a test per service.</para>
    /// </remarks>
    private async Task<ModelCheckResult> CheckSingleModelAsync(ModelNode modelNode)
    {
        var result = new ModelCheckResult
        {
            ModelId = modelNode.Id
        };

        try
        {
            // Drained first, so what comes back afterwards belongs to *this* check. `getErrorString`
            // returns the accumulated messages and empties the buffer, so reading it here discards
            // anything left by an earlier command — without which a model that checked cleanly could
            // be shown the error from one checked before it. (B116 is open against omc 1.26's
            // behaviour here, which is why this discards rather than relying on it.)
            _ = await SafeErrorStringAsync();

            var checkResult = await _omc!.CheckModelAsync(modelNode.Id);
            if (checkResult)
            {
                result.Success = true;

                // Checked is not the same as had nothing to say: omc reports warnings through the
                // same error string, and a model that passes with six of them returns true. Read on
                // success as well, so the result dialog can show what the tool actually said (B170).
                result.Log = await SafeErrorStringAsync();
            }
            else
            {
                var error = await _omc.GetErrorStringAsync();
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
            Error("OpenModelicaCheckingService", $"Error checking model: {modelNode.Id}", ex);
            return await FailedResultAsync(modelNode.Id, ex);
        }

        return result;
    }

    /// <summary>
    /// The result for a check that threw: the tool's own account of what went wrong where it can
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
            result.ErrorMessage = _omc != null
                ? await _omc.GetErrorStringAsync()
                : failure.Message;
        }
        catch (Exception innerEx)
        {
            Warn("OpenModelicaCheckingService", $"Failed to get OpenModelica error message: {innerEx.Message}");
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
        _omc = null;
        await _omcFactory.ResetAsync();
    }
}
