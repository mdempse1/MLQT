using MLQT.Services.DataTypes;
using ModelicaGraph;
using ModelicaGraph.DataTypes;

namespace MLQT.Services.Interfaces;

/// <summary>
/// Service interface for checking Modelica models using an external tool (Dymola, OpenModelica, etc.).
/// Handles background processing, progress reporting, and cancellation.
/// </summary>
public interface IModelCheckingService
{
    /// <summary>
    /// Event fired when checking progress changes.
    /// </summary>
    event Action<ModelCheckProgress>? OnProgressChanged;

    /// <summary>
    /// Event fired when a model check completes (success or failure).
    /// </summary>
    event Action<ModelCheckResult>? OnModelChecked;

    /// <summary>
    /// Event fired when all checking is complete.
    /// </summary>
    event Action<ModelCheckProgress>? OnCheckingComplete;

    /// <summary>
    /// Gets whether checking is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the current progress information.
    /// </summary>
    ModelCheckProgress CurrentProgress { get; }

    /// <summary>
    /// Gets the name of the checking tool (e.g., "Dymola", "OpenModelica").
    /// </summary>
    string ToolName { get; }

    /// <summary>
    /// Checks a single model.
    /// </summary>
    /// <param name="modelNode">The model node to check.</param>
    /// <param name="graph">The graph containing file information.</param>
    /// <returns>The result of the model check.</returns>
    Task<ModelCheckResult> CheckModelAsync(ModelNode modelNode, DirectedGraph graph);

    /// <summary>
    /// Checks a model and all its child models (if it's a package).
    /// Progress is reported via events.
    /// </summary>
    /// <param name="modelNode">The model or package node to check.</param>
    /// <param name="graph">The graph containing all models and files.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task StartCheckingAsync(ModelNode modelNode, DirectedGraph graph, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops any running check operation.
    /// </summary>
    void StopChecking();

    /// <summary>
    /// Ensures the library file is loaded in the external tool.
    /// </summary>
    /// <param name="filePath">Path to the Modelica file.</param>
    /// <returns>True if the file was loaded successfully, false otherwise with error message.</returns>
    Task<(bool Success, string? ErrorMessage)> EnsureLibraryLoadedAsync(string filePath);

    /// <summary>
    /// Resets the service state, clearing any cached connections.
    /// </summary>
    Task ResetAsync();
}
