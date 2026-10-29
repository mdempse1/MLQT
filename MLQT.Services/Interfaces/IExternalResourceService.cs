using ModelicaGraph;
using MLQT.Services.DataTypes;

namespace MLQT.Services.Interfaces;

/// <summary>
/// Service for analyzing, validating, and monitoring external resource files
/// referenced by Modelica models. Reads resource nodes and edges from the graph
/// created by GraphBuilder, generates warnings, and monitors resource file changes.
/// </summary>
public interface IExternalResourceService
{
    /// <summary>
    /// Analyzes all external resource references from the graph.
    /// Reads resource nodes and edges created by GraphBuilder.AnalyzeDependencies().
    /// Generates warnings for missing files and absolute paths.
    /// </summary>
    Task AnalyzeResourcesAsync(DirectedGraph graph);

    /// <summary>
    /// Analyzes resources for specific models only (incremental update after file refresh).
    /// Reads resource edges for the specified models from the graph.
    /// </summary>
    Task AnalyzeResourcesForModelsAsync(IEnumerable<string> modelIds, DirectedGraph graph);

    /// <summary>
    /// Gets all resource references for a specific model.
    /// </summary>
    List<ExternalResourceReference> GetResourcesForModel(string modelId);

    /// <summary>
    /// Gets all model IDs that reference a specific resource file path.
    /// </summary>
    List<string> GetModelsReferencingResource(string resolvedFilePath);

    /// <summary>
    /// Gets all current validation warnings (missing files, absolute paths).
    /// </summary>
    List<ResourceWarning> GetWarnings();

    /// <summary>
    /// Clears resource data and warnings for specific models (used during incremental refresh).
    /// </summary>
    void ClearDataForModels(IEnumerable<string> modelIds);

    /// <summary>
    /// Starts monitoring all resolved resource file paths for changes.
    /// Creates FileSystemWatchers for directories containing referenced resource files.
    /// </summary>
    void StartMonitoringResources();

    /// <summary>
    /// Stops monitoring all resource files and disposes watchers.
    /// </summary>
    void StopMonitoringResources();

    /// <summary>
    /// Gets all resource references across all models.
    /// </summary>
    List<ExternalResourceReference> GetAllResources();

    /// <summary>
    /// Raised when a referenced resource file is added, changed or deleted on disk, after the change
    /// is queued. The desktop app counts these with the library file changes on its refresh button.
    /// </summary>
    event Action? OnPendingResourceChangesUpdated;

    /// <summary>How many referenced resource files have changed on disk and not yet been taken.</summary>
    int PendingResourceChangeCount { get; }

    /// <summary>
    /// The classes that reference a resource file changed on disk since the last call, which are the
    /// ones whose resource analysis and checks are out of date, and clears the queue.
    /// </summary>
    /// <remarks>
    /// Until B521 the watchers queued each change where nothing read it, so a file that was restored
    /// or deleted changed nothing on screen until the project was reloaded.
    /// </remarks>
    IReadOnlyList<string> TakePendingResourceChanges();
}
