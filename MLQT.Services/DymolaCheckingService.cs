using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using DymolaInterface.Interfaces;
using static MLQT.Services.LoggingService;
using DymolaInterface;

namespace MLQT.Services;

/// <summary>
/// Service for checking Modelica models using Dymola.
/// Handles background processing, progress reporting, and cancellation.
/// </summary>
public class DymolaCheckingService : IModelCheckingService
{
    private readonly IDymolaInterfaceFactory _dymolaFactory;
    private DymolaInterface.DymolaInterface? _dymola;
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

    public DymolaCheckingService(IDymolaInterfaceFactory dymolaFactory)
    {
        _dymolaFactory = dymolaFactory;
    }

    public void UpdateSettings(DymolaSettings settings)
    {
        _dymolaFactory.UpdateSettings(settings);
    }

    public async Task<(bool Success, string? ErrorMessage)> EnsureLibraryLoadedAsync(string filePath)
    {
        try
        {
            _dymola = await _dymolaFactory.GetOrCreateAsync();

            var isOpen = await _dymola.OpenModelAsync(filePath, false, false);
            if (!isOpen)
            {
                if (File.Exists(filePath))
                {
                    // File exists so maybe Dymola already had a version open
                    await _dymola.ClearAsync();
                    isOpen = await _dymola.OpenModelAsync(filePath, false, false);
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

    public async Task<ModelCheckResult> CheckModelAsync(ModelNode modelNode, DirectedGraph graph)
    {
        var result = new ModelCheckResult
        {
            ModelId = modelNode.Id
        };

        try
        {
            if (_dymola == null)
            {
                _dymola = await _dymolaFactory.GetOrCreateAsync();
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

            var checkResult = await _dymola.CheckModelAsync(modelNode.Id, false, false);
            if (checkResult)
            {
                result.Success = true;
            }
            else
            {
                var error = await _dymola.GetLastErrorAsync();
                result.Success = false;

                if (error.Contains("Error: the model is too complex for the current license"))
                {
                    result.Summary = "Model too complex for demo license";
                }
                else
                {
                    result.Summary = "Dymola Check Failed";
                }
                result.ErrorMessage = error;
            }
        }
        catch (Exception ex)
        {
            Error("DymolaCheckingService", $"Error checking model: {modelNode.Id}", ex);
            result.Success = false;
            result.Summary = "Dymola Check Failed";
            try
            {
                result.ErrorMessage = _dymola != null
                    ? await _dymola.GetLastErrorAsync()
                    : ex.Message;
            }
            catch (Exception innerEx)
            {
                Warn("DymolaCheckingService", $"Failed to get Dymola error message: {innerEx.Message}");
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
            _dymola = await _dymolaFactory.GetOrCreateAsync();

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
            Error("DymolaCheckingService", "Unexpected error during model checking", ex);
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
            var checkResult = await _dymola!.CheckModelAsync(modelNode.Id, false, false);
            if (checkResult)
            {
                result.Success = true;
            }
            else
            {
                var error = await _dymola.GetLastErrorAsync();
                result.Success = false;

                if (error.Contains("Error: the model is too complex for the current license"))
                {
                    result.Summary = "Model too complex for demo license";
                }
                else
                {
                    result.Summary = "Dymola Check Failed";
                }
                result.ErrorMessage = error;
            }
        }
        catch (Exception ex)
        {
            Error("DymolaCheckingService", $"Error checking single model: {modelNode.Id}", ex);
            result.Success = false;
            result.Summary = "Dymola Check Failed";
            try
            {
                result.ErrorMessage = _dymola != null
                    ? await _dymola.GetLastErrorAsync()
                    : ex.Message;
            }
            catch (Exception innerEx)
            {
                Warn("DymolaCheckingService", $"Failed to get Dymola error message: {innerEx.Message}");
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
        _dymola = null;
        await _dymolaFactory.ResetAsync();
    }
}
