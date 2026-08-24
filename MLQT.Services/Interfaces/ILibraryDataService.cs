using MLQT.Services.DataTypes;
using ModelicaGraph;
using ModelicaGraph.DataTypes;

namespace MLQT.Services.Interfaces;

/// <summary>
/// Service for managing loaded Modelica libraries and providing tree data on-demand.
/// Supports lazy loading of tree nodes for efficient handling of large libraries.
/// </summary>
public interface ILibraryDataService
{
    /// <summary>
    /// Gets all currently loaded libraries.
    /// </summary>
    IReadOnlyList<LoadedLibrary> Libraries { get; }

    /// <summary>
    /// Gets the name and root path of each loaded library, as needed by
    /// <c>GraphBuilder</c> to resolve <c>modelica://</c> URIs.
    /// </summary>
    List<LibraryInfo> GetLibraryInfos();

    /// <summary>
    /// Runs full dependency analysis over the combined graph exactly once, coalescing concurrent
    /// callers onto a single run and returning immediately when the graph is already analysed.
    ///
    /// Every consumer that needs <c>UsedModelIds</c>/<c>UsedByModelIds</c> — the startup pipeline and
    /// the style-checking graph analyses — must go through here. Calling
    /// <c>GraphBuilder.AnalyzeDependenciesAsync</c> directly lets two runs overlap, which is how the
    /// graph analyses used to observe a half-built graph and report a different number of findings
    /// from one launch to the next.
    /// </summary>
    /// <param name="progressLog">Optional progress callback, used only if this call starts the run.</param>
    Task EnsureDependenciesAnalyzedAsync(Action<string>? progressLog = null);

    /// <summary>
    /// Adds a library from a file path.
    /// </summary>
    /// <param name="filePath">Path to the .mo file.</param>
    /// <param name="content">Optional content if already loaded.</param>
    /// <returns>The loaded library.</returns>
    Task<LoadedLibrary> AddLibraryFromFileAsync(string filePath, string? content = null);

    /// <summary>
    /// Adds a library from a directory (recursive loading of .mo files).
    /// </summary>
    /// <param name="directoryPath">Path to the directory containing .mo files.</param>
    /// <returns>The loaded library.</returns>
    Task<LoadedLibrary> AddLibraryFromDirectoryAsync(string directoryPath);

    /// <summary>
    /// Adds an encrypted library — one shipping an unreadable <c>package.moe</c> — by
    /// reconstructing its classes from the vendor's generated documentation.
    ///
    /// <para>The result is read-only and deliberately incomplete: names, descriptions, base
    /// classes and whether a class has an icon, and nothing else. It exists so that references
    /// into the library resolve, inherited icons are seen, and extends chains can be walked. The
    /// library is never reported on and never written to.</para>
    /// </summary>
    /// <param name="directoryPath">The encrypted library's root directory.</param>
    /// <returns>The loaded library. Its <see cref="LoadedLibrary.ModelIds"/> is empty when the
    /// library ships no usable documentation, which leaves its namespace opaque rather than
    /// wrongly asserting that it contains no classes.</returns>
    Task<LoadedLibrary> AddEncryptedLibraryFromDirectoryAsync(string directoryPath);

    /// <summary>
    /// Adds a library from a zip file.
    /// </summary>
    /// <param name="files">Dictionary of file paths to content from the zip.</param>
    /// <returns>The loaded library.</returns>
    Task<LoadedLibrary> AddLibraryFromZipAsync(Dictionary<string, string> files);

    /// <summary>
    /// Removes a loaded library.
    /// </summary>
    /// <param name="libraryId">The library ID to remove.</param>
    void RemoveLibrary(string libraryId);

    /// <summary>
    /// Clears all loaded libraries.
    /// </summary>
    void ClearAllLibraries();

    /// <summary>
    /// Reloads a single file, updating affected models in the graph.
    /// Removes old models from the file, re-parses, and updates library indexes.
    /// </summary>
    /// <param name="filePath">Path to the file to reload.</param>
    /// <returns>List of affected model IDs (both removed and newly added).</returns>
    Task<List<string>> ReloadFileAsync(string filePath);

    /// <summary>
    /// Removes all models associated with a specific file from the graph.
    /// </summary>
    /// <param name="filePath">Path to the file whose models should be removed.</param>
    /// <returns>List of removed model IDs.</returns>
    List<string> RemoveModelsFromFile(string filePath);

    /// <summary>
    /// Batch update: applies a set of changed file paths to the graph using
    /// GraphBuilder.UpdateGraphForChangedFiles, then rebuilds library indexes.
    /// More efficient than calling ReloadFileAsync/RemoveModelsFromFile individually.
    /// </summary>
    /// <param name="changedFilePaths">Absolute paths of files that were added, modified, or deleted.</param>
    /// <param name="rootPath">Root directory of the library (for resolving relative paths).</param>
    /// <returns>Set of affected model IDs.</returns>
    Task<HashSet<string>> UpdateChangedFilesAsync(IReadOnlyCollection<string> changedFilePaths, string rootPath);

    /// <summary>
    /// Gets the top-level models for the tree view, each with display metadata (icon SVG, LibraryId)
    /// populated. The UI layer wraps these into its own tree-item type.
    /// </summary>
    /// <returns>Top-level models.</returns>
    Task<IReadOnlyList<ModelNode>> GetTopLevelModelsAsync();

    /// <summary>
    /// Gets the child models for a given parent node (sorted by package.order / source order), each
    /// with display metadata populated. Pass null to get the top-level models.
    /// </summary>
    /// <param name="parentNode">The parent node to get children for, or null for top level.</param>
    /// <returns>Child models.</returns>
    Task<IReadOnlyList<ModelNode>> GetChildModelsAsync(ModelNode? parentNode);

    /// <summary>
    /// Returns whether a model has child models (i.e. the tree node is expandable).
    /// </summary>
    /// <param name="modelId">The fully qualified model ID.</param>
    bool ModelHasChildren(string modelId);

    /// <summary>
    /// Gets a model node by its fully qualified ID.
    /// </summary>
    /// <param name="modelId">The fully qualified model ID.</param>
    /// <returns>The model node, or null if not found.</returns>
    ModelNode? GetModelById(string modelId);

    /// <summary>
    /// Gets all model nodes across all loaded libraries.
    /// </summary>
    /// <returns>All model nodes.</returns>
    IEnumerable<ModelNode> GetAllModels();

    /// <summary>
    /// Gets the combined graph containing all models from all libraries.
    /// Useful for cross-library dependency analysis.
    /// </summary>
    DirectedGraph CombinedGraph { get; }

    /// <summary>
    /// Event fired when libraries are added or removed.
    /// </summary>
    event Action? OnLibrariesChanged;

    /// <summary>
    /// Event fired when tree data needs to be refreshed.
    /// </summary>
    event Action? OnTreeDataChanged;

    /// <summary>
    /// When true, OnTreeDataChanged events are suppressed. Used during batch
    /// operations (e.g., refreshing multiple files) to avoid triggering expensive
    /// side effects (VCS status queries) on every individual file reload.
    /// The caller must fire OnTreeDataChanged once after unsetting this flag.
    /// </summary>
    bool SuppressTreeDataChangedEvents { get; set; }
}
