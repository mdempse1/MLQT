using System.ComponentModel;
using ModelContextProtocol.Server;
using MLQT.McpServer.Dtos;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Session and library management tools: load repositories and libraries into the in-memory
/// graph, enumerate what is loaded, and unload. Almost every other tool requires a library to be
/// loaded first, so these are the entry point.
/// </summary>
[McpServerToolType]
public sealed class SessionTools
{
    private readonly ILibraryDataService _libraries;
    private readonly IRepositoryService _repositories;

    public SessionTools(ILibraryDataService libraries, IRepositoryService repositories)
    {
        _libraries = libraries;
        _repositories = repositories;
    }

    [McpServerTool(Name = "load_repository")]
    [Description("Add a version-controlled repository (Git or SVN working copy) or a plain directory " +
                "that contains one or more Modelica libraries, then load the discovered libraries into " +
                "memory. Auto-detects the VCS type and discovers libraries (each is a directory with a " +
                "package.mo). Use this for a checked-out repository root such as the Modelica Standard " +
                "Library. For a single library folder or .mo file, use load_library instead. Read-only: " +
                "this never modifies the repository.")]
    public async Task<LoadRepositoryResult> LoadRepository(
        [Description("Absolute path to the local repository working copy or directory.")] string path,
        [Description("Optional display name; derived from the path if omitted.")] string? name = null,
        [Description("Load all discovered libraries into memory (default true). Set false to only " +
                     "discover them without loading.")] bool loadLibraries = true)
    {
        var result = await _repositories.AddRepositoryAsync(
            path, checkoutPath: null, name: name, startMonitoring: false);

        if (!result.Success || result.Repository is null)
        {
            return new LoadRepositoryResult(
                false, null, null, null,
                result.ErrorMessage ?? "Failed to add repository.",
                [], [], result.Warnings);
        }

        var repo = result.Repository;
        var discovered = result.DiscoveredLibraries
            .Select(d => new DiscoveredLibrarySummary(d.LibraryName, d.RelativePath, d.FullPath))
            .ToList();

        var warnings = result.Warnings.ToList();
        if (discovered.Count == 0)
        {
            warnings.Add(
                $"No Modelica libraries (directories containing package.mo) were found under '{path}'. " +
                "If the library lives in a subfolder, pass that subfolder to load_repository or load_library.");
        }

        var loaded = new List<LibrarySummary>();
        if (loadLibraries)
        {
            await _repositories.LoadLibrariesAsync(repo.Id);
            loaded = _libraries.Libraries
                .Where(l => l.RepositoryId == repo.Id)
                .Select(ToSummary)
                .ToList();
        }

        return new LoadRepositoryResult(
            true, repo.Id, repo.Name, repo.VcsType.ToString(), null,
            discovered, loaded, warnings);
    }

    [McpServerTool(Name = "load_library")]
    [Description("Load a single Modelica library directly (not via a repository): either a directory " +
                "containing a package.mo file, or a single .mo file. Returns the loaded library summary. " +
                "For a repository root containing several libraries, use load_repository instead.")]
    public async Task<object> LoadLibrary(
        [Description("Absolute path to a library directory (with package.mo) or a single .mo file.")]
        string path)
    {
        LoadedLibrary library;
        if (File.Exists(path) && path.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
        {
            library = await _libraries.AddLibraryFromFileAsync(path);
        }
        else if (Directory.Exists(path))
        {
            library = await _libraries.AddLibraryFromDirectoryAsync(path);
        }
        else
        {
            return new ToolError(
                $"Path not found, or not a .mo file / directory: '{path}'. Pass an absolute path to a " +
                "library directory containing a package.mo file, or to a single .mo file.");
        }

        // A directory with no package.mo (or an empty one) loads nothing — tell the caller how to fix it
        // rather than returning an empty library that looks loaded.
        if (library.ModelIds.Count == 0)
        {
            _libraries.RemoveLibrary(library.Id);
            return new ToolError(
                $"No Modelica models were found at '{path}'. Point load_library at a library directory " +
                "containing a package.mo file, or a single .mo file. For a repository whose libraries live " +
                "in subfolders, use load_repository instead.");
        }

        return ToSummary(library);
    }

    [McpServerTool(Name = "list_libraries")]
    [Description("List all Modelica libraries currently loaded in memory, with their model counts. " +
                "Returns an empty list if nothing is loaded yet.")]
    public IReadOnlyList<LibrarySummary> ListLibraries()
        => _libraries.Libraries.Select(ToSummary).ToList();

    [McpServerTool(Name = "list_repositories")]
    [Description("List all repositories that have been added this session, with their VCS type, " +
                "current branch and revision.")]
    public IReadOnlyList<RepositorySummary> ListRepositories()
        => _repositories.Repositories.Select(r => new RepositorySummary(
            r.Id, r.Name, r.VcsType.ToString(), r.LocalPath, r.VcsRootPath,
            r.CurrentBranch, r.CurrentRevision, r.IsLoaded, r.LibraryIds.Count)).ToList();

    [McpServerTool(Name = "discover_libraries")]
    [Description("Discover the Modelica libraries within an already-added repository without loading " +
                "them. Returns each library's name and path. Pass a repositoryId from load_repository " +
                "or list_repositories.")]
    public async Task<object> DiscoverLibraries(
        [Description("The repository id (from load_repository or list_repositories).")] string repositoryId)
    {
        if (_repositories.GetRepository(repositoryId) is null)
            return new ToolError($"No repository with id '{repositoryId}'. Call list_repositories.");

        var discovered = await _repositories.DiscoverLibrariesAsync(repositoryId);
        return discovered
            .Select(d => new DiscoveredLibrarySummary(d.LibraryName, d.RelativePath, d.FullPath))
            .ToList();
    }

    [McpServerTool(Name = "unload_library")]
    [Description("Unload a previously loaded library, removing its models from the in-memory graph. " +
                "Pass a libraryId from list_libraries.")]
    public object UnloadLibrary(
        [Description("The library id (from list_libraries).")] string libraryId)
    {
        if (_libraries.Libraries.Count == 0)
            return new ToolError("No libraries are loaded. Nothing to unload.");
        if (_libraries.Libraries.All(l => l.Id != libraryId))
            return new ToolError($"No loaded library with id '{libraryId}'. Call list_libraries to see loaded ids.");

        _libraries.RemoveLibrary(libraryId);
        return new { success = true, unloadedLibraryId = libraryId };
    }

    private static LibrarySummary ToSummary(LoadedLibrary l) => new(
        l.Id, l.Name, l.SourceType.ToString(), l.SourcePath,
        l.ModelIds.Count, l.TopLevelModelIds.Count, l.RepositoryId, l.Revision);
}
