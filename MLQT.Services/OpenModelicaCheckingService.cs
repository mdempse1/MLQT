using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using OpenModelicaInterface.Interfaces;
using static MLQT.Services.LoggingService;
using OpenModelicaInterface;


namespace MLQT.Services;

/// <summary>
/// Service for checking Modelica models using OpenModelica.
/// Handles background processing, progress reporting, and cancellation.
/// </summary>
public class OpenModelicaCheckingService : IModelCheckingService
{
    private readonly IOpenModelicaInterfaceFactory _omcFactory;
    private OpenModelicaInterface.OpenModelicaInterface? _omc;
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

    public async Task<ModelCheckResult> CheckModelAsync(ModelNode modelNode, DirectedGraph graph)
    {
        var result = new ModelCheckResult
        {
            ModelId = modelNode.Id
        };

        try
        {
            if (_omc == null)
            {
                _omc = await _omcFactory.GetOrCreateAsync();
            }

            // Ensure library is loaded
            var fileNode = modelNode.ContainingFileId != null ? graph.GetNode<FileNode>(modelNode.ContainingFileId) : null;
            if (fileNode != null)
            {
                var (loadSuccess, loadError) = await EnsureLibraryLoadedAsync(fileNode.FilePath);
                if (!loadSuccess)
                {
                    result.Success = false;
                    result.Summary = "Failed to load library";
                    result.ErrorMessage = loadError;
                    return result;
                }
            }

            var checkResult = await _omc.CheckModelAsync(modelNode.Id);
            if (checkResult)
            {
                result.Success = true;
            }
            else
            {
                var error = await _omc.GetErrorStringAsync();
                result.Success = false;

                if (error.Contains("Error: the model is too complex for the current license"))
                {
                    result.Summary = "Model too complex for demo license";
                }
                else
                {
                    result.Summary = "OpenModelica Check Failed";
                }
                result.ErrorMessage = error;
            }
        }
        catch (Exception ex)
        {
            Error("OpenModelicaCheckingService", $"Error checking model: {modelNode.Id}", ex);
            result.Success = false;
            result.Summary = "OpenModelica Check Failed";
            try
            {
                result.ErrorMessage = _omc != null
                    ? await _omc.GetErrorStringAsync()
                    : ex.Message;
            }
            catch (Exception innerEx)
            {
                Warn("OpenModelicaCheckingService", $"Failed to get OpenModelica error message: {innerEx.Message}");
                result.ErrorMessage = ex.Message;
            }
        }

        return result;
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
            _omc = await _omcFactory.GetOrCreateAsync();

            // Ensure library is loaded
            var fileNode = modelNode.ContainingFileId != null ? graph.GetNode<FileNode>(modelNode.ContainingFileId) : null;
            if (fileNode != null)
            {
                var (loadSuccess, loadError) = await EnsureLibraryLoadedAsync(fileNode.FilePath);
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
                    .Where(m => m.Id.StartsWith(modelNode.Id + ".") &&
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

                // Only fire OnModelChecked for failures to reduce UI updates
                if (!result.Success)
                {
                    OnModelChecked?.Invoke(result);
                }

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

    private void FireThrottledProgressUpdate()
    {
        var now = DateTime.UtcNow;
        if (now - _lastProgressUpdate >= _progressUpdateInterval)
        {
            _lastProgressUpdate = now;
            OnProgressChanged?.Invoke(_currentProgress);
        }
    }

    private async Task<ModelCheckResult> CheckSingleModelAsync(ModelNode modelNode)
    {
        var result = new ModelCheckResult
        {
            ModelId = modelNode.Id
        };

        try
        {
            var checkResult = await _omc!.CheckModelAsync(modelNode.Id);
            if (checkResult)
            {
                result.Success = true;
            }
            else
            {
                var error = await _omc.GetErrorStringAsync();
                result.Success = false;

                if (error.Contains("Error: the model is too complex for the current license"))
                {
                    result.Summary = "Model too complex for demo license";
                }
                else
                {
                    result.Summary = "OpenModelica Check Failed";
                }
                result.ErrorMessage = error;
            }
        }
        catch (Exception ex)
        {
            Error("OpenModelicaCheckingService", $"Error checking single model: {modelNode.Id}", ex);
            result.Success = false;
            result.Summary = "OpenModelica Check Failed";
            try
            {
                result.ErrorMessage = _omc != null
                    ? await _omc.GetErrorStringAsync()
                    : ex.Message;
            }
            catch (Exception innerEx)
            {
                Warn("OpenModelicaCheckingService", $"Failed to get OpenModelica error message: {innerEx.Message}");
                result.ErrorMessage = ex.Message;
            }
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
