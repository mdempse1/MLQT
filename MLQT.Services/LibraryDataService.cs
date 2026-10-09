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
        // Dropped whether or not the announcement is suppressed: a bulk load is exactly when the
        // answer goes stale, and the one announcement at the end of it must not be served a set
        // built before the libraries arrived.
        lock (_descendantParserErrorsLock)
        {
            _descendantParserErrors = null;
            _descendantParserErrorsGeneration++;
        }

        // Every rendered icon too: an icon is drawn from base classes that may be in the library
        // that just arrived or left, or in the class just reloaded (B349).
        Interlocked.Increment(ref _iconGeneration);

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
            // A single-file library resolves modelica:// URIs relative to its containing directory,
            // whatever its source type says (B428).
            return new LibraryInfo(lib.Name, lib.RootDirectory,
                isEncrypted: lib.IsReadOnly);
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

            _dependencyAnalysisTask = Task.Run(async () =>
            {
                // Again if a library arrived while it ran: that run did not see the new classes, so it
                // leaves the graph unmarked (B352), and a caller awaiting this would otherwise go on
                // with no edges for them. Bounded, because a stream of loads is not a reason to spin.
                for (var attempt = 0; attempt < 3 && !_combinedGraph.DependenciesAnalyzed; attempt++)
                    await GraphBuilder.AnalyzeDependenciesAsync(_combinedGraph, GetLibraryInfos(), progressLog);
            });
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

            Register(library);

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

            Register(library);

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
        var detected = EncryptedLibraryDetector.Detect(directoryPath)
            ?? throw new InvalidOperationException(
                $"'{directoryPath}' is not an encrypted Modelica library (no {EncryptedLibraryDetector.EncryptedPackageFileName}).");

        var source = new EncryptedDirectoryClassSource(detected);
        var library = new LoadedLibrary
        {
            SourcePath = directoryPath,
            SourceType = LibrarySourceType.EncryptedDirectory
        };

        await LoadReadOnlyAsync(library, source, CancellationToken.None, () =>
        {
            if (!detected.HasDocumentation)
            {
                Warn("LibraryDataService",
                    $"Encrypted library '{detected.Name}' ships no documentation; its classes cannot be recovered");
                return;
            }

            Debug("LibraryDataService",
                $"Read {source.Document.Classes.Count} documented classes from {source.Document.FilesRead} help files " +
                $"({source.Document.FilesSkipped} skipped) for '{detected.Name}'");
        });
        return library;
    }

    /// <inheritdoc/>
    public async Task<LoadedLibrary> AddLibraryFromSourceAsync(
        IReadOnlyClassSource source, CancellationToken cancellationToken = default)
    {
        if (source.Kind != ReadOnlySourceKind.Supplied)
            throw new ArgumentException(
                $"Only a supplied library is added this way; '{source.LibraryName}' is {source.Kind}, which " +
                "has a loader of its own.", nameof(source));

        var library = new LoadedLibrary
        {
            SourcePath = source.Location ?? ReadOnlySources.InMemoryRoot(source.LibraryName),
            SourceType = LibrarySourceType.Supplied
        };

        await LoadReadOnlyAsync(library, source, cancellationToken, onRead: null);
        return library;
    }

    /// <summary>
    /// The one load of a read-only library, whatever its source: precedence asked before reading,
    /// the classes put into the graph by <see cref="ReadOnlySourceLoader"/>, and the library
    /// registered — which applies precedence again, for a copy that arrived while this one loaded.
    /// </summary>
    /// <param name="library">The library to fill, with its source path and type already set.</param>
    /// <param name="source">Where its classes come from.</param>
    /// <param name="cancellationToken">Stops the load before it registers anything.</param>
    /// <param name="onRead">Called after the source is read, for a log line only the caller can write.</param>
    private async Task LoadReadOnlyAsync(
        LoadedLibrary library, IReadOnlyClassSource source, CancellationToken cancellationToken, Action? onRead)
    {
        var description = $"{Describe(source.Kind)} '{source.LibraryName}' from {library.SourcePath}";
        LogProcessStart("LibraryDataService", $"Loading {description}");

        library.Name = source.LibraryName;
        library.Version = source.LibraryVersion;
        library.ReadOnlySource = source.Kind;

        try
        {
            // A copy that outranks this one is already loaded, so it would be retired the moment it
            // registered. Asked before the source is read rather than after, so an encrypted library
            // whose source is checked out costs a directory probe instead of a pass over its help
            // HTML and a graph full of stubs built only to be taken out again - and a supplied one is
            // never read for nothing. RepositoryService asks the readable-source half of this earlier
            // still, from discovery; this is for every caller that comes straight here - the CLI's
            // dependencies, the MCP server's mlqt_load_library, the Reference Libraries setting. A
            // copy that arrives while this is loading is still caught by Register.
            LoadedLibrary? outranking;
            lock (_lock)
            {
                outranking = SourceSupersedesEncrypted.OutrankedBy(library, _libraries);
            }

            if (outranking is not null)
            {
                library.SupersededBy = outranking.SourcePath;
                Info("LibraryDataService", NotUsedMessage(library, outranking));
                LogProcessEnd("LibraryDataService", $"Loading {description}");
                return;
            }

            RetireOutrankedOnlyByVersion(library);

            var superseded = 0;
            await Task.Run(() =>
            {
                var content = source.Read(cancellationToken);
                onRead?.Invoke();

                // Supplied text is parsed in parallel, as a directory's files are; the documentation
                // path builds its nodes one by one and takes the graph lock for that, as it always has.
                ReadOnlySourceLoad load;
                if (source.Kind == ReadOnlySourceKind.RecoveredFromDocumentation)
                {
                    lock (_graphLock)
                    {
                        load = ReadOnlySourceLoader.Load(_combinedGraph, source, content, cancellationToken);
                    }

                    library.DocumentedClassCount = content.Documented.Count;
                }
                else
                {
                    load = ReadOnlySourceLoader.Load(_combinedGraph, source, content, cancellationToken);
                }

                superseded = load.Superseded;
                BuildLibraryIndex(library, _combinedGraph, load.ModelIds.ToList());
            }, cancellationToken);

            // The new models have no dependency edges yet, so anything that needs them must
            // re-analyse before it can trust the graph.
            _combinedGraph.InvalidateDependencyAnalysis();

            var registered = Register(library);

            OnLibrariesChanged?.Invoke();
            RaiseTreeDataChanged();

            if (registered)
            {
                Info("LibraryDataService",
                    $"Loaded {Describe(source.Kind)} '{library.Name}' {library.Version} with {library.ModelIds.Count} " +
                    "classes (reference only)" +
                    (superseded > 0 ? $"; {superseded} left to the copy already loaded for them" : ""));
            }

            LogProcessEnd("LibraryDataService", $"Loading {description}");
        }
        catch (Exception ex)
        {
            LogProcessFailed("LibraryDataService", $"Loading {description}", ex);
            throw;
        }
    }

    private static string Describe(ReadOnlySourceKind? kind) => kind switch
    {
        ReadOnlySourceKind.RecoveredFromDocumentation => "encrypted library",
        ReadOnlySourceKind.Supplied => "supplied library",
        _ => "library"
    };

    /// <summary>Why <paramref name="library"/> is not used, now that <paramref name="winner"/> outranks it.</summary>
    private static string NotUsedMessage(LoadedLibrary library, LoadedLibrary winner) =>
        SourceSupersedesEncrypted.VersionsDisagree(library, winner)
            ? $"{Capitalised(Describe(library.ReadOnlySource))} '{library.Name}' {library.Version} at {library.SourcePath} " +
              $"is not used: it describes a different release from the {Describe(winner.ReadOnlySource)} " +
              $"{winner.Version} installed at {winner.SourcePath}"
            : $"{Capitalised(Describe(library.ReadOnlySource))} '{library.Name}' at {library.SourcePath} is not used: " +
              $"{(winner.IsReadOnly ? "a " + Describe(winner.ReadOnlySource) : "readable source")} for it is " +
              $"loaded from {winner.SourcePath}";

    private static string Capitalised(string text) => char.ToUpperInvariant(text[0]) + text[1..];

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
            await Task.Run(() =>
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

                BuildLibraryIndex(library, _combinedGraph, modelIds);
            });

            // Invalidated like every other load, rather than analysed here. The full analysis this ran
            // went around EnsureDependenciesAnalyzedAsync's gate, so it could race a run already in
            // flight over the same edges, and it passed no library roots, so modelica:// references
            // resolved differently from every other run (B352, and B166's third cause).
            _combinedGraph.InvalidateDependencyAnalysis();

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

            Register(library);

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
                RemoveSuppliedNodes(library);
                _libraries.Remove(library);
            }
        }

        OnLibrariesChanged?.Invoke();
        RaiseTreeDataChanged();
    }

    /// <inheritdoc/>
    public bool RelocateLibrary(string libraryId, string directoryPath)
    {
        lock (_lock)
        {
            var library = _libraries.FirstOrDefault(l => l.Id == libraryId);
            if (library is null)
                return false;

            // Under the lock, because LibraryContainingPath reads it there: it is what places a
            // class from a new file in this library, and the old path placed none of them (B417).
            library.SourcePath = directoryPath;
            if (library.SourceType == LibrarySourceType.File)
                library.SourceType = LibrarySourceType.Directory;
        }

        Info("LibraryDataService", $"Library now loaded from directory {directoryPath}");
        OnLibrariesChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Adds a newly loaded library to the list, applying <see cref="SourceSupersedesEncrypted"/> on
    /// the way in. Every load path registers through here, so the rule cannot be missing from one.
    /// </summary>
    /// <returns>False when the library itself was superseded and is not registered: a read-only copy
    /// arriving after one that outranks it. Its classes are gone from the graph and its index is
    /// emptied, so a caller that counts or keeps it sees a library that contributes nothing.</returns>
    /// <remarks>
    /// The check and the add are one step under the lock. Two parallel loads of the same library —
    /// the ordinary shape of a project that holds a checkout and a tool's library folder — therefore
    /// cannot both miss each other: whichever registers second sees the first.
    /// </remarks>
    private bool Register(LoadedLibrary library)
    {
        List<(LoadedLibrary Library, LoadedLibrary Winner, int Removed)> retired = [];
        bool registered;

        lock (_lock)
        {
            foreach (var superseded in SourceSupersedesEncrypted.Retires(library, _libraries))
            {
                var winner = ReferenceEquals(superseded, library)
                    ? SourceSupersedesEncrypted.OutrankedBy(library, _libraries)!
                    : library;
                retired.Add((superseded, winner, RetireLocked(superseded, winner)));
            }

            registered = !retired.Any(r => ReferenceEquals(r.Library, library));
            if (registered)
                _libraries.Add(library);
        }

        LogRetired(retired);
        return registered;
    }

    /// <summary>
    /// Retires, before <paramref name="arriving"/> is loaded, the copies of its library it wins
    /// against <b>only by version</b> - a supplied copy of another release, when the installed build
    /// recovered from documentation arrives.
    /// </summary>
    /// <remarks>
    /// Every other retirement waits for <see cref="Register"/>, because there the graph already
    /// agrees: a class arriving from a higher-ranked copy replaces the lower one's as it lands. Here
    /// it does not - the supplied classes outrank the recovered ones class by class, so the recovered
    /// build's classes would be left out as they loaded, and retiring the supplied copy afterwards
    /// would leave neither. Taking it out first lets them load into the place it held.
    /// </remarks>
    private void RetireOutrankedOnlyByVersion(LoadedLibrary arriving)
    {
        List<(LoadedLibrary Library, LoadedLibrary Winner, int Removed)> retired = [];
        lock (_lock)
        {
            foreach (var superseded in SourceSupersedesEncrypted.Retires(arriving, _libraries)
                         .Where(l => !ReferenceEquals(l, arriving)
                                     && ReadOnlySources.Precedence(l.ReadOnlySource)
                                        > ReadOnlySources.Precedence(arriving.ReadOnlySource))
                         .ToList())
            {
                retired.Add((superseded, arriving, RetireLocked(superseded, arriving)));
            }
        }

        LogRetired(retired);
    }

    /// <summary>Takes a superseded library out of the graph and the list. Called under <c>_lock</c>.</summary>
    /// <returns>How many of its classes were removed from the graph.</returns>
    private int RetireLocked(LoadedLibrary superseded, LoadedLibrary winner)
    {
        var removed = RemoveSuppliedNodes(superseded);
        _libraries.Remove(superseded);
        superseded.ModelIds = [];
        superseded.TopLevelModelIds = [];
        superseded.ChildrenByParent = new();
        superseded.SupersededBy = winner.SourcePath;
        return removed;
    }

    private static void LogRetired(List<(LoadedLibrary Library, LoadedLibrary Winner, int Removed)> retired)
    {
        foreach (var (superseded, winner, removed) in retired)
        {
            Info("LibraryDataService", NotUsedMessage(superseded, winner) +
                (removed > 0 ? $"; {removed} of its class(es) the other copy does not have were removed" : ""));
        }
    }

    /// <summary>
    /// Takes out of the graph the nodes this library actually supplies, and nothing else.
    /// </summary>
    /// <returns>How many class nodes were removed.</returns>
    /// <remarks>
    /// <para><b>Not every id in <see cref="LoadedLibrary.ModelIds"/>.</b> A read-only library that
    /// was loaded before a copy that outranks it lists ids whose node is now that copy's, and
    /// removing by the list deleted the user's own classes from the graph along with the vendor's.
    /// A library supplies classes of its own kind only, which is the same question
    /// <see cref="Owns"/> answers for the tree.</para>
    ///
    /// <para>A read-only library's file nodes go with it - an encrypted library's <c>package.moe</c>,
    /// a supplied library's in-memory files. They hold nothing once the classes are gone, and a file
    /// node for a read-only source left in the graph is one more path that every write has to
    /// remember not to take at face value.</para>
    /// </remarks>
    private int RemoveSuppliedNodes(LoadedLibrary library)
    {
        var supplied = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in library.ModelIds)
        {
            if (_combinedGraph.GetNode<ModelNode>(id) is { } node && Owns(library, node))
                supplied.Add(id);
        }

        // One pass over the graph's edges - a whole library is the largest removal there is.
        _combinedGraph.RemoveNodes(supplied);

        if (library.SourceType == LibrarySourceType.EncryptedDirectory && !string.IsNullOrEmpty(library.SourcePath))
        {
            // The same path the loader gave the stub builder, so the same id. Removed whatever its
            // ContainedModelIds says: RemoveNodes does not update a file's list, and nothing else is
            // in this file - source that replaced a stub was detached from it as it arrived.
            _combinedGraph.RemoveNode(GraphBuilder.GenerateFileId(
                Path.Combine(library.SourcePath, EncryptedLibraryDetector.EncryptedPackageFileName)));
        }
        else if (library.SourceType == LibrarySourceType.Supplied && ReadOnlySources.IsInMemoryPath(library.SourcePath))
        {
            // Every file of a supplied library is under its root, and nothing else can be: the
            // prefix is never a path on disk.
            var root = library.SourcePath + "/";
            foreach (var file in _combinedGraph.FileNodes
                         .Where(f => f.FilePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                         .ToList())
                _combinedGraph.RemoveNode(file.Id);
        }

        return supplied.Count;
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

            // Every removal below is set-based and makes a single pass over each index. Written as a
            // loop over the models — which is how it read until 2026-09-08 — each of the three inner
            // structures is walked once per model, and a file holding 4,478 generated classes then
            // took 54 seconds to reload after a one-word edit. See DirectedGraph.RemoveNodes.
            var removing = new HashSet<string>(modelIdsInFile, StringComparer.Ordinal);

            // Remove from library indexes
            foreach (var library in _libraries)
            {
                library.ModelIds.ExceptWith(removing);
                library.TopLevelModelIds.RemoveAll(removing.Contains);

                // Remove these models from ChildrenByParent lists where they appear as children.
                //
                // NOTE: Do NOT remove them as parent keys (ChildrenByParent.Remove(modelId))
                // because child models may exist in separate files and still need their
                // parent-child relationship preserved. The children list will be rebuilt
                // when the file is reloaded.
                foreach (var children in library.ChildrenByParent.Values)
                {
                    children.RemoveAll(removing.Contains);
                }
            }

            // Remove the models and the file node from the graph, in one pass over its edges.
            _combinedGraph.RemoveNodes(removing);
            _combinedGraph.RemoveNode(fileId);

            Debug("LibraryDataService", $"Removed {modelIdsInFile.Count} models from file: {filePath}");
        }

        return removedModelIds;
    }

    /// <inheritdoc/>
    public async Task RefreshDependenciesAsync(IReadOnlyCollection<string> modelIds)
    {
        if (modelIds.Count == 0 || !_combinedGraph.DependenciesAnalyzed)
            return;

        // On the pool, whoever the caller is. AnalyzeDependenciesForModelsAsync is async in name
        // only - its Parallel.ForEach blocks the thread that calls it - and Code Review calls this
        // from a button handler, on the UI thread. Suppressing rules on a Dymola FMU import model
        // reloads its 4,478 classes, and the app stopped responding for the 39 s this took.
        var libraries = GetLibraryInfos();
        await Task.Run(async () =>
        {
            await GraphBuilder.AnalyzeDependenciesForModelsAsync(
                _combinedGraph, modelIds.ToHashSet(StringComparer.Ordinal), libraries);
            _combinedGraph.ReconcileDependencyEdges();
        });
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
                foreach (var model in _combinedGraph.GetModelsInFile(fileId))
                {
                    library = LibraryOwnership.Owner(_libraries, model.Id, GetModelById);
                    if (library != null) break;
                }
            }
        }

        // If file doesn't exist in graph yet, try to find library by path
        if (library == null)
        {
            lock (_lock)
                library = LibraryContainingPath(filePath);
        }

        // Before the old classes go: what they import on behalf of classes below them in other files.
        EnclosingScopeChanges enclosingImports;
        lock (_lock)
            enclosingImports = EnclosingScopeChanges.Capture(_combinedGraph, [fileId]);

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

        // A package whose imports changed changes what names mean in its children's files too (B347),
        // and so does a class added to or removed from it (B387).
        lock (_lock)
            affectedModelIds.AddRange(enclosingImports.DescendantsToReanalyse(_combinedGraph));

        RaiseTreeDataChanged();

        // Each class once. A file whose classes are unchanged contributes every id twice — once as
        // removed, once as re-added — and a caller cannot tell that from a class genuinely listed for
        // two reasons. It reaches a parallel re-check as two entries, where both can pass the
        // already-checked guard before either sets it, and the class's findings are then reported
        // twice.
        return affectedModelIds.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The loaded library whose source a file lies in, by path — for a file the graph has no classes
    /// from yet, so no class can say. Asked with <see cref="PathContainment.IsWithin"/>, not a bare
    /// <c>StartsWith</c>: <c>…/Lib</c> is a prefix of <c>…/LibExtra/New.mo</c>, and the prefix test
    /// gave a new class in <c>LibExtra</c> to <c>Lib</c> whenever <c>Lib</c> was loaded first
    /// (B323 follow-up). The nearest root wins, so a library nested inside another gets its own
    /// files. Callers hold <c>_lock</c>.
    /// </summary>
    private LoadedLibrary? LibraryContainingPath(string filePath)
    {
        LoadedLibrary? nearest = null;
        foreach (var lib in _libraries)
        {
            if (string.IsNullOrEmpty(lib.SourcePath) || !PathContainment.IsWithin(filePath, lib.SourcePath))
                continue;

            if (nearest is null || lib.SourcePath.Length > nearest.SourcePath.Length)
                nearest = lib;
        }

        return nearest;
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
                        library = LibraryContainingPath(fileNode.FilePath);
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
    /// <para>A library supplies classes of its own kind: readable ones, supplied stubs, or stubs
    /// recovered from documentation (<see cref="ReadOnlySources.KindOf"/>). Since B268 a lower-ranked
    /// copy is never registered beside a higher one of the same library, so this rarely has two
    /// candidates to choose between; it is what <see cref="RemoveSuppliedNodes"/> asks so that removing
    /// a library cannot take another library's classes with it, whatever the index says.</para>
    /// </summary>
    private static bool Owns(LoadedLibrary library, ModelNode node) =>
        ReadOnlySources.KindOf(node) == library.ReadOnlySource;

    /// <inheritdoc/>
    public int TotalModelCount
    {
        get
        {
            lock (_lock)
            {
                // Distinct ids that are actually in the graph. Both halves earn their place: the set
                // is what stops a class listed by two libraries being counted twice (two readable
                // checkouts of one library, now that an encrypted build never sits beside its source),
                // and the graph
                // lookup is what stops an id a library still lists after its node has gone being
                // counted at all.
                //
                // Deliberately *not* filtered by Owns. Which library owns the node cannot change how
                // many distinct classes there are, and a check that cannot change the answer is a
                // check that will be believed to do something.
                var counted = new HashSet<string>(StringComparer.Ordinal);
                foreach (var library in _libraries)
                    foreach (var modelId in library.ModelIds)
                        if (_combinedGraph.GetNode<ModelNode>(modelId) is not null)
                            counted.Add(modelId);

                return counted.Count;
            }
        }
    }

    /// <summary>
    /// The classes a tree shows at its root, ready to display.
    /// </summary>
    /// <remarks>
    /// <para><b>Actually asynchronous, which the name had been promising without keeping.</b> It
    /// returned a completed task, so a caller awaiting it ran the whole thing on its own thread —
    /// and the caller is a Blazor component, whose thread the desktop host also uses for its window
    /// message pump. Preparing a class for display renders its icon, which resolves and parses its
    /// base classes, so the first refresh after a load spent about a second there: measured at
    /// 1,477ms of a 1,522ms tree refresh, and still around 1,040ms once the icons were cached and
    /// only rendered once (B258).</para>
    ///
    /// <para><b>And the preparing happens outside the lock.</b> Deciding which library owns a class
    /// needs it; rendering that class's icon does not, and holding it through work that reaches into
    /// the graph and parses other classes is what made an unrelated caller's
    /// <see cref="GetAllModels"/> wait 872ms.</para>
    /// </remarks>
    public async Task<IReadOnlyList<ModelNode>> GetTopLevelModelsAsync()
    {
        // One preparer at a time. Every open tree asks for *all* top-level classes and filters to
        // its own repository afterwards, so four repositories meant four passes over the same
        // hundred-odd libraries — and because they run at the same moment, none of them finds the
        // icons the others are rendering. Measured: four trees finishing on the same millisecond,
        // each reporting ~1,420ms (B258). Queued instead, the first pass does the work and the rest
        // find it done, which is the same wall clock for a quarter of the effort.
        await _preparingTopLevel.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                // Keyed by model id, because two libraries claiming the same top-level class are
                // claiming the *same node object*. Adding it once per claiming library put the
                // library in the tree twice, and — since preparing it for display stamps the
                // library id onto the shared node — both copies ended up attributed to whichever
                // library was processed last. The case that produced it, a tool's encrypted build
                // beside the user's checkout, no longer loads both (B268); two readable checkouts of
                // one library in different repositories still would.
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
                }

                    var items = new List<ModelNode>(byModelId.Count);
                    foreach (var (node, library) in byModelId.Values)
                    {
                        PrepareModelForDisplay(node, library);
                        items.Add(node);
                    }

            return (IReadOnlyList<ModelNode>)items;
            });
        }
        finally
        {
            _preparingTopLevel.Release();
        }
    }

    // See GetTopLevelModelsAsync: concurrent trees would otherwise each render the same icons.
    private readonly SemaphoreSlim _preparingTopLevel = new(1, 1);

    /// <inheritdoc/>
    public Task<IReadOnlyList<ModelNode>> GetChildModelsAsync(ModelNode? parentNode)
    {
        if (parentNode == null)
        {
            return GetTopLevelModelsAsync();
        }

        // Off the caller's thread for the same reason as GetTopLevelModelsAsync: expanding a node
        // prepares each child for display, which renders its icon the first time (B258). A package
        // of two hundred classes would otherwise render two hundred icons on the dispatcher while
        // the user waits for the node to open.
        return Task.Run<IReadOnlyList<ModelNode>>(() =>
        {
            List<ModelNode> children;
            LoadedLibrary owner;

            lock (_lock)
            {
                var parentModel = _combinedGraph.GetNode<ModelNode>(parentNode.Id);
                if (parentModel == null)
                    return [];

                // The library for this parent is the one that owns it, not merely the first that
                // claims it: two copies of a library list the same parent with different children, and
                // taking the first claimant meant expanding a package could show the wrong set. Since
                // B268 an encrypted build is not loaded beside its source, so the claimants are two
                // readable copies when there are two at all.
                var library = LibraryOwnership.Owner(_libraries, parentModel.Id, _ => parentModel);
                if (library == null)
                    return [];

                if (!library.ChildrenByParent.TryGetValue(parentModel.Id, out var childIds))
                    return [];

                children = SortByPackageOrder(
                    childIds
                        .Where(id => library.ModelIds.Contains(id))
                        .Select(id => _combinedGraph.GetNode<ModelNode>(id))
                        .Where(m => m != null)
                        .Cast<ModelNode>()
                        .ToList(),
                    parentModel);
                owner = library;
            }

            // Outside the lock: rendering an icon reaches into the graph and parses other classes,
            // and holding the service's lock through that is what made an unrelated caller wait.
            foreach (var child in children)
                PrepareModelForDisplay(child, owner);

            return children;
        });
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
    public LoadedLibrary? GetOwningLibrary(string modelId)
    {
        LoadedLibrary[] snapshot;
        lock (_lock)
        {
            snapshot = _libraries.ToArray();
        }

        return LibraryOwnership.Owner(snapshot, modelId, GetModelById);
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

    private IReadOnlySet<string>? _descendantParserErrors;

    /// <summary>
    /// Counts the invalidations of <see cref="_descendantParserErrors"/>, so a set built from a
    /// snapshot that a library change has since overtaken is returned to its caller but not kept
    /// (B356).
    /// </summary>
    private int _descendantParserErrorsGeneration;
    private readonly object _descendantParserErrorsLock = new();

    /// <summary>Counts the changes that can make a rendered icon stale (B349).</summary>
    private int _iconGeneration;

    /// <summary>A test's way in between the snapshot and the assignment.</summary>
    internal Action? AfterParserErrorSnapshot { get; set; }

    /// <inheritdoc/>
    public IReadOnlySet<string> ModelsWithDescendantParserErrors()
    {
        if (Volatile.Read(ref _descendantParserErrors) is { } cached)
            return cached;

        var generation = Volatile.Read(ref _descendantParserErrorsGeneration);
        var models = GetAllModels();
        AfterParserErrorSnapshot?.Invoke();

        var descendants = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            if (!model.HasParserErrors)
                continue;

            // Every package above it, so the warning is visible from the root without expanding.
            // Climbed by containment rather than by splitting the id: a quoted identifier carries
            // dots of its own, and the split named packages that do not exist (B356, as B189 and
            // B191 found for the reveal and the change markers).
            var seen = new HashSet<string>(StringComparer.Ordinal) { model.Id };
            var parentId = model.ParentModelName;
            while (!string.IsNullOrEmpty(parentId) && seen.Add(parentId))
            {
                descendants.Add(parentId);
                parentId = GetModelById(parentId)?.ParentModelName;
            }
        }

        // Kept only if no library arrived or left while it was being built. A bulk load drops the
        // cache per library and announces once at the end, and a set built from a snapshot taken
        // before the last library landed would otherwise be served to every tree for that one
        // announcement - and kept until the next.
        lock (_descendantParserErrorsLock)
        {
            if (_descendantParserErrorsGeneration == generation)
                _descendantParserErrors = descendants;
        }

        return descendants;
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

        // And its version from the top-level package's annotation, unless its source already said.
        if (library.Version is null && library.TopLevelModelIds.Count > 0)
            library.Version = graph.GetNode<ModelNode>(library.TopLevelModelIds[0])?.Version;
    }

    /// <summary>
    /// Populates a model's display metadata for tree presentation: renders its Modelica icon to SVG
    /// (with base-class inheritance) into <see cref="ModelNode.IconSvg"/> and stamps its LibraryId.
    /// UI-agnostic — returns nothing and uses no Blazor/MudBlazor types; the UI layer wraps the model
    /// into its own tree-item representation.
    /// </summary>
    private void PrepareModelForDisplay(ModelNode model, LoadedLibrary library)
    {
        // Rendered once per class, not once per tree refresh. Extracting an icon resolves the
        // class's base classes and parses them to do it, and the tree is rebuilt on every change to
        // it — so this ran on the dispatcher, for every top-level class, every time. Measured on a
        // real project: 1,477ms of a 1,522ms refresh, repeatedly, which is the startup stutter
        // (B258). The answer is kept on the definition and discarded with the rest of the derived
        // state when the class's code changes.
        //
        // Kept against a generation as well as the class's own code (B349): the render resolves
        // base classes, often in other libraries, so a library arriving or leaving or any class being
        // reloaded can change the answer. Only the classes a tree actually shows are rendered again.
        var generation = Volatile.Read(ref _iconGeneration);
        var definition = model.Definition;
        if (definition.IconRendered && definition.IconGeneration == generation)
        {
            model.LibraryId = library.Id;
            return;
        }

        // **The flag is set after the render, not before.** Two trees can prepare the same class at
        // once now that this runs outside the lock, and claiming it first would let the second see
        // "already rendered" with the SVG still null — a class silently missing its icon until
        // something rebuilt the tree again. Rendering it twice costs a little and is always right.
        string? iconSvg = null;

        // Try to extract Modelica Icon annotation and render as SVG (with inheritance support)
        try
        {
            // Derive the initial package context from the model's fully-qualified ID.
            // The stored ModelicaCode is the extracted class body (no 'within' clause), so the
            // renderer cannot infer the package from the code itself. The package context is needed
            // to resolve unqualified extends names (e.g. "Interfaces.DiscreteSISO") via walk-up.
            var package = ModelicaName.EnclosingPackageOf(model.Id);
            var initialPackageContext = package.Length > 0 ? package : null;

            // Read once: a concurrent release can null the tree between a check and a second read
            // (B291's shape), and the render then throws.
            var parsed = definition.ParsedCode;
            iconSvg = parsed != null
                ? IconSvgRenderer.ExtractAndRenderIconWithInheritance(
                    parsed,
                    baseClassName => ResolveBaseClass(baseClassName, model),
                    size: 20,
                    fileNameResolver: fileName => ResolveImageFileName(fileName, library),
                    initialPackageContext: initialPackageContext)
                : IconSvgRenderer.ExtractAndRenderIconWithInheritance(
                    definition.ModelicaCode,
                    baseClassName => ResolveBaseClass(baseClassName, model),
                    size: 20,
                    fileNameResolver: fileName => ResolveImageFileName(fileName, library),
                    initialPackageContext: initialPackageContext);
        }
        catch (Exception ex)
        {
            // Not remembered as "no icon" (B349): a render that threw has no answer, and the next
            // refresh tries again. Whatever was drawn before stays on screen meanwhile.
            Debug("LibraryDataService", $"Icon extraction failed for model {model.Id}: {ex.Message}");
            model.LibraryId = library.Id;
            return;
        }

        model.IconSvg = iconSvg;
        definition.IconGeneration = generation;
        definition.IconRendered = true;
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
        var currentPackage = ModelicaName.EnclosingPackageOf(currentModel.Id);

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

            // A single-file library, including one found in a repository, is rooted at the
            // directory holding its file (B428).
            var rootDir = matchingLibrary.RootDirectory;

            if (string.IsNullOrEmpty(rootDir)) return null;

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
