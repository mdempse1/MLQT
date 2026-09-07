using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using RevisionControl;
using static MLQT.Services.LoggingService;

namespace MLQT.Services.Helpers;

/// <summary>
/// The two ways MLQT writes formatted Modelica back to disk. See <see cref="IFormattingPipeline"/>
/// for what each is for and when it runs.
/// </summary>
/// <remarks>
/// Extracted from <c>MainLayout</c> in phase 7a-4. It reaches the file system and the loaded graph
/// and nothing else — the progress a user sees is the host's business, which is why the only thing
/// it reports is a per-library failure, through a callback.
/// </remarks>
public sealed class FormattingPipeline : IFormattingPipeline
{
    private readonly ILibraryDataService _libraryData;
    private readonly IRepositoryService _repositories;

    private readonly Dictionary<string, DateTime> _writtenFileTimestamps =
        new(StringComparer.OrdinalIgnoreCase);

    public FormattingPipeline(ILibraryDataService libraryData, IRepositoryService repositories)
    {
        _libraryData = libraryData;
        _repositories = repositories;
    }

    public IReadOnlyDictionary<string, DateTime> WrittenFileTimestamps => _writtenFileTimestamps;

    public void ClearWrittenFileTimestamps() => _writtenFileTimestamps.Clear();

    /// <summary>
    /// True when a repository is reference-only, which means it is never written to. Says so in the
    /// log, because a user who asked for a format and got nothing deserves a reason.
    /// </summary>
    private static bool SkipReferenceOnly(Repository repository, string what)
    {
        if (!repository.IsReferenceOnly)
            return false;

        Debug(nameof(FormattingPipeline), $"Skipping {what} for {repository.Name} - reference only");
        return true;
    }

    /// <summary>The repository's modified and untracked Modelica files, as the VCS reports them.</summary>
    private HashSet<string> GetModifiedFilePathsFromVcs(Repository repository)
    {
        try
        {
            return VcsChangeResolver.FormattableModelicaFiles(
                repository.LocalPath,
                repository.VcsRootPath,
                _repositories.GetWorkingCopyChanges(repository.Id),
                File.Exists);
        }
        catch (Exception ex)
        {
            Warn(nameof(FormattingPipeline),
                $"Could not read VCS status for repository {repository.Name}: {ex.Message}");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task FormatChangedFilesAsync(
        IEnumerable<string> changedFilePaths, StyleCheckingSettings styleSettings)
    {
        var written = await IncrementalFormatter.FormatAndWriteAsync(
            _libraryData.CombinedGraph, changedFilePaths, styleSettings);

        foreach (var (filePath, writtenAt) in written)
            _writtenFileTimestamps[filePath] = writtenAt;
    }

    public async Task<int> FormatModifiedFilesAsync()
    {
        LogProcessStart(nameof(FormattingPipeline), "Formatting VCS-modified files");
        int totalFormatted = 0;

        foreach (var repository in _repositories.Repositories)
        {
            if (string.IsNullOrEmpty(repository.LocalPath) || SkipReferenceOnly(repository, "formatting"))
                continue;

            try
            {
                // Get per-repository style settings
                var styleSettings = repository.StyleSettings ?? new StyleCheckingSettings();
                if (!styleSettings.ApplyFormattingRules)
                {
                    Debug(nameof(FormattingPipeline), $"Skipping formatting for repository {repository.Name} (ApplyFormattingRules is disabled)");
                    continue;
                }

                // Get modified and untracked files from VCS
                var changedFilePaths = GetModifiedFilePathsFromVcs(repository);
                if (changedFilePaths.Count == 0)
                {
                    Debug(nameof(FormattingPipeline), $"No modified files in repository {repository.Name}");
                    continue;
                }

                Info(nameof(FormattingPipeline), $"Formatting {changedFilePaths.Count} modified file(s) in repository {repository.Name}");
                var written = await IncrementalFormatter.FormatAndWriteAsync(
                    _libraryData.CombinedGraph, changedFilePaths, styleSettings);
                foreach (var (writtenPath, writtenAt) in written)
                    _writtenFileTimestamps[writtenPath] = writtenAt;
                totalFormatted += changedFilePaths.Count;
            }
            catch (Exception ex)
            {
                Warn(nameof(FormattingPipeline), $"Failed to format modified files in repository {repository.Name}: {ex.Message}");
            }
        }

        Info(nameof(FormattingPipeline), $"Formatted {totalFormatted} modified file(s) across all repositories");
        LogProcessEnd(nameof(FormattingPipeline), "Formatting VCS-modified files");
        return totalFormatted;
    }

    /// <summary>
    /// Gets the full paths of modified, added, and untracked .mo files within the
    /// repository's Modelica library directory. VCS status covers the full VcsRootPath,
    /// so paths are filtered to LocalPath to scope analysis to Modelica files only.
    /// </summary>

    public async Task SaveAllLibrariesWithFormattingAsync(
        string? filterRepositoryId = null, Action<string, Exception>? onLibraryFailed = null)
    {
        LogProcessStart(nameof(FormattingPipeline), "Saving all libraries with formatting");

        // Reference libraries are dropped before anything else looks at the list — every kind of
        // them. The encrypted ones hold only reconstructions from vendor documentation, so there is
        // nothing here that could be written back and the saver refuses them outright; a readable one
        // is a tool's installed library, which the settings page promises is never formatted. That
        // half used to rest on filterRepositoryId being non-null, which is true of every caller today
        // and is not what the parameter means.
        IReadOnlyList<LoadedLibrary> libraries = _libraryData.Libraries
            .Where(l => l.SourceType != LibrarySourceType.EncryptedDirectory)
            .Where(l => !ReferenceOnlyScope.IsReference(l, _repositories))
            .Where(l => filterRepositoryId == null || l.RepositoryId == filterRepositoryId)
            .ToList();

        // Collect all original file paths before we start saving
        // When filtering by repository, only consider files from those libraries
        var originalFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var librarySourcePaths = libraries.Select(l => l.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var fileNode in _libraryData.CombinedGraph.FileNodes)
        {
            if (File.Exists(fileNode.FilePath) &&
                (filterRepositoryId == null || librarySourcePaths.Any(sp => fileNode.FilePath.StartsWith(sp, StringComparison.OrdinalIgnoreCase))))
            {
                originalFiles.Add(fileNode.FilePath);
            }
        }

        // Also collect package.order files that exist in the library directories
        // (excluding hidden directories like .svn, .git which may contain their own package.order files)
        var originalOrderFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries)
        {
            if (!string.IsNullOrEmpty(library.SourcePath) && Directory.Exists(library.SourcePath))
            {
                foreach (var orderFile in Directory.GetFiles(library.SourcePath, "package.order", SearchOption.AllDirectories))
                {
                    // Skip files inside hidden directories (e.g., .svn, .git)
                    if (!FileMonitoringServiceHelpers.IsInHiddenDirectory(orderFile))
                    {
                        originalOrderFiles.Add(orderFile);
                    }
                }
            }
        }

        Debug(nameof(FormattingPipeline), $"Found {originalFiles.Count} original .mo files and {originalOrderFiles.Count} package.order files");

        // Track all files written during save operations
        var allWrittenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allCreatedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modelIdToFilePath = new Dictionary<string, string>();

        // Process libraries sequentially to limit peak memory. Each library already
        // parallelizes its parse/render phases internally (Parallel.ForEach in batches).
        // Running libraries concurrently causes nested parallelism: N libraries × M cores
        // of parse trees coexisting in memory simultaneously, overwhelming 16GB machines.
        foreach (var library in libraries)
        {
            // Skip libraries without a valid source path or zip-based libraries
            if (string.IsNullOrEmpty(library.SourcePath) || library.SourceType == LibrarySourceType.Zip)
            {
                continue;
            }

            // Skip single-file libraries (they don't have a directory structure)
            if (library.SourceType == LibrarySourceType.File && !Directory.Exists(library.SourcePath))
            {
                continue;
            }

            try
            {
                Debug(nameof(FormattingPipeline), $"Saving library: {library.Name}");

                // Get repository-specific style settings, falling back to global settings
                StyleCheckingSettings styleSettings;
                if (!string.IsNullOrEmpty(library.RepositoryId))
                {
                    var repository = _repositories.GetRepository(library.RepositoryId);
                    styleSettings = repository?.StyleSettings ?? new StyleCheckingSettings();
                }
                else
                {
                    styleSettings = new StyleCheckingSettings();
                }

                // Skip formatting if ApplyFormattingRules is disabled for this repository
                // But still mark the library's existing files as "written" to prevent them from being deleted as orphans
                if (!styleSettings.ApplyFormattingRules)
                {
                    Debug(nameof(FormattingPipeline), $"Skipping formatting for library: {library.Name} (ApplyFormattingRules is disabled)");

                    // Collect existing files for this library to prevent them from being deleted
                    lock (allWrittenFiles)
                    {
                        foreach (var modelId in library.ModelIds)
                        {
                            var model = _libraryData.GetModelById(modelId);
                            if (model?.ContainingFileId != null)
                            {
                                var fileNode = _libraryData.CombinedGraph.GetNode(model.ContainingFileId) as FileNode;
                                if (fileNode != null && File.Exists(fileNode.FilePath))
                                {
                                    allWrittenFiles.Add(fileNode.FilePath);
                                }
                            }
                        }

                        // Also preserve package.order files
                        if (!string.IsNullOrEmpty(library.SourcePath) && Directory.Exists(library.SourcePath))
                        {
                            foreach (var orderFile in Directory.GetFiles(library.SourcePath, "package.order", SearchOption.AllDirectories))
                            {
                                if (!FileMonitoringServiceHelpers.IsInHiddenDirectory(orderFile))
                                {
                                    allWrittenFiles.Add(orderFile);
                                }
                            }
                        }
                    }

                    continue;
                }

                await Task.Run(() =>
                {
                    // Get the graph containing all models
                    var graph = _libraryData.CombinedGraph;

                    // Where a library is written depends on how it was loaded: beside a single
                    // file, into the parent of a package directory (the saver creates the library
                    // folder itself), or into a loose directory as it stands.
                    var saveDirectory = ModelicaPackageSaver.ResolveSaveDirectory(
                        library.SourcePath, File.Exists, Directory.Exists);

                    if (saveDirectory is null)
                    {
                        Warn(nameof(FormattingPipeline), $"No writable save directory for library {library.Name} at {library.SourcePath}");
                        return;
                    }

                    // Save the library and get information about written files using repository-specific settings
                    var saveResult = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
                        graph,
                        library.ModelIds,
                        saveDirectory,
                        showAnnotations: true,
                        formatting: styleSettings.ToFormattingOptions(),
                        excludedModelIds: styleSettings.FormattingExcludedModels);

                    // Collect written files and directories
                    lock (allWrittenFiles)
                    {
                        foreach (var file in saveResult.WrittenFiles)
                        {
                            allWrittenFiles.Add(file);
                        }
                        foreach (var dir in saveResult.CreatedDirectories)
                        {
                            allCreatedDirectories.Add(dir);
                        }
                        foreach (var kvp in saveResult.ModelIdToFilePath)
                        {
                            modelIdToFilePath[kvp.Key] = kvp.Value;
                        }
                    }

                    Debug(nameof(FormattingPipeline), $"Successfully saved library: {library.Name} ({saveResult.WrittenFiles.Count} files)");
                });
            }
            catch (Exception ex)
            {
                Error(nameof(FormattingPipeline), $"Failed to format library {library.Name}", ex);
                onLibraryFailed?.Invoke(library.Name, ex);
            }
        }

        // Collect files that are scheduled for VCS addition so we don't delete them.
        // A file added by an SVN/Git merge is "scheduled for addition" in the VCS but may
        // not be written by the formatter if it has a malformed within-clause or mismatched
        // model name.  Deleting it would cause a "scheduled for addition, but is missing"
        // commit error.
        var vcsAddedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(filterRepositoryId))
        {
            try
            {
                var repo = _repositories.GetRepository(filterRepositoryId);
                if (repo?.LocalPath != null)
                {
                    foreach (var wc in _repositories.GetWorkingCopyChanges(filterRepositoryId)
                        .Where(c => c.Status == VcsFileStatus.Added))
                    {
                        vcsAddedFiles.Add(Path.Combine(repo.LocalPath, wc.Path));
                    }
                }
            }
            catch (Exception ex)
            {
                Warn(nameof(FormattingPipeline), $"Could not read VCS status to protect added files: {ex.Message}");
            }
        }

        var orphaned = OrphanedFileSelector.SelectOrphans(
            originalFiles, originalOrderFiles, allWrittenFiles, vcsAddedFiles);

        Debug(nameof(FormattingPipeline), $"Deleting {orphaned.Count} orphaned file(s) left by the save");

        foreach (var orphanedFile in orphaned)
        {
            try
            {
                if (File.Exists(orphanedFile))
                {
                    File.Delete(orphanedFile);
                    Debug(nameof(FormattingPipeline), $"Deleted orphaned file: {orphanedFile}");
                }
            }
            catch (Exception ex)
            {
                Warn(nameof(FormattingPipeline), $"Failed to delete orphaned file {orphanedFile}: {ex.Message}");
            }
        }

        // Clean up empty directories that may have been left behind
        foreach (var library in libraries)
            EmptyDirectoryCleaner.RemoveEmptyDirectories(library.SourcePath);

        // Record formatted file timestamps for skip-if-unchanged optimization
        foreach (var filePath in allWrittenFiles)
        {
            if (filePath.EndsWith(".mo", StringComparison.OrdinalIgnoreCase) && File.Exists(filePath))
                _writtenFileTimestamps[filePath] = File.GetLastWriteTimeUtc(filePath);
        }

        // Update FileNodes in the graph with new file paths
        FileNodeReconciler.ReassignModels(_libraryData.CombinedGraph, modelIdToFilePath);

        LogProcessEnd(nameof(FormattingPipeline), "Saving all libraries with formatting");
    }

    /// <summary>
    /// Whether this repository is one MLQT must not write to. <c>settings-reference.md</c> promises a
    /// reference library is "never checked, formatted, committed or written to"; B66 delivered
    /// <em>checked</em> and this is <em>formatted</em>.
    ///
    /// <para>The formatting paths each resolve a repository and take its <c>StyleSettings</c>, and
    /// none of them asked. A reference-only repository whose committed <c>.mlqt/settings.json</c> sets
    /// <c>ApplyFormattingRules</c> — which another team's MLQT-managed library naturally would — had
    /// its modified files reformatted and written back at startup, and again after every refresh.</para>
    /// </summary>
}
