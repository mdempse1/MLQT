using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Session and library management tools: create a new library, load repositories and libraries into the
/// in-memory graph, enumerate what is loaded, reload from disk, and unload. Almost every other tool
/// requires a library to be loaded first, so these are the entry point.
/// </summary>
[McpServerToolType]
public sealed class SessionTools
{
    private static readonly Regex IdentifierRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private readonly ILibraryDataService _libraries;
    private readonly IRepositoryService _repositories;
    private readonly IExternalResourceService _resources;
    private readonly SessionState _session;

    public SessionTools(
        ILibraryDataService libraries, IRepositoryService repositories,
        IExternalResourceService resources, SessionState session)
    {
        _libraries = libraries;
        _repositories = repositories;
        _resources = resources;
        _session = session;
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

    [McpServerTool(Name = "create_library")]
    [Description("Create a brand-new, empty top-level Modelica library on disk — the first step of a new " +
                "project. Writes a directory named after the library, containing a package.mo (a top-level " +
                "'package Name ... end Name;') and an empty package.order, then loads it so you can add " +
                "classes with create_class. Provide the library name (a Modelica identifier) and the " +
                "directory to create it in (the library folder is created inside it, and missing parent " +
                "folders are created). Optionally a description and a version. Fails if the target folder " +
                "already exists. Set preview=true to see the package.mo without writing anything.")]
    public async Task<object> CreateLibrary(
        [Description("The library (top-level package) name — a valid Modelica identifier, e.g. 'MyLibrary'.")]
        string name,
        [Description("Absolute path to the folder to create the library in; a subfolder named after the " +
                     "library is created inside it (parent folders are created if missing).")]
        string directory,
        [Description("Optional one-line description for the library.")] string? description = null,
        [Description("Optional version string for the package's version annotation, e.g. '1.0.0'.")]
        string? version = null,
        [Description("Load the new library into the session after creating it (default true).")]
        bool loadIntoSession = true,
        [Description("Return the package.mo that would be written without creating anything. Default false.")]
        bool preview = false)
    {
        if (string.IsNullOrWhiteSpace(name) || !IdentifierRegex.IsMatch(name))
            return new ToolError($"name '{name}' is not a valid Modelica identifier (letters/digits/_, not starting with a digit).");
        if (string.IsNullOrWhiteSpace(directory))
            return new ToolError("directory is required (an absolute path to create the library folder in).");

        var libraryDir = Path.Combine(directory, name);
        if (Directory.Exists(libraryDir))
            return new ToolError($"A folder already exists at '{libraryDir}'. Choose a different name or location.");

        var desc = string.IsNullOrEmpty(description) ? string.Empty : $" \"{description.Replace("\"", "\\\"")}\"";
        var annotation = string.IsNullOrEmpty(version) ? string.Empty : $"  annotation (version=\"{version}\");\n";
        var packageContent = $"within;\npackage {name}{desc}\n{annotation}end {name};\n";

        if (preview)
            return new CreateLibraryResult(name, libraryDir, null, Loaded: false, PreviewOnly: true, packageContent);

        try
        {
            Directory.CreateDirectory(libraryDir);
        }
        catch (Exception ex)
        {
            return new ToolError($"Could not create the library folder '{libraryDir}': {ex.Message}.");
        }

        await File.WriteAllTextAsync(Path.Combine(libraryDir, "package.mo"), packageContent);
        await File.WriteAllTextAsync(Path.Combine(libraryDir, "package.order"), string.Empty);

        if (!loadIntoSession)
            return new CreateLibraryResult(name, libraryDir, null, Loaded: false, PreviewOnly: false, null);

        var library = await _libraries.AddLibraryFromDirectoryAsync(libraryDir);
        return new CreateLibraryResult(name, libraryDir, library.Id, Loaded: true, PreviewOnly: false, null);
    }

    [McpServerTool(Name = "load_library")]
    [Description("Load a single Modelica library directly (not via a repository): pass either the library " +
                "directory (containing package.mo), the path to its package.mo, or a single standalone .mo " +
                "file. Pointing at a package.mo loads the WHOLE library (its directory, including standalone " +
                "child .mo files), not just that one file. Also accepts an ENCRYPTED library (a directory " +
                "holding a package.moe): its classes are recovered from the vendor's generated help " +
                "documentation, giving names, descriptions, base classes and whether each has an icon — " +
                "enough for references into it to resolve — but no source, so it is read-only and is never " +
                "reported on. Returns the loaded library summary. For a " +
                "repository root containing several libraries, use load_repository instead.")]
    public async Task<object> LoadLibrary(
        [Description("Absolute path to a library directory, its package.mo, or a single standalone .mo file.")]
        string path)
    {
        LoadedLibrary library;
        if (EncryptedLibraryDetector.IsEncryptedLibraryRoot(path))
        {
            // An encrypted library has no readable source; its classes come from the vendor's
            // generated documentation and exist only so references into them resolve.
            library = await _libraries.AddEncryptedLibraryFromDirectoryAsync(path);
            if (library.ModelIds.Count == 0)
            {
                _libraries.RemoveLibrary(library.Id);
                return new ToolError(
                    $"'{library.Name}' at '{path}' is an encrypted library that ships no usable " +
                    "documentation, so none of its classes could be recovered. References into it will " +
                    "stay unresolved; there is nothing to load.");
            }

            return ToSummary(library);
        }

        if (File.Exists(path) && string.Equals(Path.GetFileName(path), "package.mo", StringComparison.OrdinalIgnoreCase))
        {
            // A package.mo IS the root of a directory package: load the whole library (its directory), not
            // just that one file — otherwise standalone child .mo files are missed. Callers routinely point
            // load_library at ".../MyLib/package.mo" meaning "load MyLib".
            library = await _libraries.AddLibraryFromDirectoryAsync(Path.GetDirectoryName(path)!);
        }
        else if (File.Exists(path) && path.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
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
                "them. Returns each library's name and path. Identify the repository by its id (the " +
                "GUID from load_repository / list_repositories) or its name — not a filesystem path.")]
    public async Task<object> DiscoverLibraries(
        [Description("The repository's id (GUID from load_repository / list_repositories) or its name. " +
                     "Not a filesystem path.")]
        string repositoryId)
    {
        var (repo, error) = EntityResolver.ResolveRepository(_repositories, repositoryId);
        if (error is not null)
            return error;

        var discovered = await _repositories.DiscoverLibrariesAsync(repo!.Id);
        return discovered
            .Select(d => new DiscoveredLibrarySummary(d.LibraryName, d.RelativePath, d.FullPath))
            .ToList();
    }

    [McpServerTool(Name = "unload_library")]
    [Description("Unload a loaded library, removing its models from the in-memory graph. Identify the " +
                "library by EITHER its id (the opaque GUID in the 'id' field from list_libraries) OR its " +
                "name (the 'name' field, e.g. 'Modelica'). The name is usually the convenient choice.")]
    public object UnloadLibrary(
        [Description("The library to unload: its id (GUID from list_libraries's 'id' field) or its name " +
                     "(e.g. 'Modelica'). Not a class id.")]
        string library)
    {
        var (match, error) = EntityResolver.ResolveLibrary(_libraries, library);
        if (error is not null)
            return error;

        _libraries.RemoveLibrary(match!.Id);
        return new { success = true, unloadedLibraryId = match.Id, name = match.Name };
    }

    [McpServerTool(Name = "reload")]
    [Description("Re-read Modelica source from disk into the in-memory graph, for when files have changed " +
                "outside this server (a manual edit, a VCS pull/checkout, or another tool). With no target, " +
                "reloads everything currently loaded. With a target, reload just that: a .mo file path, or a " +
                "library/repository by id or name. Reloading a library or repository (or everything) resets " +
                "opt-in analysis, so re-run analyze_dependencies afterwards; reloading a single file keeps " +
                "the dependency graph current incrementally.")]
    public async Task<object> Reload(
        [Description("Optional: a .mo file path, or a library/repository id or name. Omit to reload everything.")]
        string? target = null)
    {
        // Single file.
        if (!string.IsNullOrWhiteSpace(target) &&
            target.EndsWith(".mo", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
        {
            var affected = await _libraries.ReloadFileAsync(target);
            await GraphRefresh.RefreshAfterEditAsync(affected, _libraries, _resources, _session);
            return new ReloadResult("file", new[] { Path.GetFileName(target) }, affected.Count, null);
        }

        if (!string.IsNullOrWhiteSpace(target))
        {
            var (repo, _) = EntityResolver.ResolveRepository(_repositories, target);
            if (repo is not null)
            {
                await _repositories.RefreshRepositoryAsync(repo.Id);
                ResetAnalysis();
                var libs = _libraries.Libraries.Where(l => l.RepositoryId == repo.Id).Select(l => l.Name).ToList();
                return new ReloadResult("repository", libs, 0, AnalysisResetNote);
            }

            var (lib, _) = EntityResolver.ResolveLibrary(_libraries, target);
            if (lib is not null)
            {
                await ReloadLibraryAsync(lib);
                ResetAnalysis();
                return new ReloadResult("library", new[] { lib.Name }, 0, AnalysisResetNote);
            }

            return new ToolError(
                $"'{target}' is not a loaded .mo file, library or repository. Pass a .mo file path, a library " +
                "or repository name/id, or omit the argument to reload everything.");
        }

        // Reload everything: repositories first, then directly-loaded libraries.
        var reloaded = new List<string>();
        foreach (var repoId in _repositories.Repositories.Select(r => r.Id).ToList())
        {
            await _repositories.RefreshRepositoryAsync(repoId);
            reloaded.AddRange(_libraries.Libraries.Where(l => l.RepositoryId == repoId).Select(l => l.Name));
        }
        foreach (var lib in _libraries.Libraries.Where(l => l.RepositoryId is null).ToList())
        {
            reloaded.Add(lib.Name);
            await ReloadLibraryAsync(lib);
        }
        ResetAnalysis();
        return new ReloadResult("all", reloaded.Distinct().ToList(), 0, AnalysisResetNote);
    }

    private async Task ReloadLibraryAsync(LoadedLibrary library)
    {
        // Repository-backed libraries are refreshed through the repository (re-discovers add/removed files).
        if (library.RepositoryId is { } repoId && _repositories.GetRepository(repoId) is not null)
        {
            await _repositories.RefreshRepositoryAsync(repoId);
            return;
        }

        // Directly-loaded library: rebuild it from its source path (handles added/removed/edited files).
        var path = library.SourcePath;
        _libraries.RemoveLibrary(library.Id);
        if (library.SourceType == LibrarySourceType.File)
            await _libraries.AddLibraryFromFileAsync(path);
        else if (library.SourceType == LibrarySourceType.EncryptedDirectory)
            await _libraries.AddEncryptedLibraryFromDirectoryAsync(path);
        else
            await _libraries.AddLibraryFromDirectoryAsync(path);
    }

    private void ResetAnalysis()
    {
        _session.DependenciesAnalyzed = false;
        _session.ResourcesAnalyzed = false;
    }

    private const string AnalysisResetNote =
        "Opt-in analysis was reset by the reload — run analyze_dependencies again before using dependency, " +
        "impact or external-resource tools.";

    private LibrarySummary ToSummary(LoadedLibrary l) => new(
        l.Id, l.Name, l.SourceType.ToString(), l.SourcePath,
        l.ModelIds.Count, l.TopLevelModelIds.Count, l.RepositoryId, l.Revision,
        DeclaredDependencies(l));

    // The library's declared dependencies (its top-level package's `uses(...)` annotation), so the caller
    // knows what else to load — most importantly the Modelica Standard Library, at the right version.
    private IReadOnlyList<LibraryDependency> DeclaredDependencies(LoadedLibrary l)
    {
        var deps = new List<LibraryDependency>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var topId in l.TopLevelModelIds)
        {
            if (_libraries.GetModelById(topId)?.Uses is not { } uses)
                continue;
            foreach (var (name, version) in uses)
                if (seen.Add(name))
                    deps.Add(new LibraryDependency(name, string.IsNullOrEmpty(version) ? null : version));
        }
        return deps;
    }
}
