using MLQT.Services.Interfaces;
using MLQT.Services.DataTypes;
using System.Text.Json;
using ModelicaGraph;
using ModelicaParser.Helpers;
using RevisionControl;
using RevisionControl.Interfaces;
using static MLQT.Services.LoggingService;

namespace MLQT.Services;

/// <summary>
/// Service implementation for managing repositories containing Modelica libraries.
/// </summary>
public class RepositoryService : IRepositoryService
{
    private readonly ILibraryDataService _libraryDataService;
    private readonly ISettingsService _settingsService;
    private readonly IFileMonitoringService _fileMonitoringService;
    private readonly GitRevisionControlSystem _git;
    private readonly SvnRevisionControlSystem _svn;
    private readonly List<Repository> _repositories = new();
    private readonly List<ProjectProfile> _projects = new();
    private string? _activeProjectId;
    private readonly object _lock = new();
    private readonly Dictionary<string, (List<VcsWorkingCopyFile> Changes, long Ticks)> _workingCopyCache = new();
    private readonly object _workingCopyCacheLock = new();
    private const long WorkingCopyCacheLifetimeMs = 300000; // 5 minutes — event-based invalidation handles real changes

    private const string SettingsKey = "Repositories";

    public RepositoryService(
        ILibraryDataService libraryDataService,
        ISettingsService settingsService,
        IFileMonitoringService fileMonitoringService)
    {
        _libraryDataService = libraryDataService;
        _settingsService = settingsService;
        _fileMonitoringService = fileMonitoringService;
        _git = new GitRevisionControlSystem();
        _svn = new SvnRevisionControlSystem();

        // Invalidate working copy cache when repositories change (commits, branch switches, etc.)
        OnRepositoriesChanged += () => InvalidateWorkingCopyCache();
        // Invalidate when file system changes are detected
        _fileMonitoringService.OnPendingChangesUpdated += () => InvalidateWorkingCopyCache();
    }

    public IReadOnlyList<Repository> Repositories
    {
        get
        {
            lock (_lock)
            {
                return _repositories.ToList().AsReadOnly();
            }
        }
    }

    public event Action? OnRepositoriesChanged;
    public event Action<string, bool>? OnRepositoryLoadStateChanged;
    public event Action<string>? OnProjectChanged;

    public (RepositoryVcsType vcsType, bool isLocal) DetectVcsType(string pathOrUrl)
    {
        // Check if it's a URL
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme == "http" || uri.Scheme == "https")
            {
                // Could be Git or SVN - check for common patterns
                if (pathOrUrl.EndsWith(".git") ||
                    pathOrUrl.Contains("github.com") ||
                    pathOrUrl.Contains("gitlab.com") ||
                    pathOrUrl.Contains("bitbucket.org"))
                {
                    return (RepositoryVcsType.Git, false);
                }
                // Assume SVN for other HTTP URLs
                return (RepositoryVcsType.SVN, false);
            }
            if (uri.Scheme == "svn" || uri.Scheme == "svn+ssh")
            {
                return (RepositoryVcsType.SVN, false);
            }
            if (uri.Scheme == "git" || uri.Scheme == "ssh")
            {
                return (RepositoryVcsType.Git, false);
            }
        }

        // Local path - check what VCS is present
        if (Directory.Exists(pathOrUrl))
        {
            if (_git.IsValidRepository(pathOrUrl))
            {
                return (RepositoryVcsType.Git, true);
            }
            if (_svn.IsValidRepository(pathOrUrl))
            {
                return (RepositoryVcsType.SVN, true);
            }
            return (RepositoryVcsType.Local, true);
        }

        return (RepositoryVcsType.Local, false);
    }

    /// <summary>
    /// Discovers the VCS working copy root for a local path.
    /// Returns the path itself when it is already the VCS root, or for Local (non-VCS) repos.
    /// </summary>
    public string FindVcsRoot(string localPath)
    {
        return DetectVcsRoot(localPath, DetectVcsType(localPath).vcsType);
    }

    /// <summary>
    /// Returns all repositories whose VcsRootPath matches the given path.
    /// Used to coordinate refreshes when a VCS operation affects all libraries in the same repo.
    /// </summary>
    private IEnumerable<Repository> GetRepositoriesWithVcsRoot(string vcsRootPath)
    {
        lock (_lock)
        {
            return _repositories
                .Where(r => string.Equals(r.VcsRootPath, vcsRootPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    private string DetectVcsRoot(string localPath, RepositoryVcsType vcsType)
    {
        if (vcsType == RepositoryVcsType.Git)
        {
            var root = _git.FindRepositoryRoot(localPath);
            if (root != null)
                return root;
        }
        else if (vcsType == RepositoryVcsType.SVN)
        {
            var root = _svn.FindRepositoryRoot(localPath);
            if (root != null)
                return root;
        }

        return localPath;
    }

    public async Task<AddRepositoryResult> AddRepositoryAsync(
        string pathOrUrl,
        string? checkoutPath = null,
        string? name = null,
        bool startMonitoring = true,
        CancellationToken cancellationToken = default)
    {
        var result = new AddRepositoryResult();

        try
        {
            LogProcessStart("RepositoryService", $"Adding repository: {pathOrUrl}");
            var (vcsType, isLocal) = DetectVcsType(pathOrUrl);
            Debug("RepositoryService", $"Detected VCS type: {vcsType}, isLocal: {isLocal}");

            var repository = new Repository
            {
                RemotePath = pathOrUrl,
                VcsType = vcsType,
                Name = name ?? Path.GetFileName(pathOrUrl.TrimEnd('/', '\\'))
            };

            // Handle checkout if needed
            if (isLocal)
            {
                repository.LocalPath = pathOrUrl;
            }
            else
            {
                if (string.IsNullOrEmpty(checkoutPath))
                {
                    result.ErrorMessage = "Checkout path required for remote repositories.";
                    return result;
                }

                repository.LocalPath = checkoutPath;

                // Clone/checkout the repository
                var checkoutSuccess = await Task.Run(() =>
                {
                    return vcsType switch
                    {
                        RepositoryVcsType.Git => _git.CheckoutRevision(pathOrUrl, "HEAD", checkoutPath),
                        RepositoryVcsType.SVN => _svn.CheckoutRevision(pathOrUrl, "HEAD", checkoutPath),
                        _ => false
                    };
                }, cancellationToken);

                if (!checkoutSuccess)
                {
                    result.ErrorMessage = "Failed to checkout repository.";
                    return result;
                }
            }

            // Detect the VCS working copy root (may differ from LocalPath for subdirectory repos)
            repository.VcsRootPath = DetectVcsRoot(repository.LocalPath, vcsType);

            // Get current revision info
            await UpdateRevisionInfoAsync(repository);

            // Discover libraries
            var discoveredLibraries = await DiscoverLibrariesInPathAsync(repository.LocalPath);
            repository.DiscoveredLibraries = discoveredLibraries.ToDictionary(
                d => d.RelativePath,
                d => d.LibraryName);

            // Load settings if they exist, create a new settings class if they don't
            var settingsPath = Path.Combine(repository.LocalPath, ".mlqt", "settings.json");
            if (File.Exists(settingsPath))
            {
                var settings = JsonSerializer.Deserialize<StyleCheckingSettings>(File.ReadAllText(settingsPath));
                repository.StyleSettings = settings;

            }
            else
            {
                StyleCheckingSettings settings = new();
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                repository.StyleSettings = settings;
                Info("RepositoryService", $"No settings.json found for '{repository.Name}' — using defaults");
            }

            repository.IsLoaded = true;
            repository.LastLoadedAt = DateTime.UtcNow;

            lock (_lock)
            {
                _repositories.Add(repository);
            }

            result.Success = true;
            result.Repository = repository;
            result.DiscoveredLibraries = discoveredLibraries;

            // Start file monitoring for this repository (unless we're in initial load).
            // Monitor VcsRootPath rather than LocalPath so that changes to any file in the
            // VCS working copy are detected, even when LocalPath is a subdirectory of the root.
            if (startMonitoring)
            {
                _fileMonitoringService.StartMonitoring(repository.Id, repository.VcsRootPath);
            }

            OnRepositoriesChanged?.Invoke();

            // Save settings
            await SaveRepositorySettingsAsync();

            Info("RepositoryService", $"Successfully added repository: {repository.Name} with {discoveredLibraries.Count} libraries");
            LogProcessEnd("RepositoryService", $"Adding repository: {pathOrUrl}");
        }
        catch (Exception ex)
        {
            Error("RepositoryService", $"Failed to add repository: {pathOrUrl}", ex);
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public async Task<List<DiscoveredLibraryInfo>> DiscoverLibrariesAsync(string repositoryId)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
            return new List<DiscoveredLibraryInfo>();

        return await DiscoverLibrariesInPathAsync(repository.LocalPath);
    }

    private async Task<List<DiscoveredLibraryInfo>> DiscoverLibrariesInPathAsync(string basePath)
    {
        var libraries = new List<DiscoveredLibraryInfo>();

        await Task.Run(() =>
        {
            // Check top-level for package.mo
            var topLevelPackage = Path.Combine(basePath, "package.mo");
            if (File.Exists(topLevelPackage))
            {
                var libraryName = ExtractLibraryName(topLevelPackage);
                libraries.Add(new DiscoveredLibraryInfo
                {
                    RelativePath = "",
                    LibraryName = libraryName ?? Path.GetFileName(basePath),
                    FullPath = basePath
                });
            }
            else 
            {
                // Check immediate subdirectories only if the top-level directory didn't contain a package.mo file
                foreach (var subDir in Directory.GetDirectories(basePath))
                {
                    // Skip hidden directories (like .git, .svn)
                    var dirName = Path.GetFileName(subDir);
                    if (dirName.StartsWith("."))
                        continue;

                    var packagePath = Path.Combine(subDir, "package.mo");
                    if (File.Exists(packagePath))
                    {
                        var libraryName = ExtractLibraryName(packagePath);
                        libraries.Add(new DiscoveredLibraryInfo
                        {
                            RelativePath = dirName,
                            LibraryName = libraryName ?? dirName,
                            FullPath = subDir
                        });
                    }
                }
                //There might also be some models stored here
                foreach (var file in Directory.GetFiles(basePath, "*.mo", SearchOption.TopDirectoryOnly))
                {
                    var fileName = Path.GetFileName(file);
                    var libraryName = ExtractLibraryName(file);
                    libraries.Add(new DiscoveredLibraryInfo
                    {
                        RelativePath = fileName,
                        LibraryName = libraryName ?? fileName.Replace(".mo",""),
                        FullPath = file
                    });
                }
            }
        });

        return libraries;
    }

    private string? ExtractLibraryName(string packageMoPath)
    {
        try
        {
            var content = File.ReadAllText(packageMoPath);
            var models = ModelicaParserHelper.ExtractModels(content);
            var topLevel = models.FirstOrDefault(m => m.ParentModelName == null);
            return topLevel?.Name;
        }
        catch (Exception ex)
        {
            Warn("RepositoryService", $"Failed to extract library name from {packageMoPath}: {ex.Message}");
            return null;
        }
    }

    public async Task LoadLibrariesAsync(
        string repositoryId,
        IEnumerable<string>? libraryPaths = null,
        CancellationToken cancellationToken = default)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
            return;

        LogProcessStart("RepositoryService", $"Loading libraries from repository: {repository.Name}");
        OnRepositoryLoadStateChanged?.Invoke(repositoryId, true);

        try
        {
            var pathsToLoad = libraryPaths?.ToList()
                ?? repository.DiscoveredLibraries.Keys.ToList();

            Info("RepositoryService", $"Loading {pathsToLoad.Count} libraries from repository: {repository.Name}");

            var sourceType = repository.VcsType switch
            {
                RepositoryVcsType.Git => LibrarySourceType.Git,
                RepositoryVcsType.SVN => LibrarySourceType.SVN,
                _ => LibrarySourceType.Directory
            };

            // Load all libraries in parallel for much faster repository loading
            var tasks = pathsToLoad.Select(relativePath => Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullPath = string.IsNullOrEmpty(relativePath)
                    ? repository.LocalPath
                    : Path.Combine(repository.LocalPath, relativePath);

                try
                {
                    Debug("RepositoryService", $"Loading library from: {fullPath}");
                    var library = await _libraryDataService.AddLibraryFromDirectoryAsync(fullPath);
                    library.RepositoryId = repositoryId;
                    library.RelativePathInRepository = relativePath;
                    library.SourceType = sourceType;
                    library.Revision = repository.CurrentRevision;

                    lock (_lock)
                    {
                        if (!repository.LibraryIds.Contains(library.Id))
                        {
                            repository.LibraryIds.Add(library.Id);
                        }
                    }
                    Debug("RepositoryService", $"Successfully loaded library: {library.Name}");
                }
                catch (Exception ex)
                {
                    Error("RepositoryService", $"Failed to load library from {fullPath}", ex);
                }
            }, cancellationToken)).ToList();

            await Task.WhenAll(tasks);

            await SaveRepositorySettingsAsync();
            LogProcessEnd("RepositoryService", $"Loading libraries from repository: {repository.Name}");
        }
        catch (Exception ex)
        {
            LogProcessFailed("RepositoryService", $"Loading libraries from repository: {repository.Name}", ex);
            throw;
        }
        finally
        {
            OnRepositoryLoadStateChanged?.Invoke(repositoryId, false);
        }
    }

    public void RemoveRepository(string repositoryId, bool unloadLibraries = true)
    {
        Repository? repository;
        lock (_lock)
        {
            repository = _repositories.FirstOrDefault(r => r.Id == repositoryId);
            if (repository == null)
                return;

            _repositories.Remove(repository);
        }

        // Stop file monitoring for this repository
        _fileMonitoringService.StopMonitoring(repositoryId);

        if (unloadLibraries)
        {
            foreach (var libraryId in repository.LibraryIds)
            {
                _libraryDataService.RemoveLibrary(libraryId);
            }
        }

        OnRepositoriesChanged?.Invoke();

        // Save settings asynchronously
        _ = SaveRepositorySettingsAsync();
    }

    public async Task RefreshRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
            return;

        // Clear pending file changes for this repository since we're doing a full refresh
        _fileMonitoringService.ClearPendingChanges(repositoryId);

        // Update revision info
        await UpdateRevisionInfoAsync(repository);

        // Re-discover libraries
        var discoveredLibraries = await DiscoverLibrariesInPathAsync(repository.LocalPath);

        lock (_lock)
        {
            repository.DiscoveredLibraries = discoveredLibraries.ToDictionary(
                d => d.RelativePath,
                d => d.LibraryName);
            repository.LastLoadedAt = DateTime.UtcNow;
        }

        //Now reload the libraries in this repository as things might have changed
        foreach (var libraryId in repository.LibraryIds)
        {
            _libraryDataService.RemoveLibrary(libraryId);
        }
        //Now load the discovered libraries        
        await LoadLibrariesAsync(repositoryId, repository.DiscoveredLibraries.Keys.ToList(), new CancellationToken());

        OnRepositoriesChanged?.Invoke();
    }

    public Repository? GetRepository(string repositoryId)
    {
        lock (_lock)
        {
            return _repositories.FirstOrDefault(r => r.Id == repositoryId);
        }
    }

    public Repository? GetRepositoryForLibrary(string libraryId)
    {
        lock (_lock)
        {
            return _repositories.FirstOrDefault(r => r.LibraryIds.Contains(libraryId));
        }
    }

    public async Task SaveRepositorySettingsAsync()
    {
        var settings = new RepositorySettingsCollection();

        lock (_lock)
        {
            // Build entries from current runtime repositories for the active project
            var activeEntries = new List<RepositorySettingsEntry>();
            foreach (var repo in _repositories)
            {
                activeEntries.Add(new RepositorySettingsEntry
                {
                    Id = repo.Id,
                    Name = repo.Name,
                    RemotePath = repo.RemotePath,
                    LocalPath = repo.LocalPath,
                    VcsRootPath = repo.VcsRootPath,
                    VcsType = repo.VcsType.ToString(),
                    PreferredRevision = repo.CurrentRevision,
                    AutoLoad = true,
                    LibraryPaths = repo.DiscoveredLibraries.Keys.ToList()
                });

                //Save the formatting settings into the repository so that every user gets the same
                var settingsPath = Path.Combine(repo.LocalPath, ".mlqt", "settings.json");
                var json = JsonSerializer.Serialize(repo.StyleSettings, new JsonSerializerOptions { WriteIndented = true });
                var settingsDir = Path.GetDirectoryName(settingsPath);
                if (settingsDir != null && !Directory.Exists(settingsDir))
                    Directory.CreateDirectory(settingsDir);
                File.WriteAllText(settingsPath, json);
            }

            // Ensure at least a default project exists
            if (_projects.Count == 0)
            {
                var defaultProject = new ProjectProfile { Name = "Default" };
                _projects.Add(defaultProject);
                _activeProjectId = defaultProject.Id;
            }

            // Keep the active project's in-memory ProjectProfile in sync with current runtime
            // repositories. Without this, switching projects would save stale (empty) repo
            // lists for the previously-active project, overwriting correctly persisted data.
            var activeInMemoryProject = _projects.FirstOrDefault(p => p.Id == _activeProjectId);
            if (activeInMemoryProject != null)
                activeInMemoryProject.Repositories = activeEntries;

            // Build the projects list, updating the active project with current runtime state
            foreach (var project in _projects)
            {
                if (project.Id == _activeProjectId)
                {
                    settings.Projects.Add(new ProjectProfile
                    {
                        Id = project.Id,
                        Name = project.Name,
                        Repositories = activeEntries
                    });
                }
                else
                {
                    settings.Projects.Add(project);
                }
            }

            settings.ActiveProjectId = _activeProjectId;
        }

        await _settingsService.SetAsync(SettingsKey, settings);
    }

    public async Task LoadRepositorySettingsAsync(string? projectId = null, CancellationToken cancellationToken = default)
    {
        LogProcessStart("RepositoryService", "Loading repository settings");
        var settings = await _settingsService.GetAsync(SettingsKey, new RepositorySettingsCollection());

        // Migration: if no projects defined but legacy Repositories exist, migrate them
        if (settings.Projects.Count == 0 && settings.Repositories.Count > 0)
        {
            Info("RepositoryService", $"Migrating {settings.Repositories.Count} legacy repositories to Default project");
            var defaultProject = new ProjectProfile
            {
                Name = "Default",
                Repositories = settings.Repositories.ToList()
            };
            settings.Projects.Add(defaultProject);
            settings.ActiveProjectId = defaultProject.Id;
            settings.Repositories.Clear();
            await _settingsService.SetAsync(SettingsKey, settings);
        }

        // If no projects at all, create a Default empty project
        if (settings.Projects.Count == 0)
        {
            var defaultProject = new ProjectProfile { Name = "Default" };
            settings.Projects.Add(defaultProject);
            settings.ActiveProjectId = defaultProject.Id;
        }

        // Store all projects
        lock (_lock)
        {
            _projects.Clear();
            _projects.AddRange(settings.Projects);
        }

        // Determine the active project
        var activeId = projectId ?? settings.ActiveProjectId ?? settings.Projects.First().Id;
        var activeProject = settings.Projects.FirstOrDefault(p => p.Id == activeId)
                            ?? settings.Projects.First();
        _activeProjectId = activeProject.Id;

        Info("RepositoryService", $"Loading project '{activeProject.Name}' with {activeProject.Repositories.Count} repositories");

        foreach (var entry in activeProject.Repositories)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (!entry.AutoLoad)
            {
                Debug("RepositoryService", $"Skipping repository (AutoLoad=false): {entry.Name}");
                continue;
            }

            // Check if local path still exists
            if (!Directory.Exists(entry.LocalPath))
            {
                Warn("RepositoryService", $"Repository path no longer exists, skipping: {entry.LocalPath}");
                continue;
            }

            try
            {
                // Don't start monitoring during initial load - we'll start it after startup completes
                var result = await AddRepositoryAsync(
                    entry.LocalPath,
                    null,
                    entry.Name,
                    startMonitoring: false,
                    cancellationToken);

                if (result.Success && result.Repository != null)
                {
                    // Restore the original ID to maintain consistency
                    lock (_lock)
                    {
                        result.Repository.Id = entry.Id;
                    }
                }
                else if (!result.Success)
                {
                    Warn("RepositoryService", $"Failed to add repository {entry.Name}: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                Error("RepositoryService", $"Exception while loading repository {entry.Name}", ex);
            }
        }

        // Load libraries for all repositories in parallel
        var tasks = Repositories.Select(repo =>
            Task.Run(() => LoadLibrariesAsync(repo.Id, null, cancellationToken), cancellationToken)
        ).ToList();

        await Task.WhenAll(tasks);

        LogProcessEnd("RepositoryService", "Loading repository settings");
    }

    // ========== Project Profile Management ==========

    public IReadOnlyList<ProjectProfile> GetProjects()
    {
        lock (_lock)
        {
            return _projects.ToList().AsReadOnly();
        }
    }

    public ProjectProfile? GetActiveProject()
    {
        lock (_lock)
        {
            return _projects.FirstOrDefault(p => p.Id == _activeProjectId);
        }
    }

    public ProjectProfile CreateProject(string name)
    {
        var project = new ProjectProfile { Name = name };
        lock (_lock)
        {
            _projects.Add(project);
        }
        _ = SaveRepositorySettingsAsync();
        return project;
    }

    public void RenameProject(string projectId, string newName)
    {
        lock (_lock)
        {
            var project = _projects.FirstOrDefault(p => p.Id == projectId);
            if (project != null)
                project.Name = newName;
        }
        _ = SaveRepositorySettingsAsync();
    }

    public bool DeleteProject(string projectId)
    {
        lock (_lock)
        {
            if (_projects.Count <= 1)
                return false;

            var project = _projects.FirstOrDefault(p => p.Id == projectId);
            if (project == null)
                return false;

            _projects.Remove(project);
        }
        _ = SaveRepositorySettingsAsync();
        return true;
    }

    public async Task SwitchProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        LogProcessStart("RepositoryService", $"Switching to project: {projectId}");

        // Save current project's state before switching
        await SaveRepositorySettingsAsync();

        // Unload all current repositories and clear the graph completely
        // Using ClearAllLibraries instead of per-library RemoveLibrary to ensure
        // FileNodes are also removed from the graph (RemoveLibrary only removes ModelNodes,
        // leaving orphan FileNodes that would cause SaveAllLibrariesWithFormattingAsync
        // to delete .mo files from the old project's directories)
        lock (_lock)
        {
            _repositories.Clear();
        }

        _fileMonitoringService.StopAllMonitoring();
        _libraryDataService.ClearAllLibraries();

        OnRepositoriesChanged?.Invoke();

        // Set active project
        _activeProjectId = projectId;
        var project = _projects.FirstOrDefault(p => p.Id == projectId);
        if (project == null)
        {
            LogProcessEnd("RepositoryService", "Switching project - project not found");
            return;
        }

        Info("RepositoryService", $"Loading project '{project.Name}' with {project.Repositories.Count} repositories");

        // Load the new project's repos
        foreach (var entry in project.Repositories)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (!entry.AutoLoad || !Directory.Exists(entry.LocalPath))
                continue;

            try
            {
                var result = await AddRepositoryAsync(
                    entry.LocalPath,
                    null,
                    entry.Name,
                    startMonitoring: false,
                    cancellationToken);

                if (result.Success && result.Repository != null)
                {
                    lock (_lock)
                    {
                        result.Repository.Id = entry.Id;
                    }
                }
            }
            catch (Exception ex)
            {
                Error("RepositoryService", $"Exception while loading repository {entry.Name}", ex);
            }
        }

        // Load libraries for all repositories in parallel
        var tasks = Repositories.Select(repo =>
            Task.Run(() => LoadLibrariesAsync(repo.Id, null, cancellationToken), cancellationToken)
        ).ToList();

        await Task.WhenAll(tasks);

        // Save with new active project
        await SaveRepositorySettingsAsync();

        OnProjectChanged?.Invoke(projectId);
        LogProcessEnd("RepositoryService", $"Switching to project: {projectId}");
    }

    public void StartMonitoringAllRepositories()
    {
        lock (_lock)
        {
            foreach (var repository in _repositories)
            {
                _fileMonitoringService.StartMonitoring(repository.Id, repository.VcsRootPath);
            }
        }
        Info("RepositoryService", $"Started file monitoring for {_repositories.Count} repositories");
    }

    public void ClearAllRepositories()
    {
        List<Repository> repositoriesToClear;
        lock (_lock)
        {
            repositoriesToClear = _repositories.ToList();
            _repositories.Clear();
        }

        // Stop all file monitoring
        _fileMonitoringService.StopAllMonitoring();

        // Unload all libraries from cleared repositories
        foreach (var repo in repositoriesToClear)
        {
            foreach (var libraryId in repo.LibraryIds)
            {
                _libraryDataService.RemoveLibrary(libraryId);
            }
        }

        OnRepositoriesChanged?.Invoke();

        // Save settings asynchronously
        _ = SaveRepositorySettingsAsync();
    }

    private async Task UpdateRevisionInfoAsync(Repository repository)
    {
        await Task.Run(() =>
        {
            IRevisionControlSystem? vcs = repository.VcsType switch
            {
                RepositoryVcsType.Git => _git,
                RepositoryVcsType.SVN => _svn,
                _ => null
            };

            if (vcs != null)
            {
                repository.CurrentRevision = vcs.GetCurrentRevision(repository.VcsRootPath);
                repository.CurrentBranch = repository.VcsType == RepositoryVcsType.SVN
                    ? _svn.GetCurrentBranch(repository.VcsRootPath, repository.StyleSettings?.SvnBranchDirectories)
                    : vcs.GetCurrentBranch(repository.VcsRootPath);

                if (repository.CurrentRevision != null)
                {
                    repository.RevisionDescription = vcs.GetRevisionDescription(
                        repository.VcsRootPath,
                        repository.CurrentRevision);
                }
            }
        });
    }

    public List<VcsLogEntry> GetLogEntries(string repositoryId, VcsLogOptions? options = null)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null || repository.VcsType == RepositoryVcsType.Local)
        {
            return new List<VcsLogEntry>();
        }

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        if (repository.VcsType == RepositoryVcsType.SVN)
            return _svn.GetLogEntries(repository.VcsRootPath, options, repository.StyleSettings?.SvnBranchDirectories);

        return vcs.GetLogEntries(repository.VcsRootPath, options);
    }

    public List<VcsChangedFile> GetChangedFiles(string repositoryId, string revision)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null || repository.VcsType == RepositoryVcsType.Local)
        {
            return new List<VcsChangedFile>();
        }

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        return vcs.GetChangedFiles(repository.VcsRootPath, revision);
    }

    public async Task<VcsUpdateResult> UpdateRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
        {
            return new VcsUpdateResult
            {
                Success = false,
                ErrorMessage = "Repository not found."
            };
        }

        if (repository.VcsType == RepositoryVcsType.Local)
        {
            return new VcsUpdateResult
            {
                Success = true,
                HasChanges = false,
                NewRevision = null
            };
        }

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        var result = await Task.Run(() => vcs.UpdateToLatest(repository.VcsRootPath), cancellationToken);

        // Update the repository's revision info if successful
        if (result.Success && result.HasChanges)
        {
            await UpdateRevisionInfoAsync(repository);
            OnRepositoriesChanged?.Invoke();
        }

        return result;
    }

    public List<VcsWorkingCopyFile> GetWorkingCopyChanges(string repositoryId)
    {
        // Check cache first
        lock (_workingCopyCacheLock)
        {
            if (_workingCopyCache.TryGetValue(repositoryId, out var cached))
            {
                var age = Environment.TickCount64 - cached.Ticks;
                if (age < WorkingCopyCacheLifetimeMs)
                    return cached.Changes;
            }
        }

        var repository = GetRepository(repositoryId);
        if (repository == null || repository.VcsType == RepositoryVcsType.Local)
        {
            return new List<VcsWorkingCopyFile>();
        }

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        var changes = vcs.GetWorkingCopyChanges(repository.VcsRootPath);

        lock (_workingCopyCacheLock)
        {
            _workingCopyCache[repositoryId] = (changes, Environment.TickCount64);
        }

        return changes;
    }

    /// <inheritdoc/>
    public void InvalidateWorkingCopyCache(string? repositoryId = null)
    {
        lock (_workingCopyCacheLock)
        {
            if (repositoryId != null)
                _workingCopyCache.Remove(repositoryId);
            else
                _workingCopyCache.Clear();
        }
    }

    public List<VcsBranchInfo> GetBranches(string repositoryId, bool includeRemote = false)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null || repository.VcsType == RepositoryVcsType.Local)
        {
            return new List<VcsBranchInfo>();
        }

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        if (repository.VcsType == RepositoryVcsType.SVN)
            return _svn.GetBranches(repository.VcsRootPath, includeRemote, repository.StyleSettings?.SvnBranchDirectories);

        return vcs.GetBranches(repository.VcsRootPath, includeRemote);
    }

    public async Task<VcsCommitResult> CommitAsync(string repositoryId, string message, IEnumerable<string>? filesToCommit = null, IProgress<string>? progress = null)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
        {
            return new VcsCommitResult
            {
                Success = false,
                ErrorMessage = "Repository not found."
            };
        }

        if (repository.VcsType == RepositoryVcsType.Local)
        {
            return new VcsCommitResult
            {
                Success = false,
                ErrorMessage = "Cannot commit to a local directory without version control."
            };
        }

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        var result = await Task.Run(() => vcs.Commit(repository.VcsRootPath, message, filesToCommit, progress));

        // Update the repository's revision info if successful
        if (result.Success)
        {
            await UpdateRevisionInfoAsync(repository);
            OnRepositoriesChanged?.Invoke();
        }

        return result;
    }

    public async Task<VcsOperationResult> RevertFilesAsync(string repositoryId, IEnumerable<string> filesToRevert)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
        {
            return new VcsOperationResult
            {
                Success = false,
                ErrorMessage = "Repository not found."
            };
        }

        if (repository.VcsType == RepositoryVcsType.Local)
        {
            return new VcsOperationResult
            {
                Success = false,
                ErrorMessage = "Cannot revert files in a local directory without version control."
            };
        }

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        var result = await Task.Run(() => vcs.RevertFiles(repository.VcsRootPath, filesToRevert));

        // Update revision info only — the caller (LibraryBrowser) reloads only the specific
        // reverted .mo files via LibraryDataService.ReloadFileAsync, which is far cheaper than
        // RefreshRepositoryAsync (which reloads every library from scratch).
        // Pending file monitor changes are intentionally left intact so OnVcsFilesChanged can
        // do targeted formatting and analysis instead of falling back to re-analysing everything.
        if (result.Success)
        {
            await UpdateRevisionInfoAsync(repository);
            OnRepositoriesChanged?.Invoke();
        }

        return result;
    }

    public async Task<VcsOperationResult> SwitchBranchAsync(string repositoryId, string branchName)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
        {
            return new VcsOperationResult
            {
                Success = false,
                ErrorMessage = "Repository not found."
            };
        }

        if (repository.VcsType == RepositoryVcsType.Local)
        {
            return new VcsOperationResult
            {
                Success = false,
                ErrorMessage = "Cannot switch branches in a local directory without version control."
            };
        }

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        var result = await Task.Run(() => vcs.SwitchBranch(repository.VcsRootPath, branchName));

        // Refresh all repositories that share this VCS root (they all switched branches)
        if (result.Success)
        {
            foreach (var repo in GetRepositoriesWithVcsRoot(repository.VcsRootPath))
            {
                await UpdateRevisionInfoAsync(repo);
                await RefreshRepositoryAsync(repo.Id);
            }
            OnRepositoriesChanged?.Invoke();
        }

        return result;
    }

    public async Task<VcsOperationResult> CreateBranchAsync(string repositoryId, string branchName, bool switchToBranch = true)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
        {
            return new VcsOperationResult
            {
                Success = false,
                ErrorMessage = "Repository not found."
            };
        }

        if (repository.VcsType == RepositoryVcsType.Local)
        {
            return new VcsOperationResult
            {
                Success = false,
                ErrorMessage = "Cannot create branches in a local directory without version control."
            };
        }

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        var result = repository.VcsType == RepositoryVcsType.SVN
            ? await Task.Run(() => _svn.CreateBranch(repository.VcsRootPath, branchName, switchToBranch, repository.StyleSettings?.SvnBranchDirectories))
            : await Task.Run(() => vcs.CreateBranch(repository.VcsRootPath, branchName, switchToBranch));

        // Update the repository's revision info if successful
        if (result.Success && switchToBranch)
        {
            await UpdateRevisionInfoAsync(repository);
            OnRepositoriesChanged?.Invoke();
        }

        return result;
    }

    public string? GetFileContentAtRevision(string repositoryId, string filePath, string? revision = null)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
        {
            return null;
        }

        if (repository.VcsType == RepositoryVcsType.Local)
        {
            // For local directories, just read the current file
            var fullPath = Path.Combine(repository.LocalPath, filePath);
            return File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
        }

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        return vcs.GetFileContentAtRevision(repository.VcsRootPath, filePath, revision);
    }

    public async Task<VcsOperationResult> CheckoutRevisionAsync(string repositoryId, string revision, CancellationToken cancellationToken = default)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
        {
            return new VcsOperationResult { Success = false, ErrorMessage = "Repository not found." };
        }

        if (repository.VcsType == RepositoryVcsType.Local)
        {
            return new VcsOperationResult { Success = false, ErrorMessage = "Cannot checkout a revision in a local directory without version control." };
        }

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        var success = await Task.Run(() => vcs.CheckoutRevision(repository.VcsRootPath, revision, repository.VcsRootPath), cancellationToken);

        if (success)
        {
            // Refresh all repositories that share this VCS root
            foreach (var repo in GetRepositoriesWithVcsRoot(repository.VcsRootPath))
                await UpdateRevisionInfoAsync(repo);
            OnRepositoriesChanged?.Invoke();
            return new VcsOperationResult { Success = true };
        }

        return new VcsOperationResult { Success = false, ErrorMessage = $"Failed to checkout revision {revision}." };
    }

    public async Task<VcsMergeResult> MergeBranchAsync(string repositoryId, string sourceBranch)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
        {
            return new VcsMergeResult
            {
                Success = false,
                ErrorMessage = "Repository not found."
            };
        }

        if (repository.VcsType == RepositoryVcsType.Local)
        {
            return new VcsMergeResult
            {
                Success = false,
                ErrorMessage = "Cannot merge branches in a local directory without version control."
            };
        }

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        var result = await Task.Run(() => vcs.MergeBranch(repository.VcsRootPath, sourceBranch));

        // Refresh all repositories that share this VCS root (merge affects the full working copy)
        if (result.Success || result.HasConflicts)
        {
            foreach (var repo in GetRepositoriesWithVcsRoot(repository.VcsRootPath))
                await UpdateRevisionInfoAsync(repo);
            OnRepositoriesChanged?.Invoke();
        }

        return result;
    }

    public async Task<VcsOperationResult> CleanWorkspaceAsync(string repositoryId)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
            return new VcsOperationResult { Success = false, ErrorMessage = "Repository not found." };

        if (repository.VcsType == RepositoryVcsType.Local)
            return new VcsOperationResult { Success = false, ErrorMessage = "Clean workspace is not supported for local directories." };

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        var success = await Task.Run(() => vcs.CleanWorkspace(repository.VcsRootPath));
        return new VcsOperationResult
        {
            Success = success,
            ErrorMessage = success ? null : "Failed to clean working copy."
        };
    }

    public async Task<VcsOperationResult> ResolveConflictAsync(string repositoryId, string filePath, ConflictResolutionChoice choice)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
            return new VcsOperationResult { Success = false, ErrorMessage = "Repository not found." };

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        return await Task.Run(() => vcs.ResolveConflict(repository.VcsRootPath, filePath, choice));
    }

    public async Task<VcsOperationResult> PushAsync(string repositoryId)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
            return new VcsOperationResult { Success = false, ErrorMessage = "Repository not found." };

        if (repository.VcsType == RepositoryVcsType.Local)
            return new VcsOperationResult { Success = false, ErrorMessage = "Push is not supported for local directories." };

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        var result = await Task.Run(() => vcs.Push(repository.VcsRootPath, repository.CurrentBranch));
        if (result.Success)
            await UpdateRevisionInfoAsync(repository);
        return result;
    }

    public async Task<(string? ours, string? theirs)> GetConflictVersionsAsync(string repositoryId, string filePath)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
            return (null, null);

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        return await Task.Run(() => vcs.GetConflictVersions(repository.VcsRootPath, filePath));
    }

    public async Task<VcsMergeResult> RebaseAsync(string repositoryId, string targetBranch)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
            return new VcsMergeResult { ErrorMessage = "Repository not found." };

        if (repository.VcsType != RepositoryVcsType.Git)
            return new VcsMergeResult { ErrorMessage = "Rebase is only supported for Git repositories." };

        return await Task.Run(() => _git.Rebase(repository.VcsRootPath, targetBranch));
    }

    public async Task<VcsMergeResult> ContinueRebaseAsync(string repositoryId)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
            return new VcsMergeResult { ErrorMessage = "Repository not found." };

        return await Task.Run(() => _git.ContinueRebase(repository.VcsRootPath));
    }

    public async Task<VcsOperationResult> AbortRebaseAsync(string repositoryId)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
            return new VcsOperationResult { ErrorMessage = "Repository not found." };

        var result = await Task.Run(() => _git.AbortRebase(repository.VcsRootPath));
        if (result.Success)
        {
            foreach (var repo in GetRepositoriesWithVcsRoot(repository.VcsRootPath))
                await UpdateRevisionInfoAsync(repo);
        }
        return result;
    }

    public async Task<VcsOperationResult> ForcePushAsync(string repositoryId)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null)
            return new VcsOperationResult { ErrorMessage = "Repository not found." };

        if (repository.VcsType == RepositoryVcsType.Local)
            return new VcsOperationResult { ErrorMessage = "Push is not supported for local directories." };

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        var result = await Task.Run(() => vcs.ForcePush(repository.VcsRootPath, repository.CurrentBranch));
        if (result.Success)
            await UpdateRevisionInfoAsync(repository);
        return result;
    }

    public async Task<bool> IsBranchPushedAsync(string repositoryId)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null || repository.VcsType == RepositoryVcsType.Local)
            return false;

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        return await Task.Run(() => vcs.IsBranchPushed(repository.VcsRootPath));
    }

    public async Task<string?> GetPullRequestUrlAsync(string repositoryId, string? baseBranch = null)
    {
        var repository = GetRepository(repositoryId);
        if (repository == null || repository.VcsType == RepositoryVcsType.Local)
            return null;

        IRevisionControlSystem vcs = repository.VcsType switch
        {
            RepositoryVcsType.Git => _git,
            RepositoryVcsType.SVN => _svn,
            _ => throw new InvalidOperationException("Unsupported VCS type")
        };

        return await Task.Run(() => vcs.GetPullRequestUrl(repository.VcsRootPath, baseBranch));
    }
}
