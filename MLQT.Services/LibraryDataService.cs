using MLQT.Services.Interfaces;
using MLQT.Services.DataTypes;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Icons;
using MudBlazor;
using static MLQT.Services.LoggingService;
using MLQT.Services.Helpers;

namespace MLQT.Services;

/// <summary>
/// Singleton service that manages loaded Modelica libraries and provides
/// tree data on-demand for efficient lazy loading in the UI.
/// </summary>
public class LibraryDataService : ILibraryDataService
{
    private readonly List<LoadedLibrary> _libraries = new();
    private readonly object _lock = new();
    private readonly object _graphLock = new();

    /// <summary>
    /// Combined graph for cross-library operations.
    /// </summary>
    private readonly DirectedGraph _combinedGraph = new();

    /// <inheritdoc/>
    public IReadOnlyList<LoadedLibrary> Libraries
    {
        get
        {
            lock (_lock)
            {
                return _libraries.ToList().AsReadOnly();
            }
        }
    }

    /// <inheritdoc/>
    public DirectedGraph CombinedGraph => _combinedGraph;

    /// <inheritdoc/>
    public event Action? OnLibrariesChanged;

    /// <inheritdoc/>
    public event Action? OnTreeDataChanged;

    /// <inheritdoc/>
    public async Task<LoadedLibrary> AddLibraryFromFileAsync(string filePath, string? content = null)
    {
        LogProcessStart("LibraryDataService", $"Loading library from file: {filePath}");
        var library = new LoadedLibrary
        {
            SourcePath = filePath,
            SourceType = LibrarySourceType.File
        };

        try
        {
            await Task.Run(() =>
            {
                // Load directly into the combined graph
                List<string> modelIds;
                if (content != null)
                {
                    modelIds = GraphBuilder.LoadModelicaFile(_combinedGraph, filePath, content);
                }
                else
                {
                    modelIds = GraphBuilder.LoadModelicaFile(_combinedGraph, filePath, File.ReadAllText(filePath));
                }
                BuildLibraryIndex(library, _combinedGraph, modelIds);
            });

            lock (_lock)
            {
                _libraries.Add(library);
            }

            OnLibrariesChanged?.Invoke();
            OnTreeDataChanged?.Invoke();

            Info("LibraryDataService", $"Successfully loaded library '{library.Name}' with {library.ModelIds.Count} models");
            LogProcessEnd("LibraryDataService", $"Loading library from file: {filePath}");
            return library;
        }
        catch (Exception ex)
        {
            LogProcessFailed("LibraryDataService", $"Loading library from file: {filePath}", ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<LoadedLibrary> AddLibraryFromDirectoryAsync(string directoryPath)
    {
        LogProcessStart("LibraryDataService", $"Loading library from directory: {directoryPath}");
        var library = new LoadedLibrary
        {
            SourcePath = directoryPath,
            SourceType = LibrarySourceType.Directory
        };

        try
        {
            await Task.Run(() =>
            {
                // Get only the Modelica files that are part of the package structure
                // (files in directories that contain a package.mo file)
                var validFiles = GetPackageModelicaFiles(directoryPath);
                Debug("LibraryDataService", $"Found {validFiles.Count} Modelica files in package structure");

                // Load the valid files into the combined graph
                var modelIDs = GraphBuilder.LoadModelicaFiles(_combinedGraph, validFiles.ToArray());

                // Also process package.order files for proper ordering
                ProcessPackageOrderFiles(validFiles);

                BuildLibraryIndex(library, _combinedGraph, modelIDs);
            });

            lock (_lock)
            {
                _libraries.Add(library);
            }

            OnLibrariesChanged?.Invoke();
            OnTreeDataChanged?.Invoke();

            Info("LibraryDataService", $"Successfully loaded library '{library.Name}' with {library.ModelIds.Count} models from directory");
            LogProcessEnd("LibraryDataService", $"Loading library from directory: {directoryPath}");
            return library;
        }
        catch (Exception ex)
        {
            LogProcessFailed("LibraryDataService", $"Loading library from directory: {directoryPath}", ex);
            throw;
        }
    }

    /// <summary>
    /// Gets all Modelica files that are part of the package structure.
    /// Only includes files from directories that contain a package.mo file.
    /// This excludes example files in Resources or other non-package directories.
    /// </summary>
    private List<string> GetPackageModelicaFiles(string rootDirectory)
    {
        var validFiles = new List<string>();

        // Check if the root directory itself is a package (has package.mo)
        var rootPackageMo = Path.Combine(rootDirectory, "package.mo");
        if (File.Exists(rootPackageMo))
        {
            // This is a proper package directory - collect all .mo files from valid package directories
            CollectPackageFiles(rootDirectory, validFiles);
        }
        else
        {
            // Root doesn't have package.mo - just load any .mo files in the root directory
            // (this handles single-file libraries or loose model files)
            if (File.Exists(rootDirectory) && rootDirectory.EndsWith(".mo"))
            {
                validFiles.AddRange(rootDirectory);
            }
            else if (Directory.Exists(rootDirectory))
            {
                var rootMoFiles = Directory.GetFiles(rootDirectory, "*.mo", SearchOption.TopDirectoryOnly);
                validFiles.AddRange(rootMoFiles);
            }
        }

        return validFiles;
    }

    /// <summary>
    /// Recursively collects all .mo files from a package directory and its sub-packages.
    /// A directory is considered a sub-package if it contains a package.mo file.
    /// </summary>
    private void CollectPackageFiles(string packageDirectory, List<string> validFiles)
    {
        // Add all .mo files in this package directory
        var moFiles = Directory.GetFiles(packageDirectory, "*.mo", SearchOption.TopDirectoryOnly);
        validFiles.AddRange(moFiles);
        var hiddenDir = Path.Combine(packageDirectory, ".");

        // Recursively process subdirectories that are also packages (contain package.mo)
        foreach (var subDir in Directory.GetDirectories(packageDirectory))
        {
            // Skip hidden directories (like .git, .svn)
            if (subDir.StartsWith(hiddenDir))
                continue;

            // Only recurse into subdirectories that have a package.mo file
            var subPackageMo = Path.Combine(subDir, "package.mo");
            if (File.Exists(subPackageMo))
            {
                CollectPackageFiles(subDir, validFiles);
            }
            // Directories without package.mo are skipped (e.g., Resources, Examples that are not packages)
        }
    }

    /// <summary>
    /// Processes package.order files for the loaded Modelica files.
    /// This is similar to what GraphBuilder.LoadModelicaDirectory does,
    /// but we need to do it here since we're using LoadModelicaFiles instead.
    /// </summary>
    private void ProcessPackageOrderFiles(List<string> loadedFiles)
    {
        // Find all package.mo files that were loaded
        var packageMoFiles = loadedFiles.Where(f =>
            Path.GetFileName(f).Equals("package.mo", StringComparison.OrdinalIgnoreCase));

        foreach (var packageMoFile in packageMoFiles)
        {
            var directory = Path.GetDirectoryName(packageMoFile);
            if (directory == null) continue;

            var packageOrderPath = Path.Combine(directory, "package.order");
            if (!File.Exists(packageOrderPath)) continue;

            // Read the package.order file
            var packageOrderContent = File.ReadAllLines(packageOrderPath);

            // Find the top-level package model from the package.mo file
            var fileId = GraphBuilder.GenerateFileId(packageMoFile);
            var modelsInFile = _combinedGraph.GetModelsInFile(fileId);

            // Find the top-level package
            var topLevelPackage = modelsInFile
                .Where(m =>
                {
                    if (m.ClassType != "package")
                        return false;

                    var parentName = m.ParentModelName;

                    if (string.IsNullOrEmpty(parentName))
                        return true;

                    var parentNode = _combinedGraph.GetNode<ModelNode>(parentName);
                    if (parentNode != null)
                    {
                        var parentFileId = parentNode.ContainingFileId;
                        return parentFileId != fileId;
                    }

                    return false;
                })
                .FirstOrDefault();

            if (topLevelPackage != null)
            {
                topLevelPackage.PackageOrder = packageOrderContent;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<LoadedLibrary> AddLibraryFromZipAsync(Dictionary<string, string> files)
    {
        LogProcessStart("LibraryDataService", $"Loading library from zip with {files.Count} files");
        var library = new LoadedLibrary
        {
            SourceType = LibrarySourceType.Zip
        };

        try
        {
            await Task.Run(async () =>
            {
                // Load directly into the combined graph
                // Note: Using lock here since Parallel.ForEach may cause race conditions
                List<string> modelIds = new();
                foreach (var kvp in files)
                {
                    var filePath = kvp.Key;
                    var content = kvp.Value;
                    modelIds.AddRange(GraphBuilder.LoadModelicaFile(_combinedGraph, filePath, content));
                }

                await GraphBuilder.AnalyzeDependenciesAsync(_combinedGraph);
                BuildLibraryIndex(library, _combinedGraph, modelIds);
            });

            // Set name from first top-level model if available
            if (library.TopLevelModelIds.Count > 0)
            {
                var firstModel = _combinedGraph.GetNode<ModelNode>(library.TopLevelModelIds.First());
                if (firstModel != null)
                {
                    library.Name = firstModel.Definition.Name;
                    library.SourcePath = firstModel.Definition.Name;
                }
            }

            lock (_lock)
            {
                _libraries.Add(library);
            }

            OnLibrariesChanged?.Invoke();
            OnTreeDataChanged?.Invoke();

            Info("LibraryDataService", $"Successfully loaded library '{library.Name}' with {library.ModelIds.Count} models from zip");
            LogProcessEnd("LibraryDataService", $"Loading library from zip with {files.Count} files");
            return library;
        }
        catch (Exception ex)
        {
            LogProcessFailed("LibraryDataService", $"Loading library from zip", ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public void RemoveLibrary(string libraryId)
    {
        lock (_lock)
        {
            var library = _libraries.FirstOrDefault(l => l.Id == libraryId);
            if (library != null)
            {
                // Remove all models belonging to this library from the combined graph
                foreach (var modelId in library.ModelIds)
                {
                    _combinedGraph.RemoveNode(modelId);
                }

                _libraries.Remove(library);
            }
        }

        OnLibrariesChanged?.Invoke();
        OnTreeDataChanged?.Invoke();
    }

    /// <inheritdoc/>
    public void ClearAllLibraries()
    {
        lock (_lock)
        {
            _libraries.Clear();
            _combinedGraph.Clear();
        }

        OnLibrariesChanged?.Invoke();
        OnTreeDataChanged?.Invoke();
    }

    /// <inheritdoc/>
    public List<string> RemoveModelsFromFile(string filePath)
    {
        var removedModelIds = new List<string>();

        // Safety check: never process files in hidden directories (.git, .svn, etc.)
        if (FileMonitoringServiceHelpers.IsInHiddenDirectory(filePath))
        {
            Warn("LibraryDataService", $"Skipping file in hidden directory: {filePath}");
            return removedModelIds;
        }

        var fileId = GraphBuilder.GenerateFileId(filePath);

        lock (_lock)
        {
            // Get all models in this file
            var fileNode = _combinedGraph.GetNode<FileNode>(fileId);
            if (fileNode == null)
            {
                Debug("LibraryDataService", $"No file node found for: {filePath}");
                return removedModelIds;
            }

            var modelIdsInFile = fileNode.ContainedModelIds.ToList();
            removedModelIds.AddRange(modelIdsInFile);

            // Remove from library indexes
            foreach (var library in _libraries)
            {
                foreach (var modelId in modelIdsInFile)
                {
                    library.ModelIds.Remove(modelId);
                    library.TopLevelModelIds.Remove(modelId);

                    // Remove this model from ChildrenByParent lists where it appears as a child
                    foreach (var children in library.ChildrenByParent.Values)
                    {
                        children.Remove(modelId);
                    }

                    // NOTE: Do NOT remove the model as a parent key (ChildrenByParent.Remove(modelId))
                    // because child models may exist in separate files and still need their
                    // parent-child relationship preserved. The children list will be rebuilt
                    // when the file is reloaded.
                }
            }

            // Remove models from graph
            foreach (var modelId in modelIdsInFile)
            {
                _combinedGraph.RemoveNode(modelId);
            }

            // Remove the file node
            _combinedGraph.RemoveNode(fileId);

            Debug("LibraryDataService", $"Removed {modelIdsInFile.Count} models from file: {filePath}");
        }

        return removedModelIds;
    }

    /// <inheritdoc/>
    public async Task<List<string>> ReloadFileAsync(string filePath)
    {
        var affectedModelIds = new List<string>();

        // Safety check: never process files in hidden directories (.git, .svn, etc.)
        if (FileMonitoringServiceHelpers.IsInHiddenDirectory(filePath))
        {
            Warn("LibraryDataService", $"Skipping file in hidden directory: {filePath}");
            return affectedModelIds;
        }

        // First, find which library contains this file
        LoadedLibrary? library = null;
        var fileId = GraphBuilder.GenerateFileId(filePath);

        lock (_lock)
        {
            var fileNode = _combinedGraph.GetNode<FileNode>(fileId);
            if (fileNode != null)
            {
                var modelsInFile = _combinedGraph.GetModelsInFile(fileId);
                foreach (var model in modelsInFile)
                {
                    foreach (var lib in _libraries)
                    {
                        if (lib.ModelIds.Contains(model.Id))
                        {
                            library = lib;
                            break;
                        }
                    }
                    if (library != null) break;
                }
            }
        }

        // If file doesn't exist in graph yet, try to find library by path
        if (library == null)
        {
            lock (_lock)
            {
                foreach (var lib in _libraries)
                {
                    if (!string.IsNullOrEmpty(lib.SourcePath) &&
                        filePath.StartsWith(lib.SourcePath, StringComparison.OrdinalIgnoreCase))
                    {
                        library = lib;
                        break;
                    }
                }
            }
        }

        // Remove old models from this file
        var removedIds = RemoveModelsFromFile(filePath);
        affectedModelIds.AddRange(removedIds);

        // Re-parse and add the file if it exists
        if (File.Exists(filePath))
        {
            await Task.Run(() =>
            {
                var newModelIds = GraphBuilder.LoadModelicaFile(_combinedGraph, filePath, File.ReadAllText(filePath));
                affectedModelIds.AddRange(newModelIds);

                // Update library index with new models
                if (library != null)
                {
                    lock (_lock)
                    {
                        library.ModelIds.UnionWith(newModelIds);

                        // Rebuild parent-child relationships for new models
                        foreach (var modelId in newModelIds)
                        {
                            var model = _combinedGraph.GetNode<ModelNode>(modelId);
                            if (model == null) continue;

                            var parentName = model.ParentModelName;

                            if (string.IsNullOrEmpty(parentName))
                            {
                                if (!library.TopLevelModelIds.Contains(model.Id))
                                    library.TopLevelModelIds.Add(model.Id);
                            }
                            else
                            {
                                if (!library.ChildrenByParent.ContainsKey(parentName))
                                    library.ChildrenByParent[parentName] = new List<string>();
                                if (!library.ChildrenByParent[parentName].Contains(model.Id))
                                    library.ChildrenByParent[parentName].Add(model.Id);
                            }
                        }
                    }
                }

                Debug("LibraryDataService", $"Reloaded file with {newModelIds.Count} models: {filePath}");
            });
        }

        OnTreeDataChanged?.Invoke();

        return affectedModelIds;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyCollection<TreeItemData<ModelNode>>> GetTopLevelTreeItemsAsync()
    {
        var items = new List<TreeItemData<ModelNode>>();

        lock (_lock)
        {
            foreach (var library in _libraries)
            {
                foreach (var modelId in library.TopLevelModelIds)
                {
                    var model = _combinedGraph.GetNode<ModelNode>(modelId);
                    if (model != null)
                    {
                        var treeItem = CreateTreeItemFromModel(model, library);
                        items.Add(treeItem);
                    }
                }
            }
        }

        return Task.FromResult<IReadOnlyCollection<TreeItemData<ModelNode>>>(items);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyCollection<TreeItemData<ModelNode>>> GetChildTreeItemsAsync(ModelNode? parentNode)
    {
        if (parentNode == null)
        {
            return GetTopLevelTreeItemsAsync();
        }

        var items = new List<TreeItemData<ModelNode>>();

        lock (_lock)
        {
            // Find the library for this parent
            foreach (var library in _libraries)
            {
                if (library.ModelIds.Contains(parentNode.Id))
                {
                    var parentModel = _combinedGraph.GetNode<ModelNode>(parentNode.Id);
                    if (parentModel == null)
                        break;

                    // Get children from the ChildrenByParent dictionary
                    if (library.ChildrenByParent.TryGetValue(parentModel.Id, out var childIds))
                    {
                        var childModels = childIds
                            .Where(id => library.ModelIds.Contains(id))
                            .Select(id => _combinedGraph.GetNode<ModelNode>(id))
                            .Where(m => m != null)
                            .Cast<ModelNode>()
                            .ToList();

                        // Sort by package.order if available
                        childModels = SortByPackageOrder(childModels, parentModel);

                        foreach (var child in childModels)
                        {
                            var treeItem = CreateTreeItemFromModel(child, library);
                            items.Add(treeItem);
                        }
                    }

                    break; // Found the parent, no need to check other libraries
                }
            }
        }

        return Task.FromResult<IReadOnlyCollection<TreeItemData<ModelNode>>>(items);
    }

    /// <inheritdoc/>
    public ModelNode? GetModelById(string modelId)
    {
        // Models are stored in the CombinedGraph, just look them up directly
        return _combinedGraph.GetNode<ModelNode>(modelId);
    }

    /// <inheritdoc/>
    public IEnumerable<ModelNode> GetAllModels()
    {
        lock (_lock)
        {
            var allModelIds = _libraries.SelectMany(l => l.ModelIds).ToHashSet();
            return _combinedGraph.ModelNodes.Where(m => allModelIds.Contains(m.Id)).ToList();
        }
    }

    /// <summary>
    /// Builds the index for a library (model IDs, top-level models, and children).
    /// Identifies which models from the graph belong to this library based on when they were added.
    /// </summary>
    private void BuildLibraryIndex(LoadedLibrary library, DirectedGraph graph, List<string> modelIds)
    {
        // // Get all models currently in the graph that aren't already in another library
        // var existingModelIds = new HashSet<string>();
        // lock (_lock)
        // {
        //     foreach (var existingLib in _libraries)
        //     {
        //         existingModelIds.UnionWith(existingLib.ModelIds);
        //     }
        // }

        // // Find models that are new (belong to this library)
        // var newModels = graph.ModelNodes
        //     .Where(m => !existingModelIds.Contains(m.Id))
        //     .ToList();

        // Store model IDs (not the full ModelNode objects - those are in CombinedGraph)
        library.ModelIds = modelIds.ToHashSet();

        // Build parent-child relationships and find top-level models
        library.ChildrenByParent = new Dictionary<string, List<string>>();
        library.TopLevelModelIds = new List<string>();

        // Iterate library.ModelIds (HashSet) to avoid duplicates — GraphBuilder can return
        // the same model ID multiple times when both an original and a prefixed class
        // (e.g., redeclare function extends X) produce the same fully qualified name.
        foreach (var modelId in library.ModelIds)
        {
            var model = graph.GetNode<ModelNode>(modelId);
            if (model == null) continue;
            var parentName = model.ParentModelName;

            if (string.IsNullOrEmpty(parentName))
            {
                library.TopLevelModelIds.Add(model.Id);
            }
            else
            {
                if (!library.ChildrenByParent.ContainsKey(parentName))
                {
                    library.ChildrenByParent[parentName] = new List<string>();
                }
                library.ChildrenByParent[parentName].Add(model.Id);
            }
        }

        // Set library name from first top-level model
        if (library.TopLevelModelIds.Any() && string.IsNullOrEmpty(library.Name))
        {
            var firstModel = graph.GetNode<ModelNode>(library.TopLevelModelIds.First());
            if (firstModel != null)
            {
                library.Name = firstModel.Definition.Name;
            }
        }
    }

    /// <summary>
    /// Creates a TreeItemData from a ModelNode.
    /// </summary>
    private TreeItemData<ModelNode> CreateTreeItemFromModel(ModelNode model, LoadedLibrary library)
    {
        var classType = model.ClassType;

        // Try to extract Modelica Icon annotation and render as SVG (with inheritance support)
        try
        {
            // Derive the initial package context from the model's fully-qualified ID.
            // The stored ModelicaCode is the extracted class body (no 'within' clause), so the
            // renderer cannot infer the package from the code itself. The package context is needed
            // to resolve unqualified extends names (e.g. "Interfaces.DiscreteSISO") via walk-up.
            var dotIdx = model.Id.LastIndexOf('.');
            var initialPackageContext = dotIdx > 0 ? model.Id[..dotIdx] : null;

            model.IconSvg = model.Definition.ParsedCode != null
                ? IconSvgRenderer.ExtractAndRenderIconWithInheritance(
                    model.Definition.ParsedCode,
                    baseClassName => ResolveBaseClass(baseClassName, model),
                    size: 20,
                    fileNameResolver: fileName => ResolveImageFileName(fileName, library),
                    initialPackageContext: initialPackageContext)
                : IconSvgRenderer.ExtractAndRenderIconWithInheritance(
                    model.Definition.ModelicaCode,
                    baseClassName => ResolveBaseClass(baseClassName, model),
                    size: 20,
                    fileNameResolver: fileName => ResolveImageFileName(fileName, library),
                    initialPackageContext: initialPackageContext);
        }
        catch (Exception ex)
        {
            // Icon extraction failed, will use default icon
            Debug("LibraryDataService", $"Icon extraction failed for model {model.Id}: {ex.Message}");
        }

        model.LibraryId = library.Id;

        var icon = classType switch
        {
            "function" => Icons.Material.Filled.Functions,
            "block" => Icons.Material.Filled.ViewModule,
            "connector" => Icons.Material.Filled.Power,
            "record" => Icons.Material.Filled.DataObject,
            "package" => Icons.Material.Filled.FolderOpen,
            _ => Icons.Material.Filled.ModelTraining
        };

        // Check if this model has children using the ChildrenByParent dictionary
        var hasChildren = library.ChildrenByParent.ContainsKey(model.Id) &&
                          library.ChildrenByParent[model.Id].Count > 0;

        return new TreeItemData<ModelNode>
        {
            Value = model,
            Icon = icon,
            Expandable = hasChildren,
            Expanded = false,
            Children = null // Children will be loaded on-demand via ServerData
        };
    }

    /// <summary>
    /// Resolves a base class name to its Modelica code for icon inheritance.
    /// </summary>
    private string? ResolveBaseClass(string baseClassName, ModelNode currentModel)
    {
        // Try exact match first
        var baseModel = _combinedGraph.GetNode<ModelNode>(baseClassName);
        if (baseModel != null)
            return baseModel.Definition.ModelicaCode;

        // Try resolving relative to the current model's package
        var currentPackage = currentModel.Id.Contains('.')
            ? currentModel.Id.Substring(0, currentModel.Id.LastIndexOf('.'))
            : "";

        if (!string.IsNullOrEmpty(currentPackage))
        {
            var qualifiedName = $"{currentPackage}.{baseClassName}";
            baseModel = _combinedGraph.GetNode<ModelNode>(qualifiedName);
            if (baseModel != null)
                return baseModel.Definition.ModelicaCode;
        }

        return null;
    }

    /// <summary>
    /// Resolves a Bitmap fileName reference to a base64 data URI for embedding in SVG.
    /// Handles modelica:// URIs by mapping the library name to its root path.
    /// </summary>
    private string? ResolveImageFileName(string fileName, LoadedLibrary library)
    {
        string? absolutePath = null;

        if (fileName.StartsWith("modelica://", StringComparison.OrdinalIgnoreCase))
        {
            // Format: modelica://LibraryName/path/to/resource
            var path = fileName.Substring("modelica://".Length);
            var slashIndex = path.IndexOf('/');
            if (slashIndex < 0) return null;

            var libraryName = path.Substring(0, slashIndex);
            var resourceRelativePath = path.Substring(slashIndex + 1).Replace('/', Path.DirectorySeparatorChar);

            // Find the library whose top-level name matches
            LoadedLibrary? matchingLibrary;
            lock (_lock)
            {
                matchingLibrary = _libraries.FirstOrDefault(lib =>
                    string.Equals(lib.Name, libraryName, StringComparison.OrdinalIgnoreCase));
            }

            if (matchingLibrary == null) return null;

            // File-type libraries use a .mo file path; all other types (Directory, Git, SVN)
        // have SourcePath pointing directly to the library root directory.
        var rootDir = matchingLibrary.SourceType == LibrarySourceType.File
                ? Path.GetDirectoryName(matchingLibrary.SourcePath)
                : matchingLibrary.SourcePath;

            if (rootDir == null) return null;

            absolutePath = Path.Combine(rootDir, resourceRelativePath);
        }
        else if (Path.IsPathRooted(fileName))
        {
            absolutePath = fileName;
        }

        if (absolutePath == null || !File.Exists(absolutePath))
            return null;

        try
        {
            var bytes = File.ReadAllBytes(absolutePath);
            var mimeType = GetMimeTypeFromExtension(Path.GetExtension(absolutePath));
            return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch (Exception ex)
        {
            Debug("LibraryDataService", $"Failed to load image file '{absolutePath}': {ex.Message}");
            return null;
        }
    }

    private static string GetMimeTypeFromExtension(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "image/png"
        };

    /// <summary>
    /// Sorts child models by package.order if available, falling back to NestedChildrenOrder
    /// (the order from the source file) when no package.order exists.
    /// </summary>
    private List<ModelNode> SortByPackageOrder(List<ModelNode> childModels, ModelNode parentModel)
    {
        // Try to get package.order first (from package.order file)
        string[]? order = parentModel.PackageOrder;

        // Fall back to NestedChildrenOrder (order from source file) if no package.order
        order ??= parentModel.NestedChildrenOrder;

        if (order == null)
            return childModels;

        var sortedChildModels = new List<ModelNode>();
        var childModelsDictionary = new Dictionary<string, ModelNode>();
        foreach (var m in childModels)
        {
            childModelsDictionary.TryAdd(m.Name, m);
        }

        foreach (var modelName in order)
        {
            if (childModelsDictionary.TryGetValue(modelName, out var model))
            {
                sortedChildModels.Add(model);
                childModelsDictionary.Remove(modelName);
            }
        }

        // Add any remaining child models that weren't in the order list
        foreach (var model in childModelsDictionary.Values)
        {
            sortedChildModels.Add(model);
        }

        return sortedChildModels;
    }
}
