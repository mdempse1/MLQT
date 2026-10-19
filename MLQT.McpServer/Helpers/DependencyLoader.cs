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
/// and every copy seen is reported. A search never replaces a library already loaded.</para>
/// </remarks>
internal sealed class DependencyLoader
{
    private const string Explicit = "explicit";
    private const string Search = "search";

    private readonly ILibraryDataService _libraries;
    private readonly IRepositoryService _repositories;
    private readonly SessionState _session;
    private readonly Func<string, string?> _environment;

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

    public async Task<LoadResult> LoadAsync(IReadOnlyList<string> paths, bool withDependencies, bool replace, ToolFolders tool)
    {
        foreach (var path in paths)
            await LoadExplicitAsync(path, replace);

        if (withDependencies)
            await LoadDependenciesAsync(tool);

        // A tool was named but nothing of it was found: say so, since the search was narrower than asked.
        if (withDependencies && tool.Note is { } note && tool.Folders.Count == 0)
            _warnings.Add(note);

        return new LoadResult(_loaded, _replaced, _notFound, _warnings, _errors, tool.Install, tool.Folders);
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

        var library = await TryLoadAsync(path);
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
                folders.AddRange(modelicaPath.Select(f => (f, "MODELICAPATH")));

                var (chosen, foundBy, rule, seen) = Choose(dependency, wanted, folders);
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

    // The search's choice among every copy seen, in the order the folders were given.
    private static (LibraryCandidate? Chosen, string? FoundBy, string? Rule, List<LibraryCandidate> Seen) Choose(
        string name, ModelicaVersion? wanted, List<(string Folder, string FoundBy)> folders)
    {
        var byFolder = new List<(string FoundBy, IReadOnlyList<LibraryCandidate> Candidates)>();
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, foundBy) in folders)
        {
            if (!seenFolders.Add(Path.GetFullPath(folder)))
                continue;
            byFolder.Add((foundBy, LibraryCandidates.Find(folder, name)));
        }
        var seen = byFolder.SelectMany(f => f.Candidates).ToList();

        if (wanted is null)
        {
            foreach (var (foundBy, candidates) in byFolder.Where(f => f.Candidates.Count > 0))
            {
                var unversioned = candidates.FirstOrDefault(c => IsUnversioned(c.Path, name));
                return (unversioned ?? candidates.OrderByDescending(c => c.Version).First(), foundBy,
                    unversioned is not null ? "no version asked for" : "no version asked for; the newest", seen);
            }
            return (null, null, null, seen);
        }

        foreach (var (foundBy, candidates) in byFolder)
        {
            var exact = candidates.Where(c => c.Version is not null && c.Version.SameReleaseAs(wanted))
                .OrderByDescending(c => c.Version).FirstOrDefault();
            if (exact is not null)
                return (exact, foundBy, "the version asked for", seen);
        }
        foreach (var (foundBy, candidates) in byFolder)
        {
            var compatible = candidates.Where(c => c.NoneFromVersions.Any(v => v.SameReleaseAs(wanted)))
                .OrderByDescending(c => c.Version).FirstOrDefault();
            if (compatible is not null)
                return (compatible, foundBy,
                    $"version {compatible.Version?.Text ?? "?"}, which declares conversion(noneFromVersion=\"{wanted.Text}\")", seen);
        }
        return (null, null, null, seen);
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

    private static bool IsUnversioned(string path, string name) =>
        Path.GetFileName(path.TrimEnd('/', '\\')) is var leaf && (leaf == name || leaf == name + ".mo");

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
