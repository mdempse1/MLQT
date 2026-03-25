using System.Collections.Concurrent;
using System.Text;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaGraph.DataTypes;
using ModelicaGraph.Interfaces;

namespace ModelicaGraph;

/// <summary>
/// Helper class for building and populating a DirectedGraph from Modelica files.
/// </summary>
public static class GraphBuilder
{
   /// <summary>
    /// Loads a Modelica file and adds it to the graph along with all models it contains.
    /// </summary>
    /// <param name="graph">The graph to populate.</param>
    /// <param name="filePath">Path to the Modelica file.</param>
    /// <returns>The list of models read from the file</returns>
    public static List<string> LoadModelicaFile(DirectedGraph graph, string filePath, string content)
    {
        // Normalize line endings once - all downstream methods skip re-normalization
        var normalizedContent = ModelicaParserHelper.NormalizeLineEndings(content);

        // Create a file node (content is not stored to save memory — re-read from disk if needed)
        var fileId = GenerateFileId(filePath);
        var fileNode = new FileNode(fileId, filePath);

        graph.AddNode(fileNode);

        // Extract all models from the file (capturing any lexer/parser errors)
        List<string> modelIDs = new();
        var (models, fileParserErrors) = ModelicaParserHelper.ExtractModelsWithErrors(normalizedContent);

        // Track child order for packages
        var packageChildOrder = new Dictionary<string, List<string>>();

        // Add each model to the graph
        foreach (var modelInfo in models)
        {
            var modelId = GenerateModelId(modelInfo.ParentModelName, modelInfo.Name);
            modelIDs.Add(modelId);
            var modelNode = new ModelNode(modelId, modelInfo.Name, modelInfo.SourceCode);
            //modelNode.Definition.CodeContext = modelInfo.CodeContext;

            // Store additional information as typed properties
            modelNode.ClassType = modelInfo.ClassType;
            modelNode.StartLine = modelInfo.StartLine;
            modelNode.StopLine = modelInfo.StopLine;
            modelNode.IsNested = modelInfo.IsNested;
            modelNode.CanBeStoredStandalone = modelInfo.CanBeStoredStandalone;
            modelNode.ElementPrefix = modelInfo.ElementPrefix;
            modelNode.Version = modelInfo.Version;
            modelNode.Uses = modelInfo.Uses;
            modelNode.ParentModelName = modelInfo.ParentModelName;

            // ParsedCode is NOT created here — it's parsed lazily on first access
            // by AnalyzeDependenciesAsync, StyleChecking, icon extraction, etc.
            // This avoids parsing each model individually during loading (the file
            // is already parsed once by ExtractModels above), saving significant
            // CPU time and memory for large repositories.

            if (modelInfo.ParentModelName != null)
            {
                // Track the order of children for the parent package
                if (!packageChildOrder.ContainsKey(modelInfo.ParentModelName))
                {
                    packageChildOrder[modelInfo.ParentModelName] = new List<string>();
                }
                packageChildOrder[modelInfo.ParentModelName].Add(modelInfo.Name);
            }

            graph.AddNode(modelNode);

            // Link the file to the model
            graph.AddFileContainsModel(fileId, modelId);
        }

        // Store the child order in package properties
        foreach (var kvp in packageChildOrder)
        {
            var packageId = kvp.Key;
            var childNames = kvp.Value.ToArray(); // Reverse to maintain original order when using stack-based traversal

            // Find the package model node
            var packageNode = graph.GetNode<ModelNode>(packageId);
            if (packageNode != null)
            {
                // Store the nested children order
                packageNode.NestedChildrenOrder = childNames;
            }
        }

        // Distribute file-level parser errors to the appropriate model based on line range.
        // This makes errors available immediately after loading (before EnsureParsed runs).
        if (fileParserErrors.Count > 0)
        {
            foreach (var error in fileParserErrors)
            {
                // Find the model whose line range contains this error
                var owningModel = models
                    .Where(m => error.Line >= m.StartLine && error.Line <= m.StopLine)
                    .OrderBy(m => m.StopLine - m.StartLine) // prefer most specific (innermost) model
                    .FirstOrDefault();

                if (owningModel != null)
                {
                    var modelId = GenerateModelId(owningModel.ParentModelName, owningModel.Name);
                    var modelNode = graph.GetNode<ModelNode>(modelId);
                    modelNode?.Definition.ParserErrors.Add(error);
                }
                else
                {
                    // Error outside any model range — attach to the first model in the file
                    var firstModel = models.FirstOrDefault();
                    if (firstModel != null)
                    {
                        var modelId = GenerateModelId(firstModel.ParentModelName, firstModel.Name);
                        var modelNode = graph.GetNode<ModelNode>(modelId);
                        modelNode?.Definition.ParserErrors.Add(error);
                    }
                }
            }
        }

        // Deduplicate — prefixed classes (e.g., redeclare function extends X) can produce
        // the same model ID as the standalone class they modify
        return modelIDs.Distinct().ToList();
    }

    /// <summary>
    /// Loads multiple Modelica files and adds them to the graph.
    /// </summary>
    /// <param name="graph">The graph to populate.</param>
    /// <param name="filePaths">Paths to the Modelica files.</param>
    /// <returns>List models that were added.</returns>
    public static List<string> LoadModelicaFiles(DirectedGraph graph, params string[] filePaths)
    {
        const int batchSize = 200;
        var modelIDs = new ConcurrentBag<string>();

        // Process files in batches to limit peak memory from parse trees.
        // ExtractModels creates a full ANTLR parse tree per file; with unbounded
        // parallelism across thousands of files, these trees accumulate on the LOH
        // before GC can collect them. Batching gives the GC natural collection points.
        for (int i = 0; i < filePaths.Length; i += batchSize)
        {
            var count = Math.Min(batchSize, filePaths.Length - i);
            var batch = new ArraySegment<string>(filePaths, i, count);

            Parallel.ForEach(batch, filePath =>
            {
                var models = LoadModelicaFile(graph, filePath, File.ReadAllText(filePath, Encoding.Latin1));
                foreach (var model in models)
                {
                    modelIDs.Add(model);
                }
            });

            // Hint GC between batches — parse trees from ExtractModels are now unreachable
            if (i + count < filePaths.Length)
                GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
        }

        return modelIDs.ToList();
    }

    /// <summary>
    /// Loads all Modelica files from a directory and adds them to the graph.
    /// Also reads package.order files and stores them in package model nodes.
    /// </summary>
    /// <param name="graph">The graph to populate.</param>
    /// <param name="directoryPath">Path to the directory containing Modelica files.</param>
    /// <param name="searchPattern">Search pattern for files (default: "*.mo").</param>
    /// <param name="searchOption">Search option (default: TopDirectoryOnly).</param>
    /// <returns>List models that were added.</returns>
    public static List<string> LoadModelicaDirectory(
        DirectedGraph graph,
        string directoryPath,
        string searchPattern = "*.mo",
        SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        var filePaths = Directory.GetFiles(directoryPath, searchPattern, searchOption);
        //var fileNodes = LoadModelicaFiles(graph, filePaths);
        var modelList = LoadModelicaFiles(graph, filePaths);

        // Read package.order files for each directory that contains a package.mo
        //foreach (var filePath in filePaths)
        Parallel.ForEach(filePaths, filePath => 
        {
            if (Path.GetFileName(filePath).Equals("package.mo", StringComparison.OrdinalIgnoreCase))
            {
                var directory = Path.GetDirectoryName(filePath);
                if (directory != null)
                {
                    var packageOrderPath = Path.Combine(directory, "package.order");
                    if (File.Exists(packageOrderPath))
                    {
                        // Read the package.order file
                        var packageOrderContent = File.ReadAllLines(packageOrderPath, Encoding.Latin1);

                        // Find the TOP-LEVEL package model from the package.mo file
                        // A package.order file only applies to the main package in the file, not nested packages
                        var fileId = GenerateFileId(filePath);
                        var modelsInFile = graph.GetModelsInFile(fileId);

                        // Find the top-level package (the one without "." in its name relative to the file)
                        // For example, if the file is Blocks\package.mo, we want Modelica.Blocks, not Modelica.Blocks.Examples
                        var topLevelPackage = modelsInFile
                            .Where(m =>
                            {
                                var classType = m.ClassType;
                                if (classType != "package")
                                    return false;

                                // Check if this is a top-level package (not nested)
                                // Top-level packages don't have a ParentModelName property or it's empty
                                var parentName = m.ParentModelName;

                                // If it has no parent, it's top-level
                                if (string.IsNullOrEmpty(parentName))
                                    return true;

                                // If it has a parent, check if the parent is defined in a different file
                                // (meaning this is the top package in THIS file)
                                var parentNode = graph.GetNode<ModelNode>(parentName);
                                if (parentNode != null)
                                {
                                    var parentFileId = parentNode.ContainingFileId;
                                    return parentFileId != fileId;  // Parent is in a different file
                                }

                                return false;
                            })
                            .FirstOrDefault();

                        // Store the package.order content only in the top-level package
                        if (topLevelPackage != null)
                        {
                            topLevelPackage.PackageOrder = packageOrderContent;
                        }
                    }
                }
            }
        });
        return modelList;
        //return fileNodes;
    }

    /// <summary>
    /// Analyzes model dependencies, extracts external resources, and discovers
    /// loadSelector parameters using a single combined visitor pass per model.
    /// Creates model-to-model dependency edges and resource nodes with edges.
    ///
    /// Optimized for performance:
    /// - Parse trees are cached during LoadModelicaFile, avoiding re-parsing
    /// - Single ModelAnalyzer visitor per model replaces three separate visitors
    /// - Parallelizes visitor traversal across models
    /// - Batches edge additions after parallel phase
    /// - Separate Pass 2 for cross-model loadSelector modification tracking
    /// </summary>
    /// <param name="graph">The graph to analyze.</param>
    /// <param name="libraries">Library information for resolving modelica:// URIs.</param>
    /// <param name="postAnalysisAction">Optional callback invoked for each model after dependency
    /// analysis while the parse tree is still available. This allows callers to piggyback on the
    /// parse (e.g., run style checking) without requiring a separate re-parse pass.</param>
    public static async Task AnalyzeDependenciesAsync(DirectedGraph graph, IEnumerable<LibraryInfo>? libraries = null, Action<string>? progressLog = null, Action<ModelNode>? postAnalysisAction = null)
    {
        const int batchSize = 500;

        var allModels = graph.ModelNodes.ToList();
        progressLog?.Invoke($"Starting dependency analysis for {allModels.Count} models");

        // Phase 1+2: Parse, analyze, and add edges in batches to limit peak memory.
        // ANTLR parse trees are 10-50x larger than source code and large trees land on the
        // Large Object Heap (LOH). Processing all models at once causes GC to lag behind
        // allocation, spiking memory far above the steady-state footprint.
        // Batching limits concurrent garbage to (batchSize × avg parse tree) and gives
        // the GC natural collection points between batches.
        progressLog?.Invoke("Phase 1+2: Parsing, analyzing, and adding edges in batches");
        var modelResources = new Dictionary<string, List<ExternalResourceInfo>>();
        int totalProcessed = 0;

        foreach (var batch in Batch(allModels, batchSize))
        {
            var batchResults = new ConcurrentBag<(string sourceId, HashSet<string> dependencies, List<ExternalResourceInfo> resources)>();

            Parallel.ForEach(batch, model =>
            {
                try
                {
                    var parseTree = model.Definition.EnsureParsed();
                    if (parseTree == null) return;

                    var analyzer = new ModelAnalyzer(model.Id, graph);
                    analyzer.Visit(parseTree);

                    // Run the post-analysis callback while the parse tree is still available
                    postAnalysisAction?.Invoke(model);

                    // Release parse tree immediately to free memory
                    model.Definition.ParsedCode = null;

                    batchResults.Add((model.Id, analyzer.ReferencedModels, analyzer.Resources));
                }
                catch
                {
                    // If analysis fails, skip this model
                }
            });

            // Add edges from this batch (single-threaded for graph consistency)
            foreach (var (sourceId, dependencies, resources) in batchResults)
            {
                foreach (var referencedModelId in dependencies)
                {
                    try { graph.AddModelUsesModel(sourceId, referencedModelId); }
                    catch { /* Edge target may not exist */ }
                }

                if (resources.Count > 0)
                    modelResources[sourceId] = resources;
            }

            totalProcessed += batch.Count;
            progressLog?.Invoke($"Phase 1+2: {totalProcessed}/{allModels.Count} models processed");

            // Hint GC to collect — parse trees and ANTLR token streams from this batch
            // are now unreachable. Without this hint, LOH objects accumulate across batches.
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
        }

        // Phase 3: LoadSelector Pass 2 — find modifications of discovered parameters
        // Now that all loadSelector and loadResource parameters are known across the graph,
        // re-scan models to find modifications of those parameters in component instances.
        progressLog?.Invoke("Phase 3: LoadSelector pass 2");
        bool hasTrackedParams = allModels.Any(m =>
            m.LoadSelectorParameters.Count > 0 ||
            m.LoadResourceParameters.Count > 0);

        if (hasTrackedParams)
        {
            totalProcessed = 0;
            foreach (var batch in Batch(allModels, batchSize))
            {
                var pass2Results = new ConcurrentBag<(string modelId, List<ExternalResourceInfo> resources)>();

                Parallel.ForEach(batch, model =>
                {
                    try
                    {
                        var parseTree = model.Definition.EnsureParsed();
                        if (parseTree == null) return;

                        var modAnalyzer = new LoadSelectorModificationAnalyzer(model.Id, graph);
                        modAnalyzer.Visit(parseTree);

                        // Release parse tree immediately
                        model.Definition.ParsedCode = null;

                        if (modAnalyzer.Resources.Count > 0)
                            pass2Results.Add((model.Id, modAnalyzer.Resources));
                    }
                    catch
                    {
                        // If analysis fails, skip this model
                    }
                });

                // Merge pass 2 results with existing resources
                foreach (var (modelId, resources) in pass2Results)
                {
                    if (modelResources.TryGetValue(modelId, out var existingList))
                    {
                        foreach (var resource in resources)
                        {
                            if (!existingList.Any(r =>
                                r.RawPath == resource.RawPath &&
                                r.ReferenceType == resource.ReferenceType &&
                                r.ParameterName == resource.ParameterName))
                            {
                                existingList.Add(resource);
                            }
                        }
                    }
                    else
                    {
                        modelResources[modelId] = resources;
                    }
                }

                totalProcessed += batch.Count;
                progressLog?.Invoke($"Phase 3: {totalProcessed}/{allModels.Count} models processed");
                GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
            }
        }

        // Phase 4: Resolve paths and create resource nodes with edges
        progressLog?.Invoke($"Phase 4: Resolving {modelResources.Count} models with external resources");
        var libraryList = libraries?.ToList() ?? new List<LibraryInfo>();
        foreach (var kvp in modelResources)
        {
            var modelId = kvp.Key;
            var resources = kvp.Value;
            var model = graph.GetNode<ModelNode>(modelId);
            if (model == null) continue;

            string? includeDirectory = null;
            string? libraryDirectory = null;

            foreach (var info in resources)
            {
                if (info.ReferenceType == ResourceReferenceType.ExternalIncludeDirectory)
                    includeDirectory = ResolveModelicaUri(info.RawPath, libraryList);
                else if (info.ReferenceType == ResourceReferenceType.ExternalLibraryDirectory)
                    libraryDirectory = ResolveModelicaUri(info.RawPath, libraryList);
            }

            foreach (var info in resources)
            {
                CreateResourceNodeAndEdge(graph, model, info, libraryList, includeDirectory, libraryDirectory);
            }
        }

        progressLog?.Invoke("Dependency analysis complete");
    }

    /// <summary>
    /// Splits a list into batches of the specified size.
    /// </summary>
    private static IEnumerable<List<T>> Batch<T>(List<T> source, int batchSize)
    {
        for (int i = 0; i < source.Count; i += batchSize)
        {
            yield return source.GetRange(i, Math.Min(batchSize, source.Count - i));
        }
    }

    /// <summary>
    /// Analyzes dependencies for a specific subset of models, leaving all other models untouched.
    /// Use this during incremental refresh when only a few files have changed.
    ///
    /// For each model in <paramref name="modelIds"/>:
    ///   1. Clears its existing dependency edges (forward and reverse).
    ///   2. Re-runs the ModelAnalyzer visitor on its cached parse tree.
    ///   3. Rebuilds model-to-model and model-to-resource edges.
    ///
    /// After calling this, call <see cref="DirectedGraph.ReconcileDependencyEdges"/> to repair
    /// forward _edges and reverse UsedByModelIds on models NOT in the analysis set that referenced
    /// any of the re-added models.
    /// </summary>
    /// <param name="graph">The graph to update.</param>
    /// <param name="modelIds">IDs of models to re-analyze. IDs not found in the graph are ignored.</param>
    /// <param name="libraries">Library information for resolving modelica:// URIs.</param>
    /// <param name="postAnalysisAction">Optional callback invoked for each model after dependency
    /// analysis while the parse tree is still available.</param>
    public static async Task AnalyzeDependenciesForModelsAsync(
        DirectedGraph graph,
        IReadOnlySet<string> modelIds,
        IEnumerable<LibraryInfo>? libraries = null,
        Action<ModelNode>? postAnalysisAction = null)
    {
        if (modelIds.Count == 0)
            return;

        var models = graph.ModelNodes
            .Where(m => modelIds.Contains(m.Id))
            .ToList();

        if (models.Count == 0)
            return;

        // Clear existing dependency edges for the models being re-analyzed
        foreach (var model in models)
            graph.RemoveModelDependencyEdges(model.Id);

        // Phase 1: Parallel analysis — same as AnalyzeDependenciesAsync but scoped to target models.
        // Parse trees are released immediately after each model to minimize memory usage.
        var analysisResults = new ConcurrentBag<(string sourceId, HashSet<string> dependencies, List<ExternalResourceInfo> resources)>();

        Parallel.ForEach(models, model =>
        {
            try
            {
                var parseTree = model.Definition.EnsureParsed();
                if (parseTree == null) return;

                var analyzer = new ModelAnalyzer(model.Id, graph);
                analyzer.Visit(parseTree);

                // Run the post-analysis callback while the parse tree is still available
                postAnalysisAction?.Invoke(model);

                // Release parse tree immediately to free memory
                model.Definition.ParsedCode = null;

                analysisResults.Add((model.Id, analyzer.ReferencedModels, analyzer.Resources));
            }
            catch
            {
                // If analysis fails, skip this model
            }
        });

        // Phase 2: Batch add dependency edges
        var modelResources = new Dictionary<string, List<ExternalResourceInfo>>();
        foreach (var (sourceId, dependencies, resources) in analysisResults)
        {
            foreach (var referencedModelId in dependencies)
            {
                try { graph.AddModelUsesModel(sourceId, referencedModelId); }
                catch { }
            }

            if (resources.Count > 0)
                modelResources[sourceId] = resources;
        }

        // Phase 3: LoadSelector Pass 2 — only for target models with tracked parameters.
        // Parse trees were released in Phase 1; EnsureParsed() re-parses on demand.
        bool hasTrackedParams = models.Any(m =>
            m.LoadSelectorParameters.Count > 0 || m.LoadResourceParameters.Count > 0);

        if (hasTrackedParams)
        {
            var pass2Results = new ConcurrentBag<(string modelId, List<ExternalResourceInfo> resources)>();

            Parallel.ForEach(models, model =>
            {
                try
                {
                    var parseTree = model.Definition.EnsureParsed();
                    if (parseTree == null) return;

                    var modAnalyzer = new LoadSelectorModificationAnalyzer(model.Id, graph);
                    modAnalyzer.Visit(parseTree);

                    // Release parse tree immediately
                    model.Definition.ParsedCode = null;

                    if (modAnalyzer.Resources.Count > 0)
                        pass2Results.Add((model.Id, modAnalyzer.Resources));
                }
                catch { }
            });

            foreach (var (modelId, resources) in pass2Results)
            {
                if (modelResources.TryGetValue(modelId, out var existingList))
                {
                    foreach (var resource in resources)
                    {
                        if (!existingList.Any(r =>
                            r.RawPath == resource.RawPath &&
                            r.ReferenceType == resource.ReferenceType &&
                            r.ParameterName == resource.ParameterName))
                        {
                            existingList.Add(resource);
                        }
                    }
                }
                else
                {
                    modelResources[modelId] = resources;
                }
            }
        }

        // Phase 4: Resolve paths and create resource nodes with edges
        var libraryList = libraries?.ToList() ?? new List<LibraryInfo>();
        foreach (var kvp in modelResources)
        {
            var model = graph.GetNode<ModelNode>(kvp.Key);
            if (model == null) continue;

            string? includeDirectory = null;
            string? libraryDirectory = null;

            foreach (var info in kvp.Value)
            {
                if (info.ReferenceType == ResourceReferenceType.ExternalIncludeDirectory)
                    includeDirectory = ResolveModelicaUri(info.RawPath, libraryList);
                else if (info.ReferenceType == ResourceReferenceType.ExternalLibraryDirectory)
                    libraryDirectory = ResolveModelicaUri(info.RawPath, libraryList);
            }

            foreach (var info in kvp.Value)
                CreateResourceNodeAndEdge(graph, model, info, libraryList, includeDirectory, libraryDirectory);
        }

        // Parse trees were already released in Phases 1 and 3 immediately after use.
        await Task.CompletedTask; // preserve async signature for future parallel additions
    }

    /// <summary>
    /// Creates a resource node (file or directory) and an edge from the model to it.
    /// </summary>
    private static void CreateResourceNodeAndEdge(
        DirectedGraph graph,
        ModelNode model,
        ExternalResourceInfo info,
        List<LibraryInfo> libraries,
        string? includeDirectory,
        string? libraryDirectory)
    {
        string? resolvedPath = null;
        bool isDirectory = false;

        switch (info.ReferenceType)
        {
            case ResourceReferenceType.LoadResource:
            case ResourceReferenceType.UriReference:
            case ResourceReferenceType.LoadSelector:
                // These are file references - resolve the modelica:// URI or relative path
                resolvedPath = ResolveResourcePath(info.RawPath, model, graph, libraries);
                break;

            case ResourceReferenceType.ExternalIncludeDirectory:
            case ResourceReferenceType.ExternalLibraryDirectory:
            case ResourceReferenceType.ExternalSourceDirectory:
                // These are directory references - create directory node AND scan for files within
                resolvedPath = ResolveModelicaUri(info.RawPath, libraries);
                if (resolvedPath != null && Directory.Exists(resolvedPath))
                {
                    // Create the directory node
                    var dirNode = graph.GetOrCreateResourceDirectoryNode(resolvedPath);
                    var dirEdge = new ResourceEdge
                    {
                        RawPath = info.RawPath,
                        ReferenceType = info.ReferenceType,
                        ParameterName = info.ParameterName,
                        IsAbsolutePath = IsAbsoluteFilePath(info.RawPath)
                    };
                    try
                    {
                        graph.AddModelReferencesResource(model.Id, dirNode.Id, dirEdge);
                    }
                    catch { /* Edge may already exist */ }

                    // Scan directory for source files and create nodes for each
                    // Note: IncludeDirectory often contains both .c and .h files in practice (e.g., C-Sources)
                    var extensions = info.ReferenceType switch
                    {
                        ResourceReferenceType.ExternalIncludeDirectory => new[] { ".c", ".cpp", ".cxx", ".h", ".hpp", ".hxx" },
                        ResourceReferenceType.ExternalSourceDirectory => new[] { ".c", ".cpp", ".cxx", ".h", ".hpp", ".hxx" },
                        ResourceReferenceType.ExternalLibraryDirectory => new[] { ".lib", ".dll", ".a", ".so", ".dylib" },
                        _ => Array.Empty<string>()
                    };
                    ScanDirectoryForFiles(graph, dirNode, extensions);
                }
                return; // Already handled

            case ResourceReferenceType.ExternalInclude:
                // Parse #include "filename.h" and resolve using IncludeDirectory
                var headerFile = ParseIncludeDirective(info.RawPath);
                if (headerFile != null)
                {
                    var incDir = includeDirectory ?? GetDefaultIncludeDirectory(model.Id, libraries);
                    if (incDir != null)
                        resolvedPath = Path.Combine(incDir, headerFile);
                }
                break;

            case ResourceReferenceType.ExternalLibrary:
                // Resolve all platform variants of the library file
                var libDir = libraryDirectory ?? GetDefaultLibraryDirectory(model.Id, libraries);
                if (libDir != null)
                {
                    var libraryFiles = ResolveAllLibraryFiles(info.RawPath, libDir);
                    foreach (var libPath in libraryFiles)
                    {
                        var libNode = graph.GetOrCreateResourceFileNode(libPath);
                        var libEdge = new ResourceEdge
                        {
                            RawPath = info.RawPath,
                            ReferenceType = info.ReferenceType,
                            ParameterName = info.ParameterName,
                            IsAbsolutePath = false
                        };
                        try
                        {
                            graph.AddModelReferencesResource(model.Id, libNode.Id, libEdge);
                        }
                        catch
                        {
                            // Edge may already exist, ignore
                        }
                    }
                }
                return; // Already handled, don't fall through to single-file logic
        }

        if (resolvedPath == null)
            return;

        // Create the resource node
        IGraphNode resourceNode;
        if (isDirectory)
        {
            resourceNode = graph.GetOrCreateResourceDirectoryNode(resolvedPath);
        }
        else
        {
            resourceNode = graph.GetOrCreateResourceFileNode(resolvedPath);
        }

        // Create the edge
        var edge = new ResourceEdge
        {
            RawPath = info.RawPath,
            ReferenceType = info.ReferenceType,
            ParameterName = info.ParameterName,
            IsAbsolutePath = IsAbsoluteFilePath(info.RawPath)
        };

        try
        {
            graph.AddModelReferencesResource(model.Id, resourceNode.Id, edge);
        }
        catch
        {
            // Edge may already exist, ignore
        }
    }

    /// <summary>
    /// Resolves a resource path to an absolute file system path.
    /// Handles modelica:// URIs, absolute paths, and relative paths.
    /// </summary>
    private static string? ResolveResourcePath(
        string rawPath,
        ModelNode model,
        DirectedGraph graph,
        List<LibraryInfo> libraries)
    {
        if (rawPath.StartsWith("modelica://", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveModelicaUri(rawPath, libraries);
        }
        else if (Path.IsPathRooted(rawPath))
        {
            // Absolute path - use as-is
            return rawPath;
        }
        else
        {
            // Relative path - resolve relative to the file containing this model
            var containingFileId = model.ContainingFileId;
            if (containingFileId != null)
            {
                var fileNode = graph.GetNode<FileNode>(containingFileId);
                if (fileNode != null)
                {
                    var fileDir = Path.GetDirectoryName(fileNode.FilePath);
                    if (fileDir != null)
                    {
                        return Path.GetFullPath(Path.Combine(fileDir, rawPath));
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a modelica:// URI to an absolute file system path.
    /// Format: modelica://LibraryName/path/to/resource.ext
    /// or: modelica://LibraryName.SubPackage/path/to/resource.ext
    /// Also handles malformed URIs with double slashes (e.g., modelica://Lib//Resources/...)
    /// </summary>
    private static string? ResolveModelicaUri(string uri, List<LibraryInfo> libraries)
    {
        // Strip the modelica:// prefix
        if (!uri.StartsWith("modelica://", StringComparison.OrdinalIgnoreCase))
            return null;

        var path = uri.Substring("modelica://".Length);

        if (string.IsNullOrEmpty(path))
            return null;

        // Split into library identifier and resource path
        var slashIndex = path.IndexOf('/');
        if (slashIndex < 0)
            return null;

        var libraryIdentifier = path.Substring(0, slashIndex);
        var resourcePath = path.Substring(slashIndex + 1);

        // Handle malformed URIs with double slashes (e.g., modelica://Lib//Resources/...)
        // Trim any leading slashes from the resource path
        resourcePath = resourcePath.TrimStart('/');

        // Also normalize any remaining double slashes within the path
        while (resourcePath.Contains("//"))
        {
            resourcePath = resourcePath.Replace("//", "/");
        }

        if (string.IsNullOrEmpty(resourcePath))
            return null;

        // The library identifier may contain dots (e.g., "Modelica.Blocks")
        // The first part before the dot is the library name
        var libraryName = libraryIdentifier.Split('.')[0];

        // Find the library
        var library = libraries.FirstOrDefault(l =>
            l.Name.Equals(libraryName, StringComparison.OrdinalIgnoreCase));

        if (library == null)
            return null;

        // Get the library root directory
        var libraryRoot = library.RootPath;

        if (libraryRoot == null)
            return null;

        // If the library identifier has sub-package parts (e.g., "Modelica.Blocks"),
        // these map to subdirectories
        var subPackageParts = libraryIdentifier.Split('.');
        var basePath = libraryRoot;
        for (int i = 1; i < subPackageParts.Length; i++)
        {
            basePath = Path.Combine(basePath, subPackageParts[i]);
        }

        // Combine with the resource path (using OS-specific separators)
        var fullPath = Path.GetFullPath(
            Path.Combine(basePath, resourcePath.Replace('/', Path.DirectorySeparatorChar)));

        return fullPath;
    }

    /// <summary>
    /// Parses an Include directive to extract the filename.
    /// E.g., '#include "ModelicaStandardTables.h"' returns "ModelicaStandardTables.h"
    /// Handles both regular quotes and escaped quotes (\" from ANTLR string tokens).
    /// </summary>
    private static string? ParseIncludeDirective(string rawInclude)
    {
        // First unescape any escaped quotes from ANTLR token text
        // The Include annotation value comes from a Modelica string like:
        //   Include="#include \"ModelicaStandardTables.h\""
        // ANTLR preserves the escape sequence, so we need to convert \" to "
        var unescaped = rawInclude
            .Replace("\\\"", "\"")
            .Replace("\\'", "'");

        // Handle: #include "filename.h" or #include <filename.h>
        var match = System.Text.RegularExpressions.Regex.Match(
            unescaped,
            @"#include\s*[""<]([^"">]+)[>""]");

        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Gets the default IncludeDirectory for a model based on its library.
    /// Default: modelica://LibraryName/Resources/Include
    /// </summary>
    private static string? GetDefaultIncludeDirectory(string modelId, List<LibraryInfo> libraries)
    {
        var libraryName = modelId.Split('.')[0];
        var library = libraries.FirstOrDefault(l =>
            l.Name.Equals(libraryName, StringComparison.OrdinalIgnoreCase));

        if (library == null)
            return null;

        return Path.Combine(library.RootPath, "Resources", "Include");
    }

    /// <summary>
    /// Gets the default LibraryDirectory for a model based on its library.
    /// Default: modelica://LibraryName/Resources/Library
    /// </summary>
    private static string? GetDefaultLibraryDirectory(string modelId, List<LibraryInfo> libraries)
    {
        var libraryName = modelId.Split('.')[0];
        var library = libraries.FirstOrDefault(l =>
            l.Name.Equals(libraryName, StringComparison.OrdinalIgnoreCase));

        if (library == null)
            return null;

        return Path.Combine(library.RootPath, "Resources", "Library");
    }

    /// <summary>
    /// Resolves all platform variants of a library file by searching all platform
    /// and compiler subdirectories. Returns all existing library files found.
    /// </summary>
    private static List<string> ResolveAllLibraryFiles(string libraryName, string libraryDir)
    {
        var foundFiles = new List<string>();
        var addedPaths = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        // All known platform directories
        var platforms = new[]
        {
            ("win32", new[] { ".lib", ".dll", ".a" }),
            ("win64", new[] { ".lib", ".dll", ".a" }),
            ("linux32", new[] { ".a", ".so" }),
            ("linux64", new[] { ".a", ".so" }),
            ("darwin64", new[] { ".a", ".dylib" }),
            ("darwin32", new[] { ".a", ".dylib" })
        };

        // Common compiler subdirectories to search
        var compilerDirs = new[] { "vs2022", "vs2019", "vs2017", "vs2015", "vs2013", "vs2010",
                                   "gcc", "gcc47", "gcc48", "gcc49", "clang" };

        // Search each platform directory
        foreach (var (platform, extensions) in platforms)
        {
            var platformDir = Path.Combine(libraryDir, platform);
            if (!Directory.Exists(platformDir))
                continue;

            // Search compiler subdirectories first
            foreach (var compiler in compilerDirs)
            {
                var compilerDir = Path.Combine(platformDir, compiler);
                if (Directory.Exists(compilerDir))
                {
                    SearchLibraryInDirectory(libraryName, compilerDir, extensions, foundFiles, addedPaths);
                }
            }

            // Then search platform directory directly
            SearchLibraryInDirectory(libraryName, platformDir, extensions, foundFiles, addedPaths);
        }

        // Also search library directory directly (for platform-agnostic libraries)
        var allExtensions = new[] { ".lib", ".dll", ".a", ".so", ".dylib" };
        SearchLibraryInDirectory(libraryName, libraryDir, allExtensions, foundFiles, addedPaths);

        return foundFiles;
    }

    /// <summary>
    /// Searches for library files in a specific directory and adds found files to the list.
    /// </summary>
    private static void SearchLibraryInDirectory(
        string libraryName,
        string directory,
        string[] extensions,
        List<string> foundFiles,
        HashSet<string> addedPaths)
    {
        foreach (var ext in extensions)
        {
            // Try with lib prefix (Unix convention)
            var libPath = Path.Combine(directory, $"lib{libraryName}{ext}");
            if (File.Exists(libPath))
            {
                var normalizedPath = Path.GetFullPath(libPath);
                if (addedPaths.Add(normalizedPath))
                    foundFiles.Add(normalizedPath);
            }

            // Try without lib prefix
            libPath = Path.Combine(directory, $"{libraryName}{ext}");
            if (File.Exists(libPath))
            {
                var normalizedPath = Path.GetFullPath(libPath);
                if (addedPaths.Add(normalizedPath))
                    foundFiles.Add(normalizedPath);
            }
        }
    }

    /// <summary>
    /// Scans a directory (and subdirectories) for files with the specified extensions
    /// and creates ResourceFileNode entries for each file found.
    /// </summary>
    private static void ScanDirectoryForFiles(
        DirectedGraph graph,
        ResourceDirectoryNode dirNode,
        string[] extensions)
    {
        if (!Directory.Exists(dirNode.ResolvedPath))
            return;

        try
        {
            // Scan all files in this directory and subdirectories
            // Files are added to the directory's ContainedFileIds list rather than
            // creating model-to-file edges. This allows accurate reference counting:
            // - The directory shows which models reference it
            // - Individual files only show references from direct Include directives
            foreach (var ext in extensions)
            {
                var files = Directory.GetFiles(dirNode.ResolvedPath, $"*{ext}", SearchOption.AllDirectories);
                foreach (var filePath in files)
                {
                    var normalizedPath = Path.GetFullPath(filePath);
                    var fileNode = graph.GetOrCreateResourceFileNode(normalizedPath);

                    // Track that this file is contained in this directory
                    dirNode.AddContainedFile(fileNode.Id);
                }
            }
        }
        catch
        {
            // Directory access may fail, ignore
        }
    }

    /// <summary>
    /// Checks if a path is an absolute file system path (not a modelica:// URI).
    /// </summary>
    private static bool IsAbsoluteFilePath(string path)
    {
        if (path.StartsWith("modelica://", StringComparison.OrdinalIgnoreCase))
            return false;
        return Path.IsPathRooted(path);
    }

    /// <summary>
    /// Generates a unique file ID from a file path.
    /// On Windows, paths are normalized to lowercase for case-insensitive matching.
    /// </summary>
    /// <summary>
    /// Incrementally updates a graph by re-parsing only the files that changed.
    /// Removes models from changed/deleted files and re-parses changed/added files.
    /// </summary>
    /// <param name="graph">The existing graph to update in-place.</param>
    /// <param name="rootPath">Root directory of the new checkout (where changed files are read from).</param>
    /// <param name="changedRelativeFiles">Set of relative file paths that changed (from DetectChangedFiles).</param>
    /// <returns>List of model IDs that were affected (removed or added).</returns>
    public static List<string> UpdateGraphForChangedFiles(
        DirectedGraph graph,
        string rootPath,
        HashSet<string> changedRelativeFiles)
    {
        var affectedModelIds = new List<string>();

        // Filter to .mo files only
        var changedMoFiles = changedRelativeFiles
            .Where(f => f.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var changedOrderFiles = changedRelativeFiles
            .Where(f => f.EndsWith("package.order", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Step 1: Remove models from changed/deleted .mo files
        foreach (var relativePath in changedMoFiles)
        {
            var fullPath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var fileId = GenerateFileId(fullPath);

            // Collect models in this file before removing
            var modelsInFile = graph.GetModelsInFile(fileId).ToList();
            foreach (var model in modelsInFile)
            {
                affectedModelIds.Add(model.Id);
                graph.RemoveNode(model.Id);
            }
            graph.RemoveNode(fileId);
        }

        // Step 2: Re-parse changed/added .mo files that still exist on disk
        var filesToReparse = changedMoFiles
            .Select(f => Path.Combine(rootPath, f.Replace('/', Path.DirectorySeparatorChar)))
            .Where(File.Exists)
            .ToArray();

        if (filesToReparse.Length > 0)
        {
            var newModelIds = LoadModelicaFiles(graph, filesToReparse);
            affectedModelIds.AddRange(newModelIds);
        }

        // Step 3: Update changed package.order files
        foreach (var relativePath in changedOrderFiles)
        {
            var fullPath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath)) continue;

            // Find the package model by looking at the directory's package.mo
            var directory = Path.GetDirectoryName(fullPath);
            if (directory == null) continue;

            var packageMoPath = Path.Combine(directory, "package.mo");
            if (!File.Exists(packageMoPath)) continue;

            var packageFileId = GenerateFileId(packageMoPath);
            var packageModels = graph.GetModelsInFile(packageFileId).ToList();
            if (packageModels.Count > 0)
            {
                var packageNode = packageModels[0]; // The package model
                var orderLines = File.ReadAllLines(fullPath)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrEmpty(l))
                    .ToArray();
                packageNode.PackageOrder = orderLines;
            }
        }

        return affectedModelIds.Distinct().ToList();
    }

    public static string GenerateFileId(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);

        // On Windows, normalize to lowercase for case-insensitive matching
        if (OperatingSystem.IsWindows())
        {
            fullPath = fullPath.ToLowerInvariant();
        }

        return $"file:{fullPath}";
    }

    /// <summary>
    /// Generates a unique model ID by combining the parent name with the model name.
    /// Uses dot notation to represent hierarchical relationships.
    /// </summary>
    /// <param name="parentName">The parent model name (can be null for top-level models).</param>
    /// <param name="modelName">The model name.</param>
    /// <returns>A unique model ID in the form "Parent.Child" or just "ModelName" for top-level models.</returns>
    public static string GenerateModelId(string? parentName, string modelName)
    {
        // Combine parent and model name for unique ID using dot notation
        return parentName!=null && parentName.Length>0 ? $"{parentName}.{modelName}" : modelName;
    }
}
