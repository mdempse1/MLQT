using System.Reflection;
using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services;
using ModelicaParser.Helpers;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using ModelicaGraph;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Session and library management tools: create a new library, load repositories and libraries into the
/// in-memory graph, enumerate what is loaded, reload from disk, and unload. Almost every other tool
/// requires a library to be loaded first, so these are the entry point.
/// </summary>
[Obfuscation]
[McpServerToolType]
public sealed class SessionTools
{
    private static readonly Regex IdentifierRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private readonly ILibraryDataService _libraries;
    private readonly IRepositoryService _repositories;
    private readonly IExternalResourceService _resources;
    private readonly SessionState _session;
    private readonly ICodeReviewService? _codeReview;

    public SessionTools(
        ILibraryDataService libraries, IRepositoryService repositories,
        IExternalResourceService resources, SessionState session, ICodeReviewService? codeReview = null)
    {
        _libraries = libraries;
        _repositories = repositories;
        _resources = resources;
        _session = session;
        _codeReview = codeReview;
    }

    internal async Task<LoadRepositoryResult> LoadRepository(
        string path,
        string? name = null,
        bool loadLibraries = true,
        bool detailed = false)
    {
        // The same working copy again - loaded after a discoverOnly look at it, say - is the repository
        // already registered, not a second one: with two, everything that defaults to "the one repository
        // loaded" no longer knows which, and a reset reported each library twice.
        if (_repositories.Repositories.FirstOrDefault(r => SameFolder(r.LocalPath, path)) is { } known)
        {
            var again = (await _repositories.DiscoverLibrariesAsync(known.Id))
                .Select(d => new DiscoveredLibrarySummary(d.LibraryName, d.RelativePath, d.FullPath))
                .ToList();
            return await Answer(known, again, [], path, loadLibraries, detailed);
        }

        var result = await _repositories.AddRepositoryAsync(
            path, checkoutPath: null, name: name, startMonitoring: false);

        if (!result.Success || result.Repository is null)
        {
            return new LoadRepositoryResult(
                false, null, null, null,
                result.ErrorMessage ?? "Failed to add repository.",
                [], [], result.Warnings);
        }

        var discovered = result.DiscoveredLibraries
            .Select(d => new DiscoveredLibrarySummary(d.LibraryName, d.RelativePath, d.FullPath))
            .ToList();
        return await Answer(result.Repository, discovered, result.Warnings, path, loadLibraries, detailed);
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private async Task<LoadRepositoryResult> Answer(Repository repo, List<DiscoveredLibrarySummary> discovered,
        IEnumerable<string> addWarnings, string path, bool loadLibraries, bool detailed)
    {
        var warnings = addWarnings.ToList();
        if (discovered.Count == 0)
        {
            warnings.Add(
                $"No Modelica libraries (directories containing package.mo) were found under '{path}'. " +
                "If the library lives in a subfolder, pass that subfolder to mlqt_load.");
        }

        var loaded = new List<LibrarySummary>();
        if (loadLibraries)
        {
            await _repositories.LoadLibrariesAsync(repo.Id);
            loaded = _libraries.Libraries
                .Where(l => l.RepositoryId == repo.Id)
                .Select(l => ToSummary(l, detailed))
                .ToList();
        }

        // Once everything discovered is loaded, the discovered list repeats the loaded one (B503: one such
        // answer was 31k characters).
        var allLoaded = loadLibraries && discovered.All(d => loaded.Any(l => l.Name == d.LibraryName));
        return new LoadRepositoryResult(
            true, repo.Id, repo.Name, WireNames.Of(repo.VcsType), null,
            detailed || !allLoaded ? discovered : [], loaded, warnings);
    }

    [McpServerTool(Name = "mlqt_create_library", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Create a brand-new, empty top-level Modelica library on disk — the first step of a new " +
                "project. Writes a directory named after the library, containing a package.mo (a top-level " +
                "'package Name ... end Name;') and an empty package.order, then loads it so you can add " +
                "classes with mlqt_write_class. Provide the library name (a Modelica identifier) and the " +
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

        await ModelicaFileEncoding.WriteAllTextAsync(Path.Combine(libraryDir, "package.mo"), packageContent);
        await ModelicaFileEncoding.WriteAllTextAsync(Path.Combine(libraryDir, "package.order"), string.Empty);

        if (!loadIntoSession)
            return new CreateLibraryResult(name, libraryDir, null, Loaded: false, PreviewOnly: false, null);

        var library = await _libraries.AddLibraryFromDirectoryAsync(libraryDir);
        return new CreateLibraryResult(name, libraryDir, library.Id, Loaded: true, PreviewOnly: false, null);
    }

    internal IReadOnlyList<LibrarySummary> ListLibraries(
        bool detailed = false)
        => _libraries.Libraries.Select(l => ToSummary(l, detailed)).ToList();

    internal IReadOnlyList<RepositorySummary> ListRepositories()
        => _repositories.Repositories.Select(r => new RepositorySummary(
            r.Id, r.Name, WireNames.Of(r.VcsType), r.LocalPath, r.VcsRootPath,
            r.CurrentBranch, r.CurrentRevision, r.IsLoaded, r.LibraryIds.Count)).ToList();

    internal object UnloadLibrary(
        string? library = null,
        bool all = false)
    {
        if (all)
            return Reset();
        if (string.IsNullOrWhiteSpace(library))
            return new ToolError("Name the library to unload, or pass all=true to unload everything.");

        var (match, error) = EntityResolver.ResolveLibrary(_libraries, library);
        if (error is not null)
            return error;

        _libraries.RemoveLibrary(match!.Id);
        return new UnloadLibraryResult(true, match.Id, match.Name);
    }

    /// <summary>
    /// Back to an empty session: every repository and library unloaded, the findings and the session's
    /// analysis forgotten. A library the host supplied from memory is kept - only the host can supply it
    /// again - and named. The way to start new work on a server an earlier conversation, or another
    /// task, has used (B505).
    /// </summary>
    internal SessionResetResult Reset()
    {
        var kept = _libraries.Libraries.Where(IsSupplied).Select(l => l.Name).ToList();
        var repositories = _repositories.Repositories.Count;
        var unloaded = _libraries.Libraries.Where(l => !IsSupplied(l)).Select(l => l.Name).ToList();
        _repositories.ClearAllRepositories();
        foreach (var library in _libraries.Libraries.Where(l => !IsSupplied(l)).ToList())
            _libraries.RemoveLibrary(library.Id);
        _codeReview?.ClearLogMessages();
        _session.Reset();
        return new SessionResetResult(unloaded, kept, repositories);
    }

    internal async Task<object> Reload(
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
                if (IsSupplied(lib))
                    return new ToolError(
                        $"'{lib.Name}' was supplied from memory by the host, so there is nothing on disk to re-read; " +
                        "it is still loaded, unchanged. Only the host that supplied it can replace it.");

                await ReloadLibraryAsync(lib);
                ResetAnalysis();
                return new ReloadResult("library", new[] { lib.Name }, 0, AnalysisResetNote);
            }

            return new ToolError(
                $"'{target}' is not a loaded .mo file, library or repository. Pass a .mo file path, a library " +
                "or repository name/id, or omit the argument to reload everything.");
        }

        // Reload everything: repositories first, then directly-loaded libraries. A supplied library is
        // left as it is - see IsSupplied - and named in the result rather than passed over in silence.
        var reloaded = new List<string>();
        var notReloaded = new List<string>();
        foreach (var repoId in _repositories.Repositories.Select(r => r.Id).ToList())
        {
            await _repositories.RefreshRepositoryAsync(repoId);
            reloaded.AddRange(_libraries.Libraries.Where(l => l.RepositoryId == repoId).Select(l => l.Name));
        }
        foreach (var lib in _libraries.Libraries.Where(l => l.RepositoryId is null).ToList())
        {
            if (IsSupplied(lib))
            {
                notReloaded.Add(lib.Name);
                continue;
            }
            reloaded.Add(lib.Name);
            await ReloadLibraryAsync(lib);
        }
        ResetAnalysis();
        var note = notReloaded.Count == 0
            ? AnalysisResetNote
            : AnalysisResetNote + " Not reloaded, because the host supplied them from memory and there is nothing on " +
              $"disk to re-read: {string.Join(", ", notReloaded)}. They are still loaded, unchanged.";
        return new ReloadResult("all", reloaded.Distinct().ToList(), 0, note, notReloaded);
    }

    /// <summary>
    /// A library the host supplied from memory (<see cref="ILibraryDataService.AddLibraryFromSourceAsync"/>).
    /// Its <see cref="LoadedLibrary.SourcePath"/> is an <c>mlqt-readonly://</c> root, which names no files
    /// on disk: reloaded from it, the library was unloaded first and the load from the path then threw,
    /// so the session lost it. Only the host holding its text can supply it again.
    /// </summary>
    private static bool IsSupplied(LoadedLibrary library) =>
        library.ReadOnlySource == ReadOnlySourceKind.Supplied;

    private async Task ReloadLibraryAsync(LoadedLibrary library)
    {
        // Repository-backed libraries are refreshed through the repository (re-discovers add/removed files).
        if (library.RepositoryId is { } repoId && _repositories.GetRepository(repoId) is not null)
        {
            await _repositories.RefreshRepositoryAsync(repoId);
            return;
        }

        // Directly-loaded library: rebuild it from its source path (handles added/removed/edited
        // files). The path is reloaded the same way it was loaded, because that decision is made
        // from the path rather than remembered here.
        var path = library.SourcePath;
        _libraries.RemoveLibrary(library.Id);
        await _libraries.AddLibraryFromPathAsync(path);
    }

    private void ResetAnalysis()
    {
        _session.DependenciesAnalyzed = false;
        _session.ResourcesAnalyzed = false;
    }

    private const string AnalysisResetNote =
        "Opt-in analysis was reset by the reload — run mlqt_dependencies again before using dependency, " +
        "impact or external-resource tools.";

    private LibrarySummary ToSummary(LoadedLibrary l) => ToSummary(l, detailed: true);

    // Concise lists only the dependencies not loaded: those are the ones an agent has to act on.
    private LibrarySummary ToSummary(LoadedLibrary l, bool detailed) => new(
        l.Id, l.Name, WireNames.Of(l.SourceType), l.SourcePath,
        l.ModelIds.Count, l.TopLevelModelIds.Count, l.RepositoryId, l.Revision,
        detailed ? DeclaredDependencies(l) : DeclaredDependencies(l).Where(d => _libraries.Libraries.All(x => x.Name != d.Name)).ToList(),
        l.Version, l.ReadOnlySource is { } readOnlySource ? WireNames.Of(readOnlySource) : null,
        _session.LoadedAt(l.Id).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture));

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
