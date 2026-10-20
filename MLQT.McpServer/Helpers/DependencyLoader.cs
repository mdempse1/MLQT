using MLQT.McpServer.Dtos;
using MLQT.McpServer.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Loads the libraries a caller names and, if asked, what they depend on (B503,
/// <c>.claude/skills/skill-mcp-library-loading.md</c>, *Finding dependencies* and *When the simulator's
/// session decides*).
/// </summary>
/// <remarks>
/// <para><b>Explicit first.</b> Every path the caller passes is loaded before anything is searched for, so a
/// search cannot get in first with another version. An explicit library replaces a copy an earlier search
/// found; two explicit copies of one library need <c>replace</c>, because then the caller has to choose.</para>
/// <para><b>Then the search</b>, for each <c>uses</c> not yet satisfied, transitively: the folder beside the
/// library that needs it, its repository's root, the simulation tool's library folders, then
/// MODELICAPATH. The first exact version found wins, the largest build of it; failing that, the newest
/// copy declaring <c>conversion(noneFromVersion=...)</c> for the version asked for; failing that, nothing,
/// and every copy seen is reported. A search never replaces a library already loaded - except that a load
/// naming another tool install than the last search used unloads what searches found, and searches again
/// (B504).</para>
/// </remarks>
internal sealed class DependencyLoader
{
    private const string Explicit = "explicit";
    private const string Search = "search";

    private readonly ILibraryDataService _libraries;
    private readonly IRepositoryService _repositories;
    private readonly SessionState _session;
    private readonly Func<string, string?> _environment;
    private readonly List<(string Folder, string FoundBy)> _extraFolders = [];

    private readonly List<LoadedLibraryItem> _loaded = [];
    private readonly List<ReplacedLibraryItem> _replaced = [];
    private readonly List<MissingDependency> _notFound = [];
    private readonly List<string> _warnings = [];
    private readonly List<string> _errors = [];
    private readonly List<LoadRepositoryResult> _repositoriesLoaded = [];

    /// <summary>
    /// Loads a directory that holds no library of its own - a working copy, or a folder of libraries - as
    /// <c>mlqt_load</c> does, so its settings and changes are known; with false, only discovers
    /// its libraries. Set by the tool, so that there is one implementation of it (B503).
    /// </summary>
    public Func<string, bool, Task<LoadRepositoryResult>>? LoadRepository { get; init; }

    /// <summary>Discover a repository's libraries without loading them.</summary>
    public bool DiscoverOnly { get; init; }

    public DependencyLoader(ILibraryDataService libraries, IRepositoryService repositories, SessionState session,
        Func<string, string?> environment)
    {
        _libraries = libraries;
        _repositories = repositories;
        _session = session;
        _environment = environment;
    }

    /// <summary>Folders a startup script added to its ModelicaPath, searched after the tool's own.</summary>
    public void AddSearchFolders(IEnumerable<string> folders, string foundBy) =>
        _extraFolders.AddRange(folders.Select(f => (f, foundBy)));

    public async Task<LoadResult> LoadAsync(IReadOnlyList<string> paths, bool withDependencies, bool replace, ToolFolders tool)
    {
        foreach (var path in paths)
            await LoadExplicitAsync(path, replace);

        if (withDependencies)
        {
            var unloaded = SearchAgainIfTheInstallChanged(tool.Install);
            await LoadDependenciesAsync(tool);
            ReportWhatTheNewSearchFound(unloaded);
        }

        // A tool was named but nothing of it was found: say so, since the search was narrower than asked.
        if (withDependencies && tool.Note is { } note && tool.Folders.Count == 0)
            _warnings.Add(note);

        return new LoadResult(_loaded, _replaced, _notFound, _warnings, _errors, tool.Install, tool.InstallReason, tool.Folders,
            Repositories: _repositoriesLoaded.Count > 0 ? _repositoriesLoaded : null);
    }

    /// <summary>
    /// When <paramref name="install"/> is not the install the session's last search used, unloads every
    /// library a search found, so the search about to run decides them all again from the new one. A
    /// search never replaces a loaded library, so without this a second load naming the install that is
    /// running only filled the gaps, and kept the old install's copies of the rest (B504). The libraries
    /// the caller named stay. Returns what was unloaded.
    /// </summary>
    private List<(string Name, string? Version, string Path)> SearchAgainIfTheInstallChanged(string? install)
    {
        var unloaded = new List<(string Name, string? Version, string Path)>();
        if (install is null)
            return unloaded;
        var previous = _session.SearchInstall;
        _session.SearchInstall = install;
        if (previous is null || SamePath(previous, install))
            return unloaded;

        foreach (var library in _libraries.Libraries.Where(l => OriginOf(l) == Search).ToList())
        {
            unloaded.Add((library.Name, library.Version, library.SourcePath));
            _libraries.RemoveLibrary(library.Id);
            _session.LibraryOrigins.Remove(library.Id);
        }
        if (unloaded.Count > 0)
        {
            _session.DependenciesAnalyzed = false;
            _session.ResourcesAnalyzed = false;
            _warnings.Add($"The tool install changed from '{previous}' to '{install}', so the {unloaded.Count} " +
                          "libraries the earlier search found were unloaded and searched for again. If other " +
                          "work is using this MLQT server, its dependencies changed with them.");
        }
        return unloaded;
    }

    // Each library unloaded for a new install: replaced when the search found it somewhere else, said
    // when it found nothing; one found again at the same path is as it was.
    private void ReportWhatTheNewSearchFound(List<(string Name, string? Version, string Path)> unloaded)
    {
        foreach (var (name, version, path) in unloaded)
        {
            var now = _libraries.Libraries.FirstOrDefault(l => l.Name == name);
            if (now is null)
                _warnings.Add($"{name} {version} from '{path}' was unloaded for the new install, and no copy of it was found there.");
            else if (!SamePath(now.SourcePath, path))
                _replaced.Add(new ReplacedLibraryItem(name, version, path, now.Version, now.SourcePath));
        }
    }

    /// <summary>
    /// The refusal for a second copy of a library the caller already named. MLQT holds one copy of each
    /// library and a class id does not say whose, so the copy loaded may be another task's, or an earlier
    /// conversation's: this said only "pass replace=true", and a task that did would have moved the other's
    /// edits onto its own copy without either knowing (B505). It says how old the copy is, and what
    /// replacing it does, so that the caller can tell whether it is its own.
    /// </summary>
    private string AnotherCopy(string name, LoadedLibrary existing, string path) =>
        $"'{name}' is already loaded from '{existing.SourcePath}', loaded {_session.LoadedAgo(existing.Id)}, and " +
        $"'{path}' is another copy. MLQT holds one copy of a library, and a class id does not say whose: if " +
        "this conversation did not load that copy, it may be another task's using this MLQT server, or an " +
        $"earlier conversation's. Replacing it unloads it, and every later MLQT edit to {name}'s classes - that " +
        "other work's included - goes to this copy. Pass replace=true only if this conversation loaded the " +
        "other copy, or the user says so; otherwise ask the user. mlqt_session shows what is loaded and when.";

    private static string Superseded(string name, string path, string source) =>
        $"'{name}' at '{path}' is encrypted, and a readable copy is already loaded from '{source}', which is used instead.";

    private string OriginOf(LoadedLibrary library) =>
        _session.LibraryOrigins.TryGetValue(library.Id, out var origin) ? origin : Explicit;

    private async Task LoadExplicitAsync(string path, bool replace)
    {
        var root = LibraryRoot(path);
        if (root is null)
        {
            _errors.Add($"'{path}' is not a library folder, a package.mo or a .mo file.");
            return;
        }

        // A directory with no package.mo of its own is a working copy or a folder of libraries.
        if (LoadRepository is { } loadRepository && Directory.Exists(root) &&
            !File.Exists(Path.Combine(root, "package.mo")) && !File.Exists(Path.Combine(root, "package.moe")))
        {
            await LoadRepositoryAsync(loadRepository, root);
            return;
        }

        var name = NameFromPath(root);
        var existing = _libraries.Libraries.FirstOrDefault(l => l.Name == name);
        if (existing is not null)
        {
            if (SamePath(existing.SourcePath, root))
            {
                _session.LibraryOrigins[existing.Id] = Explicit;
                _loaded.Add(Item(existing, Explicit, "already loaded", null, null));
                return;
            }
            // An encrypted build beside readable source is not another copy to choose between: the source
            // wins whole (B268), so it is said, not refused - a refusal offers replace, which would swap
            // the source for a build with none.
            if (!existing.IsReadOnly && File.Exists(Path.Combine(root, "package.moe")))
            {
                _warnings.Add(Superseded(name, path, existing.SourcePath));
                return;
            }
            if (OriginOf(existing) == Explicit && !replace)
            {
                _errors.Add(AnotherCopy(name, existing, path));
                return;
            }
            _libraries.RemoveLibrary(existing.Id);
            _session.LibraryOrigins.Remove(existing.Id);
        }

        var library = await TryLoadAsync(root);
        if (library is null)
            return;

        // A copy in a folder not named after its library - a clone called Formula1-copy - is known for what
        // it is only once loaded. Without this it was loaded beside the first copy, silently, and every
        // class id still answered with the first: a second task's edits went to the first task's files (B505).
        if (existing is null &&
            _libraries.Libraries.FirstOrDefault(l => l.Id != library.Id && l.Name == library.Name) is { } twin)
        {
            if (OriginOf(twin) == Explicit && !replace)
            {
                _libraries.RemoveLibrary(library.Id);
                _errors.Add(AnotherCopy(library.Name, twin, path));
                return;
            }
            _libraries.RemoveLibrary(twin.Id);
            _session.LibraryOrigins.Remove(twin.Id);
            existing = twin;
        }

        _session.LibraryOrigins[library.Id] = Explicit;
        if (existing is not null)
            _replaced.Add(new ReplacedLibraryItem(library.Name, existing.Version, existing.SourcePath, library.Version, library.SourcePath));
        _loaded.Add(Item(library, Explicit, "given", null, null));
    }

    // Each library the repository holds is one the caller named, as if its path had been given.
    private async Task LoadRepositoryAsync(Func<string, bool, Task<LoadRepositoryResult>> loadRepository, string root)
    {
        var result = await loadRepository(root, !DiscoverOnly);
        _repositoriesLoaded.Add(result);
        if (!result.Success)
        {
            _errors.Add(result.ErrorMessage ?? $"'{root}' could not be loaded as a repository.");
            return;
        }
        var count = _loaded.Count;
        foreach (var library in _libraries.Libraries.Where(l => l.RepositoryId == result.RepositoryId))
        {
            _session.LibraryOrigins[library.Id] = Explicit;
            _loaded.Add(Item(library, Explicit, $"in repository '{result.Name}'", null, null));
        }
        // A folder with no library in it, an empty one or a mistyped one, loaded nothing and said nothing.
        if (_loaded.Count == count && result.DiscoveredLibraries.Count == 0)
            _errors.Add($"No Modelica classes were found at '{root}'. Point mlqt_load at a library folder holding a " +
                        "package.mo, a single .mo file, or a working copy whose libraries live in subfolders.");
    }

    private async Task LoadDependenciesAsync(ToolFolders tool)
    {
        var modelicaPath = (_environment("MODELICAPATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var asked = new HashSet<string>(StringComparer.Ordinal);

        // Breadth first, so a library's own dependencies are looked for beside it once it is loaded.
        var queue = new Queue<LoadedLibrary>(_libraries.Libraries);
        while (queue.Count > 0)
        {
            var library = queue.Dequeue();
            foreach (var (dependency, versionText) in UsesOf(library))
            {
                if (!asked.Add($"{dependency}\u0000{versionText}"))
                    continue;
                var wanted = ModelicaVersion.Parse(versionText);
                if (_libraries.Libraries.FirstOrDefault(l => l.Name == dependency) is { } present)
                {
                    NoteVersion(present, dependency, wanted, library.Name);
                    continue;
                }

                var folders = new List<(string Folder, string FoundBy)>();
                foreach (var sibling in SiblingFolders(library))
                    folders.Add(sibling);
                folders.AddRange(tool.Folders.Select(f => (f, "the tool's library folder")));
                folders.AddRange(_extraFolders);
                folders.AddRange(modelicaPath.Select(f => (f, "MODELICAPATH")));

                var (chosen, foundBy, rule, seen) = LibraryChoice.Choose(dependency, wanted, folders);
                if (chosen is null)
                {
                    _notFound.Add(new MissingDependency(dependency, versionText, library.Name,
                        seen.Select(c => new CandidateItem(c.Path, c.Version?.Text)).ToList()));
                    continue;
                }

                var loaded = await TryLoadAsync(chosen.Path);
                if (loaded is null)
                    continue;
                _session.LibraryOrigins[loaded.Id] = Search;
                _loaded.Add(Item(loaded, Search, foundBy!, rule, library.Name));
                queue.Enqueue(loaded);
            }
        }
    }

    private void NoteVersion(LoadedLibrary present, string name, ModelicaVersion? wanted, string requiredBy)
    {
        if (wanted is null || ModelicaVersion.Parse(present.Version) is not { } have || have.SameReleaseAs(wanted))
            return;
        _warnings.Add(OriginOf(present) == Explicit
            ? $"{requiredBy} uses {name} {wanted.Text}, but {name} {have.Text} was loaded explicitly and is kept: it is what was asked for, so it is what the simulator uses."
            : $"{requiredBy} uses {name} {wanted.Text}, but {name} {have.Text} is already loaded. It was not replaced; load the one you want explicitly.");
    }

    // The folder that holds the library, then its repository's root.
    private IEnumerable<(string Folder, string FoundBy)> SiblingFolders(LoadedLibrary library)
    {
        var root = LibraryRoot(library.SourcePath) ?? library.SourcePath;
        if (Path.GetDirectoryName(root.TrimEnd('/', '\\')) is { } parent)
            yield return (parent, $"beside {library.Name}");
        if (library.RepositoryId is { } repositoryId &&
            _repositories.Repositories.FirstOrDefault(r => r.Id == repositoryId)?.LocalPath is { } repositoryRoot)
            yield return (repositoryRoot, $"in {library.Name}'s repository");
    }

    private IEnumerable<(string Name, string? Version)> UsesOf(LoadedLibrary library)
    {
        foreach (var topId in library.TopLevelModelIds)
        {
            if (_libraries.GetModelById(topId)?.Uses is not { } uses)
                continue;
            foreach (var (name, version) in uses)
                yield return (name, string.IsNullOrEmpty(version) ? null : version);
        }
    }

    private async Task<LoadedLibrary?> TryLoadAsync(string path)
    {
        if (path.EndsWith(".moe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            _warnings.Add($"'{path}' is an encrypted library in a single file. MLQT reads an encrypted library " +
                          "only as a folder holding a package.moe, from its generated documentation, so this one " +
                          "was not loaded; references into it will not resolve.");
            return null;
        }
        LoadedLibrary library;
        try
        {
            library = await _libraries.AddLibraryFromPathAsync(path);
        }
        catch (ArgumentException)
        {
            _errors.Add($"'{path}' could not be loaded: not a library folder, a package.mo or a .mo file.");
            return null;
        }
        if (library.SupersededBy is { } source)
        {
            _warnings.Add(Superseded(library.Name, path, source));
            return null;
        }
        if (library.ModelIds.Count == 0)
        {
            _libraries.RemoveLibrary(library.Id);
            if (library.SourceType == LibrarySourceType.EncryptedDirectory)
                _warnings.Add($"'{library.Name}' at '{path}' is an encrypted library that ships no usable documentation, " +
                              "so none of its classes could be recovered; references into it will not resolve.");
            else
                _errors.Add($"No Modelica classes were found at '{path}'.");
            return null;
        }
        return library;
    }

    private static LoadedLibraryItem Item(LoadedLibrary library, string origin, string foundBy, string? rule, string? requiredBy) =>
        new(library.Name, library.Version, library.SourcePath, origin, foundBy, rule, requiredBy,
            library.SourceType == LibrarySourceType.EncryptedDirectory);

    /// <summary>The folder or file a path names as a library: a <c>package.mo</c> means its folder.</summary>
    internal static string? LibraryRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var full = Path.GetFullPath(path).TrimEnd('/', '\\');
        if (File.Exists(full))
            return Path.GetFileName(full) is "package.mo" or "package.moe" ? Path.GetDirectoryName(full) : full;
        return Directory.Exists(full) ? full : null;
    }

    /// <summary>A library's name from its folder or file: <c>Modelica 4.1.0+maint.om</c> is <c>Modelica</c>.</summary>
    internal static string NameFromPath(string root)
    {
        var leaf = Path.GetFileName(root);
        if (leaf.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
            leaf = leaf[..^3];
        var space = leaf.IndexOf(' ');
        return space > 0 ? leaf[..space] : leaf;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
