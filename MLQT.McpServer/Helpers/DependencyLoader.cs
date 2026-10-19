using MLQT.McpServer.Dtos;
using MLQT.McpServer.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Loads the libraries a caller names and, if asked, what they depend on (B503,
/// <c>Design/mcp-tool-consolidation.md</c>, *Finding dependencies* and *When the simulator's session
/// decides*).
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

        return new LoadResult(_loaded, _replaced, _notFound, _warnings, _errors, tool.Install, tool.InstallReason, tool.Folders);
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
                          "libraries the earlier search found were unloaded and searched for again.");
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
            if (OriginOf(existing) == Explicit && !replace)
            {
                _errors.Add($"'{name}' is already loaded from '{existing.SourcePath}', and '{path}' is another copy. " +
                            "Pass replace=true to load this one in its place.");
                return;
            }
            _libraries.RemoveLibrary(existing.Id);
            _session.LibraryOrigins.Remove(existing.Id);
        }

        var library = await TryLoadAsync(root);
        if (library is null)
            return;
        _session.LibraryOrigins[library.Id] = Explicit;
        if (existing is not null)
            _replaced.Add(new ReplacedLibraryItem(name, existing.Version, existing.SourcePath, library.Version, library.SourcePath));
        _loaded.Add(Item(library, Explicit, "given", null, null));
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
            _warnings.Add($"'{library.Name}' at '{path}' is encrypted, and a readable copy is already loaded from '{source}', which is used instead.");
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
