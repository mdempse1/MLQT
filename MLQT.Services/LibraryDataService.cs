using MLQT.Services.Interfaces;
using MLQT.Services.DataTypes;
using ModelicaGraph;
using ModelicaParser.Helpers;
using ModelicaGraph.DataTypes;
using ModelicaParser.ExternalDocs;
using ModelicaParser.Icons;
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
    // Depth rather than a flag: a project switch suppresses across the whole switch while each
    // repository's load suppresses within it, and only the outermost announcement is the one worth
    // making.
    private int _treeNotificationDepth;

    /// <inheritdoc/>
    public IDisposable SuppressTreeDataChanged()
    {
        Interlocked.Increment(ref _treeNotificationDepth);
        return new TreeNotificationScope(this);
    }

    private sealed class TreeNotificationScope(LibraryDataService owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;   // disposing twice must not lift someone else's suppression

            if (Interlocked.Decrement(ref owner._treeNotificationDepth) == 0)
                owner.OnTreeDataChanged?.Invoke();
        }
    }

    private void RaiseTreeDataChanged()
    {
        if (Volatile.Read(ref _treeNotificationDepth) == 0)
            OnTreeDataChanged?.Invoke();
    }

    /// <inheritdoc/>
    public void NotifyTreeDataChanged() => RaiseTreeDataChanged();

    // Guards EnsureDependenciesAnalyzedAsync so concurrent callers share one run instead of racing.
    private readonly object _dependencyAnalysisGate = new();
    private Task? _dependencyAnalysisTask;

    /// <inheritdoc/>
    public List<LibraryInfo> GetLibraryInfos()
    {
        return Libraries.Select(lib =>
        {
            // A file-backed library resolves modelica:// URIs relative to its containing directory.
            var rootPath = lib.SourceType == LibrarySourceType.File
                ? Path.GetDirectoryName(lib.SourcePath) ?? lib.SourcePath
                : lib.SourcePath;
            return new LibraryInfo(lib.Name, rootPath);
        }).ToList();
    }

    /// <inheritdoc/>
    public Task EnsureDependenciesAnalyzedAsync(Action<string>? progressLog = null)
    {
        if (_combinedGraph.DependenciesAnalyzed)
            return Task.CompletedTask;

        lock (_dependencyAnalysisGate)
        {
            // Re-check inside the gate: a run may have finished while we waited for it.
            if (_combinedGraph.DependenciesAnalyzed)
                return Task.CompletedTask;

            // Join an in-flight run rather than starting a second, competing one.
            if (_dependencyAnalysisTask is { IsCompleted: false })
                return _dependencyAnalysisTask;

            var libraryInfos = GetLibraryInfos();
            _dependencyAnalysisTask = Task.Run(() =>
                GraphBuilder.AnalyzeDependenciesAsync(_combinedGraph, libraryInfos, progressLog));
            return _dependencyAnalysisTask;
        }
    }

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
                    modelIds = GraphBuilder.LoadModelicaFile(_combinedGraph, filePath, ModelicaFileEncoding.ReadAllTextOnly(filePath));
                }
                BuildLibraryIndex(library, _combinedGraph, modelIds);
            });

            // The new models have no dependency edges yet, so anything that needs them must
            // re-analyse before it can trust the graph.
            _combinedGraph.InvalidateDependencyAnalysis();

            lock (_lock)
            {
                _libraries.Add(library);
            }

            OnLibrariesChanged?.Invoke();
            RaiseTreeDataChanged();

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

            // The new models have no dependency edges yet, so anything that needs them must
            // re-analyse before it can trust the graph.
            _combinedGraph.InvalidateDependencyAnalysis();

            lock (_lock)
            {
                _libraries.Add(library);
            }

            OnLibrariesChanged?.Invoke();
            RaiseTreeDataChanged();

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

    /// <inheritdoc/>
    public Task<LoadedLibrary> AddLibraryFromPathAsync(string path)
    {
        if (Directory.Exists(path))
        {
            return EncryptedLibraryDetector.IsEncryptedLibraryRoot(path)
                ? AddEncryptedLibraryFromDirectoryAsync(path)
                : AddLibraryFromDirectoryAsync(path);
        }

        if (File.Exists(path) && path.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
        {
            // A package.mo is the root of a directory package: loading only that file would miss
            // every standalone child beside it, which is never what the caller meant.
            return string.Equals(Path.GetFileName(path), "package.mo", StringComparison.OrdinalIgnoreCase)
                ? AddLibraryFromDirectoryAsync(Path.GetDirectoryName(path)!)
                : AddLibraryFromFileAsync(path);
        }

        throw new ArgumentException(
            $"'{path}' is not a Modelica library: expected a directory, a package.mo, or a .mo file.",
            nameof(path));
    }

    /// <inheritdoc/>
    public async Task<LoadedLibrary> AddEncryptedLibraryFromDirectoryAsync(string directoryPath)
    {
        LogProcessStart("LibraryDataService", $"Loading encrypted library: {directoryPath}");
        var library = new LoadedLibrary
        {
            SourcePath = directoryPath,
            SourceType = LibrarySourceType.EncryptedDirectory
        };

        try
        {
            var detected = EncryptedLibraryDetector.Detect(directoryPath)
                ?? throw new InvalidOperationException(
                    $"'{directoryPath}' is not an encrypted Modelica library (no {EncryptedLibraryDetector.EncryptedPackageFileName}).");

            library.Name = detected.Name;

            if (!detected.HasDocumentation)
            {
                // Nothing shipped that describes the library. Loading zero classes is the honest
                // outcome: the namespace stays opaque, so references into it remain unresolved
                // and are treated as external rather than as pointing at classes we "know" are
                // absent. Claiming an empty library would turn every such reference into a
                // fabricated broken-reference finding.
                Warn("LibraryDataService",
                    $"Encrypted library '{detected.Name}' ships no documentation; its classes cannot be recovered");
                library.DocumentedClassCount = 0;

                lock (_lock)
                {
                    _libraries.Add(library);
                }

                OnLibrariesChanged?.Invoke();
                RaiseTreeDataChanged();
                LogProcessEnd("LibraryDataService", $"Loading encrypted library: {directoryPath}");
                return library;
            }

            var supersededCount = 0;
            await Task.Run(() =>
            {
                var document = DymolaHelpReader.Read(detected.HelpDirectory!);
                Debug("LibraryDataService",
                    $"Read {document.Classes.Count} documented classes from {document.FilesRead} help files " +
                    $"({document.FilesSkipped} skipped) for '{detected.Name}'");

                List<string> modelIds;
                int supersededBySource;
                lock (_graphLock)
                {
                    modelIds = ExternalStubBuilder.AddDocumentedClasses(
                        _combinedGraph, document.Classes, detected.EncryptedPackagePath,
                        out supersededBySource, detected.Version);
                }

                library.DocumentedClassCount = document.Classes.Count;
                supersededCount = supersededBySource;
                BuildLibraryIndex(library, _combinedGraph, modelIds);
            });

            // The new models have no dependency edges yet, so anything that needs them must
            // re-analyse before it can trust the graph.
            _combinedGraph.InvalidateDependencyAnalysis();

            lock (_lock)
            {
                _libraries.Add(library);
            }

            OnLibrariesChanged?.Invoke();
            RaiseTreeDataChanged();

            Info("LibraryDataService",
                $"Loaded encrypted library '{library.Name}' {detected.Version} with {library.ModelIds.Count} " +
                "classes recovered from documentation (reference only)" +
                (supersededCount > 0
                    ? $"; {supersededCount} left to the source already loaded for them"
                    : ""));
            LogProcessEnd("LibraryDataService", $"Loading encrypted library: {directoryPath}");
            return library;
        }
        catch (Exception ex)
        {
            LogProcessFailed("LibraryDataService", $"Loading encrypted library: {directoryPath}", ex);
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
            // Case-insensitively, like the other twenty-one places that ask this — LibraryDiscovery
            // accepts "Foo.MO" as a library, and this was the one test that then rejected it, so the
            // library loaded with no classes at all rather than failing.
            if (File.Exists(rootDirectory) && rootDirectory.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
            {
                validFiles.Add(rootDirectory);
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
            var packageOrderContent = ModelicaFileEncoding.ReadAllLinesOnly(packageOrderPath);

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
            RaiseTreeDataChanged();

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
        RaiseTreeDataChanged();
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
        RaiseTreeDataChanged();
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
                var newModelIds = GraphBuilder.LoadModelicaFile(_combinedGraph, filePath, ModelicaFileEncoding.ReadAllTextOnly(filePath));
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

        RaiseTreeDataChanged();

        // Each class once. A file whose classes are unchanged contributes every id twice — once as
        // removed, once as re-added — and a caller cannot tell that from a class genuinely listed for
        // two reasons. It reaches a parallel re-check as two entries, where both can pass the
        // already-checked guard before either sets it, and the class's findings are then reported
        // twice.
        return affectedModelIds.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc/>
    public async Task<HashSet<string>> UpdateChangedFilesAsync(
        IReadOnlyCollection<string> changedFilePaths, string rootPath)
    {
        // Convert absolute paths to relative paths for GraphBuilder
        var normalizedRoot = Path.GetFullPath(rootPath).Replace('\\', '/').TrimEnd('/') + "/";
        var relativeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in changedFilePaths)
        {
            var fullPath = Path.GetFullPath(filePath).Replace('\\', '/');
            if (fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                relativeFiles.Add(fullPath[normalizedRoot.Length..]);
            else
                relativeFiles.Add(Path.GetRelativePath(rootPath, filePath).Replace('\\', '/'));
        }

        // Use GraphBuilder for the core graph operations (remove stale, re-parse changed)
        List<string> affectedModelIds;
        lock (_lock)
        {
            // Capture models that will be removed (for library index cleanup)
            var moFiles = relativeFiles.Where(f => f.EndsWith(".mo", StringComparison.OrdinalIgnoreCase));
            foreach (var relPath in moFiles)
            {
                var fullPath = Path.Combine(rootPath, relPath.Replace('/', Path.DirectorySeparatorChar));
                var fileId = GraphBuilder.GenerateFileId(fullPath);
                var modelsInFile = _combinedGraph.GetModelsInFile(fileId).ToList();
                foreach (var model in modelsInFile)
                {
                    foreach (var lib in _libraries)
                    {
                        lib.ModelIds.Remove(model.Id);
                        lib.TopLevelModelIds.Remove(model.Id);
                        foreach (var children in lib.ChildrenByParent.Values)
                            children.Remove(model.Id);
                    }
                }
            }
        }

        affectedModelIds = await Task.Run(() =>
            GraphBuilder.UpdateGraphForChangedFiles(_combinedGraph, rootPath, relativeFiles));

        // Rebuild library indexes for newly added models
        lock (_lock)
        {
            foreach (var modelId in affectedModelIds)
            {
                var model = _combinedGraph.GetNode<ModelNode>(modelId);
                if (model == null) continue;

                // Find which library this model belongs to by checking file paths
                LoadedLibrary? library = null;
                if (model.ContainingFileId != null)
                {
                    var fileNode = _combinedGraph.GetNode<FileNode>(model.ContainingFileId);
                    if (fileNode != null)
                    {
                        foreach (var lib in _libraries)
                        {
                            if (!string.IsNullOrEmpty(lib.SourcePath) &&
                                fileNode.FilePath.StartsWith(lib.SourcePath, StringComparison.OrdinalIgnoreCase))
                            {
                                library = lib;
                                break;
                            }
                        }
                    }
                }

                if (library == null) continue;

                library.ModelIds.Add(modelId);
                if (string.IsNullOrEmpty(model.ParentModelName))
                {
                    if (!library.TopLevelModelIds.Contains(modelId))
                        library.TopLevelModelIds.Add(modelId);
                }
                else
                {
                    if (!library.ChildrenByParent.ContainsKey(model.ParentModelName))
                        library.ChildrenByParent[model.ParentModelName] = new List<string>();
                    if (!library.ChildrenByParent[model.ParentModelName].Contains(modelId))
                        library.ChildrenByParent[model.ParentModelName].Add(modelId);
                }
            }
        }

        RaiseTreeDataChanged();
        return affectedModelIds.ToHashSet();
    }

    /// <inheritdoc/>
    /// <summary>
    /// Whether this library is the one whose copy of <paramref name="node"/> is actually in the
    /// graph.
    ///
    /// <para>The same library can be loaded twice — a tool's library folder ships the encrypted
    /// build of a library the user also has checked out as source, and both are perfectly ordinary
    /// repositories in the same project. Only one copy of each class survives in the graph (source
    /// wins), but both <see cref="LoadedLibrary"/> entries still list the same ids, so "which
    /// library does this class belong to" has two answers and only one of them is right.</para>
    /// </summary>
    private static bool Owns(LoadedLibrary library, ModelNode node) =>
        node.IsExternalStub == (library.SourceType == LibrarySourceType.EncryptedDirectory);

    /// <inheritdoc/>
    public Task<IReadOnlyList<ModelNode>> GetTopLevelModelsAsync()
    {
        // Keyed by model id, because two libraries claiming the same top-level class are claiming
        // the *same node object*. Adding it once per claiming library put the library in the tree
        // twice, and — since preparing it for display stamps the library id onto the shared node —
        // both copies ended up attributed to whichever library was processed last. That is why a
        // library appeared twice under one repository and not at all under the other, and why which
        // repository it landed in varied from one library to the next.
        var byModelId = new Dictionary<string, (ModelNode Node, LoadedLibrary Library)>(StringComparer.Ordinal);

        lock (_lock)
        {
            foreach (var library in _libraries)
            {
                foreach (var modelId in library.TopLevelModelIds)
                {
                    var model = _combinedGraph.GetNode<ModelNode>(modelId);
                    if (model == null)
                        continue;

                    // First claim wins unless a later library is the one that actually owns the node.
                    if (byModelId.TryGetValue(modelId, out var claimed) && !Owns(library, model))
                        continue;

                    byModelId[modelId] = (model, library);
                }
            }

            var items = new List<ModelNode>(byModelId.Count);
            foreach (var (node, library) in byModelId.Values)
            {
                PrepareModelForDisplay(node, library);
                items.Add(node);
            }

            return Task.FromResult<IReadOnlyList<ModelNode>>(items);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ModelNode>> GetChildModelsAsync(ModelNode? parentNode)
    {
        if (parentNode == null)
        {
            return GetTopLevelModelsAsync();
        }

        var items = new List<ModelNode>();

        lock (_lock)
        {
            var parentModel = _combinedGraph.GetNode<ModelNode>(parentNode.Id);
            if (parentModel == null)
                return Task.FromResult<IReadOnlyList<ModelNode>>(items);

            // The library for this parent is the one that owns it, not merely the first that claims
            // it. Both copies of a doubly-loaded library list the same parent, but their child lists
            // differ: the encrypted one knows only what its documentation named, the source one knows
            // what is actually there. Taking the first claimant meant expanding a package could show
            // the wrong set of children entirely.
            var candidates = _libraries.Where(l => l.ModelIds.Contains(parentModel.Id)).ToList();
            var library = candidates.FirstOrDefault(l => Owns(l, parentModel)) ?? candidates.FirstOrDefault();
            if (library == null)
                return Task.FromResult<IReadOnlyList<ModelNode>>(items);

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
                    PrepareModelForDisplay(child, library);
                    items.Add(child);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<ModelNode>>(items);
    }

    /// <inheritdoc/>
    public bool ModelHasChildren(string modelId)
    {
        lock (_lock)
        {
            foreach (var library in _libraries)
            {
                if (library.ChildrenByParent.TryGetValue(modelId, out var childIds) && childIds.Count > 0)
                    return true;
            }
        }

        return false;
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
    /// Populates a model's display metadata for tree presentation: renders its Modelica icon to SVG
    /// (with base-class inheritance) into <see cref="ModelNode.IconSvg"/> and stamps its LibraryId.
    /// UI-agnostic — returns nothing and uses no Blazor/MudBlazor types; the UI layer wraps the model
    /// into its own tree-item representation.
    /// </summary>
    private void PrepareModelForDisplay(ModelNode model, LoadedLibrary library)
    {
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
